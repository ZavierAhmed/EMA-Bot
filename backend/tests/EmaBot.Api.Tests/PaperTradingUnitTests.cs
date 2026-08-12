using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class PaperTradingUnitTests
{
    [Fact]
    public void TradeMath_UsesSharedTrailingThresholdsAndOneTimeTargetDistance()
    {
        Assert.Equal(20m, TradeMath.LockPercent(50m)); Assert.Equal(40m, TradeMath.LockPercent(70m)); Assert.Equal(70m, TradeMath.LockPercent(100m));
        var original = TradeMath.InitialTarget(100m, 90m, SignalDirection.Long, 2m);
        Assert.Equal(120m, original); Assert.Equal(108m, TradeMath.TrailingStop(100m, original, SignalDirection.Long, 40m)); Assert.Equal(122m, TradeMath.ExtendedTarget(100m, original, SignalDirection.Long));
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public void FeeAwareTrailingStop_UsesExactFeeBreakevenAndNeverLocksNegativeNet(SignalDirection direction)
    {
        const decimal entry = 100m; const decimal fee = .05m; const decimal quantity = 1m;
        var breakeven = TradeMath.FeeBreakevenPrice(entry, direction, fee);
        var expected = direction == SignalDirection.Long ? entry * 1.0005m / .9995m : entry * .9995m / 1.0005m;
        var calculated = direction == SignalDirection.Long ? 100.04m : 99.96m;
        var effective = TradeMath.FeeAwareTrailingStop(calculated, entry, direction, fee);
        Assert.True(direction == SignalDirection.Long ? breakeven >= expected : breakeven <= expected);
        Assert.Equal(breakeven, effective);
        Assert.True(TradeMath.ExpectedNetAtTarget(entry, effective, quantity, direction, fee) >= 0m);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public void FeeAwareTrailingStop_KeepsMoreProfitableCalculatedStopAndZeroFeeBehavior(SignalDirection direction)
    {
        var calculated = direction == SignalDirection.Long ? 104m : 96m;
        Assert.Equal(calculated, TradeMath.FeeAwareTrailingStop(calculated, 100m, direction, .05m));
        Assert.Equal(calculated, TradeMath.FeeAwareTrailingStop(calculated, 100m, direction, 0m));
    }
}
