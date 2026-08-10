using EmaBot.Api.Binance;
using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public sealed class EmaSignalEngine
{
    public StrategyEvaluation Evaluate(IReadOnlyList<Candle> candles, TradingSettings settings)
    {
        var closed = candles.Where(candle => candle.IsClosed).OrderBy(candle => candle.CloseTimeUtc).ToArray();
        var closes = closed.Select(candle => candle.Close).ToArray();
        var ema9 = EmaCalculator.Calculate(closes, 9);
        var ema15 = EmaCalculator.Calculate(closes, 15);
        var ema100 = EmaCalculator.Calculate(closes, 100);
        var snapshots = new List<IndicatorSnapshot>(closed.Length);
        decimal? previousGap = null;
        for (var index = 0; index < closed.Length; index++)
        {
            decimal? gap = null;
            var gapState = GapState.Unchanged;
            if (ema9[index].HasValue && ema15[index].HasValue && closes[index] != 0)
            {
                gap = decimal.Abs(ema9[index]!.Value - ema15[index]!.Value) / closes[index] * 100m;
                if (previousGap.HasValue) gapState = gap > previousGap ? GapState.Expanding : gap < previousGap ? GapState.Contracting : GapState.Unchanged;
                previousGap = gap;
            }
            var trend = !ema9[index].HasValue || !ema15[index].HasValue ? TrendDirection.Neutral : ema9[index] > ema15[index] ? TrendDirection.Up : ema9[index] < ema15[index] ? TrendDirection.Down : TrendDirection.Neutral;
            snapshots.Add(new IndicatorSnapshot(closed[index].CloseTimeUtc, closes[index], ema9[index], ema15[index], ema100[index], gap, gapState, trend, closed[index].Open));
        }
        return new StrategyEvaluation(snapshots, EvaluateSnapshots(snapshots, settings));
    }

    public IReadOnlyList<StrategyEvent> EvaluateSnapshots(IReadOnlyList<IndicatorSnapshot> snapshots, TradingSettings settings)
    {
        var events = new List<StrategyEvent>();
        SignalDirection pending = SignalDirection.None;
        for (var index = 1; index < snapshots.Count; index++)
        {
            var previous = snapshots[index - 1];
            var current = snapshots[index];
            if (!HasEma9And15(previous) || !HasEma9And15(current)) continue;

            if (pending != SignalDirection.None)
            {
                var confirms = pending == SignalDirection.Long
                    ? current.Ema9 > current.Ema15 && current.Close > current.Ema9 && current.Close > current.Ema15 && current.Close > current.Open
                    : current.Ema9 < current.Ema15 && current.Close < current.Ema9 && current.Close < current.Ema15 && current.Close < current.Open;
                if (confirms) events.Add(CreateCandidate(current, pending, settings));
                else events.Add(new StrategyEvent(current.Time, pending, SignalStatus.ConfirmationFailed, current));
                pending = SignalDirection.None;
            }

            var direction = previous.Ema9 <= previous.Ema15 && current.Ema9 > current.Ema15 ? SignalDirection.Long
                : previous.Ema9 >= previous.Ema15 && current.Ema9 < current.Ema15 ? SignalDirection.Short : SignalDirection.None;
            if (direction == SignalDirection.None) continue;
            events.Add(new StrategyEvent(current.Time, direction, direction == SignalDirection.Long ? SignalStatus.BullishCrossover : SignalStatus.BearishCrossover, current));
            if (settings.WaitForConfirmationCandle)
            {
                pending = direction;
                events.Add(new StrategyEvent(current.Time, direction, SignalStatus.AwaitingConfirmation, current));
            }
            else events.Add(CreateCandidate(current, direction, settings));
        }
        return events;
    }

    private static StrategyEvent CreateCandidate(IndicatorSnapshot snapshot, SignalDirection direction, TradingSettings settings)
    {
        var allowed = !settings.UseEma100Filter || (snapshot.Ema100.HasValue && (direction == SignalDirection.Long
            ? snapshot.Ema9 > snapshot.Ema100 && snapshot.Ema15 > snapshot.Ema100
            : snapshot.Ema9 < snapshot.Ema100 && snapshot.Ema15 < snapshot.Ema100));
        if (!allowed) return new StrategyEvent(snapshot.Time, direction, SignalStatus.RejectedByEma100Filter, snapshot);
        if (settings.MinEmaGapPercent > 0 && (!snapshot.GapPercent.HasValue || snapshot.GapPercent.Value < settings.MinEmaGapPercent)) return new StrategyEvent(snapshot.Time, direction, SignalStatus.RejectedByEmaGap, snapshot);
        return new StrategyEvent(snapshot.Time, direction, direction == SignalDirection.Long ? SignalStatus.LongSignal : SignalStatus.ShortSignal, snapshot);
    }
    private static bool HasEma9And15(IndicatorSnapshot snapshot) => snapshot.Ema9.HasValue && snapshot.Ema15.HasValue;
}
