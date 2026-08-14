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
    public void WeakShort_UsesSignalHighAndTenthAtrBuffer()
    {
        var candles = Candles(); candles[^1] = candles[^1] with { Open = 100m, High = 105m, Low = 95m, Close = 105m };
        var result = AdaptiveInitialStopRules.Find(candles, candles.Length - 1, Snapshot(candles[^1], GapState.Contracting), SignalDirection.Short);
        Assert.Equal(ReversalPowerBand.Weak, result.ReversalPowerBand); Assert.Equal(105m, result.AnchorPrice); Assert.Equal(result.Atr14!.Value * .10m, result.Buffer); Assert.Equal(105m + result.Buffer, result.Price); Assert.Equal(StopSourceType.AdaptiveSignalCandle, result.Source);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public void Normal_UsesTwoCandleMicrostructureAndFifthAtrBuffer(SignalDirection direction)
    {
        var candles = Candles(); candles[^2] = candles[^2] with { Low = 80m, High = 140m }; candles[^1] = candles[^1] with { Open = 95m, High = 110m, Low = 90m, Close = 100m };
        var result = AdaptiveInitialStopRules.Find(candles, candles.Length - 1, Snapshot(candles[^1], GapState.Unchanged), direction);
        Assert.Equal(ReversalPowerBand.Normal, result.ReversalPowerBand); Assert.Equal(result.Atr14!.Value * .20m, result.Buffer); Assert.Equal(StopSourceType.AdaptiveMicroStructure, result.Source);
        Assert.Equal(direction == SignalDirection.Long ? 80m : 140m, result.AnchorPrice);
    }

    [Fact]
    public void StrongLong_UsesThreeCandleMicrostructureAndThreeTenthsAtrBuffer()
    {
        var candles = Candles(); candles[^3] = candles[^3] with { Low = 80m }; candles[^2] = candles[^2] with { Low = 90m }; candles[^1] = candles[^1] with { Open = 90m, High = 110m, Low = 85m, Close = 110m };
        var result = AdaptiveInitialStopRules.Find(candles, candles.Length - 1, Snapshot(candles[^1], GapState.Expanding), SignalDirection.Long);
        Assert.Equal(ReversalPowerBand.Strong, result.ReversalPowerBand); Assert.Equal(80m, result.AnchorPrice); Assert.Equal(result.Atr14!.Value * .30m, result.Buffer); Assert.Equal(StopSourceType.AdaptiveMicroStructure, result.Source);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 22.5)]
    [InlineData(15, 45)]
    public void PowerScore_BodyComponentIsCapped(decimal body, decimal expected)
    {
        var candle = new Candle(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), 100m - body, 120m, 100m, 100m, 1m, true);
        Assert.Equal(expected, AdaptiveInitialStopRules.ReversalPower(Snapshot(candle, GapState.Contracting), candle, 10m, SignalDirection.Long));
    }

    [Theory]
    [InlineData(SignalDirection.Long, 90, 0)]
    [InlineData(SignalDirection.Long, 100, 17.5)]
    [InlineData(SignalDirection.Long, 110, 35)]
    [InlineData(SignalDirection.Short, 110, 0)]
    [InlineData(SignalDirection.Short, 100, 17.5)]
    [InlineData(SignalDirection.Short, 90, 35)]
    public void PowerScore_CloseLocationIsDirectional(SignalDirection direction, decimal close, decimal expected)
    {
        var candle = new Candle(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), close, 110m, 90m, close, 1m, true);
        Assert.Equal(expected, AdaptiveInitialStopRules.ReversalPower(Snapshot(candle, GapState.Contracting), candle, 10m, direction));
    }

    [Fact]
    public void Atr14_UsesInitialAverageThenWilderStep_AndReturnsNullWhenUnavailable()
    {
        var candles = Enumerable.Range(0, 15).Select(index => new Candle(DateTimeOffset.UnixEpoch.AddMinutes(index), DateTimeOffset.UnixEpoch.AddMinutes(index + 1), 100m, 101m + (index == 14 ? 1m : 0m), 99m - (index == 14 ? 1m : 0m), 100m, 1m, true)).ToArray();
        Assert.Null(AtrCalculator.Wilder14(candles, 12)); Assert.Equal(2m, AtrCalculator.Wilder14(candles, 13)); Assert.Equal((2m * 13m + 4m) / 14m, AtrCalculator.Wilder14(candles, 14));
    }

    [Fact]
    public void AdaptiveWithoutAtr_FallsBackToLegacyWithVisibleSource()
    {
        var candles = Candles().Take(10).ToArray(); var snapshot = Snapshot(candles[5], GapState.Expanding); var expected = SwingStopRules.Find(candles, 5, SignalDirection.Long);
        var actual = InitialStopSelector.Select(candles, 5, 5, snapshot, SignalDirection.Long, new TradingSettings { UseAdaptiveInitialStop = true });
        Assert.Equal(expected.Price, actual.Price); Assert.Equal(StopSourceType.AdaptiveLegacyFallback, actual.Source); Assert.True(actual.UseAdaptiveInitialStop); Assert.Null(actual.Atr14); Assert.Null(actual.ReversalPowerScore);
    }

    [Theory]
    [InlineData(SignalDirection.Long, 100, 98, 1.5, 103)]
    [InlineData(SignalDirection.Short, 100, 102, 1.5, 97)]
    public void AdaptiveRiskGeometry_UsesExistingRiskRewardTarget(SignalDirection direction, decimal entry, decimal stop, decimal riskReward, decimal expectedTarget)
    {
        Assert.Equal(expectedTarget, TradeMath.InitialTarget(entry, stop, direction, riskReward));
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
