using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public sealed record PaperRuntimeSnapshot(int SessionId, string ConnectionState, DateTimeOffset? LastUpdateUtc, IReadOnlyDictionary<string, PaperSymbolRuntimeSnapshot> Symbols);
public sealed record PaperDecisionRuntimeEvent(DateTimeOffset TimeUtc, DateTimeOffset? CandleCloseTimeUtc, string Stage, SignalDirection? Direction, string Message, decimal? Ema9 = null, decimal? Ema15 = null, decimal? Ema100 = null, decimal? GapPercent = null, GapState? GapState = null, decimal? StopPrice = null, StopSourceType? StopSource = null, DateTimeOffset? ExpectedEntryOpenUtc = null, decimal? Bid = null, decimal? Ask = null, decimal? EntryPrice = null, decimal? Lots = null, decimal? RequiredMargin = null);
public sealed record PaperPendingEntryRuntimeSnapshot(SignalDirection Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, DateTimeOffset ExpectedEntryOpenUtc, decimal StopPrice, StopSourceType StopSource, DateTimeOffset StopSourceTimeUtc, IndicatorSnapshot Snapshot, bool IsReentry);
public sealed record PaperSymbolRuntimeSnapshot(decimal? LatestPrice, decimal? LatestBid, decimal? LatestAsk, DateTimeOffset? LastMarketEventUtc, DateTimeOffset? LastClosedCandleUtc, IndicatorSnapshot? Indicator, PaperPendingEntryRuntimeSnapshot? PendingEntry, PaperTrade? OpenTrade, SignalDirection? TrendRegimeDirection, DateTimeOffset? TrendRegimeCrossoverTimeUtc, bool ReentryEligible, bool ReentryConsumed, PaperDecisionRuntimeEvent? LastDecision, IReadOnlyList<PaperDecisionRuntimeEvent> RecentDecisions)
{
    public SignalDirection? PendingDirection => PendingEntry?.Direction;
}

