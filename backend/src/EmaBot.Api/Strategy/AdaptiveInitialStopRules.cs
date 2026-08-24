using EmaBot.Api.Market;
using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public sealed record InitialStopSelection(decimal Price, StopSourceType Source, DateTimeOffset Time, bool UseAdaptiveInitialStop, decimal? Atr14 = null, decimal? ReversalPowerScore = null, ReversalPowerBand? ReversalPowerBand = null, decimal? AnchorPrice = null, decimal? Buffer = null);

public static class AtrCalculator
{
    public static decimal? Wilder14(IReadOnlyList<Candle> candles, int inclusiveIndex)
    {
        if (inclusiveIndex < 13 || candles.Count <= inclusiveIndex) return null;
        decimal atr = 0m;
        for (var index = 0; index < 14; index++) atr += TrueRange(candles, index);
        atr /= 14m;
        for (var index = 14; index <= inclusiveIndex; index++) atr = (atr * 13m + TrueRange(candles, index)) / 14m;
        return atr;
    }

    private static decimal TrueRange(IReadOnlyList<Candle> candles, int index)
    {
        var candle = candles[index];
        if (index == 0) return candle.High - candle.Low;
        var previousClose = candles[index - 1].Close;
        return decimal.Max(candle.High - candle.Low, decimal.Max(decimal.Abs(candle.High - previousClose), decimal.Abs(candle.Low - previousClose)));
    }
}

public static class AdaptiveInitialStopRules
{
    public static InitialStopSelection Find(IReadOnlyList<Candle> candles, int signalIndex, IndicatorSnapshot signal, SignalDirection direction)
    {
        if (signalIndex < 0 || signalIndex >= candles.Count || candles[signalIndex].CloseTimeUtc > signal.Time) throw new ArgumentOutOfRangeException(nameof(signalIndex));
        var atr = AtrCalculator.Wilder14(candles, signalIndex);
        if (!atr.HasValue) throw new InvalidOperationException("ATR14 is unavailable for the signal candle.");

        return Find(candles, signalIndex, signal, direction, atr.Value);
    }

    internal static InitialStopSelection Find(IReadOnlyList<Candle> candles, int signalIndex, IndicatorSnapshot signal, SignalDirection direction, decimal atr)
    {
        if (signalIndex < 0 || signalIndex >= candles.Count || candles[signalIndex].CloseTimeUtc > signal.Time) throw new ArgumentOutOfRangeException(nameof(signalIndex));

        var score = ReversalPower(signal, candles[signalIndex], atr, direction);
        var band = score < 45m ? ReversalPowerBand.Weak : score < 70m ? ReversalPowerBand.Normal : ReversalPowerBand.Strong;
        var lookback = band == ReversalPowerBand.Weak ? 1 : band == ReversalPowerBand.Normal ? 2 : 3;
        var start = Math.Max(0, signalIndex - lookback + 1);
        var structural = candles.Skip(start).Take(signalIndex - start + 1).ToArray();
        var anchorCandle = direction == SignalDirection.Long
            ? structural.Aggregate((best, candidate) => candidate.Low < best.Low ? candidate : best)
            : structural.Aggregate((best, candidate) => candidate.High > best.High ? candidate : best);
        var anchor = direction == SignalDirection.Long ? anchorCandle.Low : anchorCandle.High;
        var multiplier = band == ReversalPowerBand.Weak ? .10m : band == ReversalPowerBand.Normal ? .20m : .30m;
        var buffer = atr * multiplier;
        var price = direction == SignalDirection.Long ? anchor - buffer : anchor + buffer;
        var source = band == ReversalPowerBand.Weak ? StopSourceType.AdaptiveSignalCandle : StopSourceType.AdaptiveMicroStructure;
        return new(price, source, anchorCandle.CloseTimeUtc, true, atr, score, band, anchor, buffer);
    }

    public static decimal ReversalPower(IndicatorSnapshot signal, Candle candle, decimal atr14, SignalDirection direction)
    {
        if (atr14 <= 0m) return 0m;
        var body = decimal.Min(decimal.Abs(candle.Close - candle.Open) / atr14, 1m) * 45m;
        var range = candle.High - candle.Low;
        var location = range <= 0m ? 0m : direction == SignalDirection.Long ? (candle.Close - candle.Low) / range : (candle.High - candle.Close) / range;
        location = decimal.Clamp(location, 0m, 1m) * 35m;
        var gap = signal.GapState switch { GapState.Expanding => 20m, GapState.Unchanged => 10m, _ => 0m };
        return decimal.Clamp(body + location + gap, 0m, 100m);
    }
}

public static class InitialStopSelector
{
    public static InitialStopSelection Select(IReadOnlyList<Candle> candles, int legacyStopIndex, int signalIndex, IndicatorSnapshot signal, SignalDirection direction, TradingSettings settings)
    {
        if (!settings.UseAdaptiveInitialStop)
        {
            var legacy = SwingStopRules.Find(candles, legacyStopIndex, direction);
            return new(legacy.Price, legacy.Source, legacy.Time, false);
        }
        if (AtrCalculator.Wilder14(candles, signalIndex) is { } ) return AdaptiveInitialStopRules.Find(candles, signalIndex, signal, direction);
        var fallback = SwingStopRules.Find(candles, legacyStopIndex, direction);
        return new(fallback.Price, StopSourceType.AdaptiveLegacyFallback, fallback.Time, true);
    }
}
