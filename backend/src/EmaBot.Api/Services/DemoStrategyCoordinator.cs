using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed record DemoStrategyRuntimeSnapshot(int SessionId, string ConnectionState, DateTimeOffset? LastUpdateUtc, IReadOnlyDictionary<string, DateTimeOffset?> LastClosedCandles);

// The coordinator observes live MT5 bars only.  It intentionally has no execution-bridge
// dependency: all broker writes remain inside DemoExecutionService.
public sealed class DemoStrategyCoordinator(
    IServiceScopeFactory scopeFactory,
    IHistoricalMarketDataProviderResolver historicalProviders,
    IMarketBarStreamProvider stream,
    IInstrumentCatalogProvider catalog,
    EmaSignalEngine strategy,
    IOptions<DemoStrategyAutomationOptions> options,
    ILogger<DemoStrategyCoordinator> logger) : IHostedService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly DemoStrategyAutomationOptions automation = options.Value;
    private RuntimeSession? active;

    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken token)
    {
        RuntimeSession? state;
        await gate.WaitAsync(token);
        try { state = active; active = null; state?.Cancellation.Cancel(); }
        finally { gate.Release(); }
        if (state?.Worker is not null) await ObserveAsync(state.Worker);
        state?.Cancellation.Dispose();
    }

    public DemoStrategyRuntimeSnapshot? GetRuntimeSnapshot()
    {
        var state = active;
        return state is null ? null : new(state.Session.Id, state.ConnectionState, state.LastUpdateUtc, state.Symbols.ToDictionary(pair => pair.Key, pair => pair.Value.LastClosedCandleUtc));
    }

    public async Task StartSessionAsync(int sessionId, bool resume, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (active is not null) throw new InvalidOperationException("A Demo strategy session is already active.");
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var session = await database.DemoStrategySessions.Include(item => item.Symbols).SingleOrDefaultAsync(item => item.Id == sessionId, token) ?? throw new KeyNotFoundException("Demo strategy session not found.");
            if (resume && session.Status != DemoStrategySessionStatus.Interrupted) throw new InvalidOperationException("Only interrupted Demo strategy sessions can be resumed.");
            if (!resume && session.Status != DemoStrategySessionStatus.Created) throw new InvalidOperationException("Only a newly created Demo strategy session can be started.");
            if (resume)
            {
                // A pre-crash next-bar window cannot be reconstructed or submitted later.
                var stranded = await database.DemoStrategyIntents.Where(item => item.DemoStrategySessionId == session.Id && item.DemoExecutionId == null && (item.Status == DemoStrategyIntentStatus.Created || item.Status == DemoStrategyIntentStatus.WaitingForEntryWindow || item.Status == DemoStrategyIntentStatus.Submitting)).ToListAsync(token);
                foreach (var intent in stranded)
                {
                    var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == intent.ClientExecutionId, token);
                    if (execution is not null)
                    {
                        intent.DemoExecutionId = execution.Id;
                        intent.Status = execution.State == DemoExecutionState.ReconciliationRequired ? DemoStrategyIntentStatus.ReconciliationRequired : execution.State == DemoExecutionState.Rejected ? DemoStrategyIntentStatus.Rejected : DemoStrategyIntentStatus.ExecutionLinked;
                        intent.Reason = "Recovered the existing DemoExecution by durable ClientExecutionId; no broker submission was retried.";
                        intent.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    }
                    else Finish(intent, DemoStrategyIntentStatus.Expired, "The application restarted before this entry window completed; it will never be submitted late.");
                }
                await database.SaveChangesAsync(token);
            }
            // A successful start outlives its HTTP/request caller.  The request token is used
            // above for startup I/O only; session stop, host stop, and stream failure own this
            // independent worker lifetime.
            var proposed = new RuntimeSession(session, new CancellationTokenSource());
            foreach (var symbol in session.Symbols) proposed.Symbols[symbol.BrokerSymbol] = new RuntimeSymbol(symbol);
            try { await WarmupAsync(proposed, token); }
            catch
            {
                proposed.Cancellation.Cancel(); proposed.Cancellation.Dispose();
                session.Status = DemoStrategySessionStatus.Interrupted; session.InterruptedAtUtc = DateTimeOffset.UtcNow; session.FailureMessage = "Historical MT5 warmup data is unavailable; observation was not started.";
                await database.SaveChangesAsync(token);
                throw;
            }
            session.Status = DemoStrategySessionStatus.Running; session.StartedAtUtc ??= DateTimeOffset.UtcNow; session.InterruptedAtUtc = null; session.FailureMessage = null;
            await database.SaveChangesAsync(token);
            active = proposed;
            proposed.Worker = Task.Run(() => RunStreamAsync(proposed), CancellationToken.None);
            logger.LogInformation("Demo strategy session {SessionId} started for {SymbolCount} symbols.", session.Id, session.Symbols.Count);
        }
        finally { gate.Release(); }
    }

    public async Task StopSessionAsync(int sessionId, CancellationToken token)
    {
        RuntimeSession? state = null;
        await gate.WaitAsync(token);
        try
        {
            if (active is not null)
            {
                if (active.Session.Id != sessionId) throw new InvalidOperationException("A different Demo strategy session is running in this application.");
                state = active; state.AcceptSignals = false; active = null; state.Cancellation.Cancel();
            }
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var session = await database.DemoStrategySessions.SingleOrDefaultAsync(item => item.Id == sessionId, token) ?? throw new KeyNotFoundException("Demo strategy session not found.");
            foreach (var intent in await database.DemoStrategyIntents.Where(item => item.DemoStrategySessionId == sessionId && (item.Status == DemoStrategyIntentStatus.Created || item.Status == DemoStrategyIntentStatus.WaitingForEntryWindow)).ToListAsync(token))
                Finish(intent, DemoStrategyIntentStatus.Expired, "The session stopped before the exact entry window; no broker order was submitted.");
            session.Status = DemoStrategySessionStatus.Stopped; session.StoppedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token);
        }
        finally { gate.Release(); }
        if (state?.Worker is not null) await ObserveAsync(state.Worker);
        state?.Cancellation.Dispose();
        logger.LogInformation("Demo strategy session {SessionId} stopped without closing any broker execution.", sessionId);
    }

    internal async Task ProcessUpdateForTestAsync(MarketBarUpdate update, CancellationToken token = default)
    {
        var state = active ?? throw new InvalidOperationException("No active Demo strategy session.");
        await ProcessUpdateAsync(state, update, token);
    }

    private async Task WarmupAsync(RuntimeSession state, CancellationToken token)
    {
        var historical = historicalProviders.Resolve(MarketDataSource.Mt5Exness);
        foreach (var runtime in state.Symbols.Values)
        {
            var candles = await historical.GetLatestAsync(runtime.Symbol.BrokerSymbol, state.Session.Interval, 200, token);
            runtime.Candles.AddRange(candles.Where(item => item.IsClosed).OrderBy(item => item.OpenTimeUtc).TakeLast(200));
            runtime.LastClosedCandleUtc = runtime.Candles.LastOrDefault()?.CloseTimeUtc;
        }
    }

    private async Task RunStreamAsync(RuntimeSession state)
    {
        try { await stream.StreamAsync(state.Symbols.Keys.ToArray(), state.Session.Interval, (update, token) => ProcessUpdateAsync(state, update, token), value => state.ConnectionState = value, state.Cancellation.Token); }
        catch (Exception exception) when (!state.Cancellation.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Demo strategy session {SessionId} market observation was interrupted.", state.Session.Id);
            await MarkInterruptedAsync(state, "MT5 market observation was interrupted. Explicit resume is required.");
        }
    }

    private async Task ProcessUpdateAsync(RuntimeSession state, MarketBarUpdate update, CancellationToken token)
    {
        if (!state.Symbols.TryGetValue(update.Symbol, out var runtime) || update.Timeframe != state.Session.Interval) return;
        await gate.WaitAsync(token);
        try
        {
            state.LastUpdateUtc = update.EventTimeUtc;
            runtime.LastMarketEventUtc = update.EventTimeUtc;
            if (!update.IsClosed && state.AcceptSignals) await TryEnterWindowAsync(state, runtime, update, token);
            if (!update.IsClosed || runtime.LastClosedCandleUtc == update.CloseTimeUtc) return;
            var streamGap = runtime.LastClosedCandleUtc is { } previous && update.OpenTimeUtc > previous.AddMilliseconds(1);
            if (streamGap) await ResyncAsync(state, runtime, update, token);
            runtime.LastClosedCandleUtc = update.CloseTimeUtc;
            runtime.Candles.RemoveAll(item => item.OpenTimeUtc == update.OpenTimeUtc);
            runtime.Candles.Add(new Candle(update.OpenTimeUtc, update.CloseTimeUtc, update.Open, update.High, update.Low, update.Close, update.Volume, true));
            if (runtime.Candles.Count > 200) runtime.Candles.RemoveRange(0, runtime.Candles.Count - 200);
            await PersistSymbolAsync(runtime, token);
            // A gap reconstructs historical indicator state only.  The close that exposed the
            // gap is never allowed to manufacture an entry against a successor quote.
            if (streamGap) return;
            if (!state.AcceptSignals) return;
            var evaluation = strategy.Evaluate(runtime.Candles, Settings(state.Session));
            foreach (var signal in evaluation.Events.Where(item => item.Time == update.CloseTimeUtc && (item.Status == SignalStatus.LongSignal || item.Status == SignalStatus.ShortSignal)))
                await CreateIntentAsync(state, runtime, evaluation.Events, signal, token);
            logger.LogInformation("Demo strategy closed candle processed for {SessionId}/{Symbol} at {CloseTime}.", state.Session.Id, runtime.Symbol.BrokerSymbol, update.CloseTimeUtc);
        }
        finally { gate.Release(); }
    }

    private async Task CreateIntentAsync(RuntimeSession state, RuntimeSymbol runtime, IReadOnlyList<StrategyEvent> events, StrategyEvent signal, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var existing = await database.DemoStrategyIntents.SingleOrDefaultAsync(item => item.DemoStrategySessionId == state.Session.Id && item.DemoStrategySessionSymbolId == runtime.Symbol.Id && item.SignalTimeUtc == signal.Time && item.Direction == signal.Direction, token);
        if (existing is not null) return;
        var executionService = scope.ServiceProvider.GetRequiredService<IDemoExecutionService>();
        var unresolvedExecution = await ReconcileOpenExecutionsAndFindUnresolvedAsync(database, runtime.Symbol.BrokerSymbol, executionService, token);
        var unresolved = unresolvedExecution is not null;
        var crossover = events.LastOrDefault(item => item.Time <= signal.Time && item.Direction == signal.Direction && item.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover);
        var crossoverIndex = runtime.Candles.FindIndex(item => item.CloseTimeUtc == crossover?.Time);
        var signalIndex = runtime.Candles.FindIndex(item => item.CloseTimeUtc == signal.Time);
        if (crossoverIndex < 1 || signalIndex < 0) return;
        var stop = InitialStopSelector.Select(runtime.Candles, crossoverIndex, signalIndex, signal.Snapshot, signal.Direction, Settings(state.Session));
        var intent = new DemoStrategyIntent
        {
            DemoStrategySessionId = state.Session.Id, DemoStrategySessionSymbolId = runtime.Symbol.Id, Direction = signal.Direction,
            CrossoverTimeUtc = crossover!.Time, SignalTimeUtc = signal.Time, ExpectedEntryOpenUtc = signal.Time.AddMilliseconds(1),
            SignalOpen = signal.Snapshot.Open, SignalClose = signal.Snapshot.Close, SignalEma9 = signal.Snapshot.Ema9, SignalEma15 = signal.Snapshot.Ema15, SignalEma100 = signal.Snapshot.Ema100, SignalGapPercent = signal.Snapshot.GapPercent, SignalGapState = signal.Snapshot.GapState,
            StructuralStopLoss = stop.Price, StopSourceType = stop.Source, StopSourceTimeUtc = stop.Time, IntendedVolumeLots = state.Session.FixedLots,
            ClientExecutionId = Guid.NewGuid(), Status = unresolved ? DemoStrategyIntentStatus.Blocked : DemoStrategyIntentStatus.WaitingForEntryWindow,
            Reason = unresolved ? ExposureReason(signal.Direction, unresolvedExecution!) : null, CreatedAtUtc = DateTimeOffset.UtcNow
        };
        database.DemoStrategyIntents.Add(intent);
        await database.SaveChangesAsync(token); // ClientExecutionId is durable before an entry window can submit.
        logger.LogInformation("Demo strategy signal {Direction} created intent {IntentId} for {Symbol} at {SignalTime}.", signal.Direction, intent.Id, runtime.Symbol.BrokerSymbol, signal.Time);
    }

    private async Task TryEnterWindowAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var pending = await database.DemoStrategyIntents.Where(item => item.DemoStrategySessionSymbolId == runtime.Symbol.Id && item.Status == DemoStrategyIntentStatus.WaitingForEntryWindow).OrderBy(item => item.Id).ToListAsync(token);
        foreach (var intent in pending)
        {
            if (update.OpenTimeUtc > intent.ExpectedEntryOpenUtc)
            {
                Finish(intent, DemoStrategyIntentStatus.Expired, "The exact next-bar entry window was missed; the strategy will not chase a later quote.");
                await database.SaveChangesAsync(token);
                continue;
            }
            if (update.OpenTimeUtc != intent.ExpectedEntryOpenUtc) continue;
            var entry = DemoStrategyExecutionRules.EntryPrice(intent.Direction, update.Bid, update.Ask);
            if (entry is null) continue; // wait for the first reliable quote in this exact bar only.
            if (!automation.Enabled) { Finish(intent, DemoStrategyIntentStatus.Blocked, "Demo strategy automation is disabled; the exact entry window will not be queued or retried."); await database.SaveChangesAsync(token); continue; }
            if ((intent.Direction == SignalDirection.Long && intent.StructuralStopLoss >= entry) || (intent.Direction == SignalDirection.Short && intent.StructuralStopLoss <= entry)) { Finish(intent, DemoStrategyIntentStatus.Rejected, "The structural stop is on the wrong side of the executable entry quote."); await database.SaveChangesAsync(token); continue; }
            if (state.Session.MaxStopDistancePercent > 0m && TradeMath.StopDistancePercent(entry.Value, intent.StructuralStopLoss) > state.Session.MaxStopDistancePercent) { Finish(intent, DemoStrategyIntentStatus.Rejected, "The structural stop exceeds the configured maximum stop distance."); await database.SaveChangesAsync(token); continue; }
            var instrument = await catalog.GetAsync(runtime.Symbol.BrokerSymbol, token);
            if (instrument is null || !instrument.IsSelected || !string.Equals(instrument.Spec.BrokerSymbol, runtime.Symbol.BrokerSymbol, StringComparison.Ordinal)) { Finish(intent, DemoStrategyIntentStatus.Blocked, "MT5 instrument data is unavailable for this exact entry window."); await database.SaveChangesAsync(token); continue; }
            if (!DemoStrategyExecutionRules.AllowsDirection(instrument.TradeMode, intent.Direction)) { Finish(intent, DemoStrategyIntentStatus.Blocked, "MT5 trade mode does not permit this strategy direction at the exact entry window."); await database.SaveChangesAsync(token); continue; }
            if (DemoStrategyExecutionRules.ValidateFixedLots(instrument.Spec, intent.IntendedVolumeLots) is { } volumeFailure) { Finish(intent, DemoStrategyIntentStatus.Rejected, volumeFailure); await database.SaveChangesAsync(token); continue; }
            var target = TradeMath.InitialTarget(entry.Value, intent.StructuralStopLoss, intent.Direction, state.Session.RiskReward);
            if (!DemoStrategyExecutionRules.StopAndTargetMeetBrokerMinimum(instrument.Spec, intent.Direction, entry.Value, update.Bid!.Value, update.Ask!.Value, intent.StructuralStopLoss, target)) { Finish(intent, DemoStrategyIntentStatus.Rejected, "The initial stop or target violates MT5 broker stop-level requirements."); await database.SaveChangesAsync(token); continue; }
            var executionService = scope.ServiceProvider.GetRequiredService<IDemoExecutionService>();
            var readiness = await executionService.ReadinessAsync(token);
            if (!readiness.Ready) { Finish(intent, DemoStrategyIntentStatus.Blocked, "DemoExecution readiness failed at the exact entry window; no late retry is permitted."); await database.SaveChangesAsync(token); continue; }
            // A different lifecycle/manual execution can appear after the signal closes.  The
            // final durable check is deliberately after deterministic validation/readiness and
            // before Submitting/SubmitAsync; reconciliation remains the E11.5 authority.
            var unresolvedExecution = await ReconcileOpenExecutionsAndFindUnresolvedAsync(database, runtime.Symbol.BrokerSymbol, executionService, token);
            if (unresolvedExecution is not null) { Finish(intent, DemoStrategyIntentStatus.Blocked, ExposureReason(intent.Direction, unresolvedExecution)); await database.SaveChangesAsync(token); continue; }
            intent.IntendedTakeProfit = target; intent.Status = DemoStrategyIntentStatus.Submitting; intent.SubmittedAtUtc = DateTimeOffset.UtcNow; intent.UpdatedAtUtc = intent.SubmittedAtUtc;
            await database.SaveChangesAsync(token); // strategy state is durable before DemoExecution submits its own durable broker intent.
            var execution = await executionService.SubmitAsync(new SubmitDemoOrder(intent.ClientExecutionId, runtime.Symbol.BrokerSymbol, intent.Direction == SignalDirection.Long ? "Buy" : "Sell", intent.IntendedVolumeLots, intent.StructuralStopLoss, target), token);
            intent.DemoExecutionId = execution.Id;
            intent.Status = execution.State == DemoExecutionState.ReconciliationRequired ? DemoStrategyIntentStatus.ReconciliationRequired : execution.State == DemoExecutionState.Rejected ? DemoStrategyIntentStatus.Rejected : DemoStrategyIntentStatus.ExecutionLinked;
            intent.Reason = execution.BrokerMessage ?? execution.ReconciliationNote;
            intent.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token);
            logger.LogInformation("Demo strategy intent {IntentId} linked DemoExecution {ExecutionId} in state {ExecutionState}.", intent.Id, execution.Id, execution.State);
        }
    }

    private async Task ResyncAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        var historical = historicalProviders.Resolve(MarketDataSource.Mt5Exness);
        var candles = await historical.GetLatestAsync(runtime.Symbol.BrokerSymbol, state.Session.Interval, 200, token);
        runtime.Candles.Clear(); runtime.Candles.AddRange(candles.Where(item => item.IsClosed).OrderBy(item => item.OpenTimeUtc).TakeLast(200));
        runtime.Candles.RemoveAll(item => item.OpenTimeUtc == update.OpenTimeUtc);
        runtime.Candles.Add(new Candle(update.OpenTimeUtc, update.CloseTimeUtc, update.Open, update.High, update.Low, update.Close, update.Volume, true));
        runtime.Candles.Sort((left, right) => left.OpenTimeUtc.CompareTo(right.OpenTimeUtc));
        if (runtime.Candles.Count > 200) runtime.Candles.RemoveRange(0, runtime.Candles.Count - 200);
        logger.LogInformation("Demo strategy session {SessionId} resynchronized {Symbol}; no historical entry was created.", state.Session.Id, runtime.Symbol.BrokerSymbol);
    }

    private async Task PersistSymbolAsync(RuntimeSymbol runtime, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var symbol = await database.DemoStrategySessionSymbols.SingleAsync(item => item.Id == runtime.Symbol.Id, token);
        symbol.LastProcessedClosedCandleUtc = runtime.LastClosedCandleUtc; symbol.LastMarketEventUtc = runtime.LastMarketEventUtc;
        await database.SaveChangesAsync(token);
    }

    private async Task MarkInterruptedAsync(RuntimeSession state, string message)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var session = await database.DemoStrategySessions.SingleAsync(item => item.Id == state.Session.Id);
        session.Status = DemoStrategySessionStatus.Interrupted; session.InterruptedAtUtc = DateTimeOffset.UtcNow; session.FailureMessage = message;
        await database.SaveChangesAsync();
        state.Cancellation.Cancel();
        await gate.WaitAsync(); try { if (ReferenceEquals(active, state)) active = null; } finally { gate.Release(); }
        state.Cancellation.Dispose();
    }

    private static TradingSettings Settings(DemoStrategySession session) => new() { RiskReward = session.RiskReward, MinEmaGapPercent = session.MinEmaGapPercent, MaxStopDistancePercent = session.MaxStopDistancePercent, WaitForConfirmationCandle = session.WaitForConfirmationCandle, UseEma100Filter = session.UseEma100Filter, UseAdaptiveInitialStop = session.UseAdaptiveInitialStop };
    private static async Task<DemoExecution?> ReconcileOpenExecutionsAndFindUnresolvedAsync(EmaBotDbContext database, string brokerSymbol, IDemoExecutionService executionService, CancellationToken token)
    {
        // Broker-side SL/TP closure is established only through the existing DemoExecution
        // reconciliation path.  This covers prior sessions and manually-created E11.5 records.
        var openExecutionIds = await UnresolvedExecutionsForBrokerSymbol(database, brokerSymbol)
            .Where(item => item.State == DemoExecutionState.Open)
            .Select(item => item.ClientExecutionId)
            .ToArrayAsync(token);
        foreach (var executionId in openExecutionIds) await executionService.ReconcileAsync(executionId, token);
        return await UnresolvedExecutionsForBrokerSymbol(database, brokerSymbol).FirstOrDefaultAsync(token);
    }

    internal static IQueryable<DemoExecution> UnresolvedExecutionsForBrokerSymbol(EmaBotDbContext database, string brokerSymbol) =>
        database.DemoExecutions.Where(execution => execution.BrokerSymbol == brokerSymbol
            && (execution.State == DemoExecutionState.PreflightPassed || execution.State == DemoExecutionState.Submitting || execution.State == DemoExecutionState.BrokerAccepted || execution.State == DemoExecutionState.PartiallyFilled || execution.State == DemoExecutionState.Open || execution.State == DemoExecutionState.CloseRequested || execution.State == DemoExecutionState.ReconciliationRequired));

    private static string ExposureReason(SignalDirection direction, DemoExecution execution) => direction != BrokerSide(execution.Side)
        ? "OppositeSignalDeferred: an unresolved broker execution is open; E11.6A will not close, hedge, reverse, or submit the opposite side."
        : "A broker execution for this symbol remains unresolved or open; no new automated position will be created.";
    private static SignalDirection BrokerSide(string side) => string.Equals(side, "Buy", StringComparison.OrdinalIgnoreCase) ? SignalDirection.Long : string.Equals(side, "Sell", StringComparison.OrdinalIgnoreCase) ? SignalDirection.Short : SignalDirection.None;
    private static void Finish(DemoStrategyIntent intent, DemoStrategyIntentStatus status, string reason) { intent.Status = status; intent.Reason = reason; intent.UpdatedAtUtc = DateTimeOffset.UtcNow; }
    private static async Task ObserveAsync(Task worker) { try { await worker; } catch (OperationCanceledException) { } catch { } }
    private sealed class RuntimeSession(DemoStrategySession session, CancellationTokenSource cancellation) { public DemoStrategySession Session { get; } = session; public CancellationTokenSource Cancellation { get; } = cancellation; public Dictionary<string, RuntimeSymbol> Symbols { get; } = new(StringComparer.Ordinal); public bool AcceptSignals { get; set; } = true; public string ConnectionState { get; set; } = "Starting"; public DateTimeOffset? LastUpdateUtc { get; set; } public Task? Worker { get; set; } }
    private sealed class RuntimeSymbol(DemoStrategySessionSymbol symbol) { public DemoStrategySessionSymbol Symbol { get; } = symbol; public List<Candle> Candles { get; } = []; public DateTimeOffset? LastClosedCandleUtc { get; set; } public DateTimeOffset? LastMarketEventUtc { get; set; } }
}