public sealed class PaperTradingCoordinator(
    IServiceScopeFactory scopeFactory,
    IHistoricalMarketDataProviderResolver historicalProviders,
    IMarketBarStreamProvider stream,
    EmaSignalEngine strategy,
    ILogger<PaperTradingCoordinator> logger,
    EmaBot.Api.Mt5Bridge.IMt5TradeCalculator? calculator = null) : IHostedService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private RuntimeSession? active;

    // DatabaseInitializer runs first and handles Running -> Interrupted after migrations complete.
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        RuntimeSession? state;
        await gate.WaitAsync(cancellationToken);
        try { state = active; active = null; state?.Cancellation.Cancel(); }
        finally { gate.Release(); }
        if (state?.Worker is not null) await ObserveWorkerAsync(state.Worker);
        state?.Cancellation.Dispose();
    }

    public async Task StartSessionAsync(int sessionId, bool resume, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (active is not null) throw new InvalidOperationException("A paper session is already active.");
            await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var session = await database.PaperSessions.Include(item => item.Symbols).Include(item => item.Trades).SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken) ?? throw new KeyNotFoundException("Paper session not found.");
            if (resume && session.Status != PaperSessionStatus.Interrupted) throw new InvalidOperationException("Only interrupted paper sessions can be resumed.");
            if (!resume && session.Status != PaperSessionStatus.Running) throw new InvalidOperationException("Only a newly running session can be started.");
            // Pending entries are tied to the process that observed their signal.  On resume they
            // must never be recreated, while the persisted regime state remains intact.
            if (resume)
            {
                foreach (var symbol in session.Symbols) ClearPending(symbol);
                await database.SaveChangesAsync(cancellationToken);
            }
            var proposed = new RuntimeSession(session, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            foreach (var symbol in session.Symbols) proposed.Symbols[symbol.Symbol] = new RuntimeSymbol(symbol, session.Trades.SingleOrDefault(trade => trade.PaperSessionSymbolId == symbol.Id && trade.Status == PaperTradeStatus.Open));
            try { await WarmupAsync(proposed, cancellationToken); }
            catch
            {
                proposed.Cancellation.Cancel(); proposed.Cancellation.Dispose();
                session.Status = resume ? PaperSessionStatus.Interrupted : PaperSessionStatus.Faulted;
                session.FailureMessage = resume ? "Warmup data is unavailable; resume can be retried." : "Historical warmup data is unavailable.";
                if (resume) session.InterruptedAtUtc = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                throw;
            }
            session.Status = PaperSessionStatus.Running; session.InterruptedAtUtc = null; session.FailureMessage = null;
            await database.SaveChangesAsync(cancellationToken);
            active = proposed;
            proposed.Worker = Task.Run(() => RunStreamAsync(proposed), CancellationToken.None);
            logger.LogInformation("Paper session {SessionId} started for {SymbolCount} symbols.", session.Id, session.Symbols.Count);
        }
        finally { gate.Release(); }
    }

    public async Task StopSessionAsync(int sessionId, CancellationToken cancellationToken)
    {
        RuntimeSession? state = null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (active is not null)
            {
                if (active.Session.Id != sessionId) throw new InvalidOperationException("A different paper session is running in this application.");
                if (active.Symbols.Values.Any(symbol => symbol.OpenTrade is not null && (active.Session.MarketDataSource == MarketDataSource.Mt5Exness ? ExitExecutablePrice(symbol.OpenTrade!.Direction, symbol.LatestBid, symbol.LatestAsk) is null : !symbol.LatestPrice.HasValue))) throw new InvalidOperationException("An open paper position has no reliable executable market price and cannot be stopped safely.");
                state = active; state.AcceptSignals = false; state.Cancellation.Cancel(); active = null;
            }
            else
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
                var session = await database.PaperSessions.Include(item => item.Trades).SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken) ?? throw new KeyNotFoundException("Paper session not found.");
                if (session.Status != PaperSessionStatus.Interrupted) throw new InvalidOperationException($"Paper session is {session.Status} and cannot be ended without an active runtime.");
                if (session.Trades.Any(trade => trade.Status == PaperTradeStatus.Open))
                {
                    if (session.MarketDataSource == MarketDataSource.LegacyBinance) throw new InvalidOperationException("This interrupted Legacy Binance Paper session contains an open simulated position and cannot be ended without its original live runtime.");
                    throw new InvalidOperationException("This interrupted Paper session contains an open simulated position. Resume the session while MT5 is connected, then stop it so the position can be closed using an executable Bid/Ask quote.");
                }
                session.Status = PaperSessionStatus.Stopped;
                session.StoppedAtUtc = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        finally { gate.Release(); }
        if (state is not null) await StopAfterWorkerAsync(state, cancellationToken);
        logger.LogInformation("Paper session {SessionId} stopped.", sessionId);
    }

    public PaperRuntimeSnapshot? GetRuntimeSnapshot()
    {
        var state = active;
        return state is null ? null : new PaperRuntimeSnapshot(state.Session.Id, state.ConnectionState, state.LastUpdateUtc, state.Symbols.ToDictionary(pair => pair.Key, pair => new PaperSymbolRuntimeSnapshot(pair.Value.LatestPrice, pair.Value.LatestBid, pair.Value.LatestAsk, pair.Value.LastMarketEventUtc, pair.Value.LastClosedCandleUtc, pair.Value.Indicator, PendingSnapshot(pair.Value.Pending), pair.Value.OpenTrade, pair.Value.TrendRegimeDirection, pair.Value.TrendRegimeCrossoverTimeUtc, pair.Value.ReentryEligible, pair.Value.ReentryConsumed, pair.Value.RecentDecisions.LastOrDefault(), pair.Value.RecentDecisions.AsEnumerable().Reverse().ToArray())));
    }

    private async Task WarmupAsync(RuntimeSession state, CancellationToken token)
    {
        var historical = historicalProviders.Resolve(state.Session.MarketDataSource);
        foreach (var runtime in state.Symbols.Values)
        {
            var candles = await historical.GetLatestAsync(runtime.Symbol.Symbol, state.Session.Interval, 200, token);
            runtime.Candles.AddRange(candles.Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).TakeLast(200));
            runtime.Indicator = strategy.Evaluate(runtime.Candles, Settings(state.Session)).Snapshots.LastOrDefault();
            AddDecision(runtime, new PaperDecisionRuntimeEvent(DateTimeOffset.UtcNow, null, "Warmup", null, "Warmup complete. Waiting for the next live closed candle.", runtime.Indicator?.Ema9, runtime.Indicator?.Ema15, runtime.Indicator?.Ema100, runtime.Indicator?.GapPercent, runtime.Indicator?.GapState));
        }
    }

    private async Task ResyncCandlesAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate current, CancellationToken token)
    {
        var historical = historicalProviders.Resolve(state.Session.MarketDataSource);
        var candles = await historical.GetLatestAsync(runtime.Symbol.Symbol, state.Session.Interval, 200, token);
        runtime.Candles.Clear();
        runtime.Candles.AddRange(candles.Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).TakeLast(200));
        // The current close remains the only potentially actionable candle after the resync.
        runtime.Candles.RemoveAll(candle => candle.OpenTimeUtc == current.OpenTimeUtc);
        runtime.Candles.Add(new Candle(current.OpenTimeUtc, current.CloseTimeUtc, current.Open, current.High, current.Low, current.Close, current.Volume, true));
        runtime.Candles.Sort((left, right) => left.OpenTimeUtc.CompareTo(right.OpenTimeUtc));
        if (runtime.Candles.Count > 200) runtime.Candles.RemoveRange(0, runtime.Candles.Count - 200);
        logger.LogInformation("Resynchronized closed candles for {Symbol} after a stream gap.", runtime.Symbol.Symbol);
    }

    private async Task StopAfterWorkerAsync(RuntimeSession state, CancellationToken token)
    {
        if (state.Worker is not null) await ObserveWorkerAsync(state.Worker);
        foreach (var symbol in state.Symbols.Values.Where(symbol => symbol.OpenTrade is not null))
        {
            var price = state.Session.MarketDataSource == MarketDataSource.Mt5Exness
                ? ExitExecutablePrice(symbol.OpenTrade!.Direction, symbol.LatestBid, symbol.LatestAsk) ?? throw new InvalidOperationException("An open paper position has no reliable executable Bid/Ask quote and cannot be stopped safely.")
                : symbol.LatestPrice!.Value;
            await CloseTradeAsync(state, symbol, price, PaperExitReason.SessionStopped, DateTimeOffset.UtcNow, token);
        }
        await UpdateSessionAsync(state.Session.Id, session => { session.Status = PaperSessionStatus.Stopped; session.StoppedAtUtc = DateTimeOffset.UtcNow; });
        state.Cancellation.Dispose();
    }

    private static async Task ObserveWorkerAsync(Task worker)
    {
        try { await worker; }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task RunStreamAsync(RuntimeSession state)
    {
        try
        {
            await stream.StreamAsync(state.Symbols.Keys.ToArray(), state.Session.Interval, async (update, token) => await ProcessUpdateAsync(state, update, token), value => state.ConnectionState = value, state.Cancellation.Token);
        }
        catch (Exception exception) when (!state.Cancellation.IsCancellationRequested)
        {
            logger.LogError(exception, "Paper session {SessionId} stream faulted.", state.Session.Id);
            await UpdateSessionAsync(state.Session.Id, session => { session.Status = PaperSessionStatus.Faulted; session.FailureMessage = "Live market-bar streaming could not be maintained."; });
            state.ConnectionState = "Degraded";
            await gate.WaitAsync();
            try { if (ReferenceEquals(active, state)) active = null; }
            finally { gate.Release(); }
            state.Cancellation.Dispose();
        }
    }

    internal async Task ProcessUpdateForTestAsync(MarketBarUpdate update, CancellationToken token = default)
    {
        var state = active ?? throw new InvalidOperationException("No active paper session.");
        await ProcessUpdateAsync(state, update, token);
    }

    private async Task ProcessUpdateAsync(RuntimeSession state, MarketBarUpdate update, CancellationToken token)
    {
        if (!state.Symbols.TryGetValue(update.Symbol, out var runtime) || update.Timeframe != state.Session.Interval) return;
        await gate.WaitAsync(token);
        try
        {
            runtime.LatestPrice = update.Close; runtime.LatestBid = update.Bid; runtime.LatestAsk = update.Ask; runtime.LastMarketEventUtc = update.EventTimeUtc; state.LastUpdateUtc = update.EventTimeUtc;
            var isMt5Paper = state.Session.MarketDataSource == MarketDataSource.Mt5Exness;
            if (runtime.Pending is not null && runtime.OpenTrade is null && state.AcceptSignals && (!isMt5Paper || (!update.IsClosed && update.Bid is > 0 && update.Ask is > 0)))
            {
                var expectedOpen = runtime.Pending.SignalTimeUtc.AddMilliseconds(1);
                if (update.OpenTimeUtc == expectedOpen) await EnterPendingAsync(state, runtime, update, token);
                else if (update.OpenTimeUtc > expectedOpen) { AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "EntryExpired", runtime.Pending.Direction, "The pending entry expired because the exact next-bar entry window was missed.")); runtime.Pending = null; await PersistRuntimeSymbolAsync(runtime, token); logger.LogInformation("Expired stale pending paper entry for {Symbol}.", runtime.Symbol.Symbol); }
            }
            if (runtime.OpenTrade is not null && (!isMt5Paper || (!update.IsClosed && update.Bid is > 0 && update.Ask is > 0))) await ManageOpenTradeAsync(state, runtime, update, token);
            if (!update.IsClosed || runtime.LastClosedCandleUtc == update.CloseTimeUtc) return;
            if (runtime.LastClosedCandleUtc is { } previousClose && update.OpenTimeUtc > previousClose.AddMilliseconds(1)) await ResyncCandlesAsync(state, runtime, update, token);
            runtime.LastClosedCandleUtc = update.CloseTimeUtc;
            runtime.Candles.RemoveAll(candle => candle.OpenTimeUtc == update.OpenTimeUtc);
            runtime.Candles.Add(new Candle(update.OpenTimeUtc, update.CloseTimeUtc, update.Open, update.High, update.Low, update.Close, update.Volume, true));
            if (runtime.Candles.Count > 200) runtime.Candles.RemoveRange(0, runtime.Candles.Count - 200);
            var evaluation = strategy.Evaluate(runtime.Candles, Settings(state.Session)); runtime.Indicator = evaluation.Snapshots.LastOrDefault();
            var events = evaluation.Events.Where(item => item.Time == update.CloseTimeUtc).ToArray();
            foreach (var strategyEvent in events) AddStrategyDecision(runtime, strategyEvent);
            foreach (var crossover in events.Where(item => item.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover)) { runtime.TrendRegimeDirection = crossover.Direction; runtime.TrendRegimeCrossoverTimeUtc = crossover.Time; runtime.ReentryEligible = false; runtime.ReentryConsumed = false; }
            await RecordClosedCandleAsync(state.Session.Id, runtime, events, token);
            if (!state.AcceptSignals) return;
            foreach (var signal in events.Where(item => item.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal)) await ScheduleSignalAsync(state, runtime, evaluation.Events, signal, token);
            if (runtime.OpenTrade is null && runtime.Pending is null && runtime.ReentryEligible && !runtime.ReentryConsumed && runtime.TrendRegimeDirection is { } regime && runtime.TrendRegimeCrossoverTimeUtc is { } regimeTime && runtime.Indicator is { } snapshot && IsContinuation(snapshot, regime, Settings(state.Session))) await ScheduleReentryAsync(state, runtime, snapshot, regime, regimeTime, token);
        }
        finally { gate.Release(); }
    }

    private async Task ScheduleSignalAsync(RuntimeSession state, RuntimeSymbol runtime, IReadOnlyList<StrategyEvent> events, StrategyEvent signal, CancellationToken token)
    {
        if (runtime.OpenTrade is not null || runtime.Pending is not null) { AddDecision(runtime, new PaperDecisionRuntimeEvent(signal.Time, signal.Time, "SkippedWhileOpen", signal.Direction, "Valid signal skipped because a position or pending entry already exists.")); await UpdateSessionAsync(state.Session.Id, session => session.SkippedWhilePositionOpen++); return; }
        var crossover = events.LastOrDefault(item => item.Time <= signal.Time && item.Direction == signal.Direction && (item.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover));
        var index = runtime.Candles.FindIndex(candle => candle.CloseTimeUtc == crossover?.Time); if (index < 1) return;
        var stop = SwingStopRules.Find(runtime.Candles, index, signal.Direction); runtime.Pending = new PendingEntry(signal.Direction, crossover!.Time, signal.Time, stop.Price, stop.Source, stop.Time, signal.Snapshot, false, null);
        AddDecision(runtime, PendingDecision(runtime.Pending));
        await PersistRuntimeSymbolAsync(runtime, token);
        logger.LogInformation("Paper signal {Direction} scheduled for {Symbol}.", signal.Direction, runtime.Symbol.Symbol);
    }

    private async Task ScheduleReentryAsync(RuntimeSession state, RuntimeSymbol runtime, IndicatorSnapshot snapshot, SignalDirection direction, DateTimeOffset regimeTime, CancellationToken token)
    {
        var index = runtime.Candles.FindIndex(candle => candle.CloseTimeUtc == snapshot.Time); if (index < 1) return;
        var stop = SwingStopRules.Find(runtime.Candles, index, direction);
        runtime.ReentryConsumed = true;
        runtime.ReentryEligible = false;
        runtime.Pending = new PendingEntry(direction, regimeTime, snapshot.Time, stop.Price, stop.Source, stop.Time, snapshot, true, regimeTime);
        AddDecision(runtime, new PaperDecisionRuntimeEvent(snapshot.Time, snapshot.Time, "ReentryConsumed", direction, "Re-entry eligibility consumed by a continuation setup.", snapshot.Ema9, snapshot.Ema15, snapshot.Ema100, snapshot.GapPercent, snapshot.GapState));
        AddDecision(runtime, new PaperDecisionRuntimeEvent(snapshot.Time, snapshot.Time, "ReentryScheduled", direction, "Re-entry signal scheduled for the next executable bar.", snapshot.Ema9, snapshot.Ema15, snapshot.Ema100, snapshot.GapPercent, snapshot.GapState, stop.Price, stop.Source, snapshot.Time.AddMilliseconds(1)));
        await PersistRuntimeSymbolAsync(runtime, token);
        logger.LogInformation("Paper re-entry {Direction} scheduled for {Symbol}.", direction, runtime.Symbol.Symbol);
    }

    private async Task EnterPendingAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        if (state.Session.MarketDataSource == MarketDataSource.Mt5Exness)
        {
            await EnterMt5PendingAsync(state, runtime, update, token);
            return;
        }

        var pending = runtime.Pending!; runtime.Pending = null;
        if ((pending.Direction == SignalDirection.Long && pending.Stop >= update.Open) || (pending.Direction == SignalDirection.Short && pending.Stop <= update.Open)) { await UpdateSessionAsync(state.Session.Id, session => session.InvalidStopLoss++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        if (state.Session.MaxStopDistancePercent > 0 && TradeMath.StopDistancePercent(update.Open, pending.Stop) > state.Session.MaxStopDistancePercent) { await UpdateSessionAsync(state.Session.Id, session => session.RejectedByStopDistance++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        var target = TradeMath.InitialTarget(update.Open, pending.Stop, pending.Direction, state.Session.RiskReward);
        var settings = Settings(state.Session); var size = TradeMath.CalculatePositionSize(settings, state.Session.CurrentBalanceUsdt, update.Open);
        if (state.Session.PositionSizingMode == PositionSizingMode.MarginPercent && size.MarginUsedUsdt > state.Session.CurrentBalanceUsdt - state.Session.UsedMarginUsdt) { await UpdateSessionAsync(state.Session.Id, session => session.RejectedByInsufficientMargin++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        if (TradeMath.ExpectedNetAtTarget(update.Open, target, size.Quantity, pending.Direction, state.Session.FeePercentPerSide) <= 0) { await UpdateSessionAsync(state.Session.Id, session => session.RejectedByFees++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        var quantity = size.Quantity;
        var trade = new PaperTrade { PaperSessionId = state.Session.Id, PaperSessionSymbolId = runtime.Symbol.Id, Symbol = runtime.Symbol.Symbol, Interval = state.Session.Interval, Status = PaperTradeStatus.Open, Direction = pending.Direction, CrossoverTimeUtc = pending.CrossoverTimeUtc, SignalTimeUtc = pending.SignalTimeUtc, EntryTimeUtc = update.OpenTimeUtc, EntryPrice = update.Open, Quantity = quantity, EntryNotionalUsdt = size.NotionalUsdt, PositionSizingMode = state.Session.PositionSizingMode, AccountEquityAtEntryUsdt = size.AccountEquityAtEntryUsdt, MarginUsedUsdt = size.MarginUsedUsdt, Leverage = size.Leverage, InitialStopLoss = pending.Stop, CurrentStopLoss = pending.Stop, StopSourceType = pending.StopSource, StopSourceTimeUtc = pending.StopTimeUtc, OriginalTakeProfit = target, CurrentTakeProfit = target, EntryFeeUsdt = TradeMath.Fee(update.Open, quantity, state.Session.FeePercentPerSide), TotalFeesUsdt = TradeMath.Fee(update.Open, quantity, state.Session.FeePercentPerSide), SignalOpen = pending.Snapshot.Open, SignalClose = pending.Snapshot.Close, SignalEma9 = pending.Snapshot.Ema9, SignalEma15 = pending.Snapshot.Ema15, SignalEma100 = pending.Snapshot.Ema100, SignalGapPercent = pending.Snapshot.GapPercent, SignalGapState = pending.Snapshot.GapState, IsReentry = pending.IsReentry, TrendRegimeCrossoverTimeUtc = pending.TrendRegimeCrossoverTimeUtc };
        runtime.OpenTrade = trade;
        await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); database.PaperTrades.Add(trade); database.PaperTradeEvents.Add(new PaperTradeEvent { PaperTrade = trade, TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.Entry, MarketPrice = update.Open }); await database.SaveChangesAsync(token);
        await PersistRuntimeSymbolAsync(runtime, token);
        if (state.Session.PositionSizingMode == PositionSizingMode.MarginPercent) { state.Session.UsedMarginUsdt += size.MarginUsedUsdt; await UpdateSessionAsync(state.Session.Id, session => session.UsedMarginUsdt += size.MarginUsedUsdt); }
        logger.LogInformation("Paper trade entered for {Symbol} at {Price}.", trade.Symbol, trade.EntryPrice);
    }

    private async Task EnterMt5PendingAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        var pending = runtime.Pending!;
        runtime.Pending = null;
        var entry = EntryExecutablePrice(pending.Direction, update.Bid, update.Ask);
        if (entry is null || runtime.Symbol.ContractSize is not > 0m || runtime.Symbol.CommissionPerLotPerSide is null)
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "MissingBrokerEconomics", pending.Direction, "Entry rejected because an executable quote or broker economics snapshot is unavailable.", Bid: update.Bid, Ask: update.Ask));
            await FaultSessionAsync(state, "MT5 Paper entry is missing an executable quote or broker economics snapshot.", token);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }

        if (!AllowsDirection(runtime.Symbol.TradeMode, pending.Direction))
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "TradeModeRejected", pending.Direction, $"{pending.Direction} setup rejected because the MT5 instrument trade mode does not allow this entry."));
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }

        if ((pending.Direction == SignalDirection.Long && pending.Stop >= entry.Value) || (pending.Direction == SignalDirection.Short && pending.Stop <= entry.Value))
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "InvalidStopLoss", pending.Direction, $"{pending.Direction} setup rejected: structural stop {pending.Stop} is invalid for executable entry {entry.Value}.", StopPrice: pending.Stop, EntryPrice: entry.Value));
            await UpdateSessionAsync(state.Session.Id, session => session.InvalidStopLoss++);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }
        if (state.Session.MaxStopDistancePercent > 0 && TradeMath.StopDistancePercent(entry.Value, pending.Stop) > state.Session.MaxStopDistancePercent)
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "StopDistanceRejected", pending.Direction, $"Setup rejected because stop distance {TradeMath.StopDistancePercent(entry.Value, pending.Stop):F4}% exceeds configured maximum {state.Session.MaxStopDistancePercent:F4}%.", StopPrice: pending.Stop, EntryPrice: entry.Value));
            await UpdateSessionAsync(state.Session.Id, session => session.RejectedByStopDistance++);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }

        var target = TradeMath.InitialTarget(entry.Value, pending.Stop, pending.Direction, state.Session.RiskReward);
        if (!StopsAreValid(runtime.Symbol, pending.Direction, entry.Value, pending.Stop, target))
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "BrokerStopsLevelRejected", pending.Direction, "Setup rejected because the broker stop-level requirements are not satisfied.", StopPrice: pending.Stop, EntryPrice: entry.Value));
            await UpdateSessionAsync(state.Session.Id, session => session.InvalidStopLoss++);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }

        Mt5PaperSize size;
        try { size = await SizeMt5PositionAsync(state, runtime.Symbol, pending.Direction, entry.Value, token); }
        catch (InvalidOperationException)
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "InsufficientMargin", pending.Direction, "Entry rejected because simulated free margin cannot support the requested broker volume.", EntryPrice: entry.Value));
            await UpdateSessionAsync(state.Session.Id, session => session.RejectedByInsufficientMargin++);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }
        catch (Exception)
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "MarginCalculationUnavailable", pending.Direction, "Entry rejected because MT5 margin calculation is unavailable.", EntryPrice: entry.Value));
            await FaultSessionAsync(state, "MT5 margin calculation is unavailable; no Paper trade was created.", token);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }

        var commission = 2m * size.Lots * runtime.Symbol.CommissionPerLotPerSide.Value;
        if (commission > 0m && (runtime.Symbol.TickSize is not > 0m || runtime.Symbol.TickValueProfit is not > 0m))
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "TradingCostRejected", pending.Direction, "Entry rejected because trading-cost validation requires broker tick economics.", EntryPrice: entry.Value, Lots: size.Lots));
            await UpdateSessionAsync(state.Session.Id, session => session.RejectedByTradingCosts++);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }
        try
        {
            var expected = await Calculator().CalculateProfitAsync(new EmaBot.Api.Mt5Bridge.Mt5CalculateProfitRequest(runtime.Symbol.BrokerSymbol ?? runtime.Symbol.Symbol, pending.Direction.ToString(), size.Lots, entry.Value, target), token);
            if (expected.Profit - commission <= 0m)
            {
                AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "TradingCostRejected", pending.Direction, "Entry rejected because expected net profit at the original target is not positive after configured trading costs.", EntryPrice: entry.Value, Lots: size.Lots, RequiredMargin: size.RequiredMargin));
                await UpdateSessionAsync(state.Session.Id, session => session.RejectedByTradingCosts++);
                await PersistRuntimeSymbolAsync(runtime, token);
                return;
            }
        }
        catch (Exception)
        {
            AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "ProfitCalculationUnavailable", pending.Direction, "Entry rejected because MT5 profit calculation is unavailable.", EntryPrice: entry.Value, Lots: size.Lots));
            await FaultSessionAsync(state, "MT5 profit calculation is unavailable; no Paper trade was created.", token);
            await PersistRuntimeSymbolAsync(runtime, token);
            return;
        }

        var trade = new PaperTrade
        {
            PaperSessionId = state.Session.Id, PaperSessionSymbolId = runtime.Symbol.Id, Symbol = runtime.Symbol.Symbol, Interval = state.Session.Interval,
            Status = PaperTradeStatus.Open, Direction = pending.Direction, CrossoverTimeUtc = pending.CrossoverTimeUtc, SignalTimeUtc = pending.SignalTimeUtc,
            EntryTimeUtc = update.EventTimeUtc, EntryPrice = entry.Value, Quantity = size.Lots * runtime.Symbol.ContractSize.Value,
            InitialStopLoss = pending.Stop, CurrentStopLoss = pending.Stop, StopSourceType = pending.StopSource, StopSourceTimeUtc = pending.StopTimeUtc,
            OriginalTakeProfit = target, CurrentTakeProfit = target, SignalOpen = pending.Snapshot.Open, SignalClose = pending.Snapshot.Close,
            SignalEma9 = pending.Snapshot.Ema9, SignalEma15 = pending.Snapshot.Ema15, SignalEma100 = pending.Snapshot.Ema100,
            SignalGapPercent = pending.Snapshot.GapPercent, SignalGapState = pending.Snapshot.GapState, IsReentry = pending.IsReentry,
            TrendRegimeCrossoverTimeUtc = pending.TrendRegimeCrossoverTimeUtc, Lots = size.Lots, EntryBid = update.Bid, EntryAsk = update.Ask,
            EntrySpread = update.Ask!.Value - update.Bid!.Value, RequiredMargin = size.RequiredMargin, MarginUsed = size.RequiredMargin,
            AccountEquityAtEntry = state.Session.CurrentBalance, RoundTripCommission = commission, GrossPnl = 0m, NetPnl = -commission
        };
        runtime.OpenTrade = trade;
        AddDecision(runtime, new PaperDecisionRuntimeEvent(update.EventTimeUtc, null, "Entered", pending.Direction, $"{pending.Direction} Paper trade entered at executable {(pending.Direction == SignalDirection.Long ? "Ask" : "Bid")} {entry.Value}.", pending.Snapshot.Ema9, pending.Snapshot.Ema15, pending.Snapshot.Ema100, pending.Snapshot.GapPercent, pending.Snapshot.GapState, pending.Stop, pending.StopSource, null, update.Bid, update.Ask, entry.Value, size.Lots, size.RequiredMargin));
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        database.PaperTrades.Add(trade);
        database.PaperTradeEvents.Add(new PaperTradeEvent { PaperTrade = trade, TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.Entry, MarketPrice = entry.Value });
        await database.SaveChangesAsync(token);
        state.Session.CurrentBalance -= commission;
        state.Session.UsedMargin += size.RequiredMargin;
        await UpdateSessionAsync(state.Session.Id, session => { session.CurrentBalance -= commission; session.UsedMargin += size.RequiredMargin; session.TotalTradingCosts += commission; });
        await PersistRuntimeSymbolAsync(runtime, token);
        logger.LogInformation("MT5 Paper trade entered for {Symbol} at executable {Price}.", trade.Symbol, trade.EntryPrice);
    }

    private async Task ManageOpenTradeAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        if (state.Session.MarketDataSource == MarketDataSource.Mt5Exness)
        {
            await ManageMt5OpenTradeAsync(state, runtime, update, token);
            return;
        }

        var trade = runtime.OpenTrade!; var direction = trade.Direction;
        // Only live observed prices participate in management; kline highs/lows can predate a reconnect.
        var best = direction == SignalDirection.Long ? Math.Max(trade.EntryPrice + trade.MfePrice, update.Close) : Math.Min(trade.EntryPrice - trade.MfePrice, update.Close);
        trade.MfePrice = direction == SignalDirection.Long ? Math.Max(0m, best - trade.EntryPrice) : Math.Max(0m, trade.EntryPrice - best); trade.MfePercent = trade.EntryPrice == 0 ? 0 : trade.MfePrice / trade.EntryPrice * 100m;
        var adverse = direction == SignalDirection.Long ? Math.Min(trade.EntryPrice - trade.MaePrice, update.Close) : Math.Max(trade.EntryPrice + trade.MaePrice, update.Close);
        trade.MaePrice = direction == SignalDirection.Long ? Math.Max(0m, trade.EntryPrice - adverse) : Math.Max(0m, adverse - trade.EntryPrice); trade.MaePercent = trade.EntryPrice == 0 ? 0 : trade.MaePrice / trade.EntryPrice * 100m;
        if ((direction == SignalDirection.Long && update.Close <= trade.CurrentStopLoss) || (direction == SignalDirection.Short && update.Close >= trade.CurrentStopLoss)) { await CloseTradeAsync(state, runtime, trade.CurrentStopLoss, trade.CurrentStopLoss == trade.InitialStopLoss ? PaperExitReason.InitialStopLoss : PaperExitReason.TrailingStop, update.EventTimeUtc, token); return; }
        var progress = TradeMath.Progress(trade.EntryPrice, trade.OriginalTakeProfit, best, direction); trade.BestFavorableProgressPercent = Math.Max(trade.BestFavorableProgressPercent, progress);
        if (state.Session.TrailingStopEnabled && progress >= 70m && !trade.TakeProfitExtended) { var old = trade.CurrentTakeProfit; trade.CurrentTakeProfit = TradeMath.ExtendedTarget(trade.EntryPrice, trade.OriginalTakeProfit, direction); trade.TakeProfitExtended = true; await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.TakeProfitExtended, MarketPrice = update.Close, OldTakeProfit = old, NewTakeProfit = trade.CurrentTakeProfit, ProgressPercent = progress }, token); }
        if ((direction == SignalDirection.Long && update.Close >= trade.CurrentTakeProfit) || (direction == SignalDirection.Short && update.Close <= trade.CurrentTakeProfit)) { await CloseTradeAsync(state, runtime, trade.CurrentTakeProfit, PaperExitReason.TakeProfit, update.EventTimeUtc, token); return; }
        if (!state.Session.TrailingStopEnabled) return;
        var lockPercent = TradeMath.LockPercent(progress); if (lockPercent == 0) return;
        var calculated = TradeMath.TrailingStop(trade.EntryPrice, trade.OriginalTakeProfit, direction, lockPercent);
        var next = TradeMath.FeeAwareTrailingStop(calculated, trade.EntryPrice, direction, state.Session.FeePercentPerSide); var improved = direction == SignalDirection.Long ? next > trade.CurrentStopLoss : next < trade.CurrentStopLoss;
        if (improved) { var old = trade.CurrentStopLoss; trade.CurrentStopLoss = next; await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.TrailingStopMoved, MarketPrice = update.Close, OldStop = old, NewStop = next, ProgressPercent = progress }, token); }
    }

    private async Task ManageMt5OpenTradeAsync(RuntimeSession state, RuntimeSymbol runtime, MarketBarUpdate update, CancellationToken token)
    {
        var trade = runtime.OpenTrade!;
        var exit = ExitExecutablePrice(trade.Direction, update.Bid, update.Ask);
        if (exit is null) return;
        var direction = trade.Direction;
        var best = direction == SignalDirection.Long ? Math.Max(trade.EntryPrice + trade.MfePrice, exit.Value) : Math.Min(trade.EntryPrice - trade.MfePrice, exit.Value);
        trade.MfePrice = direction == SignalDirection.Long ? Math.Max(0m, best - trade.EntryPrice) : Math.Max(0m, trade.EntryPrice - best);
        trade.MfePercent = trade.EntryPrice == 0m ? 0m : trade.MfePrice / trade.EntryPrice * 100m;
        var adverse = direction == SignalDirection.Long ? Math.Min(trade.EntryPrice - trade.MaePrice, exit.Value) : Math.Max(trade.EntryPrice + trade.MaePrice, exit.Value);
        trade.MaePrice = direction == SignalDirection.Long ? Math.Max(0m, trade.EntryPrice - adverse) : Math.Max(0m, adverse - trade.EntryPrice);
        trade.MaePercent = trade.EntryPrice == 0m ? 0m : trade.MaePrice / trade.EntryPrice * 100m;
        if ((direction == SignalDirection.Long && exit <= trade.CurrentStopLoss) || (direction == SignalDirection.Short && exit >= trade.CurrentStopLoss))
        {
            await CloseTradeAsync(state, runtime, exit.Value, trade.CurrentStopLoss == trade.InitialStopLoss ? PaperExitReason.InitialStopLoss : PaperExitReason.TrailingStop, update.EventTimeUtc, token);
            return;
        }
        var progress = TradeMath.Progress(trade.EntryPrice, trade.OriginalTakeProfit, best, direction);
        trade.BestFavorableProgressPercent = Math.Max(trade.BestFavorableProgressPercent, progress);
        if (state.Session.TrailingStopEnabled && progress >= 70m && !trade.TakeProfitExtended)
        {
            var old = trade.CurrentTakeProfit;
            trade.CurrentTakeProfit = TradeMath.ExtendedTarget(trade.EntryPrice, trade.OriginalTakeProfit, direction);
            trade.TakeProfitExtended = true;
            await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.TakeProfitExtended, MarketPrice = exit.Value, OldTakeProfit = old, NewTakeProfit = trade.CurrentTakeProfit, ProgressPercent = progress }, token);
        }
        if ((direction == SignalDirection.Long && exit >= trade.CurrentTakeProfit) || (direction == SignalDirection.Short && exit <= trade.CurrentTakeProfit))
        {
            await CloseTradeAsync(state, runtime, exit.Value, PaperExitReason.TakeProfit, update.EventTimeUtc, token);
            return;
        }
        if (!state.Session.TrailingStopEnabled) return;
        var lockPercent = TradeMath.LockPercent(progress);
        if (lockPercent == 0m) return;
        var normal = TradeMath.TrailingStop(trade.EntryPrice, trade.OriginalTakeProfit, direction, lockPercent);
        decimal economicBreakEven;
        try { economicBreakEven = await EconomicBreakEvenAsync(runtime.Symbol, trade, token); }
        catch (Exception)
        {
            await FaultSessionAsync(state, "MT5 profit calculation is unavailable while managing a Paper trade.", token);
            return;
        }
        var next = direction == SignalDirection.Long ? Math.Max(normal, economicBreakEven) : Math.Min(normal, economicBreakEven);
        var improved = direction == SignalDirection.Long ? next > trade.CurrentStopLoss : next < trade.CurrentStopLoss;
        if (improved)
        {
            var old = trade.CurrentStopLoss;
            trade.CurrentStopLoss = next;
            await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.TrailingStopMoved, MarketPrice = exit.Value, OldStop = old, NewStop = next, ProgressPercent = progress }, token);
        }
    }

    private async Task CloseTradeAsync(RuntimeSession state, RuntimeSymbol runtime, decimal exit, PaperExitReason reason, DateTimeOffset at, CancellationToken token)
    {
        if (state.Session.MarketDataSource == MarketDataSource.Mt5Exness)
        {
            await CloseMt5TradeAsync(state, runtime, exit, reason, at, token);
            return;
        }

        var trade = runtime.OpenTrade!; trade.Status = PaperTradeStatus.Closed; trade.ExitPrice = exit; trade.ExitTimeUtc = at; trade.FinalStopLoss = trade.CurrentStopLoss; trade.FinalTakeProfit = trade.CurrentTakeProfit; trade.ExitReason = reason; trade.ExitFeeUsdt = TradeMath.Fee(exit, trade.Quantity, state.Session.FeePercentPerSide); trade.TotalFeesUsdt = trade.EntryFeeUsdt + trade.ExitFeeUsdt.Value; trade.GrossPnlUsdt = TradeMath.GrossPnl(trade.EntryPrice, exit, trade.Quantity, trade.Direction); trade.NetPnlUsdt = trade.GrossPnlUsdt - trade.TotalFeesUsdt; trade.NetPnlPercent = trade.NetPnlUsdt / trade.EntryNotionalUsdt * 100m;
        runtime.ReentryEligible = (reason is PaperExitReason.InitialStopLoss or PaperExitReason.TrailingStop) && !trade.IsReentry;
        if (runtime.ReentryEligible) AddDecision(runtime, new PaperDecisionRuntimeEvent(at, null, "ReentryEligible", trade.Direction, "Position exit made one continuation re-entry eligible."));
        if (runtime.ReentryEligible) { runtime.TrendRegimeDirection = trade.Direction; runtime.TrendRegimeCrossoverTimeUtc = trade.TrendRegimeCrossoverTimeUtc ?? trade.CrossoverTimeUtc; }
        await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = at, Type = PaperTradeEventType.Exit, MarketPrice = exit }, token);
        if (state.Session.PositionSizingMode == PositionSizingMode.MarginPercent) { state.Session.CurrentBalanceUsdt += trade.NetPnlUsdt; state.Session.UsedMarginUsdt = Math.Max(0m, state.Session.UsedMarginUsdt - (trade.MarginUsedUsdt ?? 0m)); }
        await UpdateSessionAsync(state.Session.Id, session => { session.CompletedTrades++; session.NetPnlUsdt += trade.NetPnlUsdt; session.TotalFeesUsdt += trade.TotalFeesUsdt; if (session.PositionSizingMode == PositionSizingMode.MarginPercent) { session.CurrentBalanceUsdt += trade.NetPnlUsdt; session.UsedMarginUsdt = Math.Max(0m, session.UsedMarginUsdt - (trade.MarginUsedUsdt ?? 0m)); } }); runtime.OpenTrade = null; await PersistRuntimeSymbolAsync(runtime, token);
        logger.LogInformation("Paper trade {TradeId} exited as {Reason}.", trade.Id, reason);
    }

    private async Task CloseMt5TradeAsync(RuntimeSession state, RuntimeSymbol runtime, decimal exit, PaperExitReason reason, DateTimeOffset at, CancellationToken token)
    {
        var trade = runtime.OpenTrade!;
        EmaBot.Api.Mt5Bridge.Mt5ProfitCalculationPayload profit;
        try { profit = await Calculator().CalculateProfitAsync(new EmaBot.Api.Mt5Bridge.Mt5CalculateProfitRequest(runtime.Symbol.BrokerSymbol ?? runtime.Symbol.Symbol, trade.Direction.ToString(), trade.Lots ?? 0m, trade.EntryPrice, exit), token); }
        catch (Exception) { await FaultSessionAsync(state, "MT5 profit calculation is unavailable; the open Paper trade was preserved for diagnosis.", token); return; }
        trade.Status = PaperTradeStatus.Closed; trade.ExitPrice = exit; trade.ExitTimeUtc = at; trade.FinalStopLoss = trade.CurrentStopLoss; trade.FinalTakeProfit = trade.CurrentTakeProfit; trade.ExitReason = reason;
        trade.ExitBid = runtime.LatestBid; trade.ExitAsk = runtime.LatestAsk; trade.ExitSpread = runtime.LatestAsk - runtime.LatestBid;
        trade.GrossPnl = profit.Profit; trade.NetPnl = profit.Profit - (trade.RoundTripCommission ?? 0m);
        trade.NetPnlPercent = trade.AccountEquityAtEntry is > 0m ? trade.NetPnl.Value / trade.AccountEquityAtEntry.Value * 100m : 0m;
        runtime.ReentryEligible = (reason is PaperExitReason.InitialStopLoss or PaperExitReason.TrailingStop) && !trade.IsReentry;
        if (runtime.ReentryEligible) AddDecision(runtime, new PaperDecisionRuntimeEvent(at, null, "ReentryEligible", trade.Direction, "Position exit made one continuation re-entry eligible."));
        if (runtime.ReentryEligible) { runtime.TrendRegimeDirection = trade.Direction; runtime.TrendRegimeCrossoverTimeUtc = trade.TrendRegimeCrossoverTimeUtc ?? trade.CrossoverTimeUtc; }
        await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = at, Type = PaperTradeEventType.Exit, MarketPrice = exit }, token);
        state.Session.CurrentBalance += profit.Profit;
        state.Session.UsedMargin = Math.Max(0m, state.Session.UsedMargin - (trade.MarginUsed ?? 0m));
        await UpdateSessionAsync(state.Session.Id, session => { session.CompletedTrades++; session.CurrentBalance += profit.Profit; session.UsedMargin = Math.Max(0m, session.UsedMargin - (trade.MarginUsed ?? 0m)); session.NetPnl += trade.NetPnl ?? 0m; });
        runtime.OpenTrade = null;
        await PersistRuntimeSymbolAsync(runtime, token);
        logger.LogInformation("MT5 Paper trade {TradeId} exited as {Reason}.", trade.Id, reason);
    }

    private async Task RecordClosedCandleAsync(int sessionId, RuntimeSymbol runtime, IReadOnlyList<StrategyEvent> events, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await database.PaperSessionSymbols.SingleAsync(item => item.Id == runtime.Symbol.Id, token); symbol.LastKnownPrice = runtime.LatestPrice; symbol.LastMarketEventUtc = runtime.LastMarketEventUtc; symbol.LastProcessedClosedCandleUtc = runtime.LastClosedCandleUtc;
        symbol.TrendRegimeDirection = runtime.TrendRegimeDirection; symbol.TrendRegimeCrossoverTimeUtc = runtime.TrendRegimeCrossoverTimeUtc; symbol.ReentryEligible = runtime.ReentryEligible; symbol.ReentryConsumed = runtime.ReentryConsumed;
        var session = await database.PaperSessions.SingleAsync(item => item.Id == sessionId, token); session.TotalCrossovers += events.Count(item => item.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover); session.LongSignals += events.Count(item => item.Status == SignalStatus.LongSignal); session.ShortSignals += events.Count(item => item.Status == SignalStatus.ShortSignal); session.RejectedByEma100 += events.Count(item => item.Status == SignalStatus.RejectedByEma100Filter); session.RejectedByEmaGap += events.Count(item => item.Status == SignalStatus.RejectedByEmaGap); session.ConfirmationFailed += events.Count(item => item.Status == SignalStatus.ConfirmationFailed); await database.SaveChangesAsync(token);
    }
    private async Task PersistRuntimeSymbolAsync(RuntimeSymbol runtime, CancellationToken token) { await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await database.PaperSessionSymbols.SingleAsync(item => item.Id == runtime.Symbol.Id, token); symbol.LastKnownPrice = runtime.LatestPrice; symbol.LastMarketEventUtc = runtime.LastMarketEventUtc; symbol.LastProcessedClosedCandleUtc = runtime.LastClosedCandleUtc; symbol.TrendRegimeDirection = runtime.TrendRegimeDirection; symbol.TrendRegimeCrossoverTimeUtc = runtime.TrendRegimeCrossoverTimeUtc; symbol.ReentryEligible = runtime.ReentryEligible; symbol.ReentryConsumed = runtime.ReentryConsumed; ApplyPending(symbol, runtime.Pending); await database.SaveChangesAsync(token); }
    private async Task PersistTradeChangeAsync(PaperTrade runtimeTrade, PaperTradeEvent item, CancellationToken token) { await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var trade = await database.PaperTrades.SingleAsync(value => value.Id == runtimeTrade.Id, token); database.Entry(trade).CurrentValues.SetValues(runtimeTrade); item.PaperTradeId = trade.Id; database.PaperTradeEvents.Add(item); await database.SaveChangesAsync(token); }
    private async Task UpdateSessionAsync(int id, Action<PaperSession> change) { await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var session = await database.PaperSessions.SingleAsync(item => item.Id == id); change(session); await database.SaveChangesAsync(); }
    private static PaperPendingEntryRuntimeSnapshot? PendingSnapshot(PendingEntry? pending) => pending is null ? null : new(pending.Direction, pending.CrossoverTimeUtc, pending.SignalTimeUtc, pending.SignalTimeUtc.AddMilliseconds(1), pending.Stop, pending.StopSource, pending.StopTimeUtc, pending.Snapshot, pending.IsReentry);
    private static PaperDecisionRuntimeEvent PendingDecision(PendingEntry pending) => new(pending.SignalTimeUtc, pending.SignalTimeUtc, "PendingEntry", pending.Direction, $"Valid {pending.Direction} signal. Waiting for the first executable {(pending.Direction == SignalDirection.Long ? "Ask" : "Bid")} quote on the next bar.", pending.Snapshot.Ema9, pending.Snapshot.Ema15, pending.Snapshot.Ema100, pending.Snapshot.GapPercent, pending.Snapshot.GapState, pending.Stop, pending.StopSource, pending.SignalTimeUtc.AddMilliseconds(1));
    private static void AddDecision(RuntimeSymbol runtime, PaperDecisionRuntimeEvent decision) { runtime.RecentDecisions.Add(decision); if (runtime.RecentDecisions.Count > 25) runtime.RecentDecisions.RemoveAt(0); }
    private static void AddStrategyDecision(RuntimeSymbol runtime, StrategyEvent strategyEvent)
    {
        var message = strategyEvent.Status switch
        {
            SignalStatus.BullishCrossover => "Bullish EMA9/EMA15 crossover detected.",
            SignalStatus.BearishCrossover => "Bearish EMA9/EMA15 crossover detected.",
            SignalStatus.AwaitingConfirmation => $"{strategyEvent.Direction} crossover detected; waiting for confirmation candle.",
            SignalStatus.ConfirmationFailed => $"{strategyEvent.Direction} confirmation failed because the next closed candle did not satisfy the confirmation conditions.",
            SignalStatus.RejectedByEma100Filter => $"{strategyEvent.Direction} setup rejected by EMA100 filter.",
            SignalStatus.RejectedByEmaGap => $"{strategyEvent.Direction} setup rejected because EMA gap was below the configured minimum.",
            SignalStatus.LongSignal or SignalStatus.ShortSignal => $"Valid {strategyEvent.Direction} signal accepted.",
            _ => $"{strategyEvent.Status} evaluated."
        };
        AddDecision(runtime, new PaperDecisionRuntimeEvent(strategyEvent.Time, strategyEvent.Time, strategyEvent.Status.ToString(), strategyEvent.Direction, message, strategyEvent.Snapshot.Ema9, strategyEvent.Snapshot.Ema15, strategyEvent.Snapshot.Ema100, strategyEvent.Snapshot.GapPercent, strategyEvent.Snapshot.GapState));
    }
    private EmaBot.Api.Mt5Bridge.IMt5TradeCalculator Calculator() => calculator ?? throw new InvalidOperationException("MT5 trade calculation is not configured.");
    internal static decimal? EntryExecutablePrice(SignalDirection direction, decimal? bid, decimal? ask) => direction switch { SignalDirection.Long when ask is > 0m => ask, SignalDirection.Short when bid is > 0m => bid, _ => null };
    internal static decimal? ExitExecutablePrice(SignalDirection direction, decimal? bid, decimal? ask) => direction switch { SignalDirection.Long when bid is > 0m => bid, SignalDirection.Short when ask is > 0m => ask, _ => null };
    private static bool AllowsDirection(InstrumentTradeMode? mode, SignalDirection direction) => mode switch { InstrumentTradeMode.Disabled or InstrumentTradeMode.CloseOnly => false, InstrumentTradeMode.LongOnly => direction == SignalDirection.Long, InstrumentTradeMode.ShortOnly => direction == SignalDirection.Short, _ => true };
    private static bool StopsAreValid(PaperSessionSymbol symbol, SignalDirection direction, decimal entry, decimal stop, decimal target)
    {
        if (symbol.ContractSize is not > 0m || symbol.PointSize is not > 0m) return false;
        var minimum = (symbol.StopsLevelPoints ?? 0) * symbol.PointSize.Value;
        return direction == SignalDirection.Long
            ? stop < entry && target > entry && entry - stop >= minimum && target - entry >= minimum
            : stop > entry && target < entry && stop - entry >= minimum && entry - target >= minimum;
    }
    private async Task<Mt5PaperSize> SizeMt5PositionAsync(RuntimeSession state, PaperSessionSymbol symbol, SignalDirection direction, decimal entry, CancellationToken token)
    {
        if (symbol.VolumeMin is not > 0m || symbol.VolumeMax is not > 0m || symbol.VolumeStep is not > 0m) throw new InvalidOperationException("MT5 symbol volume rules are unavailable.");
        var available = state.Session.CurrentBalance - state.Session.UsedMargin;
        var budget = Math.Min(state.Session.CurrentBalance * state.Session.PaperMarginPerTradePercent / 100m, available);
        if (budget <= 0m) throw new InvalidOperationException("Insufficient simulated free margin.");
        decimal requested;
        if (state.Session.PaperPositionSizingMode == PaperPositionSizingMode.FixedLots)
        {
            requested = state.Session.PaperFixedLots;
            if (requested < symbol.VolumeMin || requested > symbol.VolumeMax) throw new InvalidOperationException("Configured fixed lots are outside the MT5 symbol limits.");
            requested = NormalizeVolumeDown(requested, symbol);
            if (requested < symbol.VolumeMin) throw new InvalidOperationException("Configured fixed lots are below the MT5 symbol minimum.");
            var margin = await MarginAsync(symbol, direction, requested, entry, token);
            if (margin > available) throw new InvalidOperationException("Insufficient simulated free margin.");
            return new Mt5PaperSize(requested, margin);
        }
        var probe = symbol.VolumeMin.Value;
        var probeMargin = await MarginAsync(symbol, direction, probe, entry, token);
        if (probeMargin <= 0m || probeMargin > budget) throw new InvalidOperationException("Insufficient simulated free margin.");
        requested = NormalizeVolumeDown(Math.Min(symbol.VolumeMax.Value, probe * budget / probeMargin), symbol);
        for (var attempt = 0; attempt < 4 && requested >= symbol.VolumeMin; attempt++)
        {
            var margin = await MarginAsync(symbol, direction, requested, entry, token);
            if (margin <= budget && margin <= available) return new Mt5PaperSize(requested, margin);
            requested = NormalizeVolumeDown(requested - symbol.VolumeStep.Value, symbol);
        }
        throw new InvalidOperationException("No MT5 lot volume fits the simulated margin budget.");
    }
    private async Task<decimal> MarginAsync(PaperSessionSymbol symbol, SignalDirection direction, decimal lots, decimal entry, CancellationToken token)
        => (await Calculator().CalculateMarginAsync(new EmaBot.Api.Mt5Bridge.Mt5CalculateMarginRequest(symbol.BrokerSymbol ?? symbol.Symbol, direction.ToString(), lots, entry), token)).RequiredMargin;
    private static decimal NormalizeVolumeDown(decimal requested, PaperSessionSymbol symbol)
    {
        var step = symbol.VolumeStep!.Value; var minimum = symbol.VolumeMin!.Value;
        var normalized = minimum + decimal.Floor((requested - minimum) / step) * step;
        if (symbol.VolumeLimit is { } limit) normalized = Math.Min(normalized, limit);
        return normalized;
    }
    private async Task<decimal> EconomicBreakEvenAsync(PaperSessionSymbol symbol, PaperTrade trade, CancellationToken token)
    {
        var commission = trade.RoundTripCommission ?? 0m;
        var lots = trade.Lots ?? 0m;
        if (commission == 0m) return trade.EntryPrice;
        if (symbol.TickSize is not > 0m || symbol.TickValueProfit is not > 0m || lots <= 0m) throw new InvalidOperationException("Cannot establish commission-aware MT5 break-even.");
        var delta = commission / (lots * (symbol.TickValueProfit.Value / symbol.TickSize.Value));
        var candidate = trade.Direction == SignalDirection.Long ? trade.EntryPrice + delta : trade.EntryPrice - delta;
        var profit = await Calculator().CalculateProfitAsync(new EmaBot.Api.Mt5Bridge.Mt5CalculateProfitRequest(symbol.BrokerSymbol ?? symbol.Symbol, trade.Direction.ToString(), lots, trade.EntryPrice, candidate), token);
        if (profit.Profit - commission < 0m) throw new InvalidOperationException("MT5 break-even validation failed.");
        return candidate;
    }
    private async Task FaultSessionAsync(RuntimeSession state, string message, CancellationToken token)
    {
        state.ConnectionState = "Degraded";
        state.AcceptSignals = false;
        await UpdateSessionAsync(state.Session.Id, session => { session.Status = PaperSessionStatus.Faulted; session.FailureMessage = message; });
    }
    private static TradingSettings Settings(PaperSession session) => new() { RiskReward = session.RiskReward, FixedOrderSizeUsdt = session.FixedOrderSizeUsdt, MinEmaGapPercent = session.MinEmaGapPercent, MaxStopDistancePercent = session.MaxStopDistancePercent, PositionSizingMode = session.PositionSizingMode, SimulatedAccountBalanceUsdt = session.CurrentBalanceUsdt, MarginPerTradePercent = session.MarginPerTradePercent, Leverage = session.Leverage, WaitForConfirmationCandle = session.WaitForConfirmationCandle, UseEma100Filter = session.UseEma100Filter, TrailingStopEnabled = session.TrailingStopEnabled, FeePercentPerSide = session.FeePercentPerSide };
    private static bool IsContinuation(IndicatorSnapshot snapshot, SignalDirection direction, TradingSettings settings) { var directional = direction == SignalDirection.Long ? snapshot.Ema9 > snapshot.Ema15 && snapshot.Close > snapshot.Ema9 && snapshot.Close > snapshot.Ema15 && snapshot.Close > snapshot.Open : snapshot.Ema9 < snapshot.Ema15 && snapshot.Close < snapshot.Ema9 && snapshot.Close < snapshot.Ema15 && snapshot.Close < snapshot.Open; if (!directional) return false; if (settings.UseEma100Filter && (!snapshot.Ema100.HasValue || (direction == SignalDirection.Long ? snapshot.Ema9 <= snapshot.Ema100 || snapshot.Ema15 <= snapshot.Ema100 : snapshot.Ema9 >= snapshot.Ema100 || snapshot.Ema15 >= snapshot.Ema100))) return false; return settings.MinEmaGapPercent == 0 || snapshot.GapPercent >= settings.MinEmaGapPercent; }
    private static void ClearPending(PaperSessionSymbol symbol) { symbol.PendingDirection = null; symbol.PendingCrossoverTimeUtc = null; symbol.PendingSignalTimeUtc = null; symbol.PendingStopPrice = null; symbol.PendingStopSourceType = null; symbol.PendingStopSourceTimeUtc = null; symbol.PendingSignalOpen = null; symbol.PendingSignalClose = null; symbol.PendingSignalEma9 = null; symbol.PendingSignalEma15 = null; symbol.PendingSignalEma100 = null; symbol.PendingSignalGapPercent = null; symbol.PendingSignalGapState = null; symbol.PendingIsReentry = false; symbol.PendingTrendRegimeCrossoverTimeUtc = null; }
    private static void ApplyPending(PaperSessionSymbol symbol, PendingEntry? pending) { ClearPending(symbol); if (pending is null) return; symbol.PendingDirection = pending.Direction; symbol.PendingCrossoverTimeUtc = pending.CrossoverTimeUtc; symbol.PendingSignalTimeUtc = pending.SignalTimeUtc; symbol.PendingStopPrice = pending.Stop; symbol.PendingStopSourceType = pending.StopSource; symbol.PendingStopSourceTimeUtc = pending.StopTimeUtc; symbol.PendingSignalOpen = pending.Snapshot.Open; symbol.PendingSignalClose = pending.Snapshot.Close; symbol.PendingSignalEma9 = pending.Snapshot.Ema9; symbol.PendingSignalEma15 = pending.Snapshot.Ema15; symbol.PendingSignalEma100 = pending.Snapshot.Ema100; symbol.PendingSignalGapPercent = pending.Snapshot.GapPercent; symbol.PendingSignalGapState = pending.Snapshot.GapState; symbol.PendingIsReentry = pending.IsReentry; symbol.PendingTrendRegimeCrossoverTimeUtc = pending.TrendRegimeCrossoverTimeUtc; }

    private sealed class RuntimeSession(PaperSession session, CancellationTokenSource cancellation) { public PaperSession Session { get; } = session; public CancellationTokenSource Cancellation { get; } = cancellation; public Dictionary<string, RuntimeSymbol> Symbols { get; } = new(StringComparer.Ordinal); public string ConnectionState { get; set; } = "Connecting"; public DateTimeOffset? LastUpdateUtc { get; set; } public bool AcceptSignals { get; set; } = true; public Task? Worker { get; set; } }
    private sealed class RuntimeSymbol(PaperSessionSymbol symbol, PaperTrade? openTrade) { public PaperSessionSymbol Symbol { get; } = symbol; public List<Candle> Candles { get; } = []; public List<PaperDecisionRuntimeEvent> RecentDecisions { get; } = []; public PaperTrade? OpenTrade { get; set; } = openTrade; public PendingEntry? Pending { get; set; } = symbol.PendingDirection is { } direction && symbol.PendingCrossoverTimeUtc is { } crossover && symbol.PendingSignalTimeUtc is { } signal && symbol.PendingStopPrice is { } stop && symbol.PendingStopSourceType is { } source && symbol.PendingStopSourceTimeUtc is { } stopTime ? new PendingEntry(direction, crossover, signal, stop, source, stopTime, new IndicatorSnapshot(signal, symbol.PendingSignalClose ?? 0m, symbol.PendingSignalEma9, symbol.PendingSignalEma15, symbol.PendingSignalEma100, symbol.PendingSignalGapPercent, symbol.PendingSignalGapState ?? GapState.Unchanged, TrendDirection.Neutral, symbol.PendingSignalOpen ?? 0m), symbol.PendingIsReentry, symbol.PendingTrendRegimeCrossoverTimeUtc) : null; public SignalDirection? TrendRegimeDirection { get; set; } = symbol.TrendRegimeDirection; public DateTimeOffset? TrendRegimeCrossoverTimeUtc { get; set; } = symbol.TrendRegimeCrossoverTimeUtc; public bool ReentryEligible { get; set; } = symbol.ReentryEligible; public bool ReentryConsumed { get; set; } = symbol.ReentryConsumed; public decimal? LatestPrice { get; set; } = symbol.LastKnownPrice; public decimal? LatestBid { get; set; } public decimal? LatestAsk { get; set; } public DateTimeOffset? LastMarketEventUtc { get; set; } = symbol.LastMarketEventUtc; public DateTimeOffset? LastClosedCandleUtc { get; set; } = symbol.LastProcessedClosedCandleUtc; public IndicatorSnapshot? Indicator { get; set; } }
    private sealed record PendingEntry(SignalDirection Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, decimal Stop, StopSourceType StopSource, DateTimeOffset StopTimeUtc, IndicatorSnapshot Snapshot, bool IsReentry, DateTimeOffset? TrendRegimeCrossoverTimeUtc);
    private sealed record Mt5PaperSize(decimal Lots, decimal RequiredMargin);
}
