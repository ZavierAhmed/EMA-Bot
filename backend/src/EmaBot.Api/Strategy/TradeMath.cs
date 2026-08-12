using EmaBot.Api.Execution;
using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public static class TradeMath
{
    public sealed record PositionSize(decimal AccountEquityAtEntryUsdt, decimal MarginUsedUsdt, decimal Leverage, decimal NotionalUsdt, decimal Quantity);

    public static decimal InitialTarget(decimal entry, decimal stop, SignalDirection direction, decimal riskReward)
    {
        var risk = decimal.Abs(entry - stop);
        return direction == SignalDirection.Long ? entry + risk * riskReward : entry - risk * riskReward;
    }

    public static decimal Quantity(decimal fixedOrderSizeUsdt, decimal entryPrice) => fixedOrderSizeUsdt / entryPrice;
    public static PositionSize CalculatePositionSize(TradingSettings settings, decimal accountEquity, decimal entryPrice)
    {
        var result = LegacyPositionSizingCalculator.Calculate(settings, accountEquity, entryPrice);
        return new PositionSize(result.AccountEquityAtEntryUsdt, result.MarginUsedUsdt, result.Leverage, result.Exposure.QuoteNotional, result.Exposure.Quantity);
    }
    public static decimal StopDistancePercent(decimal entry, decimal stop) => entry == 0 ? 0 : decimal.Abs(entry - stop) / entry * 100m;
    public static decimal ExpectedNetAtTarget(decimal entry, decimal target, decimal quantity, SignalDirection direction, decimal feePercentPerSide) => PercentageCost(feePercentPerSide).ExpectedNetPnl(entry, target, Exposure(entry, quantity), direction);
    public static decimal Fee(decimal price, decimal quantity, decimal feePercentPerSide) => PercentageCost(feePercentPerSide).EntryCost(price, Exposure(price, quantity));
    public static decimal FeeBreakevenPrice(decimal entry, SignalDirection direction, decimal feePercentPerSide)
    {
        return PercentageCost(feePercentPerSide).BreakEvenExitPrice(entry, Exposure(entry, 1m), direction);
    }
    public static decimal FeeAwareTrailingStop(decimal calculatedTrailingStop, decimal entry, SignalDirection direction, decimal feePercentPerSide)
    {
        var feeBreakeven = FeeBreakevenPrice(entry, direction, feePercentPerSide);
        return direction == SignalDirection.Long ? Math.Max(calculatedTrailingStop, feeBreakeven) : Math.Min(calculatedTrailingStop, feeBreakeven);
    }
    public static decimal GrossPnl(decimal entry, decimal exit, decimal quantity, SignalDirection direction) => TradingCostMath.GrossPnl(entry, exit, quantity, direction);
    public static decimal Progress(decimal entry, decimal originalTarget, decimal bestPrice, SignalDirection direction) => direction == SignalDirection.Long ? (bestPrice - entry) / decimal.Abs(originalTarget - entry) * 100m : (entry - bestPrice) / decimal.Abs(originalTarget - entry) * 100m;
    public static decimal LockPercent(decimal progress) => progress >= 100m ? 70m : progress >= 90m ? 60m : progress >= 80m ? 50m : progress >= 70m ? 40m : progress >= 60m ? 30m : progress >= 50m ? 20m : 0m;
    public static decimal TrailingStop(decimal entry, decimal originalTarget, SignalDirection direction, decimal lockPercent) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * lockPercent / 100m : entry - decimal.Abs(originalTarget - entry) * lockPercent / 100m;
    public static decimal ExtendedTarget(decimal entry, decimal originalTarget, SignalDirection direction) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * 1.1m : entry - decimal.Abs(originalTarget - entry) * 1.1m;
    private static PositionExposure Exposure(decimal price, decimal quantity) => new(quantity, price * quantity, null, null);
    private static PercentageNotionalCostModel PercentageCost(decimal feePercentPerSide) => new(feePercentPerSide);
}
