using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

public sealed record BacktestCalculation(IReadOnlyList<BacktestTrade> Trades, BacktestDiagnostics Diagnostics, decimal EndingEquityUsdt);
public sealed record BacktestDiagnostics(int TotalCrossovers, int LongSignals, int ShortSignals, int RejectedByEma100, int RejectedByEmaGap, int RejectedByStopDistance, int RejectedByFees, int ConfirmationFailed, int InvalidStopLoss, int SkippedWhilePositionOpen, int NoEntryCandle, int RejectedByHtfRegime = 0);

public sealed class BacktestEngine(EmaSignalEngine strategy)
{
    public BacktestCalculation Run(IReadOnlyList<Candle> input, TradingSettings settings, StrategyMarketContext? marketContext = null)
    {
        var candles = input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray();
        var evaluation = strategy.Evaluate(candles, settings);
        return RunClosedCandles(candles, settings, evaluation.Events, evaluation.Snapshots, marketContext: marketContext);
    }

    public BacktestCalculation RunResearch(IReadOnlyList<Candle> input, TradingSettings settings, DateTimeOffset requestedStartUtc, DateTimeOffset requestedEndUtc, StrategyMarketContext? marketContext = null)
    {
        var candles = input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray();
        var evaluation = strategy.Evaluate(candles, settings);
        return RunClosedCandles(candles, settings, evaluation.Events, evaluation.Snapshots, requestedStartUtc, requestedEndUtc, marketContext);
    }

    // Research-only event overload keeps segment-boundary behavior deterministic in tests.
    public BacktestCalculation RunResearchWithEvents(IReadOnlyList<Candle> input, TradingSettings settings, IReadOnlyList<StrategyEvent> events, DateTimeOffset requestedStartUtc, DateTimeOffset requestedEndUtc, StrategyMarketContext? marketContext = null)
        => RunClosedCandles(input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray(), settings, events, [], requestedStartUtc, requestedEndUtc, marketContext);

    // This overload keeps execution rules testable independently from EMA calculation.
    public BacktestCalculation RunWithEvents(IReadOnlyList<Candle> input, TradingSettings settings, IReadOnlyList<StrategyEvent> events, StrategyMarketContext? marketContext = null)
    {
        return RunClosedCandles(input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray(), settings, events, [], marketContext: marketContext);
    }

