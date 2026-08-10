using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class PositionSizingTests
{
    [Fact]
    public void MarginPercentSizing_UsesEquityMarginAndLeverage()
    {
        var size = TradeMath.CalculatePositionSize(new TradingSettings { PositionSizingMode = PositionSizingMode.MarginPercent, MarginPerTradePercent = 10m, Leverage = 5m }, 1000m, 100m);
        Assert.Equal(100m, size.MarginUsedUsdt);
        Assert.Equal(500m, size.NotionalUsdt);
        Assert.Equal(5m, size.Quantity);
        Assert.Equal(0.5m, TradeMath.Fee(100m, size.Quantity, .1m));
    }

    [Fact]
    public void FeeViability_IsIndependentOfLeverageForPercentageFees()
    {
        var fixedSize = TradeMath.CalculatePositionSize(new TradingSettings { FixedOrderSizeUsdt = 100m }, 1000m, 100m);
        var leveraged = TradeMath.CalculatePositionSize(new TradingSettings { PositionSizingMode = PositionSizingMode.MarginPercent, MarginPerTradePercent = 10m, Leverage = 5m }, 1000m, 100m);
        Assert.True(TradeMath.ExpectedNetAtTarget(100m, 100.1m, fixedSize.Quantity, SignalDirection.Long, .1m) < 0);
        Assert.True(TradeMath.ExpectedNetAtTarget(100m, 100.1m, leveraged.Quantity, SignalDirection.Long, .1m) < 0);
    }

    [Theory]
    [InlineData(100, 99.5, .5)]
    [InlineData(100, 100.5, .5)]
    public void StopDistancePercent_IsDirectionalAgnostic(decimal entry, decimal stop, decimal expected)
    {
        Assert.Equal(expected, TradeMath.StopDistancePercent(entry, stop));
    }
}
