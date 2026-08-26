using System.Diagnostics;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

// Deliberately separate from BacktestEngine: that engine remains the legacy/optimizer path.
// This executor consumes the same ordinary strategy candles, but reconstructs executable
// Bid/Ask prices from MT5 MqlRates spread evidence.
public sealed class Mt5HistoricalBacktestEngine(EmaSignalEngine strategy, IMt5TradeCalculator calculator)
{
    public const string SpreadModel = "MT5 MqlRates bar spread, constant within bar";

    public static string? ValidateNativeInstrument(InstrumentSpec spec)
    {
        if (spec.HistoricalChartMode != HistoricalChartMode.Bid) return "MT5 historical chart mode is not Bid; Exness-native execution reconstruction is unavailable.";
        if (spec.PointSize <= 0m || spec.ContractSize <= 0m || spec.VolumeMin <= 0m || spec.VolumeMax < spec.VolumeMin || spec.VolumeStep <= 0m)
            return "MT5 instrument economics are incomplete or invalid; Exness-native execution reconstruction is unavailable.";
        return null;
    }

    // Exposed for regression proof only: the native executor deliberately evaluates the unchanged Candle projection.
    public StrategyEvaluation EvaluateStrategy(IReadOnlyList<Mt5HistoricalExecutionBar> input, TradingSettings settings)
        => strategy.Evaluate(input.Where(item => item.IsClosed).OrderBy(item => item.OpenTimeUtc).Select(item => item.ToCandle()).ToArray(), settings);

