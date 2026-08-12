using EmaBot.Api.Market;
using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public static class SwingStopRules
{
    public static (decimal Price, StopSourceType Source, DateTimeOffset Time) Find(IReadOnlyList<Candle> candles, int crossoverIndex, SignalDirection direction)
    {
        var pivot = -1;
        for (var index = 2; index <= crossoverIndex - 2; index++)
        {
            var valid = direction == SignalDirection.Long
                ? candles[index].Low < candles[index - 1].Low && candles[index].Low < candles[index - 2].Low && candles[index].Low < candles[index + 1].Low && candles[index].Low < candles[index + 2].Low
                : candles[index].High > candles[index - 1].High && candles[index].High > candles[index - 2].High && candles[index].High > candles[index + 1].High && candles[index].High > candles[index + 2].High;
            if (valid) pivot = index;
        }
        if (pivot >= 0) return (direction == SignalDirection.Long ? candles[pivot].Low : candles[pivot].High, StopSourceType.Pivot, candles[pivot].CloseTimeUtc);
        var prior = candles.Skip(Math.Max(0, crossoverIndex - 10)).Take(Math.Min(10, crossoverIndex)).ToArray();
        var selected = direction == SignalDirection.Long ? prior.MinBy(candle => candle.Low) : prior.MaxBy(candle => candle.High);
        if (selected is null) throw new InvalidOperationException("A crossover requires prior completed candles for a stop.");
        return (direction == SignalDirection.Long ? selected.Low : selected.High, StopSourceType.FallbackLookback, selected.CloseTimeUtc);
    }
}
