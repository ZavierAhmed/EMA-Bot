using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class AdaptiveInitialStopRulesTests
{
    [Fact]
    public void WeakLong_UsesSignalLowAndTenthAtrBuffer()
    {
        var candles = Candles(); candles[^1] = candles[^1] with { Open = 100m, High = 101m, Low = 99m, Close = 100m };
        var result = AdaptiveInitialStopRules.Find(candles, candles.Length - 1, Snapshot(candles[^1], GapState.Contracting), SignalDirection.Long);
        Assert.Equal(ReversalPowerBand.Weak, result.ReversalPowerBand); Assert.Equal(99m, result.AnchorPrice); Assert.Equal(result.Atr14!.Value * .10m, result.Buffer); Assert.Equal(99m - result.Buffer, result.Price); Assert.Equal(StopSourceType.AdaptiveSignalCandle, result.Source);
    }

    [Fact]
    public void StrongShort_UsesThreeCandleMicrostructureAndScalesWithAtr()
    {
        var candles = Candles(); candles[^3] = candles[^3] with { High = 110m }; candles[^2] = candles[^2] with { High = 105m }; candles[^1] = candles[^1] with { Open = 109m, High = 110m, Low = 100m, Close = 100m };
        var result = AdaptiveInitialStopRules.Find(candles, candles.Length - 1, Snapshot(candles[^1], GapState.Expanding), SignalDirection.Short);
        Assert.Equal(ReversalPowerBand.Strong, result.ReversalPowerBand); Assert.Equal(110m, result.AnchorPrice); Assert.Equal(result.Atr14!.Value * .30m, result.Buffer); Assert.Equal(110m + result.Buffer, result.Price); Assert.Equal(StopSourceType.AdaptiveMicroStructure, result.Source);
    }

    [Fact]
    public void FutureCandles_DoNotChangeSelection_AndLegacyIsUnchanged()
    {
        var candles = Candles(); var signalIndex = 15; var snapshot = Snapshot(candles[signalIndex], GapState.Unchanged); var adaptive = AdaptiveInitialStopRules.Find(candles, signalIndex, snapshot, SignalDirection.Long);
        candles[18] = candles[18] with { Low = 0m }; var again = AdaptiveInitialStopRules.Find(candles, signalIndex, snapshot, SignalDirection.Long);
        Assert.Equal(adaptive, again);
        var settings = new TradingSettings { UseAdaptiveInitialStop = false }; var expected = SwingStopRules.Find(candles, 10, SignalDirection.Long); var actual = InitialStopSelector.Select(candles, 10, signalIndex, snapshot, SignalDirection.Long, settings);
        Assert.Equal(expected.Price, actual.Price); Assert.Equal(expected.Source, actual.Source); Assert.False(actual.UseAdaptiveInitialStop);
    }

    [Theory]
    [InlineData(GapState.Contracting, 0)]
    [InlineData(GapState.Unchanged, 10)]
    [InlineData(GapState.Expanding, 20)]
    public void PowerScore_IncludesExactlyGapStateComponent(GapState gap, decimal expectedGap)
    {
        var candle = new Candle(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), 100m, 110m, 90m, 100m, 1m, true);
        var score = AdaptiveInitialStopRules.ReversalPower(Snapshot(candle, gap), candle, 10m, SignalDirection.Long);
        Assert.Equal(17.5m + expectedGap, score);
    }

    private static IndicatorSnapshot Snapshot(Candle candle, GapState gap) => new(candle.CloseTimeUtc, candle.Close, 1m, 1m, 1m, 0m, gap, TrendDirection.Up, candle.Open);
    private static Candle[] Candles() => Enumerable.Range(0, 20).Select(index => { var open = (decimal)(100 + index); return new Candle(DateTimeOffset.UnixEpoch.AddMinutes(index), DateTimeOffset.UnixEpoch.AddMinutes(index + 1), open, open + 2m, open - 2m, open + 1m, 1m, true); }).ToArray();
}
