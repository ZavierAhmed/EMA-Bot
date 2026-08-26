using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

public sealed record BacktestCalculation(IReadOnlyList<BacktestTrade> Trades, BacktestDiagnostics Diagnostics, decimal EndingEquityUsdt);
public sealed record BacktestDiagnostics(int TotalCrossovers, int LongSignals, int ShortSignals, int RejectedByEma100, int RejectedByEmaGap, int RejectedByStopDistance, int RejectedByFees, int ConfirmationFailed, int InvalidStopLoss, int SkippedWhilePositionOpen, int NoEntryCandle, int RejectedByHtfRegime = 0, int RejectedByInsufficientMargin = 0, int RejectedByInvalidVolume = 0, int RejectedByTradeMode = 0);

public sealed class BacktestEngine(EmaSignalEngine strategy)
{
    public BacktestCalculation Run(IReadOnlyList<Candle> input, TradingSettings settings, StrategyMarketContext? marketContext = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candles = input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray();
        var evaluation = strategy.Evaluate(candles, settings);
        cancellationToken.ThrowIfCancellationRequested();
        return RunClosedCandles(candles, settings, evaluation.Events, evaluation.Snapshots, marketContext: marketContext, cancellationToken: cancellationToken);
    }

    public BacktestCalculation RunResearch(IReadOnlyList<Candle> input, TradingSettings settings, DateTimeOffset requestedStartUtc, DateTimeOffset requestedEndUtc, StrategyMarketContext? marketContext = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candles = input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray();
        var evaluation = strategy.Evaluate(candles, settings);
        cancellationToken.ThrowIfCancellationRequested();
        return RunClosedCandles(candles, settings, evaluation.Events, evaluation.Snapshots, requestedStartUtc, requestedEndUtc, marketContext, cancellationToken);
    }

    // Research-only event overload keeps segment-boundary behavior deterministic in tests.
    public BacktestCalculation RunResearchWithEvents(IReadOnlyList<Candle> input, TradingSettings settings, IReadOnlyList<StrategyEvent> events, DateTimeOffset requestedStartUtc, DateTimeOffset requestedEndUtc, StrategyMarketContext? marketContext = null, CancellationToken cancellationToken = default)
        => RunClosedCandles(input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray(), settings, events, [], requestedStartUtc, requestedEndUtc, marketContext, cancellationToken);

    // This overload keeps execution rules testable independently from EMA calculation.
    public BacktestCalculation RunWithEvents(IReadOnlyList<Candle> input, TradingSettings settings, IReadOnlyList<StrategyEvent> events, StrategyMarketContext? marketContext = null, CancellationToken cancellationToken = default)
        => RunClosedCandles(input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray(), settings, events, [], marketContext: marketContext, cancellationToken: cancellationToken);

    // Snapshot overload keeps the re-entry path testable independently from EMA calculation.
    public BacktestCalculation RunWithEvents(IReadOnlyList<Candle> input, TradingSettings settings, IReadOnlyList<StrategyEvent> events, IReadOnlyList<IndicatorSnapshot> snapshots, StrategyMarketContext? marketContext = null, CancellationToken cancellationToken = default)
        => RunClosedCandles(input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray(), settings, events, snapshots, marketContext: marketContext, cancellationToken: cancellationToken);

