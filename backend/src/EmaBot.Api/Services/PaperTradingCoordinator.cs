using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public sealed record PaperRuntimeSnapshot(int SessionId, string ConnectionState, DateTimeOffset? LastUpdateUtc, IReadOnlyDictionary<string, PaperSymbolRuntimeSnapshot> Symbols);
public sealed record PaperSymbolRuntimeSnapshot(decimal? LatestPrice, DateTimeOffset? LastMarketEventUtc, DateTimeOffset? LastClosedCandleUtc, IndicatorSnapshot? Indicator, SignalDirection? PendingDirection, PaperTrade? OpenTrade);

public sealed class PaperTradingCoordinator(
    IServiceScopeFactory scopeFactory,
    IBinanceFuturesMarketDataClient marketData,
    IBinanceFuturesStreamClient stream,
    EmaSignalEngine strategy,
    ILogger<PaperTradingCoordinator> logger) : IHostedService
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
            var proposed = new RuntimeSession(session, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            foreach (var symbol in session.Symbols) proposed.Symbols[symbol.Symbol] = new RuntimeSymbol(symbol, session.Trades.SingleOrDefault(trade => trade.PaperSessionSymbolId == symbol.Id && trade.Status == PaperTradeStatus.Open));
            try { await WarmupAsync(proposed, cancellationToken); }
            catch
            {
                proposed.Cancellation.Cancel(); proposed.Cancellation.Dispose();
                session.Status = resume ? PaperSessionStatus.Interrupted : PaperSessionStatus.Faulted;
                session.FailureMessage = resume ? "Warmup data is unavailable; resume can be retried." : "Public Binance warmup data is unavailable.";
                if (resume) session.InterruptedAtUtc = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                throw;
            }
            session.Status = PaperSessionStatus.Running; session.InterruptedAtUtc = null; session.FailureMessage = null;
            if (resume) foreach (var symbol in session.Symbols) ClearPending(symbol);
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
            if (active is null || active.Session.Id != sessionId) throw new InvalidOperationException("That paper session is not running in this application.");
            if (active.Symbols.Values.Any(symbol => symbol.OpenTrade is not null && !symbol.LatestPrice.HasValue)) throw new InvalidOperationException("An open paper position has no reliable latest market price and cannot be stopped safely.");
            state = active; state.AcceptSignals = false; state.Cancellation.Cancel(); active = null;
        }
        finally { gate.Release(); }
        if (state is not null) await StopAfterWorkerAsync(state, cancellationToken);
        logger.LogInformation("Paper session {SessionId} stopped.", sessionId);
    }

    public PaperRuntimeSnapshot? GetRuntimeSnapshot()
    {
        var state = active;
        return state is null ? null : new PaperRuntimeSnapshot(state.Session.Id, state.ConnectionState, state.LastUpdateUtc, state.Symbols.ToDictionary(pair => pair.Key, pair => new PaperSymbolRuntimeSnapshot(pair.Value.LatestPrice, pair.Value.LastMarketEventUtc, pair.Value.LastClosedCandleUtc, pair.Value.Indicator, pair.Value.Pending?.Direction, pair.Value.OpenTrade)));
    }

    private async Task WarmupAsync(RuntimeSession state, CancellationToken token)
    {
        foreach (var runtime in state.Symbols.Values)
        {
            var candles = await marketData.GetKlinesAsync(runtime.Symbol.Symbol, state.Session.Interval, null, null, 200, token);
            runtime.Candles.AddRange(candles.Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).TakeLast(200));
        }
    }

    private async Task ResyncCandlesAsync(RuntimeSession state, RuntimeSymbol runtime, BinanceKlineUpdate current, CancellationToken token)
    {
        var candles = await marketData.GetKlinesAsync(runtime.Symbol.Symbol, state.Session.Interval, null, null, 200, token);
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
        foreach (var symbol in state.Symbols.Values.Where(symbol => symbol.OpenTrade is not null)) await CloseTradeAsync(state, symbol, symbol.LatestPrice!.Value, PaperExitReason.SessionStopped, DateTimeOffset.UtcNow, token);
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
            await UpdateSessionAsync(state.Session.Id, session => { session.Status = PaperSessionStatus.Faulted; session.FailureMessage = "Public Binance stream could not be maintained."; });
            state.ConnectionState = "Degraded";
            await gate.WaitAsync();
            try { if (ReferenceEquals(active, state)) active = null; }
            finally { gate.Release(); }
            state.Cancellation.Dispose();
        }
    }

    internal async Task ProcessUpdateForTestAsync(BinanceKlineUpdate update, CancellationToken token = default)
    {
        var state = active ?? throw new InvalidOperationException("No active paper session.");
        await ProcessUpdateAsync(state, update, token);
    }

    private async Task ProcessUpdateAsync(RuntimeSession state, BinanceKlineUpdate update, CancellationToken token)
    {
        if (!state.Symbols.TryGetValue(update.Symbol, out var runtime) || update.Interval != state.Session.Interval) return;
        await gate.WaitAsync(token);
        try
        {
            runtime.LatestPrice = update.Close; runtime.LastMarketEventUtc = update.EventTimeUtc; state.LastUpdateUtc = update.EventTimeUtc;
            if (runtime.Pending is not null && runtime.OpenTrade is null && state.AcceptSignals)
            {
                var expectedOpen = runtime.Pending.SignalTimeUtc.AddMilliseconds(1);
                if (update.OpenTimeUtc == expectedOpen) await EnterPendingAsync(state, runtime, update, token);
                else if (update.OpenTimeUtc > expectedOpen) { runtime.Pending = null; await PersistRuntimeSymbolAsync(runtime, token); logger.LogInformation("Expired stale pending paper entry for {Symbol}.", runtime.Symbol.Symbol); }
            }
            if (runtime.OpenTrade is not null) await ManageOpenTradeAsync(state, runtime, update, token);
            if (!update.IsClosed || runtime.LastClosedCandleUtc == update.CloseTimeUtc) return;
            if (runtime.LastClosedCandleUtc is { } previousClose && update.OpenTimeUtc > previousClose.AddMilliseconds(1)) await ResyncCandlesAsync(state, runtime, update, token);
            runtime.LastClosedCandleUtc = update.CloseTimeUtc;
            runtime.Candles.RemoveAll(candle => candle.OpenTimeUtc == update.OpenTimeUtc);
            runtime.Candles.Add(new Candle(update.OpenTimeUtc, update.CloseTimeUtc, update.Open, update.High, update.Low, update.Close, update.Volume, true));
            if (runtime.Candles.Count > 200) runtime.Candles.RemoveRange(0, runtime.Candles.Count - 200);
            var evaluation = strategy.Evaluate(runtime.Candles, Settings(state.Session)); runtime.Indicator = evaluation.Snapshots.LastOrDefault();
            var events = evaluation.Events.Where(item => item.Time == update.CloseTimeUtc).ToArray();
            await RecordClosedCandleAsync(state.Session.Id, runtime, events, token);
            if (!state.AcceptSignals) return;
            foreach (var signal in events.Where(item => item.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal)) await ScheduleSignalAsync(state, runtime, evaluation.Events, signal, token);
        }
        finally { gate.Release(); }
    }

    private async Task ScheduleSignalAsync(RuntimeSession state, RuntimeSymbol runtime, IReadOnlyList<StrategyEvent> events, StrategyEvent signal, CancellationToken token)
    {
        if (runtime.OpenTrade is not null || runtime.Pending is not null) { await UpdateSessionAsync(state.Session.Id, session => session.SkippedWhilePositionOpen++); return; }
        var crossover = events.LastOrDefault(item => item.Time <= signal.Time && item.Direction == signal.Direction && (item.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover));
        var index = runtime.Candles.FindIndex(candle => candle.CloseTimeUtc == crossover?.Time); if (index < 1) return;
        var stop = SwingStopRules.Find(runtime.Candles, index, signal.Direction); runtime.Pending = new PendingEntry(signal.Direction, crossover!.Time, signal.Time, stop.Price, stop.Source, stop.Time, signal.Snapshot);
        await PersistRuntimeSymbolAsync(runtime, token);
        logger.LogInformation("Paper signal {Direction} scheduled for {Symbol}.", signal.Direction, runtime.Symbol.Symbol);
    }

    private async Task EnterPendingAsync(RuntimeSession state, RuntimeSymbol runtime, BinanceKlineUpdate update, CancellationToken token)
    {
        var pending = runtime.Pending!; runtime.Pending = null;
        if ((pending.Direction == SignalDirection.Long && pending.Stop >= update.Open) || (pending.Direction == SignalDirection.Short && pending.Stop <= update.Open)) { await UpdateSessionAsync(state.Session.Id, session => session.InvalidStopLoss++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        if (state.Session.MaxStopDistancePercent > 0 && TradeMath.StopDistancePercent(update.Open, pending.Stop) > state.Session.MaxStopDistancePercent) { await UpdateSessionAsync(state.Session.Id, session => session.RejectedByStopDistance++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        var target = TradeMath.InitialTarget(update.Open, pending.Stop, pending.Direction, state.Session.RiskReward);
        var settings = Settings(state.Session); var size = TradeMath.CalculatePositionSize(settings, state.Session.CurrentBalanceUsdt, update.Open);
        if (state.Session.PositionSizingMode == PositionSizingMode.MarginPercent && size.MarginUsedUsdt > state.Session.CurrentBalanceUsdt - state.Session.UsedMarginUsdt) { await UpdateSessionAsync(state.Session.Id, session => session.RejectedByInsufficientMargin++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        if (TradeMath.ExpectedNetAtTarget(update.Open, target, size.Quantity, pending.Direction, state.Session.FeePercentPerSide) <= 0) { await UpdateSessionAsync(state.Session.Id, session => session.RejectedByFees++); await PersistRuntimeSymbolAsync(runtime, token); return; }
        var quantity = size.Quantity;
        var trade = new PaperTrade { PaperSessionId = state.Session.Id, PaperSessionSymbolId = runtime.Symbol.Id, Symbol = runtime.Symbol.Symbol, Interval = state.Session.Interval, Status = PaperTradeStatus.Open, Direction = pending.Direction, CrossoverTimeUtc = pending.CrossoverTimeUtc, SignalTimeUtc = pending.SignalTimeUtc, EntryTimeUtc = update.OpenTimeUtc, EntryPrice = update.Open, Quantity = quantity, EntryNotionalUsdt = size.NotionalUsdt, PositionSizingMode = state.Session.PositionSizingMode, AccountEquityAtEntryUsdt = size.AccountEquityAtEntryUsdt, MarginUsedUsdt = size.MarginUsedUsdt, Leverage = size.Leverage, InitialStopLoss = pending.Stop, CurrentStopLoss = pending.Stop, StopSourceType = pending.StopSource, StopSourceTimeUtc = pending.StopTimeUtc, OriginalTakeProfit = target, CurrentTakeProfit = target, EntryFeeUsdt = TradeMath.Fee(update.Open, quantity, state.Session.FeePercentPerSide), TotalFeesUsdt = TradeMath.Fee(update.Open, quantity, state.Session.FeePercentPerSide), SignalOpen = pending.Snapshot.Open, SignalClose = pending.Snapshot.Close, SignalEma9 = pending.Snapshot.Ema9, SignalEma15 = pending.Snapshot.Ema15, SignalEma100 = pending.Snapshot.Ema100, SignalGapPercent = pending.Snapshot.GapPercent, SignalGapState = pending.Snapshot.GapState };
        runtime.OpenTrade = trade;
        await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); database.PaperTrades.Add(trade); database.PaperTradeEvents.Add(new PaperTradeEvent { PaperTrade = trade, TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.Entry, MarketPrice = update.Open }); await database.SaveChangesAsync(token);
        await PersistRuntimeSymbolAsync(runtime, token);
        if (state.Session.PositionSizingMode == PositionSizingMode.MarginPercent) { state.Session.UsedMarginUsdt += size.MarginUsedUsdt; await UpdateSessionAsync(state.Session.Id, session => session.UsedMarginUsdt += size.MarginUsedUsdt); }
        logger.LogInformation("Paper trade entered for {Symbol} at {Price}.", trade.Symbol, trade.EntryPrice);
    }

    private async Task ManageOpenTradeAsync(RuntimeSession state, RuntimeSymbol runtime, BinanceKlineUpdate update, CancellationToken token)
    {
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
        var next = TradeMath.TrailingStop(trade.EntryPrice, trade.OriginalTakeProfit, direction, lockPercent); var improved = direction == SignalDirection.Long ? next > trade.CurrentStopLoss : next < trade.CurrentStopLoss;
        if (improved) { var old = trade.CurrentStopLoss; trade.CurrentStopLoss = next; await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = update.EventTimeUtc, Type = PaperTradeEventType.TrailingStopMoved, MarketPrice = update.Close, OldStop = old, NewStop = next, ProgressPercent = progress }, token); }
    }

    private async Task CloseTradeAsync(RuntimeSession state, RuntimeSymbol runtime, decimal exit, PaperExitReason reason, DateTimeOffset at, CancellationToken token)
    {
        var trade = runtime.OpenTrade!; trade.Status = PaperTradeStatus.Closed; trade.ExitPrice = exit; trade.ExitTimeUtc = at; trade.FinalStopLoss = trade.CurrentStopLoss; trade.FinalTakeProfit = trade.CurrentTakeProfit; trade.ExitReason = reason; trade.ExitFeeUsdt = TradeMath.Fee(exit, trade.Quantity, state.Session.FeePercentPerSide); trade.TotalFeesUsdt = trade.EntryFeeUsdt + trade.ExitFeeUsdt.Value; trade.GrossPnlUsdt = TradeMath.GrossPnl(trade.EntryPrice, exit, trade.Quantity, trade.Direction); trade.NetPnlUsdt = trade.GrossPnlUsdt - trade.TotalFeesUsdt; trade.NetPnlPercent = trade.NetPnlUsdt / trade.EntryNotionalUsdt * 100m;
        await PersistTradeChangeAsync(trade, new PaperTradeEvent { TimeUtc = at, Type = PaperTradeEventType.Exit, MarketPrice = exit }, token);
        if (state.Session.PositionSizingMode == PositionSizingMode.MarginPercent) { state.Session.CurrentBalanceUsdt += trade.NetPnlUsdt; state.Session.UsedMarginUsdt = Math.Max(0m, state.Session.UsedMarginUsdt - (trade.MarginUsedUsdt ?? 0m)); }
        await UpdateSessionAsync(state.Session.Id, session => { session.CompletedTrades++; session.NetPnlUsdt += trade.NetPnlUsdt; session.TotalFeesUsdt += trade.TotalFeesUsdt; if (session.PositionSizingMode == PositionSizingMode.MarginPercent) { session.CurrentBalanceUsdt += trade.NetPnlUsdt; session.UsedMarginUsdt = Math.Max(0m, session.UsedMarginUsdt - (trade.MarginUsedUsdt ?? 0m)); } }); runtime.OpenTrade = null;
        logger.LogInformation("Paper trade {TradeId} exited as {Reason}.", trade.Id, reason);
    }

    private async Task RecordClosedCandleAsync(int sessionId, RuntimeSymbol runtime, IReadOnlyList<StrategyEvent> events, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await database.PaperSessionSymbols.SingleAsync(item => item.Id == runtime.Symbol.Id, token); symbol.LastKnownPrice = runtime.LatestPrice; symbol.LastMarketEventUtc = runtime.LastMarketEventUtc; symbol.LastProcessedClosedCandleUtc = runtime.LastClosedCandleUtc;
        var session = await database.PaperSessions.SingleAsync(item => item.Id == sessionId, token); session.TotalCrossovers += events.Count(item => item.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover); session.LongSignals += events.Count(item => item.Status == SignalStatus.LongSignal); session.ShortSignals += events.Count(item => item.Status == SignalStatus.ShortSignal); session.RejectedByEma100 += events.Count(item => item.Status == SignalStatus.RejectedByEma100Filter); session.RejectedByEmaGap += events.Count(item => item.Status == SignalStatus.RejectedByEmaGap); session.ConfirmationFailed += events.Count(item => item.Status == SignalStatus.ConfirmationFailed); await database.SaveChangesAsync(token);
    }
    private async Task PersistRuntimeSymbolAsync(RuntimeSymbol runtime, CancellationToken token) { await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await database.PaperSessionSymbols.SingleAsync(item => item.Id == runtime.Symbol.Id, token); symbol.LastKnownPrice = runtime.LatestPrice; symbol.LastMarketEventUtc = runtime.LastMarketEventUtc; symbol.LastProcessedClosedCandleUtc = runtime.LastClosedCandleUtc; ApplyPending(symbol, runtime.Pending); await database.SaveChangesAsync(token); }
    private async Task PersistTradeChangeAsync(PaperTrade runtimeTrade, PaperTradeEvent item, CancellationToken token) { await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var trade = await database.PaperTrades.SingleAsync(value => value.Id == runtimeTrade.Id, token); database.Entry(trade).CurrentValues.SetValues(runtimeTrade); item.PaperTradeId = trade.Id; database.PaperTradeEvents.Add(item); await database.SaveChangesAsync(token); }
    private async Task UpdateSessionAsync(int id, Action<PaperSession> change) { await using var scope = scopeFactory.CreateAsyncScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var session = await database.PaperSessions.SingleAsync(item => item.Id == id); change(session); await database.SaveChangesAsync(); }
    private static TradingSettings Settings(PaperSession session) => new() { RiskReward = session.RiskReward, FixedOrderSizeUsdt = session.FixedOrderSizeUsdt, MinEmaGapPercent = session.MinEmaGapPercent, MaxStopDistancePercent = session.MaxStopDistancePercent, PositionSizingMode = session.PositionSizingMode, SimulatedAccountBalanceUsdt = session.CurrentBalanceUsdt, MarginPerTradePercent = session.MarginPerTradePercent, Leverage = session.Leverage, WaitForConfirmationCandle = session.WaitForConfirmationCandle, UseEma100Filter = session.UseEma100Filter, TrailingStopEnabled = session.TrailingStopEnabled, FeePercentPerSide = session.FeePercentPerSide };
    private static void ClearPending(PaperSessionSymbol symbol) { symbol.PendingDirection = null; symbol.PendingCrossoverTimeUtc = null; symbol.PendingSignalTimeUtc = null; symbol.PendingStopPrice = null; symbol.PendingStopSourceType = null; symbol.PendingStopSourceTimeUtc = null; symbol.PendingSignalOpen = null; symbol.PendingSignalClose = null; symbol.PendingSignalEma9 = null; symbol.PendingSignalEma15 = null; symbol.PendingSignalEma100 = null; symbol.PendingSignalGapPercent = null; symbol.PendingSignalGapState = null; }
    private static void ApplyPending(PaperSessionSymbol symbol, PendingEntry? pending) { ClearPending(symbol); if (pending is null) return; symbol.PendingDirection = pending.Direction; symbol.PendingCrossoverTimeUtc = pending.CrossoverTimeUtc; symbol.PendingSignalTimeUtc = pending.SignalTimeUtc; symbol.PendingStopPrice = pending.Stop; symbol.PendingStopSourceType = pending.StopSource; symbol.PendingStopSourceTimeUtc = pending.StopTimeUtc; symbol.PendingSignalOpen = pending.Snapshot.Open; symbol.PendingSignalClose = pending.Snapshot.Close; symbol.PendingSignalEma9 = pending.Snapshot.Ema9; symbol.PendingSignalEma15 = pending.Snapshot.Ema15; symbol.PendingSignalEma100 = pending.Snapshot.Ema100; symbol.PendingSignalGapPercent = pending.Snapshot.GapPercent; symbol.PendingSignalGapState = pending.Snapshot.GapState; }

    private sealed class RuntimeSession(PaperSession session, CancellationTokenSource cancellation) { public PaperSession Session { get; } = session; public CancellationTokenSource Cancellation { get; } = cancellation; public Dictionary<string, RuntimeSymbol> Symbols { get; } = new(StringComparer.Ordinal); public string ConnectionState { get; set; } = "Connecting"; public DateTimeOffset? LastUpdateUtc { get; set; } public bool AcceptSignals { get; set; } = true; public Task? Worker { get; set; } }
    private sealed class RuntimeSymbol(PaperSessionSymbol symbol, PaperTrade? openTrade) { public PaperSessionSymbol Symbol { get; } = symbol; public List<Candle> Candles { get; } = []; public PaperTrade? OpenTrade { get; set; } = openTrade; public PendingEntry? Pending { get; set; } = symbol.PendingDirection is { } direction && symbol.PendingCrossoverTimeUtc is { } crossover && symbol.PendingSignalTimeUtc is { } signal && symbol.PendingStopPrice is { } stop && symbol.PendingStopSourceType is { } source && symbol.PendingStopSourceTimeUtc is { } stopTime ? new PendingEntry(direction, crossover, signal, stop, source, stopTime, new IndicatorSnapshot(signal, symbol.PendingSignalClose ?? 0m, symbol.PendingSignalEma9, symbol.PendingSignalEma15, symbol.PendingSignalEma100, symbol.PendingSignalGapPercent, symbol.PendingSignalGapState ?? GapState.Unchanged, TrendDirection.Neutral, symbol.PendingSignalOpen ?? 0m)) : null; public decimal? LatestPrice { get; set; } = symbol.LastKnownPrice; public DateTimeOffset? LastMarketEventUtc { get; set; } = symbol.LastMarketEventUtc; public DateTimeOffset? LastClosedCandleUtc { get; set; } = symbol.LastProcessedClosedCandleUtc; public IndicatorSnapshot? Indicator { get; set; } }
    private sealed record PendingEntry(SignalDirection Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, decimal Stop, StopSourceType StopSource, DateTimeOffset StopTimeUtc, IndicatorSnapshot Snapshot);
}