    private static BacktestCalculation RunClosedCandles(Candle[] candles, TradingSettings settings, IReadOnlyList<StrategyEvent> events, IReadOnlyList<IndicatorSnapshot> snapshots, DateTimeOffset? requestedStartUtc = null, DateTimeOffset? requestedEndUtc = null, StrategyMarketContext? marketContext = null)
    {
        var trades = new List<BacktestTrade>(); var invalid = 0; var skipped = 0; var noEntry = 0; var rejectedStop = 0; var rejectedFees = 0; var rejectedHtf = 0; var occupiedUntil = -1;
        var maxExecutionIndex = requestedEndUtc.HasValue ? Array.FindLastIndex(candles, candle => candle.CloseTimeUtc <= requestedEndUtc.Value) : candles.Length - 1;
        var segmentEvents = events.Where(e => (!requestedStartUtc.HasValue || e.Time >= requestedStartUtc.Value) && (!requestedEndUtc.HasValue || e.Time <= requestedEndUtc.Value)).ToArray();
        var equity = settings.SimulatedAccountBalanceUsdt; var reenteredRegimes = new HashSet<DateTimeOffset>();
        foreach (var signal in segmentEvents.Where(e => e.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal))
        {
            var signalIndex = Array.FindIndex(candles, c => c.CloseTimeUtc == signal.Time);
            if (signalIndex < 0 || signalIndex + 1 > maxExecutionIndex || requestedEndUtc.HasValue && candles[signalIndex + 1].OpenTimeUtc > requestedEndUtc.Value) { noEntry++; continue; }
            if (signalIndex < occupiedUntil) { skipped++; continue; }
            var direction = signal.Direction; var crossover = events.LastOrDefault(e => e.Time <= signal.Time && e.Direction == direction && (e.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover));
            var crossoverIndex = Array.FindIndex(candles, c => c.CloseTimeUtc == crossover?.Time); if (crossoverIndex < 0) continue;
            if (!PassesHtf(settings, marketContext, signal.Time, direction, out var htf)) { rejectedHtf++; continue; }
            var stop = InitialStopSelector.Select(candles, crossoverIndex, signalIndex, signal.Snapshot, direction, settings); var entryIndex = signalIndex + 1; var entry = candles[entryIndex].Open;
            if ((direction == SignalDirection.Long && stop.Price >= entry) || (direction == SignalDirection.Short && stop.Price <= entry)) { invalid++; continue; }
            if (settings.MaxStopDistancePercent > 0 && TradeMath.StopDistancePercent(entry, stop.Price) > settings.MaxStopDistancePercent) { rejectedStop++; continue; }
            var size = TradeMath.CalculatePositionSize(settings, equity, entry);
            var target = TradeMath.InitialTarget(entry, stop.Price, direction, settings.RiskReward);
            if (TradeMath.ExpectedNetAtTarget(entry, target, size.Quantity, direction, settings.FeePercentPerSide) <= 0) { rejectedFees++; continue; }
            var trade = Execute(candles, entryIndex, crossoverIndex, signal, direction, entry, stop, settings, size, maxExecutionIndex, htf); trades.Add(trade);
            if (settings.PositionSizingMode == PositionSizingMode.MarginPercent) equity += trade.NetPnlUsdt;
            // A SL/TP exit is intrabar. A signal at that candle's close may enter on the following open;
            // signals from earlier closes remain unavailable while the position was open.
            occupiedUntil = Array.FindIndex(candles, c => c.CloseTimeUtc == trade.ExitTimeUtc);
            if (settings.SameTrendReentryEnabled && snapshots.Count > 0 && (trade.ExitReason is BacktestExitReason.StopLoss or BacktestExitReason.TrailingStop) && reenteredRegimes.Add(crossover!.Time))
            {
                var opposite = direction == SignalDirection.Long ? TrendDirection.Down : TrendDirection.Up;
                var reentry = snapshots.Where(snapshot => { var index = Array.FindIndex(candles, candle => candle.CloseTimeUtc == snapshot.Time); var ageBars = candles.Count(candle => candle.CloseTimeUtc > crossover.Time && candle.CloseTimeUtc <= snapshot.Time); return index >= occupiedUntil && index <= maxExecutionIndex && ageBars <= settings.MaxReentryAgeBars; }).TakeWhile(snapshot => snapshot.TrendDirection != opposite).FirstOrDefault(snapshot => IsContinuation(snapshot, direction, settings));
                if (reentry is not null)
                {
                    var reentrySignalIndex = Array.FindIndex(candles, candle => candle.CloseTimeUtc == reentry.Time);
                    if (reentrySignalIndex >= 0 && reentrySignalIndex + 1 <= maxExecutionIndex && (!requestedEndUtc.HasValue || candles[reentrySignalIndex + 1].OpenTimeUtc <= requestedEndUtc.Value))
                    {
                        if (!PassesHtf(settings, marketContext, reentry.Time, direction, out var reentryHtf)) { rejectedHtf++; continue; }
                        var reentrySignal = new StrategyEvent(reentry.Time, direction, direction == SignalDirection.Long ? SignalStatus.ReentryLongSignal : SignalStatus.ReentryShortSignal, reentry);
                        var reentryStop = InitialStopSelector.Select(candles, reentrySignalIndex, reentrySignalIndex, reentry, direction, settings); var reentryEntry = candles[reentrySignalIndex + 1].Open;
                        if ((direction == SignalDirection.Long ? reentryStop.Price < reentryEntry : reentryStop.Price > reentryEntry) && (settings.MaxStopDistancePercent == 0 || TradeMath.StopDistancePercent(reentryEntry, reentryStop.Price) <= settings.MaxStopDistancePercent))
                        {
                            var reentrySize = TradeMath.CalculatePositionSize(settings, equity, reentryEntry); var reentryTarget = TradeMath.InitialTarget(reentryEntry, reentryStop.Price, direction, settings.RiskReward);
                            if (TradeMath.ExpectedNetAtTarget(reentryEntry, reentryTarget, reentrySize.Quantity, direction, settings.FeePercentPerSide) > 0)
                            {
                                var second = Execute(candles, reentrySignalIndex + 1, crossoverIndex, reentrySignal, direction, reentryEntry, reentryStop, settings, reentrySize, maxExecutionIndex, reentryHtf);
                                second.IsReentry = true; second.TrendRegimeCrossoverTimeUtc = crossover.Time; second.ReentryAgeBars = candles.Count(candle => candle.CloseTimeUtc > crossover.Time && candle.CloseTimeUtc <= reentry.Time); trades.Add(second);
                                if (settings.PositionSizingMode == PositionSizingMode.MarginPercent) equity += second.NetPnlUsdt;
                                occupiedUntil = Array.FindIndex(candles, candle => candle.CloseTimeUtc == second.ExitTimeUtc);
                            }
                        }
                    }
                }
            }
        }
        return new(trades, new(segmentEvents.Count(e => e.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover), segmentEvents.Count(e => e.Status == SignalStatus.LongSignal), segmentEvents.Count(e => e.Status == SignalStatus.ShortSignal), segmentEvents.Count(e => e.Status == SignalStatus.RejectedByEma100Filter), segmentEvents.Count(e => e.Status == SignalStatus.RejectedByEmaGap), rejectedStop, rejectedFees, segmentEvents.Count(e => e.Status == SignalStatus.ConfirmationFailed), invalid, skipped, noEntry, rejectedHtf), equity);
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

    private static BacktestTrade Execute(Candle[] candles, int entryIndex, int crossoverIndex, StrategyEvent signal, SignalDirection direction, decimal entry, InitialStopSelection stop, TradingSettings settings, TradeMath.PositionSize size, int maxExecutionIndex, HigherTimeframeDiagnostic? htf)
    {
        var risk = decimal.Abs(entry - stop.Price); var originalTp = TradeMath.InitialTarget(entry, stop.Price, direction, settings.RiskReward); var managementEvents = new List<BacktestTradeEvent> { new() { TimeUtc = candles[entryIndex].OpenTimeUtc, EffectiveTimeUtc = candles[entryIndex].OpenTimeUtc, Type = BacktestTradeEventType.Entry, MarketPrice = entry } };
        var currentStop = stop.Price; var currentTp = originalTp; var extended = false; var max = entry; var min = entry; var exitIndex = maxExecutionIndex; var exitPrice = candles[maxExecutionIndex].Close; var reason = BacktestExitReason.EndOfData; var conflict = false;
        for (var i = entryIndex; i <= maxExecutionIndex; i++)
        {
            var c = candles[i];
            var sl = direction == SignalDirection.Long ? c.Low <= currentStop : c.High >= currentStop;
            var tp = direction == SignalDirection.Long ? c.High >= currentTp : c.Low <= currentTp;
            if (sl || tp)
            {
                conflict = sl && tp;
                reason = sl ? currentStop == stop.Price ? BacktestExitReason.StopLoss : BacktestExitReason.TrailingStop : BacktestExitReason.TakeProfit;
                exitPrice = sl ? currentStop : currentTp;
                exitIndex = i;
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
        }
        var quantity = size.Quantity; var notional = size.NotionalUsdt; var entryFee = TradeMath.Fee(entry, quantity, settings.FeePercentPerSide); var exitFee = TradeMath.Fee(exitPrice, quantity, settings.FeePercentPerSide); var gross = TradeMath.GrossPnl(entry, exitPrice, quantity, direction); var net = gross - entryFee - exitFee; var snap = signal.Snapshot;
        managementEvents.Add(new BacktestTradeEvent { TimeUtc = candles[exitIndex].CloseTimeUtc, EffectiveTimeUtc = candles[exitIndex].CloseTimeUtc, Type = BacktestTradeEventType.Exit, MarketPrice = exitPrice });
        return new BacktestTrade { Direction = direction, CrossoverTimeUtc = candles[crossoverIndex].CloseTimeUtc, SignalTimeUtc = signal.Time, EntryTimeUtc = candles[entryIndex].OpenTimeUtc, ExitTimeUtc = candles[exitIndex].CloseTimeUtc, EntryPrice = entry, ExitPrice = exitPrice, Quantity = quantity, EntryNotionalUsdt = notional, PositionSizingMode = settings.PositionSizingMode, AccountEquityAtEntryUsdt = size.AccountEquityAtEntryUsdt, MarginUsedUsdt = size.MarginUsedUsdt, Leverage = size.Leverage, InitialStopLoss = stop.Price, FinalStopLoss = currentStop, StopSourceType = stop.Source, StopSourceTimeUtc = stop.Time, OriginalTakeProfit = originalTp, FinalTakeProfit = currentTp, TakeProfitExtended = extended, ExitReason = reason, SameCandleExitConflict = conflict, EntryFeeUsdt = entryFee, ExitFeeUsdt = exitFee, TotalFeesUsdt = entryFee + exitFee, GrossPnlUsdt = gross, NetPnlUsdt = net, NetPnlPercent = net / notional * 100m, GrossRMultiple = gross / (risk * quantity), NetRMultiple = net / (risk * quantity), MfePrice = direction == SignalDirection.Long ? max - entry : entry - min, MfePercent = (direction == SignalDirection.Long ? max - entry : entry - min) / entry * 100m, MaePrice = direction == SignalDirection.Long ? entry - min : max - entry, MaePercent = (direction == SignalDirection.Long ? entry - min : max - entry) / entry * 100m, SignalOpen = snap.Open, SignalClose = snap.Close, SignalEma9 = snap.Ema9, SignalEma15 = snap.Ema15, SignalEma100 = snap.Ema100, SignalGapPercent = snap.GapPercent, SignalGapState = snap.GapState, HtfTimeframe = htf?.Timeframe, SignalHtfCandleCloseTimeUtc = htf?.CandleCloseTimeUtc, SignalHtfEma100Slope20Percent = htf?.Ema100Slope20Percent, SignalHtfAtr14Percent = htf?.Atr14Percent, UseAdaptiveInitialStop = stop.UseAdaptiveInitialStop, SignalAtr14 = stop.Atr14, ReversalPowerScore = stop.ReversalPowerScore, ReversalPowerBand = stop.ReversalPowerBand, StopAnchorPrice = stop.AnchorPrice, StopBuffer = stop.Buffer, Events = managementEvents };
    }
}