    public async Task<Mt5HistoricalBacktestCalculation> RunAsync(IReadOnlyList<Mt5HistoricalExecutionBar> input, TradingSettings settings, InstrumentCatalogItem instrument, decimal commissionPerLotPerSide, string accountCurrency, StrategyMarketContext? context, CancellationToken token)
    {
        if (ValidateNativeInstrument(instrument.Spec) is { } invalidInstrument) throw new InvalidOperationException(invalidInstrument);
        if (commissionPerLotPerSide < 0m) throw new InvalidOperationException("Configured commission per lot per side is invalid.");
        if (string.IsNullOrWhiteSpace(accountCurrency)) throw new InvalidOperationException("MT5 account currency is unavailable.");
        var bars = input.Where(x => x.IsClosed).OrderBy(x => x.OpenTimeUtc).ToArray();
        if (bars.Any(x => x.SpreadPoints < 0)) throw new InvalidOperationException("MT5 historical spread points are invalid.");
        var candles = bars.Select(x => x.ToCandle()).ToArray();
        var evaluationStopwatch = Stopwatch.StartNew();
        var evaluation = EvaluateStrategy(bars, settings);
        evaluationStopwatch.Stop();
        var byClose = candles.Select((c, i) => (c.CloseTimeUtc, i)).ToDictionary(x => x.CloseTimeUtc, x => x.i);
        var crossovers = evaluation.Events.Where(x => x.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover)
            .GroupBy(x => x.Time).ToDictionary(x => x.Key, x => x.Last());
        var oppositeSignals = evaluation.Events.Where(x => x.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal)
            .GroupBy(x => x.Time).ToDictionary(x => x.Key, x => x.Last());
        var trades = new List<BacktestTrade>(); var equity = settings.PaperStartingBalance; var occupiedUntil = -1; var reenteredRegimes = new HashSet<DateTimeOffset>();
        var snapshotsByIndex = evaluation.Snapshots.Where(snapshot => byClose.ContainsKey(snapshot.Time)).ToDictionary(snapshot => byClose[snapshot.Time]);
        var rejectedStop = 0; var rejectedCosts = 0; var invalid = 0; var skipped = 0; var noEntry = 0; var rejectedTradeMode = 0; var rejectedInsufficientMargin = 0; var rejectedInvalidVolume = 0;
        var timer = Stopwatch.StartNew(); var metrics = new EconomicsMetrics();
        foreach (var signal in evaluation.Events.Where(x => x.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal).OrderBy(x => x.Time))
        {
            token.ThrowIfCancellationRequested();
            if (!byClose.TryGetValue(signal.Time, out var signalIndex) || signalIndex + 1 >= bars.Length) { noEntry++; continue; }
            if (signalIndex < occupiedUntil) { skipped++; continue; }
            var direction = signal.Direction;
            if (!DemoStrategyExecutionRules.AllowsDirection(instrument.TradeMode, direction)) { rejectedTradeMode++; continue; }
            if (!crossovers.TryGetValue(signal.Time, out var crossover) || !byClose.TryGetValue(crossover.Time, out var crossoverIndex)) continue;
            if (!PassesHtf(settings, context, signal.Time, direction, out var htf)) continue;
            var stop = InitialStopSelector.Select(candles, crossoverIndex, signalIndex, signal.Snapshot, direction, settings);
            var entryIndex = signalIndex + 1; var entryBar = Quote(bars[entryIndex], instrument.Spec.PointSize);
            var entry = direction == SignalDirection.Long ? entryBar.AskOpen : entryBar.BidOpen;
            var target = TradeMath.InitialTarget(entry, stop.Price, direction, settings.RiskReward);
            if ((direction == SignalDirection.Long && stop.Price >= entry) || (direction == SignalDirection.Short && stop.Price <= entry)) { invalid++; continue; }
            if (settings.MaxStopDistancePercent > 0m && TradeMath.StopDistancePercent(entry, stop.Price) > settings.MaxStopDistancePercent) { rejectedStop++; continue; }
            if (!DemoStrategyExecutionRules.StopAndTargetMeetBrokerMinimum(instrument.Spec, direction, entry, entryBar.BidOpen, entryBar.AskOpen, stop.Price, target)) { rejectedStop++; continue; }
            var sizing = await SizeAsync(instrument.Spec, direction, entry, equity, settings, metrics, token);
            if (!sizing.Success)
            {
                if (sizing.FailureReason == NativeSizingFailure.InsufficientMargin) rejectedInsufficientMargin++;
                else if (sizing.FailureReason == NativeSizingFailure.InvalidVolume) rejectedInvalidVolume++;
                continue;
            }
            var sizedLots = sizing.Lots!;
            var expected = await ProfitAsync(instrument.Spec.BrokerSymbol, direction, sizedLots.Lots, entry, target, metrics, token);
            var roundTrip = 2m * sizedLots.Lots * commissionPerLotPerSide;
            if (expected <= roundTrip) { rejectedCosts++; continue; }
            var executed = await ExecuteAsync(bars, candles, entryIndex, crossoverIndex, signal, direction, entry, entryBar, stop, target, sizedLots, commissionPerLotPerSide, settings, oppositeSignals, instrument.Spec, htf, metrics, token);
            trades.Add(executed.Trade); equity += executed.Trade.NetPnl!.Value; occupiedUntil = executed.ExitIndex;
            // Keep the legacy BacktestEngine's deliberately strict first-continuation-candidate semantics.
            if (settings.SameTrendReentryEnabled && executed.Trade.ExitReason is BacktestExitReason.StopLoss or BacktestExitReason.TrailingStop && reenteredRegimes.Add(crossover.Time))
            {
                var oppositeTrend = direction == SignalDirection.Long ? TrendDirection.Down : TrendDirection.Up;
                var lastCandidate = Math.Min(bars.Length - 2, crossoverIndex + settings.MaxReentryAgeBars);
                for (var candidate = Math.Max(occupiedUntil, 0); candidate <= lastCandidate; candidate++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!snapshotsByIndex.TryGetValue(candidate, out var reentrySnapshot)) continue;
                    if (reentrySnapshot.TrendDirection == oppositeTrend) break;
                    if (!IsContinuation(reentrySnapshot, direction, settings)) continue;
                    // The first continuation is consumed even when native broker/economic checks reject it.
                    if (!PassesHtf(settings, context, reentrySnapshot.Time, direction, out var reentryHtf)) break;
                    var reentryStop = InitialStopSelector.Select(candles, candidate, candidate, reentrySnapshot, direction, settings);
                    var reentryIndex = candidate + 1; var reentryQuote = Quote(bars[reentryIndex], instrument.Spec.PointSize); var reentryEntry = direction == SignalDirection.Long ? reentryQuote.AskOpen : reentryQuote.BidOpen;
                    var reentryTarget = TradeMath.InitialTarget(reentryEntry, reentryStop.Price, direction, settings.RiskReward);
                    if (!DemoStrategyExecutionRules.AllowsDirection(instrument.TradeMode, direction)) { rejectedTradeMode++; break; }
                    if ((direction == SignalDirection.Long ? reentryStop.Price >= reentryEntry : reentryStop.Price <= reentryEntry) ||
                        settings.MaxStopDistancePercent > 0m && TradeMath.StopDistancePercent(reentryEntry, reentryStop.Price) > settings.MaxStopDistancePercent ||
                        !DemoStrategyExecutionRules.StopAndTargetMeetBrokerMinimum(instrument.Spec, direction, reentryEntry, reentryQuote.BidOpen, reentryQuote.AskOpen, reentryStop.Price, reentryTarget)) break;
                    var reentrySizing = await SizeAsync(instrument.Spec, direction, reentryEntry, equity, settings, metrics, token);
                    if (!reentrySizing.Success)
                    {
                        if (reentrySizing.FailureReason == NativeSizingFailure.InsufficientMargin) rejectedInsufficientMargin++;
                        else if (reentrySizing.FailureReason == NativeSizingFailure.InvalidVolume) rejectedInvalidVolume++;
                        break;
                    }
                    var reentryLots = reentrySizing.Lots!;
                    var reentryExpected = await ProfitAsync(instrument.Spec.BrokerSymbol, direction, reentryLots.Lots, reentryEntry, reentryTarget, metrics, token);
                    if (reentryExpected <= 2m * reentryLots.Lots * commissionPerLotPerSide) { rejectedCosts++; break; }
                    var reentrySignal = new StrategyEvent(reentrySnapshot.Time, direction, direction == SignalDirection.Long ? SignalStatus.ReentryLongSignal : SignalStatus.ReentryShortSignal, reentrySnapshot);
                    var reentryExecution = await ExecuteAsync(bars, candles, reentryIndex, crossoverIndex, reentrySignal, direction, reentryEntry, reentryQuote, reentryStop, reentryTarget, reentryLots, commissionPerLotPerSide, settings, oppositeSignals, instrument.Spec, reentryHtf, metrics, token);
                    reentryExecution.Trade.IsReentry = true; reentryExecution.Trade.TrendRegimeCrossoverTimeUtc = crossover.Time; reentryExecution.Trade.ReentryAgeBars = candidate - crossoverIndex;
                    trades.Add(reentryExecution.Trade); equity += reentryExecution.Trade.NetPnl!.Value; occupiedUntil = reentryExecution.ExitIndex;
                    break;
                }
            }
        }
        timer.Stop();
        var diagnostics = new BacktestDiagnostics(
            evaluation.Events.Count(x => x.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover),
            evaluation.Events.Count(x => x.Status == SignalStatus.LongSignal), evaluation.Events.Count(x => x.Status == SignalStatus.ShortSignal),
            evaluation.Events.Count(x => x.Status == SignalStatus.RejectedByEma100Filter), evaluation.Events.Count(x => x.Status == SignalStatus.RejectedByEmaGap), rejectedStop, 0,
            evaluation.Events.Count(x => x.Status == SignalStatus.ConfirmationFailed), invalid, skipped, noEntry,
            0, rejectedInsufficientMargin, rejectedInvalidVolume, rejectedTradeMode);
        return new(trades, diagnostics, equity, rejectedCosts, metrics.Calls, timer.ElapsedMilliseconds, evaluationStopwatch.ElapsedMilliseconds);
    }

    private async Task<Mt5HistoricalExecution> ExecuteAsync(Mt5HistoricalExecutionBar[] bars, Candle[] candles, int entryIndex, int crossoverIndex, StrategyEvent signal, SignalDirection direction, decimal entry, QuoteBar entryQuote, InitialStopSelection initialStop, decimal originalTarget, Mt5Lots sizing, decimal commission, TradingSettings settings, IReadOnlyDictionary<DateTimeOffset, StrategyEvent> signals, InstrumentSpec spec, HigherTimeframeDiagnostic? htf, EconomicsMetrics metrics, CancellationToken token)
    {
        var stop = initialStop.Price; var target = originalTarget; var extended = false; var best = entry; var worst = entry; var exitIndex = bars.Length - 1; var exitQuote = Quote(bars[^1], spec.PointSize); var exit = direction == SignalDirection.Long ? exitQuote.BidClose : exitQuote.AskClose; var reason = BacktestExitReason.EndOfData; var conflict = false; var events = new List<BacktestTradeEvent> { new() { TimeUtc = bars[entryIndex].OpenTimeUtc, EffectiveTimeUtc = bars[entryIndex].OpenTimeUtc, Type = BacktestTradeEventType.Entry, MarketPrice = entry } };
        for (var i = entryIndex; i < bars.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var q = Quote(bars[i], spec.PointSize);
            if (i > entryIndex && settings.ExitOnOppositeCrossover && signals.TryGetValue(candles[i - 1].CloseTimeUtc, out var opposite) && opposite.Direction != direction)
            { exit = direction == SignalDirection.Long ? q.BidOpen : q.AskOpen; exitIndex = i; exitQuote = q; reason = BacktestExitReason.OppositeCrossover; break; }
            var low = direction == SignalDirection.Long ? q.BidLow : q.AskLow; var high = direction == SignalDirection.Long ? q.BidHigh : q.AskHigh;
            var hitStop = direction == SignalDirection.Long ? low <= stop : high >= stop;
            var hitTarget = direction == SignalDirection.Long ? high >= target : low <= target;
            if (hitStop || hitTarget)
            { conflict = hitStop && hitTarget; exit = hitStop ? stop : target; exitIndex = i; exitQuote = q; reason = hitStop ? stop == initialStop.Price ? BacktestExitReason.StopLoss : BacktestExitReason.TrailingStop : BacktestExitReason.TakeProfit; break; }
            best = direction == SignalDirection.Long ? Math.Max(best, high) : Math.Min(best, low);
            worst = direction == SignalDirection.Long ? Math.Min(worst, low) : Math.Max(worst, high);
            if (settings.TrailingStopEnabled)
            {
                var progress = TradeMath.Progress(entry, originalTarget, best, direction); var lockPercent = TradeMath.LockPercent(progress);
                if (lockPercent > 0m)
                {
                    var normal = TradeMath.TrailingStop(entry, originalTarget, direction, lockPercent);
                    var breakeven = await EconomicBreakEvenAsync(spec.BrokerSymbol, direction, sizing.Lots, entry, commission, spec, metrics, token);
                    var next = direction == SignalDirection.Long ? Math.Max(normal, breakeven) : Math.Min(normal, breakeven);
                    if (direction == SignalDirection.Long ? next > stop : next < stop) { var old = stop; stop = next; events.Add(new() { TimeUtc = bars[i].CloseTimeUtc, Type = BacktestTradeEventType.TrailingStopMoved, MarketPrice = direction == SignalDirection.Long ? q.BidClose : q.AskClose, OldStop = old, NewStop = stop, ProgressPercent = progress }); }
                }
                if (progress >= 70m && !extended) { var old = target; target = TradeMath.ExtendedTarget(entry, originalTarget, direction); extended = true; events.Add(new() { TimeUtc = bars[i].CloseTimeUtc, Type = BacktestTradeEventType.TakeProfitExtended, MarketPrice = direction == SignalDirection.Long ? q.BidClose : q.AskClose, OldTakeProfit = old, NewTakeProfit = target, ProgressPercent = progress }); }
            }
        }
        var gross = await ProfitAsync(spec.BrokerSymbol, direction, sizing.Lots, entry, exit, metrics, token); var sideCommission = sizing.Lots * commission; var net = gross - sideCommission * 2m;
        var risk = decimal.Abs(await ProfitAsync(spec.BrokerSymbol, direction, sizing.Lots, entry, initialStop.Price, metrics, token));
        events.Add(new() { TimeUtc = bars[exitIndex].CloseTimeUtc, EffectiveTimeUtc = bars[exitIndex].CloseTimeUtc, Type = BacktestTradeEventType.Exit, MarketPrice = exit });
        var snap = signal.Snapshot;
        var trade = new BacktestTrade { Direction = direction, CrossoverTimeUtc = candles[crossoverIndex].CloseTimeUtc, SignalTimeUtc = signal.Time, EntryTimeUtc = bars[entryIndex].OpenTimeUtc, ExitTimeUtc = reason == BacktestExitReason.OppositeCrossover ? bars[exitIndex].OpenTimeUtc : bars[exitIndex].CloseTimeUtc, EntryPrice = entry, ExitPrice = exit, Quantity = sizing.Lots, PositionSizingMode = settings.PositionSizingMode, InitialStopLoss = initialStop.Price, FinalStopLoss = stop, StopSourceType = initialStop.Source, StopSourceTimeUtc = initialStop.Time, OriginalTakeProfit = originalTarget, FinalTakeProfit = target, TakeProfitExtended = extended, ExitReason = reason, SameCandleExitConflict = conflict, GrossPnlUsdt = gross, NetPnlUsdt = net, EntryFeeUsdt = sideCommission, ExitFeeUsdt = sideCommission, TotalFeesUsdt = sideCommission * 2m, NetPnlPercent = sizing.Equity == 0 ? 0 : net / sizing.Equity * 100m, GrossRMultiple = risk == 0 ? 0 : gross / risk, NetRMultiple = risk == 0 ? 0 : net / risk, MfePrice = direction == SignalDirection.Long ? best - entry : entry - best, MaePrice = direction == SignalDirection.Long ? entry - worst : worst - entry, SignalOpen = snap.Open, SignalClose = snap.Close, SignalEma9 = snap.Ema9, SignalEma15 = snap.Ema15, SignalEma100 = snap.Ema100, SignalGapPercent = snap.GapPercent, SignalGapState = snap.GapState, HtfTimeframe = htf?.Timeframe, SignalHtfCandleCloseTimeUtc = htf?.CandleCloseTimeUtc, SignalHtfEma100Slope20Percent = htf?.Ema100Slope20Percent, SignalHtfAtr14Percent = htf?.Atr14Percent, UseAdaptiveInitialStop = initialStop.UseAdaptiveInitialStop, SignalAtr14 = initialStop.Atr14, ReversalPowerScore = initialStop.ReversalPowerScore, ReversalPowerBand = initialStop.ReversalPowerBand, StopAnchorPrice = initialStop.AnchorPrice, StopBuffer = initialStop.Buffer, Events = events,
            Lots = sizing.Lots, EntryBid = entryQuote.BidOpen, EntryAsk = entryQuote.AskOpen, EntrySpread = entryQuote.Spread, ExitBid = reason is BacktestExitReason.StopLoss or BacktestExitReason.TakeProfit or BacktestExitReason.TrailingStop ? (direction == SignalDirection.Long ? exit : exit - exitQuote.Spread) : exitQuote.BidClose, ExitAsk = reason is BacktestExitReason.StopLoss or BacktestExitReason.TakeProfit or BacktestExitReason.TrailingStop ? (direction == SignalDirection.Short ? exit : exit + exitQuote.Spread) : exitQuote.AskClose, ExitSpread = exitQuote.Spread, RequiredMargin = sizing.Margin, MarginUsed = sizing.Margin, AccountEquityAtEntry = sizing.Equity, EntryCommission = sideCommission, ExitCommission = sideCommission, RoundTripCommission = sideCommission * 2m, GrossPnl = gross, NetPnl = net, InitialRiskAmount = risk };
        return new(trade, exitIndex);
    }

    private async Task<NativeSizingResult> SizeAsync(InstrumentSpec spec, SignalDirection direction, decimal entry, decimal equity, TradingSettings settings, EconomicsMetrics metrics, CancellationToken token)
    {
        if (settings.PaperPositionSizingMode == PaperPositionSizingMode.FixedLots)
        {
            if (DemoStrategyExecutionRules.ValidateFixedLots(spec, settings.PaperFixedLots) is not null) return NativeSizingResult.InvalidVolume;
            var margin = await MarginAsync(spec.BrokerSymbol, direction, settings.PaperFixedLots, entry, metrics, token);
            return margin <= equity ? NativeSizingResult.For(new(settings.PaperFixedLots, margin, equity)) : NativeSizingResult.InsufficientMargin;
        }
        var budget = equity * settings.PaperMarginPerTradePercent / 100m;
        if (budget <= 0m) return NativeSizingResult.InsufficientMargin;
        var probeMargin = await MarginAsync(spec.BrokerSymbol, direction, spec.VolumeMin, entry, metrics, token);
        if (probeMargin <= 0m || probeMargin > budget) return NativeSizingResult.InsufficientMargin;
        var lots = NormalizeDown(Math.Min(spec.VolumeMax, spec.VolumeMin * budget / probeMargin), spec);
        if (spec.VolumeLimit is > 0m) lots = Math.Min(lots, spec.VolumeLimit.Value);
        if (lots < spec.VolumeMin) return NativeSizingResult.InvalidVolume;
        for (var attempt = 0; attempt < 4 && lots >= spec.VolumeMin; attempt++)
        {
            var margin = await MarginAsync(spec.BrokerSymbol, direction, lots, entry, metrics, token);
            if (margin <= budget && margin <= equity) return NativeSizingResult.For(new(lots, margin, equity));
            lots = NormalizeDown(lots - spec.VolumeStep, spec);
        }
        return NativeSizingResult.InsufficientMargin;
    }

    private async Task<decimal> EconomicBreakEvenAsync(string symbol, SignalDirection direction, decimal lots, decimal entry, decimal commission, InstrumentSpec spec, EconomicsMetrics metrics, CancellationToken token)
    {
        var cost = 2m * lots * commission;
        if (cost == 0m) return entry;
        if (spec.TickSize is not > 0m || spec.TickValueProfit is not > 0m || lots <= 0m) throw new InvalidOperationException("Cannot establish commission-aware MT5 break-even.");
        var delta = cost / (lots * (spec.TickValueProfit.Value / spec.TickSize.Value));
        var candidate = direction == SignalDirection.Long ? entry + delta : entry - delta;
        var profit = await ProfitAsync(symbol, direction, lots, entry, candidate, metrics, token);
        if (profit < cost) throw new InvalidOperationException("MT5 break-even validation failed.");
        return candidate;
    }

    private async Task<decimal> MarginAsync(string symbol, SignalDirection direction, decimal lots, decimal entry, EconomicsMetrics metrics, CancellationToken token) { metrics.Calls++; return (await calculator.CalculateMarginAsync(new(symbol, direction == SignalDirection.Long ? "Long" : "Short", lots, entry), token)).RequiredMargin; }
    private async Task<decimal> ProfitAsync(string symbol, SignalDirection direction, decimal lots, decimal entry, decimal exit, EconomicsMetrics metrics, CancellationToken token) { metrics.Calls++; return (await calculator.CalculateProfitAsync(new(symbol, direction == SignalDirection.Long ? "Long" : "Short", lots, entry, exit), token)).Profit; }
    private static decimal NormalizeDown(decimal lots, InstrumentSpec spec) => spec.VolumeMin + decimal.Floor((lots - spec.VolumeMin) / spec.VolumeStep) * spec.VolumeStep;
    private static bool PassesHtf(TradingSettings s, StrategyMarketContext? c, DateTimeOffset t, SignalDirection d, out HigherTimeframeDiagnostic? diagnostic) { diagnostic = null; if (!s.UseHtfRegimeFilter) return true; diagnostic = HigherTimeframeRegime.Calculate(t, d, c?.HigherTimeframe, c?.HigherTimeframeCandles); return HigherTimeframeRegime.PassesH2(diagnostic, d); }
    private static bool IsContinuation(IndicatorSnapshot snapshot, SignalDirection direction, TradingSettings settings)
    {
        var directional = direction == SignalDirection.Long
            ? snapshot.Ema9 > snapshot.Ema15 && snapshot.Close > snapshot.Ema9 && snapshot.Close > snapshot.Ema15 && snapshot.Close > snapshot.Open
            : snapshot.Ema9 < snapshot.Ema15 && snapshot.Close < snapshot.Ema9 && snapshot.Close < snapshot.Ema15 && snapshot.Close < snapshot.Open;
        if (!directional) return false;
        if (settings.UseEma100Filter && (!snapshot.Ema100.HasValue || (direction == SignalDirection.Long ? snapshot.Ema9 <= snapshot.Ema100 || snapshot.Ema15 <= snapshot.Ema100 : snapshot.Ema9 >= snapshot.Ema100 || snapshot.Ema15 >= snapshot.Ema100))) return false;
        return settings.MinEmaGapPercent == 0m || snapshot.GapPercent >= settings.MinEmaGapPercent;
    }
    private static QuoteBar Quote(Mt5HistoricalExecutionBar bar, decimal point) { var spread = bar.SpreadPoints * point; return new(bar.Open, bar.High, bar.Low, bar.Close, bar.Open + spread, bar.High + spread, bar.Low + spread, bar.Close + spread, spread); }
    private enum NativeSizingFailure { InsufficientMargin, InvalidVolume }
    private sealed record NativeSizingResult(Mt5Lots? Lots, NativeSizingFailure? FailureReason)
    {
        public bool Success => Lots is not null;
        public static NativeSizingResult For(Mt5Lots lots) => new(lots, null);
        public static NativeSizingResult InsufficientMargin { get; } = new(null, NativeSizingFailure.InsufficientMargin);
        public static NativeSizingResult InvalidVolume { get; } = new(null, NativeSizingFailure.InvalidVolume);
    }
    private sealed record Mt5Lots(decimal Lots, decimal Margin, decimal Equity);
    private sealed record Mt5HistoricalExecution(BacktestTrade Trade, int ExitIndex);
    private sealed record QuoteBar(decimal BidOpen, decimal BidHigh, decimal BidLow, decimal BidClose, decimal AskOpen, decimal AskHigh, decimal AskLow, decimal AskClose, decimal Spread);
    private sealed class EconomicsMetrics { public int Calls; }
}

public sealed record Mt5HistoricalBacktestCalculation(IReadOnlyList<BacktestTrade> Trades, BacktestDiagnostics Diagnostics, decimal EndingBalance, int RejectedByTradingCosts, int EconomicsCallCount, long EconomicsElapsedMilliseconds, long StrategyEvaluationElapsedMilliseconds);
