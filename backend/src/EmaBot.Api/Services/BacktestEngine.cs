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
        var evaluation = strategy.Evaluate(candles, settings); var events = evaluation.Events;
        var trades = new List<BacktestTrade>(); var invalid = 0; var skipped = 0; var noEntry = 0; var occupiedUntil = -1;
        foreach (var signal in events.Where(e => e.Status is SignalStatus.LongSignal or SignalStatus.ShortSignal))
        {
            var signalIndex = Array.FindIndex(candles, c => c.CloseTimeUtc == signal.Time);
            if (signalIndex < 0 || signalIndex + 1 >= candles.Length) { noEntry++; continue; }
            if (signalIndex < occupiedUntil) { skipped++; continue; }
            var direction = signal.Direction; var crossover = events.LastOrDefault(e => e.Time <= signal.Time && e.Direction == direction && e.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover);
            var crossoverIndex = Array.FindIndex(candles, c => c.CloseTimeUtc == crossover?.Time); if (crossoverIndex < 0) continue;
            var stop = FindStop(candles, crossoverIndex, direction); var entryIndex = signalIndex + 1; var entry = candles[entryIndex].Open;
            if ((direction == SignalDirection.Long && stop.Price >= entry) || (direction == SignalDirection.Short && stop.Price <= entry)) { invalid++; continue; }
            var trade = Execute(candles, entryIndex, crossoverIndex, signal, direction, entry, stop, settings); trades.Add(trade); occupiedUntil = Array.FindIndex(candles, c => c.CloseTimeUtc == trade.ExitTimeUtc) + 1;
        }
        return new(trades, new(events.Count(e => e.Status is SignalStatus.BullishCrossover or SignalStatus.BearishCrossover), events.Count(e => e.Status == SignalStatus.LongSignal), events.Count(e => e.Status == SignalStatus.ShortSignal), events.Count(e => e.Status == SignalStatus.RejectedByEma100Filter), events.Count(e => e.Status == SignalStatus.ConfirmationFailed), invalid, skipped, noEntry));
    }

    private static (decimal Price, StopSourceType Source, DateTimeOffset Time) FindStop(Candle[] candles, int crossoverIndex, SignalDirection direction)
    {
        var pivot = -1;
        for (var i = 2; i <= crossoverIndex - 2; i++) { var valid = direction == SignalDirection.Long ? candles[i].Low < candles[i - 1].Low && candles[i].Low < candles[i - 2].Low && candles[i].Low < candles[i + 1].Low && candles[i].Low < candles[i + 2].Low : candles[i].High > candles[i - 1].High && candles[i].High > candles[i - 2].High && candles[i].High > candles[i + 1].High && candles[i].High > candles[i + 2].High; if (valid) pivot = i; }
        if (pivot >= 0) return (direction == SignalDirection.Long ? candles[pivot].Low : candles[pivot].High, StopSourceType.Pivot, candles[pivot].CloseTimeUtc);
        var prior = candles.Skip(Math.Max(0, crossoverIndex - 10)).Take(Math.Min(10, crossoverIndex)).ToArray(); var selected = direction == SignalDirection.Long ? prior.MinBy(c => c.Low)! : prior.MaxBy(c => c.High)!;
        return (direction == SignalDirection.Long ? selected.Low : selected.High, StopSourceType.FallbackLookback, selected.CloseTimeUtc);
    }

    private static BacktestTrade Execute(Candle[] candles, int entryIndex, int crossoverIndex, StrategyEvent signal, SignalDirection direction, decimal entry, (decimal Price, StopSourceType Source, DateTimeOffset Time) stop, TradingSettings settings)
    {
        var risk = decimal.Abs(entry - stop.Price); var originalTp = direction == SignalDirection.Long ? entry + risk * settings.RiskReward : entry - risk * settings.RiskReward;
        var currentStop = stop.Price; var currentTp = originalTp; var extended = false; var max = entry; var min = entry; var exitIndex = candles.Length - 1; var exitPrice = candles[^1].Close; var reason = BacktestExitReason.EndOfData; var conflict = false;
        for (var i = entryIndex; i < candles.Length; i++) { var c = candles[i]; var sl = direction == SignalDirection.Long ? c.Low <= currentStop : c.High >= currentStop; var tp = direction == SignalDirection.Long ? c.High >= currentTp : c.Low <= currentTp; if (sl || tp) { conflict = sl && tp; reason = sl ? BacktestExitReason.StopLoss : BacktestExitReason.TakeProfit; exitPrice = sl ? currentStop : currentTp; exitIndex = i; break; } max = Math.Max(max, c.High); min = Math.Min(min, c.Low); if (settings.TrailingStopEnabled) { var progress = direction == SignalDirection.Long ? (max - entry) / decimal.Abs(originalTp - entry) * 100m : (entry - min) / decimal.Abs(originalTp - entry) * 100m; var lockPercent = progress >= 100 ? 70 : progress >= 90 ? 60 : progress >= 80 ? 50 : progress >= 70 ? 40 : progress >= 60 ? 30 : progress >= 50 ? 20 : 0; if (lockPercent > 0) { var nextStop = direction == SignalDirection.Long ? entry + decimal.Abs(originalTp - entry) * lockPercent / 100m : entry - decimal.Abs(originalTp - entry) * lockPercent / 100m; currentStop = direction == SignalDirection.Long ? Math.Max(currentStop, nextStop) : Math.Min(currentStop, nextStop); } if (progress >= 70 && !extended) { currentTp = direction == SignalDirection.Long ? entry + decimal.Abs(originalTp - entry) * 1.1m : entry - decimal.Abs(originalTp - entry) * 1.1m; extended = true; } } }
        var quantity = settings.FixedOrderSizeUsdt / entry; var notional = entry * quantity; var entryFee = notional * settings.FeePercentPerSide / 100m; var exitFee = exitPrice * quantity * settings.FeePercentPerSide / 100m; var gross = (direction == SignalDirection.Long ? exitPrice - entry : entry - exitPrice) * quantity; var net = gross - entryFee - exitFee; var snap = signal.Snapshot;
        return new BacktestTrade { Direction = direction, CrossoverTimeUtc = candles[crossoverIndex].CloseTimeUtc, SignalTimeUtc = signal.Time, EntryTimeUtc = candles[entryIndex].OpenTimeUtc, ExitTimeUtc = candles[exitIndex].CloseTimeUtc, EntryPrice = entry, ExitPrice = exitPrice, Quantity = quantity, EntryNotionalUsdt = notional, InitialStopLoss = stop.Price, FinalStopLoss = currentStop, StopSourceType = stop.Source, StopSourceTimeUtc = stop.Time, OriginalTakeProfit = originalTp, FinalTakeProfit = currentTp, TakeProfitExtended = extended, ExitReason = reason, SameCandleExitConflict = conflict, EntryFeeUsdt = entryFee, ExitFeeUsdt = exitFee, TotalFeesUsdt = entryFee + exitFee, GrossPnlUsdt = gross, NetPnlUsdt = net, NetPnlPercent = net / notional * 100m, GrossRMultiple = gross / (risk * quantity), NetRMultiple = net / (risk * quantity), MfePrice = direction == SignalDirection.Long ? max - entry : entry - min, MfePercent = (direction == SignalDirection.Long ? max - entry : entry - min) / entry * 100m, MaePrice = direction == SignalDirection.Long ? entry - min : max - entry, MaePercent = (direction == SignalDirection.Long ? entry - min : max - entry) / entry * 100m, SignalClose = snap.Close, SignalEma9 = snap.Ema9, SignalEma15 = snap.Ema15, SignalEma100 = snap.Ema100, SignalGapPercent = snap.GapPercent, SignalGapState = snap.GapState };
    }
}
