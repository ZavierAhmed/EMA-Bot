using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

public sealed record BacktestCalculation(IReadOnlyList<BacktestTrade> Trades, BacktestDiagnostics Diagnostics);
public sealed record BacktestDiagnostics(int TotalCrossovers, int LongSignals, int ShortSignals, int RejectedByEma100, int ConfirmationFailed, int InvalidStopLoss, int SkippedWhilePositionOpen, int NoEntryCandle);

public sealed class BacktestEngine(EmaSignalEngine strategy)
{
    public BacktestCalculation Run(IReadOnlyList<Candle> input, TradingSettings settings)
    {
        var candles = input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray();
        return RunClosedCandles(candles, settings, strategy.Evaluate(candles, settings).Events);
    }

    // This overload keeps execution rules testable independently from EMA calculation.
    public BacktestCalculation RunWithEvents(IReadOnlyList<Candle> input, TradingSettings settings, IReadOnlyList<StrategyEvent> events)
    {
        return RunClosedCandles(input.Where(c => c.IsClosed).OrderBy(c => c.OpenTimeUtc).ToArray(), settings, events);
    }

    private static BacktestCalculation RunClosedCandles(Candle[] candles, TradingSettings settings, IReadOnlyList<StrategyEvent> events)
    {
        var trades = new List<BacktestTrade>(); var invalid = 0; var skipped = 0; var noEntry = 0; var occupiedUntil = -1;
        foreach (var signal in events.Where(e => e.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal))
        {
            var signalIndex = Array.FindIndex(candles, c => c.CloseTimeUtc == signal.Time);
            if (signalIndex < 0 || signalIndex + 1 >= candles.Length) { noEntry++; continue; }
            if (signalIndex < occupiedUntil) { skipped++; continue; }
            var direction = signal.Direction; var crossover = events.LastOrDefault(e => e.Time <= signal.Time && e.Direction == direction && (e.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover));
            var crossoverIndex = Array.FindIndex(candles, c => c.CloseTimeUtc == crossover?.Time); if (crossoverIndex < 0) continue;
            var stop = SwingStopRules.Find(candles, crossoverIndex, direction); var entryIndex = signalIndex + 1; var entry = candles[entryIndex].Open;
            if ((direction == SignalDirection.Long && stop.Price >= entry) || (direction == SignalDirection.Short && stop.Price <= entry)) { invalid++; continue; }
            var trade = Execute(candles, entryIndex, crossoverIndex, signal, direction, entry, stop, settings); trades.Add(trade);
            // A SL/TP exit is intrabar. A signal at that candle's close may enter on the following open;
            // signals from earlier closes remain unavailable while the position was open.
            occupiedUntil = Array.FindIndex(candles, c => c.CloseTimeUtc == trade.ExitTimeUtc);
        }
        return new(trades, new(events.Count(e => e.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover), events.Count(e => e.Status == SignalStatus.LongSignal), events.Count(e => e.Status == SignalStatus.ShortSignal), events.Count(e => e.Status == SignalStatus.RejectedByEma100Filter), events.Count(e => e.Status == SignalStatus.ConfirmationFailed), invalid, skipped, noEntry));
    }

    private static BacktestTrade Execute(Candle[] candles, int entryIndex, int crossoverIndex, StrategyEvent signal, SignalDirection direction, decimal entry, (decimal Price, StopSourceType Source, DateTimeOffset Time) stop, TradingSettings settings)
    {
        var risk = decimal.Abs(entry - stop.Price); var originalTp = TradeMath.InitialTarget(entry, stop.Price, direction, settings.RiskReward);
        var currentStop = stop.Price; var currentTp = originalTp; var extended = false; var max = entry; var min = entry; var exitIndex = candles.Length - 1; var exitPrice = candles[^1].Close; var reason = BacktestExitReason.EndOfData; var conflict = false;
        for (var i = entryIndex; i < candles.Length; i++)
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
                    var nextStop = TradeMath.TrailingStop(entry, originalTp, direction, lockPercent);
                    currentStop = direction == SignalDirection.Long ? Math.Max(currentStop, nextStop) : Math.Min(currentStop, nextStop);
                }
                if (progress >= 70 && !extended)
                {
                    currentTp = TradeMath.ExtendedTarget(entry, originalTp, direction);
                    extended = true;
                }
            }
        }
        var quantity = TradeMath.Quantity(settings.FixedOrderSizeUsdt, entry); var notional = entry * quantity; var entryFee = TradeMath.Fee(entry, quantity, settings.FeePercentPerSide); var exitFee = TradeMath.Fee(exitPrice, quantity, settings.FeePercentPerSide); var gross = TradeMath.GrossPnl(entry, exitPrice, quantity, direction); var net = gross - entryFee - exitFee; var snap = signal.Snapshot;
        return new BacktestTrade { Direction = direction, CrossoverTimeUtc = candles[crossoverIndex].CloseTimeUtc, SignalTimeUtc = signal.Time, EntryTimeUtc = candles[entryIndex].OpenTimeUtc, ExitTimeUtc = candles[exitIndex].CloseTimeUtc, EntryPrice = entry, ExitPrice = exitPrice, Quantity = quantity, EntryNotionalUsdt = notional, InitialStopLoss = stop.Price, FinalStopLoss = currentStop, StopSourceType = stop.Source, StopSourceTimeUtc = stop.Time, OriginalTakeProfit = originalTp, FinalTakeProfit = currentTp, TakeProfitExtended = extended, ExitReason = reason, SameCandleExitConflict = conflict, EntryFeeUsdt = entryFee, ExitFeeUsdt = exitFee, TotalFeesUsdt = entryFee + exitFee, GrossPnlUsdt = gross, NetPnlUsdt = net, NetPnlPercent = net / notional * 100m, GrossRMultiple = gross / (risk * quantity), NetRMultiple = net / (risk * quantity), MfePrice = direction == SignalDirection.Long ? max - entry : entry - min, MfePercent = (direction == SignalDirection.Long ? max - entry : entry - min) / entry * 100m, MaePrice = direction == SignalDirection.Long ? entry - min : max - entry, MaePercent = (direction == SignalDirection.Long ? entry - min : max - entry) / entry * 100m, SignalClose = snap.Close, SignalEma9 = snap.Ema9, SignalEma15 = snap.Ema15, SignalEma100 = snap.Ema100, SignalGapPercent = snap.GapPercent, SignalGapState = snap.GapState };
    }
}