    private static BacktestCalculation RunClosedCandles(Candle[] candles, TradingSettings settings, IReadOnlyList<StrategyEvent> events, IReadOnlyList<IndicatorSnapshot> snapshots, DateTimeOffset? requestedStartUtc = null, DateTimeOffset? requestedEndUtc = null, StrategyMarketContext? marketContext = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var indexes = BacktestIndexes.Create(candles, events, snapshots);
        var trades = new List<BacktestTrade>(); var invalid = 0; var skipped = 0; var noEntry = 0; var rejectedStop = 0; var rejectedFees = 0; var rejectedHtf = 0; var occupiedUntil = -1;
        var maxExecutionIndex = requestedEndUtc.HasValue ? Array.FindLastIndex(candles, candle => candle.CloseTimeUtc <= requestedEndUtc.Value) : candles.Length - 1;
        var segmentEvents = events.Select((value, index) => new IndexedStrategyEvent(index, value)).Where(e => (!requestedStartUtc.HasValue || e.Value.Time >= requestedStartUtc.Value) && (!requestedEndUtc.HasValue || e.Value.Time <= requestedEndUtc.Value)).ToArray();
        var equity = settings.SimulatedAccountBalanceUsdt; var reenteredRegimes = new HashSet<DateTimeOffset>();
        foreach (var indexedSignal in segmentEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signal = indexedSignal.Value;
            if (signal.Status is not (SignalStatus.LongSignal or SignalStatus.ShortSignal)) continue;
            if (!indexes.CandleIndexByCloseTime.TryGetValue(signal.Time, out var signalIndex) || signalIndex + 1 > maxExecutionIndex || requestedEndUtc.HasValue && candles[signalIndex + 1].OpenTimeUtc > requestedEndUtc.Value) { noEntry++; continue; }
            if (signalIndex < occupiedUntil) { skipped++; continue; }
            var direction = signal.Direction;
            var crossover = indexes.FindCrossover(signal.Time, direction);
            if (crossover is null || !indexes.CandleIndexByCloseTime.TryGetValue(crossover.Time, out var crossoverIndex)) continue;
            if (!PassesHtf(settings, marketContext, signal.Time, direction, out var htf)) { rejectedHtf++; continue; }
            var stop = indexes.SelectInitialStop(candles, crossoverIndex, signalIndex, signal.Snapshot, direction, settings); var entryIndex = signalIndex + 1; var entry = candles[entryIndex].Open;
            if ((direction == SignalDirection.Long && stop.Price >= entry) || (direction == SignalDirection.Short && stop.Price <= entry)) { invalid++; continue; }
            if (settings.MaxStopDistancePercent > 0 && TradeMath.StopDistancePercent(entry, stop.Price) > settings.MaxStopDistancePercent) { rejectedStop++; continue; }
            var size = TradeMath.CalculatePositionSize(settings, equity, entry);
            var target = TradeMath.InitialTarget(entry, stop.Price, direction, settings.RiskReward);
            if (TradeMath.ExpectedNetAtTarget(entry, target, size.Quantity, direction, settings.FeePercentPerSide) <= 0) { rejectedFees++; continue; }
            var execution = Execute(candles, entryIndex, crossoverIndex, signal, direction, entry, stop, settings, size, maxExecutionIndex, htf, indexes, cancellationToken);
            var trade = execution.Trade; trades.Add(trade);
            if (settings.PositionSizingMode == PositionSizingMode.MarginPercent) equity += trade.NetPnlUsdt;
            // A SL/TP exit is intrabar. A signal at that candle's close may enter on the following open;
            // signals from earlier closes remain unavailable while the position was open.
            occupiedUntil = execution.ExitCandleIndex;
            if (settings.SameTrendReentryEnabled && snapshots.Count > 0 && (trade.ExitReason is BacktestExitReason.StopLoss or BacktestExitReason.TrailingStop) && reenteredRegimes.Add(crossover.Time))
            {
                var opposite = direction == SignalDirection.Long ? TrendDirection.Down : TrendDirection.Up;
                var lastCandidateIndex = Math.Min(maxExecutionIndex - 1, crossoverIndex + settings.MaxReentryAgeBars);
                for (var candidateIndex = Math.Max(occupiedUntil, 0); candidateIndex <= lastCandidateIndex; candidateIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!indexes.SnapshotByCandleIndex.TryGetValue(candidateIndex, out var reentry)) continue;
                    if (reentry.TrendDirection == opposite) break;
                    if (!IsContinuation(reentry, direction, settings)) continue;

                    // The old FirstOrDefault selected exactly one continuation candidate. Preserve that
                    // behavior even if its HTF/stop/fee checks later reject it.
                    if (!PassesHtf(settings, marketContext, reentry.Time, direction, out var reentryHtf)) { rejectedHtf++; break; }
                    var reentrySignal = new StrategyEvent(reentry.Time, direction, direction == SignalDirection.Long ? SignalStatus.ReentryLongSignal : SignalStatus.ReentryShortSignal, reentry);
                    var reentryStop = indexes.SelectInitialStop(candles, candidateIndex, candidateIndex, reentry, direction, settings); var reentryEntry = candles[candidateIndex + 1].Open;
                    if ((direction == SignalDirection.Long ? reentryStop.Price < reentryEntry : reentryStop.Price > reentryEntry) && (settings.MaxStopDistancePercent == 0 || TradeMath.StopDistancePercent(reentryEntry, reentryStop.Price) <= settings.MaxStopDistancePercent))
                    {
                        var reentrySize = TradeMath.CalculatePositionSize(settings, equity, reentryEntry); var reentryTarget = TradeMath.InitialTarget(reentryEntry, reentryStop.Price, direction, settings.RiskReward);
                        if (TradeMath.ExpectedNetAtTarget(reentryEntry, reentryTarget, reentrySize.Quantity, direction, settings.FeePercentPerSide) > 0)
                        {
                            var secondExecution = Execute(candles, candidateIndex + 1, crossoverIndex, reentrySignal, direction, reentryEntry, reentryStop, settings, reentrySize, maxExecutionIndex, reentryHtf, indexes, cancellationToken);
                            var second = secondExecution.Trade;
                            second.IsReentry = true; second.TrendRegimeCrossoverTimeUtc = crossover.Time; second.ReentryAgeBars = candidateIndex - crossoverIndex; trades.Add(second);
                            if (settings.PositionSizingMode == PositionSizingMode.MarginPercent) equity += second.NetPnlUsdt;
                            occupiedUntil = secondExecution.ExitCandleIndex;
                        }
                    }
                    break;
                }
            }
        }
        return new(trades, new(segmentEvents.Count(e => e.Value.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover), segmentEvents.Count(e => e.Value.Status == SignalStatus.LongSignal), segmentEvents.Count(e => e.Value.Status == SignalStatus.ShortSignal), segmentEvents.Count(e => e.Value.Status == SignalStatus.RejectedByEma100Filter), segmentEvents.Count(e => e.Value.Status == SignalStatus.RejectedByEmaGap), rejectedStop, rejectedFees, segmentEvents.Count(e => e.Value.Status == SignalStatus.ConfirmationFailed), invalid, skipped, noEntry, rejectedHtf), equity);
    }

    private static bool PassesHtf(TradingSettings settings, StrategyMarketContext? marketContext, DateTimeOffset signalTimeUtc, SignalDirection direction, out HigherTimeframeDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (!settings.UseHtfRegimeFilter) return true;
        diagnostic = HigherTimeframeRegime.Calculate(signalTimeUtc, direction, marketContext?.HigherTimeframe, marketContext?.HigherTimeframeCandles);
        return HigherTimeframeRegime.PassesH2(diagnostic, direction);
    }

    private static bool IsContinuation(IndicatorSnapshot snapshot, SignalDirection direction, TradingSettings settings)
    {
        var directional = direction == SignalDirection.Long
            ? snapshot.Ema9 > snapshot.Ema15 && snapshot.Close > snapshot.Ema9 && snapshot.Close > snapshot.Ema15 && snapshot.Close > snapshot.Open
            : snapshot.Ema9 < snapshot.Ema15 && snapshot.Close < snapshot.Ema9 && snapshot.Close < snapshot.Ema15 && snapshot.Close < snapshot.Open;
        if (!directional) return false;
        if (settings.UseEma100Filter && (!snapshot.Ema100.HasValue || (direction == SignalDirection.Long ? snapshot.Ema9 <= snapshot.Ema100 || snapshot.Ema15 <= snapshot.Ema100 : snapshot.Ema9 >= snapshot.Ema100 || snapshot.Ema15 >= snapshot.Ema100))) return false;
        return settings.MinEmaGapPercent == 0 || snapshot.GapPercent >= settings.MinEmaGapPercent;
    }

    private static BacktestExecutionResult Execute(Candle[] candles, int entryIndex, int crossoverIndex, StrategyEvent signal, SignalDirection direction, decimal entry, InitialStopSelection stop, TradingSettings settings, TradeMath.PositionSize size, int maxExecutionIndex, HigherTimeframeDiagnostic? htf, BacktestIndexes indexes, CancellationToken cancellationToken)
    {
        var risk = decimal.Abs(entry - stop.Price); var originalTp = TradeMath.InitialTarget(entry, stop.Price, direction, settings.RiskReward); var managementEvents = new List<BacktestTradeEvent> { new() { TimeUtc = candles[entryIndex].OpenTimeUtc, EffectiveTimeUtc = candles[entryIndex].OpenTimeUtc, Type = BacktestTradeEventType.Entry, MarketPrice = entry } };
        var currentStop = stop.Price; var currentTp = originalTp; var extended = false; var max = entry; var min = entry; var exitIndex = maxExecutionIndex; var exitPrice = candles[maxExecutionIndex].Close; var exitTime = candles[maxExecutionIndex].CloseTimeUtc; var reason = BacktestExitReason.EndOfData; var conflict = false; var oppositeExitIndex = -1;
        for (var i = entryIndex; i <= maxExecutionIndex; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = candles[i];
            if (i == oppositeExitIndex)
            {
                exitPrice = c.Open; exitIndex = i; exitTime = c.OpenTimeUtc; reason = BacktestExitReason.OppositeCrossover;
                break;
            }
            var sl = direction == SignalDirection.Long ? c.Low <= currentStop : c.High >= currentStop;
            var tp = direction == SignalDirection.Long ? c.High >= currentTp : c.Low <= currentTp;
            if (sl || tp)
            {
                conflict = sl && tp;
                reason = sl ? currentStop == stop.Price ? BacktestExitReason.StopLoss : BacktestExitReason.TrailingStop : BacktestExitReason.TakeProfit;
                exitPrice = sl ? currentStop : currentTp;
                exitIndex = i;
                exitTime = c.CloseTimeUtc;
                // OHLC does not reveal intrabar order. Record the exit fill as an excursion, but do not
                // attribute the rest of an exit candle's range to a position that may already be closed.
                if (direction == SignalDirection.Long)
                {
                    if (reason == BacktestExitReason.TakeProfit) max = Math.Max(max, exitPrice);
                    else min = Math.Min(min, exitPrice);
                }
                else
                {
                    if (reason == BacktestExitReason.TakeProfit) min = Math.Min(min, exitPrice);
                    else max = Math.Max(max, exitPrice);
                }
                break;
            }
            max = Math.Max(max, c.High);
            min = Math.Min(min, c.Low);
            if (settings.TrailingStopEnabled)
            {
                var progress = TradeMath.Progress(entry, originalTp, direction == SignalDirection.Long ? max : min, direction);
                var lockPercent = TradeMath.LockPercent(progress);
                if (lockPercent > 0)
                {
                    var calculatedStop = TradeMath.TrailingStop(entry, originalTp, direction, lockPercent);
                    var nextStop = TradeMath.FeeAwareTrailingStop(calculatedStop, entry, direction, settings.FeePercentPerSide);
                    var improved = direction == SignalDirection.Long ? nextStop > currentStop : nextStop < currentStop;
                    if (improved) { var oldStop = currentStop; currentStop = nextStop; managementEvents.Add(new BacktestTradeEvent { TimeUtc = c.CloseTimeUtc, EffectiveTimeUtc = i + 1 <= maxExecutionIndex ? candles[i + 1].OpenTimeUtc : null, Type = BacktestTradeEventType.TrailingStopMoved, MarketPrice = c.Close, OldStop = oldStop, NewStop = currentStop, ProgressPercent = progress }); }
                }
                if (progress >= 70 && !extended)
                {
                    var oldTp = currentTp; currentTp = TradeMath.ExtendedTarget(entry, originalTp, direction);
                    extended = true; managementEvents.Add(new BacktestTradeEvent { TimeUtc = c.CloseTimeUtc, EffectiveTimeUtc = i + 1 <= maxExecutionIndex ? candles[i + 1].OpenTimeUtc : null, Type = BacktestTradeEventType.TakeProfitExtended, MarketPrice = c.Close, OldTakeProfit = oldTp, NewTakeProfit = currentTp, ProgressPercent = progress });
                }
            }
            if (settings.ExitOnOppositeCrossover && i + 1 <= maxExecutionIndex && indexes.HasOppositeExecutableSignal(c.CloseTimeUtc, direction)) oppositeExitIndex = i + 1;
        }
        var quantity = size.Quantity; var notional = size.NotionalUsdt; var entryFee = TradeMath.Fee(entry, quantity, settings.FeePercentPerSide); var exitFee = TradeMath.Fee(exitPrice, quantity, settings.FeePercentPerSide); var gross = TradeMath.GrossPnl(entry, exitPrice, quantity, direction); var net = gross - entryFee - exitFee; var snap = signal.Snapshot;
        managementEvents.Add(new BacktestTradeEvent { TimeUtc = exitTime, EffectiveTimeUtc = exitTime, Type = BacktestTradeEventType.Exit, MarketPrice = exitPrice });
        return new(new BacktestTrade { Direction = direction, CrossoverTimeUtc = candles[crossoverIndex].CloseTimeUtc, SignalTimeUtc = signal.Time, EntryTimeUtc = candles[entryIndex].OpenTimeUtc, ExitTimeUtc = exitTime, EntryPrice = entry, ExitPrice = exitPrice, Quantity = quantity, EntryNotionalUsdt = notional, PositionSizingMode = settings.PositionSizingMode, AccountEquityAtEntryUsdt = size.AccountEquityAtEntryUsdt, MarginUsedUsdt = size.MarginUsedUsdt, Leverage = size.Leverage, InitialStopLoss = stop.Price, FinalStopLoss = currentStop, StopSourceType = stop.Source, StopSourceTimeUtc = stop.Time, OriginalTakeProfit = originalTp, FinalTakeProfit = currentTp, TakeProfitExtended = extended, ExitReason = reason, SameCandleExitConflict = conflict, EntryFeeUsdt = entryFee, ExitFeeUsdt = exitFee, TotalFeesUsdt = entryFee + exitFee, GrossPnlUsdt = gross, NetPnlUsdt = net, NetPnlPercent = net / notional * 100m, GrossRMultiple = gross / (risk * quantity), NetRMultiple = net / (risk * quantity), MfePrice = direction == SignalDirection.Long ? max - entry : entry - min, MfePercent = (direction == SignalDirection.Long ? max - entry : entry - min) / entry * 100m, MaePrice = direction == SignalDirection.Long ? entry - min : max - entry, MaePercent = (direction == SignalDirection.Long ? entry - min : max - entry) / entry * 100m, SignalOpen = snap.Open, SignalClose = snap.Close, SignalEma9 = snap.Ema9, SignalEma15 = snap.Ema15, SignalEma100 = snap.Ema100, SignalGapPercent = snap.GapPercent, SignalGapState = snap.GapState, HtfTimeframe = htf?.Timeframe, SignalHtfCandleCloseTimeUtc = htf?.CandleCloseTimeUtc, SignalHtfEma100Slope20Percent = htf?.Ema100Slope20Percent, SignalHtfAtr14Percent = htf?.Atr14Percent, UseAdaptiveInitialStop = stop.UseAdaptiveInitialStop, SignalAtr14 = stop.Atr14, ReversalPowerScore = stop.ReversalPowerScore, ReversalPowerBand = stop.ReversalPowerBand, StopAnchorPrice = stop.AnchorPrice, StopBuffer = stop.Buffer, Events = managementEvents }, exitIndex);
    }

    private sealed record BacktestExecutionResult(BacktestTrade Trade, int ExitCandleIndex);
    private sealed record IndexedStrategyEvent(int Index, StrategyEvent Value);

    private sealed class BacktestIndexes
    {
        private readonly CrossoverLookup longCrossovers;
        private readonly CrossoverLookup shortCrossovers;
        private readonly HashSet<DateTimeOffset> executableLongSignalTimes;
        private readonly HashSet<DateTimeOffset> executableShortSignalTimes;
        private readonly int[] latestLongPivotByCrossoverIndex;
        private readonly int[] latestShortPivotByCrossoverIndex;
        private readonly decimal?[] atr14ByCandleIndex;

        private BacktestIndexes(Dictionary<DateTimeOffset, int> candleIndexByCloseTime, CrossoverLookup longCrossovers, CrossoverLookup shortCrossovers, Dictionary<int, IndicatorSnapshot> snapshotByCandleIndex, HashSet<DateTimeOffset> executableLongSignalTimes, HashSet<DateTimeOffset> executableShortSignalTimes, int[] latestLongPivotByCrossoverIndex, int[] latestShortPivotByCrossoverIndex, decimal?[] atr14ByCandleIndex)
        {
            CandleIndexByCloseTime = candleIndexByCloseTime; this.longCrossovers = longCrossovers; this.shortCrossovers = shortCrossovers; SnapshotByCandleIndex = snapshotByCandleIndex; this.executableLongSignalTimes = executableLongSignalTimes; this.executableShortSignalTimes = executableShortSignalTimes; this.latestLongPivotByCrossoverIndex = latestLongPivotByCrossoverIndex; this.latestShortPivotByCrossoverIndex = latestShortPivotByCrossoverIndex; this.atr14ByCandleIndex = atr14ByCandleIndex;
        }

        public Dictionary<DateTimeOffset, int> CandleIndexByCloseTime { get; }
        public Dictionary<int, IndicatorSnapshot> SnapshotByCandleIndex { get; }
        public bool HasOppositeExecutableSignal(DateTimeOffset closeTimeUtc, SignalDirection direction) => direction == SignalDirection.Long ? executableShortSignalTimes.Contains(closeTimeUtc) : executableLongSignalTimes.Contains(closeTimeUtc);
        public StrategyEvent? FindCrossover(DateTimeOffset signalTimeUtc, SignalDirection direction) => direction == SignalDirection.Long ? longCrossovers.Find(signalTimeUtc) : direction == SignalDirection.Short ? shortCrossovers.Find(signalTimeUtc) : null;

        public InitialStopSelection SelectInitialStop(Candle[] candles, int legacyStopIndex, int signalIndex, IndicatorSnapshot signal, SignalDirection direction, TradingSettings settings)
        {
            if (!settings.UseAdaptiveInitialStop)
            {
                var legacy = FindSwingStop(candles, legacyStopIndex, direction);
                return new(legacy.Price, legacy.Source, legacy.Time, false);
            }
            if (atr14ByCandleIndex[signalIndex] is { } atr) return AdaptiveInitialStopRules.Find(candles, signalIndex, signal, direction, atr);
            var fallback = FindSwingStop(candles, legacyStopIndex, direction);
            return new(fallback.Price, StopSourceType.AdaptiveLegacyFallback, fallback.Time, true);
        }

        public static BacktestIndexes Create(Candle[] candles, IReadOnlyList<StrategyEvent> events, IReadOnlyList<IndicatorSnapshot> snapshots)
        {
            var candleIndexes = new Dictionary<DateTimeOffset, int>(candles.Length);
            for (var candleIndex = 0; candleIndex < candles.Length; candleIndex++) candleIndexes.TryAdd(candles[candleIndex].CloseTimeUtc, candleIndex);
            var snapshotIndexes = new Dictionary<int, IndicatorSnapshot>();
            foreach (var snapshot in snapshots) if (candleIndexes.TryGetValue(snapshot.Time, out var candleIndex)) snapshotIndexes.TryAdd(candleIndex, snapshot);

            var allEvents = events.Select((value, index) => new IndexedStrategyEvent(index, value)).ToArray();
            var longSignalTimes = new HashSet<DateTimeOffset>(); var shortSignalTimes = new HashSet<DateTimeOffset>();
            foreach (var item in allEvents)
            {
                if (item.Value.Status is not (SignalStatus.LongSignal or SignalStatus.ShortSignal)) continue;
                if (item.Value.Direction == SignalDirection.Long) longSignalTimes.Add(item.Value.Time);
                else if (item.Value.Direction == SignalDirection.Short) shortSignalTimes.Add(item.Value.Time);
            }

            var (longPivots, shortPivots) = BuildPivotIndexes(candles);
            return new(candleIndexes, CrossoverLookup.Create(allEvents, SignalDirection.Long), CrossoverLookup.Create(allEvents, SignalDirection.Short), snapshotIndexes, longSignalTimes, shortSignalTimes, longPivots, shortPivots, BuildAtr14(candles));
        }

        private (decimal Price, StopSourceType Source, DateTimeOffset Time) FindSwingStop(Candle[] candles, int crossoverIndex, SignalDirection direction)
        {
            var pivot = direction == SignalDirection.Long ? latestLongPivotByCrossoverIndex[crossoverIndex] : latestShortPivotByCrossoverIndex[crossoverIndex];
            if (pivot >= 0) return (direction == SignalDirection.Long ? candles[pivot].Low : candles[pivot].High, StopSourceType.Pivot, candles[pivot].CloseTimeUtc);
            var start = Math.Max(0, crossoverIndex - 10); var count = Math.Min(10, crossoverIndex);
            if (count == 0) throw new InvalidOperationException("A crossover requires prior completed candles for a stop.");
            var selected = start;
            for (var index = start + 1; index < start + count; index++)
            {
                if (direction == SignalDirection.Long ? candles[index].Low < candles[selected].Low : candles[index].High > candles[selected].High) selected = index;
            }
            return (direction == SignalDirection.Long ? candles[selected].Low : candles[selected].High, StopSourceType.FallbackLookback, candles[selected].CloseTimeUtc);
        }

        private static (int[] Long, int[] Short) BuildPivotIndexes(Candle[] candles)
        {
            var longPivots = new int[candles.Length]; var shortPivots = new int[candles.Length]; var latestLong = -1; var latestShort = -1;
            for (var crossoverIndex = 0; crossoverIndex < candles.Length; crossoverIndex++)
            {
                var candidate = crossoverIndex - 2;
                if (candidate >= 2)
                {
                    var longPivot = candles[candidate].Low < candles[candidate - 1].Low && candles[candidate].Low < candles[candidate - 2].Low && candles[candidate].Low < candles[candidate + 1].Low && candles[candidate].Low < candles[candidate + 2].Low;
                    var shortPivot = candles[candidate].High > candles[candidate - 1].High && candles[candidate].High > candles[candidate - 2].High && candles[candidate].High > candles[candidate + 1].High && candles[candidate].High > candles[candidate + 2].High;
                    if (longPivot) latestLong = candidate;
                    if (shortPivot) latestShort = candidate;
                }
                longPivots[crossoverIndex] = latestLong; shortPivots[crossoverIndex] = latestShort;
            }
            return (longPivots, shortPivots);
        }

        private static decimal?[] BuildAtr14(Candle[] candles)
        {
            var values = new decimal?[candles.Length];
            if (candles.Length < 14) return values;
            decimal atr = 0m;
            for (var index = 0; index < 14; index++) atr += TrueRange(candles, index);
            atr /= 14m; values[13] = atr;
            for (var index = 14; index < candles.Length; index++) { atr = (atr * 13m + TrueRange(candles, index)) / 14m; values[index] = atr; }
            return values;
        }

        private static decimal TrueRange(Candle[] candles, int index)
        {
            var candle = candles[index];
            if (index == 0) return candle.High - candle.Low;
            var previousClose = candles[index - 1].Close;
            return decimal.Max(candle.High - candle.Low, decimal.Max(decimal.Abs(candle.High - previousClose), decimal.Abs(candle.Low - previousClose)));
        }

        private sealed class CrossoverLookup(DateTimeOffset[] times, StrategyEvent?[] latestByTime)
        {
            public StrategyEvent? Find(DateTimeOffset signalTimeUtc)
            {
                var index = Array.BinarySearch(times, signalTimeUtc);
                if (index < 0) index = ~index - 1;
                return index < 0 ? null : latestByTime[index];
            }

            public static CrossoverLookup Create(IReadOnlyList<IndexedStrategyEvent> events, SignalDirection direction)
            {
                var times = new List<DateTimeOffset>(); var latest = new List<StrategyEvent?>(); var latestOriginalIndex = -1; StrategyEvent? latestEvent = null;
                foreach (var group in events.Where(item => item.Value.Direction == direction && item.Value.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover).OrderBy(item => item.Value.Time).GroupBy(item => item.Value.Time))
                {
                    var candidate = group.MaxBy(item => item.Index)!;
                    if (candidate.Index > latestOriginalIndex) { latestOriginalIndex = candidate.Index; latestEvent = candidate.Value; }
                    times.Add(group.Key); latest.Add(latestEvent);
                }
                return new(times.ToArray(), latest.ToArray());
            }
        }
    }
}
