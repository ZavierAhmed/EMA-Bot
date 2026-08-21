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
                // Recovery always starts fail-closed.  The following exact read-only
                // pass may reactivate a row, but Resume itself can never write a
                // protection or send a close.
                foreach (var management in await database.DemoStrategyPositionManagement.Where(item => item.DemoStrategySessionId == session.Id && item.State != DemoStrategyPositionManagementState.Closed).ToListAsync(token))
                {
                    management.State = DemoStrategyPositionManagementState.SuspendedAfterRestart;
                    management.LastReason = "SuspendedAfterRestart: exact read-only management recovery is required before any later live quote can manage this position.";
                    management.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
                await database.SaveChangesAsync(token);
                await RecoverManagementAfterResumeAsync(database, session, scope.ServiceProvider.GetRequiredService<IDemoExecutionService>(), token);
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
            foreach (var management in await database.DemoStrategyPositionManagement.Where(item => item.DemoStrategySessionId == sessionId && item.OppositeCloseState == DemoStrategyOppositeCloseState.Pending).ToListAsync(token))
            {
                management.OppositeCloseState = DemoStrategyOppositeCloseState.Blocked;
                management.State = DemoStrategyPositionManagementState.Blocked;
                management.LastReason = "The session stopped before the durable opposite-close directive could execute; no broker close was sent.";
                management.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
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
            if (!update.IsClosed)
            {
                if (state.AcceptSignals) await TryEnterWindowAsync(state, runtime, update, token);
                // B2 is driven exclusively by a later live executable quote.  Closed
                // candles may establish strategy signals, but never a broker-management
                // price or a synthetic future close.
                if (state.AcceptSignals) await ManageLivePositionAsync(state, runtime, update, token);
            }
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
        var oppositeCloseScheduled = unresolved && BrokerSide(unresolvedExecution!.Side) != signal.Direction
            && await ScheduleOppositeCloseAsync(database, state, runtime, unresolvedExecution, signal, token);
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
            Reason = oppositeCloseScheduled ? "OppositeSignalCloseScheduled: the existing exact automated position will be reconciled and closed only on a later executable quote." : unresolved ? ExposureReason(signal.Direction, unresolvedExecution!) : null, CreatedAtUtc = DateTimeOffset.UtcNow
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

    private async Task ManageLivePositionAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IDemoExecutionService>();
        // Resolve the current durable entry first.  A session/symbol can legitimately
        // have historical closed or restart-suspended rows, so it is never a key for
        // management ownership by itself.
        var potential = await database.DemoStrategyIntents.Include(item => item.DemoExecution)
            .Where(item => item.DemoStrategySessionId == state.Session.Id && item.DemoStrategySessionSymbolId == runtime.Symbol.Id && item.DemoExecutionId != null
                && (item.DemoExecution!.State == DemoExecutionState.Open || item.DemoExecution.State == DemoExecutionState.PreflightPassed || item.DemoExecution.State == DemoExecutionState.Submitting || item.DemoExecution.State == DemoExecutionState.BrokerAccepted || item.DemoExecution.State == DemoExecutionState.PartiallyFilled || item.DemoExecution.State == DemoExecutionState.CloseRequested || item.DemoExecution.State == DemoExecutionState.ReconciliationRequired))
            .ToListAsync(token);
        if (potential.Count != 1)
        {
            // A broker reconciliation can close the execution between B2 updates.
            // Closed rows are not candidates for new management, but any existing
            // exact management row still needs a terminal, read-only synchronization.
            if (potential.Count == 0)
            {
                var closed = await database.DemoStrategyPositionManagement.Include(item => item.DemoExecution)
                    .Where(item => item.DemoStrategySessionId == state.Session.Id && item.DemoStrategySessionSymbolId == runtime.Symbol.Id && item.State != DemoStrategyPositionManagementState.Closed && item.DemoExecution!.State == DemoExecutionState.Closed)
                    .ToListAsync(token);
                foreach (var item in closed)
                {
                    item.State = DemoStrategyPositionManagementState.Closed;
                    item.OppositeCloseState = DemoStrategyOppositeCloseState.Closed;
                    item.LastManagedAtUtc = DateTimeOffset.UtcNow;
                    item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    item.LastReason = "The exact linked execution was already reconciled Closed; no B2 write was attempted.";
                }
                if (closed.Count > 0) await database.SaveChangesAsync(token);
            }
            if (potential.Count > 1)
            {
                foreach (var item in potential) { item.Reason = "B2 management is fail-closed: more than one current linked unresolved execution exists for this session symbol."; item.UpdatedAtUtc = DateTimeOffset.UtcNow; }
                await database.SaveChangesAsync(token);
            }
            return;
        }
        var intent = potential[0];
        var execution = intent.DemoExecution!;
        if (!string.Equals(execution.BrokerSymbol, runtime.Symbol.BrokerSymbol, StringComparison.Ordinal))
        {
            intent.Reason = "B2 management is fail-closed: the exact linked execution broker symbol does not match the current session symbol."; intent.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token); return;
        }
        var management = await database.DemoStrategyPositionManagement
            .Include(item => item.DemoExecution).Include(item => item.DemoStrategyIntent)
            .SingleOrDefaultAsync(item => item.DemoExecutionId == execution.Id, token);
        if (management is null)
        {
            if (execution.State != DemoExecutionState.Open) return;
            await service.ReconcileAsync(execution.ClientExecutionId, token);
            execution = await database.DemoExecutions.SingleAsync(item => item.Id == intent.DemoExecutionId, token);
            if (execution.State != DemoExecutionState.Open || execution.AverageFillPrice is not > 0m || execution.RequestedStopLoss is not > 0m || execution.RequestedTakeProfit is not > 0m || execution.PositionTicket is not > 0 || execution.PositionIdentifier is not > 0) return;
            management = new DemoStrategyPositionManagement
            {
                DemoStrategySessionId = state.Session.Id, DemoStrategySessionSymbolId = runtime.Symbol.Id, DemoStrategyIntentId = intent.Id, DemoExecutionId = execution.Id,
                State = DemoStrategyPositionManagementState.Active, OriginalEntryPrice = execution.AverageFillPrice.Value, OriginalStopLoss = execution.RequestedStopLoss.Value, OriginalTakeProfit = execution.RequestedTakeProfit.Value,
                TakeProfitExtensionState = DemoStrategyTargetExtensionState.NotAttempted, OppositeCloseState = DemoStrategyOppositeCloseState.None,
                CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, LastReason = "Attached after exact broker reconciliation proved the current-session automated execution Open."
            };
            database.DemoStrategyPositionManagement.Add(management); await database.SaveChangesAsync(token);
            management.DemoExecution = execution;
        }
        if (management.State is DemoStrategyPositionManagementState.Closed or DemoStrategyPositionManagementState.SuspendedAfterRestart or DemoStrategyPositionManagementState.Blocked) return;
        var executionForManagement = management.DemoExecution ?? await database.DemoExecutions.SingleAsync(item => item.Id == management.DemoExecutionId, token);
        var reconciled = await service.ReconcileAsync(executionForManagement.ClientExecutionId, token);
        if (reconciled?.State == DemoExecutionState.Closed)
        {
            management.State = DemoStrategyPositionManagementState.Closed; management.OppositeCloseState = DemoStrategyOppositeCloseState.Closed; management.LastManagedAtUtc = DateTimeOffset.UtcNow; management.LastReason = "Exact execution reconciliation proved the broker position closed."; management.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token); return;
        }
        if (reconciled?.State != DemoExecutionState.Open) return;
        executionForManagement = reconciled;

        if (management.OppositeCloseState == DemoStrategyOppositeCloseState.Pending)
        {
            var closeQuote = DemoStrategyManagementPlanner.ExecutableManagementPrice(BrokerSide(executionForManagement.Side), update.Bid, update.Ask);
            if (closeQuote is null || !ManagementWritesEnabled()) return;
            var readiness = await service.ReadinessAsync(token); if (!readiness.Ready) return;
            management.OppositeCloseState = DemoStrategyOppositeCloseState.CloseRequested; management.State = DemoStrategyPositionManagementState.CloseRequested; management.OppositeCloseRequestedAtUtc = DateTimeOffset.UtcNow; management.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token); // durable before the one exact close write.
            var close = await service.CloseAsync(executionForManagement.ClientExecutionId, token);
            if (close?.State == DemoExecutionState.Closed) { management.State = DemoStrategyPositionManagementState.Closed; management.OppositeCloseState = DemoStrategyOppositeCloseState.Closed; }
            // Close ambiguity is distinct from a protection ambiguity.  Keep the
            // close-specific overall state so later updates reconcile only and never
            // submit a second close or a new protection write.
            else { management.State = DemoStrategyPositionManagementState.CloseRequested; management.OppositeCloseState = DemoStrategyOppositeCloseState.ReconciliationRequired; }
            management.LastManagedAtUtc = DateTimeOffset.UtcNow; management.UpdatedAtUtc = DateTimeOffset.UtcNow; management.LastReason = "Opposite close was delegated once to DemoExecutionService; no automatic retry is permitted.";
            await database.SaveChangesAsync(token); return;
        }
        if (management.OppositeCloseState is DemoStrategyOppositeCloseState.CloseRequested or DemoStrategyOppositeCloseState.ReconciliationRequired) return;

        if (management.PendingProtectionActionId is { } pending)
        {
            var action = await service.ReconcileManagementActionAsync(pending, token);
            if (action?.State is DemoExecutionManagementActionState.Applied or DemoExecutionManagementActionState.Rejected) ApplyProtectionOutcome(management, action, true);
            else { management.State = DemoStrategyPositionManagementState.ProtectionReconciliationRequired; management.LastReason = "A previous protection action remains ambiguous; no new action was created."; management.UpdatedAtUtc = DateTimeOffset.UtcNow; }
            await database.SaveChangesAsync(token); return;
        }
        var direction = BrokerSide(executionForManagement.Side);
        var quote = DemoStrategyManagementPlanner.ExecutableManagementPrice(direction, update.Bid, update.Ask);
        if (quote is null) return;
        // Gate-off quotes are intentionally not actionable progress.  They cannot be
        // replayed later into a trailing/extension objective when the kill switch opens.
        if (!ManagementWritesEnabled()) { management.LastManagedAtUtc = DateTimeOffset.UtcNow; management.UpdatedAtUtc = DateTimeOffset.UtcNow; management.LastReason = "B2 management is disabled; executable quote was observed but not retained as actionable progress."; await database.SaveChangesAsync(token); return; }
        management.BestFavorablePrice = DemoStrategyManagementPlanner.NextBest(direction, management.BestFavorablePrice, quote.Value);
        management.BestFavorableProgressPercent = Math.Max(management.BestFavorableProgressPercent, DemoStrategyManagementPlanner.Progress(management.OriginalEntryPrice, management.OriginalTakeProfit, management.BestFavorablePrice.Value, direction));
        management.LastManagedAtUtc = DateTimeOffset.UtcNow; management.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var progress = management.BestFavorableProgressPercent;
        var lockPercent = state.Session.TrailingStopEnabled ? TradeMath.LockPercent(progress) : 0m;
        var extend = state.Session.TrailingStopEnabled && progress >= 70m && management.TakeProfitExtensionState == DemoStrategyTargetExtensionState.NotAttempted;
        if (lockPercent <= management.HighestAttemptedLockPercent && !extend) { await database.SaveChangesAsync(token); return; }
        var instrument = await catalog.GetAsync(runtime.Symbol.BrokerSymbol, token);
        if (instrument is null || !instrument.IsSelected || !string.Equals(instrument.Spec.BrokerSymbol, runtime.Symbol.BrokerSymbol, StringComparison.Ordinal)) { management.LastReason = "No exact selected instrument grid is available; no protection write was attempted."; await database.SaveChangesAsync(token); return; }
        var desiredStop = lockPercent > management.HighestAttemptedLockPercent ? DemoStrategyManagementPlanner.Align(TradeMath.TrailingStop(management.OriginalEntryPrice, management.OriginalTakeProfit, direction, lockPercent), direction, instrument.Spec) : executionForManagement.CurrentStopLoss;
        var desiredTarget = extend ? DemoStrategyManagementPlanner.Align(TradeMath.ExtendedTarget(management.OriginalEntryPrice, management.OriginalTakeProfit, direction), direction, instrument.Spec) : executionForManagement.CurrentTakeProfit;
        if (desiredStop is not > 0m || desiredTarget is not > 0m) { management.LastReason = "No reliable generated/native protection pair is available; no protection write was attempted."; await database.SaveChangesAsync(token); return; }
        var readinessForWrite = await service.ReadinessAsync(token); if (!readinessForWrite.Ready) { management.LastReason = "DemoExecution readiness is not ready; no protection write was attempted."; await database.SaveChangesAsync(token); return; }
        var actionId = Guid.NewGuid(); management.PendingProtectionActionId = actionId; management.PendingProtectionLockPercent = lockPercent > management.HighestAttemptedLockPercent ? lockPercent : null; management.PendingProtectionExtendsTarget = extend; management.PendingDesiredStopLoss = desiredStop; management.PendingDesiredTakeProfit = desiredTarget;
        if (lockPercent > management.HighestAttemptedLockPercent) management.HighestAttemptedLockPercent = lockPercent;
        if (extend) management.TakeProfitExtensionState = DemoStrategyTargetExtensionState.Pending;
        management.UpdatedAtUtc = DateTimeOffset.UtcNow; management.LastReason = "A single final SL/TP protection action was persisted before delegation to B1.";
        await database.SaveChangesAsync(token);
        var result = await service.ModifyProtectionAsync(new ModifyDemoProtection(actionId, executionForManagement.ClientExecutionId, desiredStop, desiredTarget), token);
        ApplyProtectionOutcome(management, result, false); await database.SaveChangesAsync(token);
    }

    // Resume recovery is intentionally limited to the existing reconciliation APIs.
    // It never reconstructs a baseline, replays downtime prices, or delegates a write.
    private async Task RecoverManagementAfterResumeAsync(EmaBotDbContext database, DemoStrategySession session, IDemoExecutionService service, CancellationToken token)
    {
        var managementRows = await database.DemoStrategyPositionManagement
            .Include(item => item.DemoStrategyIntent).Include(item => item.DemoExecution).Include(item => item.DemoStrategySessionSymbol)
            .Where(item => item.DemoStrategySessionId == session.Id && item.State != DemoStrategyPositionManagementState.Closed)
            .ToListAsync(token);
        foreach (var management in managementRows)
        {
            if (!RecoveryOwnershipIsExact(management, session))
            {
                SuspendRecovery(management, "Resume recovery could not prove the durable session, symbol, intent, and execution linkage.");
                continue;
            }

            var execution = await service.ReconcileAsync(management.DemoExecution!.ClientExecutionId, token);
            if (execution?.State == DemoExecutionState.Closed)
            {
                management.State = DemoStrategyPositionManagementState.Closed;
                management.OppositeCloseState = DemoStrategyOppositeCloseState.Closed;
                management.LastReason = "Resume recovery exact reconciliation proved the linked broker position Closed.";
                management.LastManagedAtUtc = DateTimeOffset.UtcNow;
                management.UpdatedAtUtc = DateTimeOffset.UtcNow;
                continue;
            }

            var candidates = await database.DemoStrategyIntents.Include(item => item.DemoExecution)
                .Where(item => item.DemoStrategySessionId == session.Id && item.DemoStrategySessionSymbolId == management.DemoStrategySessionSymbolId && item.DemoExecutionId != null
                    && (item.DemoExecution!.State == DemoExecutionState.Open || item.DemoExecution.State == DemoExecutionState.PreflightPassed || item.DemoExecution.State == DemoExecutionState.Submitting || item.DemoExecution.State == DemoExecutionState.BrokerAccepted || item.DemoExecution.State == DemoExecutionState.PartiallyFilled || item.DemoExecution.State == DemoExecutionState.CloseRequested || item.DemoExecution.State == DemoExecutionState.ReconciliationRequired))
                .ToListAsync(token);
            if (candidates.Count != 1 || candidates[0].DemoExecutionId != management.DemoExecutionId)
            {
                SuspendRecovery(management, "Resume recovery found zero or multiple plausible current executions for the durable session symbol.");
                continue;
            }

            if (execution?.State != DemoExecutionState.Open || execution.PositionTicket is not > 0 || execution.PositionIdentifier is not > 0 || execution.CurrentStopLoss is not > 0m || execution.CurrentTakeProfit is not > 0m)
            {
                SuspendRecovery(management, "Resume recovery could not prove an exact open native position with broker-derived SL and TP.");
                continue;
            }

            if (management.PendingProtectionActionId is { } actionId)
            {
                var action = await service.ReconcileManagementActionAsync(actionId, token);
                if (action?.State is DemoExecutionManagementActionState.Applied or DemoExecutionManagementActionState.Rejected)
                    ApplyProtectionOutcome(management, action, true);
                else
                {
                    management.State = DemoStrategyPositionManagementState.ProtectionReconciliationRequired;
                    management.LastReason = "Resume recovery could not conclusively reconcile the persisted B1 protection action; no replacement action was created.";
                    management.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    continue;
                }
            }

            if (management.OppositeCloseState == DemoStrategyOppositeCloseState.Pending)
            {
                management.State = DemoStrategyPositionManagementState.ClosePending;
                management.LastReason = "Resume recovery preserved the durable pending opposite-close directive; a later new executable quote may delegate it once.";
            }
            else if (management.OppositeCloseState is DemoStrategyOppositeCloseState.CloseRequested or DemoStrategyOppositeCloseState.ReconciliationRequired)
            {
                management.State = DemoStrategyPositionManagementState.CloseRequested;
                management.LastReason = "Resume recovery preserved a previously attempted opposite close; reconciliation only, never automatic resubmission.";
            }
            else
            {
                management.State = DemoStrategyPositionManagementState.Active;
                management.LastReason = "Resume recovery proved exact native ownership and broker-derived protection; durable management progress was preserved.";
            }
            management.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        await database.SaveChangesAsync(token);
    }

    private static bool RecoveryOwnershipIsExact(DemoStrategyPositionManagement management, DemoStrategySession session) =>
        management.DemoStrategySessionId == session.Id
        && management.DemoStrategySessionSymbol?.DemoStrategySessionId == session.Id
        && management.DemoStrategyIntent?.DemoStrategySessionId == session.Id
        && management.DemoStrategyIntent?.DemoStrategySessionSymbolId == management.DemoStrategySessionSymbolId
        && management.DemoStrategyIntent?.DemoExecutionId == management.DemoExecutionId
        && management.DemoExecution is not null
        && string.Equals(management.DemoExecution.BrokerSymbol, management.DemoStrategySessionSymbol.BrokerSymbol, StringComparison.Ordinal);

    private static void SuspendRecovery(DemoStrategyPositionManagement management, string reason)
    {
        management.State = DemoStrategyPositionManagementState.SuspendedAfterRestart;
        management.LastReason = reason;
        management.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ApplyProtectionOutcome(DemoStrategyPositionManagement management, DemoExecutionManagementAction action, bool reconciled)
    {
        if (action.State == DemoExecutionManagementActionState.Applied)
        {
            if (management.PendingProtectionLockPercent is { } lockPercent) management.HighestAppliedLockPercent = Math.Max(management.HighestAppliedLockPercent, lockPercent);
            if (management.PendingProtectionExtendsTarget) { management.TakeProfitExtensionState = DemoStrategyTargetExtensionState.Applied; management.TargetExtensionAppliedAtUtc = DateTimeOffset.UtcNow; }
            management.PendingProtectionActionId = null; management.PendingProtectionLockPercent = null; management.PendingProtectionExtendsTarget = false; management.PendingDesiredStopLoss = null; management.PendingDesiredTakeProfit = null; management.State = DemoStrategyPositionManagementState.Active; management.LastReason = reconciled ? "B1 reconciliation proved the prior protection action applied." : "B1 applied the protection action.";
        }
        else if (action.State == DemoExecutionManagementActionState.Rejected)
        {
            if (management.PendingProtectionExtendsTarget) management.TakeProfitExtensionState = DemoStrategyTargetExtensionState.Rejected;
            management.PendingProtectionActionId = null; management.PendingProtectionLockPercent = null; management.PendingProtectionExtendsTarget = false; management.PendingDesiredStopLoss = null; management.PendingDesiredTakeProfit = null; management.State = DemoStrategyPositionManagementState.Active; management.LastReason = "B1 rejected the protection objective; that objective will not be retried.";
        }
        else { management.State = DemoStrategyPositionManagementState.ProtectionReconciliationRequired; management.LastReason = "B1 protection result is ambiguous; no new objective may be submitted."; }
        management.LastManagedAtUtc = DateTimeOffset.UtcNow; management.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<bool> ScheduleOppositeCloseAsync(EmaBotDbContext database, RuntimeSession state, RuntimeSymbol runtime, DemoExecution execution, StrategyEvent signal, CancellationToken token)
    {
        var management = await database.DemoStrategyPositionManagement.SingleOrDefaultAsync(item => item.DemoStrategySessionId == state.Session.Id && item.DemoStrategySessionSymbolId == runtime.Symbol.Id && item.DemoExecutionId == execution.Id, token);
        if (management is null || management.State != DemoStrategyPositionManagementState.Active) return false;
        if (!state.Session.ExitOnOppositeCrossover || !ManagementWritesEnabled()) { management.LastReason = "OppositeSignalDeferred: automatic opposite close is disabled by the session snapshot or management gate."; management.UpdatedAtUtc = DateTimeOffset.UtcNow; await database.SaveChangesAsync(token); return false; }
        if (management.OppositeCloseState != DemoStrategyOppositeCloseState.None) return management.OppositeCloseState == DemoStrategyOppositeCloseState.Pending;
        management.OppositeSignalTimeUtc = signal.Time; management.OppositeSignalDirection = signal.Direction; management.OppositeCloseState = DemoStrategyOppositeCloseState.Pending; management.State = DemoStrategyPositionManagementState.ClosePending; management.LastReason = "OppositeSignalCloseScheduled"; management.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(token); return true;
    }

    private bool ManagementWritesEnabled() => automation.Enabled && automation.ManagementEnabled;

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
