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
        var margin = settings.PositionSizingMode == PositionSizingMode.MarginPercent
            ? accountEquity * settings.MarginPerTradePercent / 100m
            : settings.FixedOrderSizeUsdt;
        var leverage = settings.PositionSizingMode == PositionSizingMode.MarginPercent ? settings.Leverage : 1m;
        var notional = settings.PositionSizingMode == PositionSizingMode.MarginPercent ? margin * leverage : settings.FixedOrderSizeUsdt;
        return new PositionSize(accountEquity, settings.PositionSizingMode == PositionSizingMode.MarginPercent ? margin : 0m, leverage, notional, notional / entryPrice);
    }
    public static decimal StopDistancePercent(decimal entry, decimal stop) => entry == 0 ? 0 : decimal.Abs(entry - stop) / entry * 100m;
    public static decimal ExpectedNetAtTarget(decimal entry, decimal target, decimal quantity, SignalDirection direction, decimal feePercentPerSide) => GrossPnl(entry, target, quantity, direction) - Fee(entry, quantity, feePercentPerSide) - Fee(target, quantity, feePercentPerSide);
    public static decimal Fee(decimal price, decimal quantity, decimal feePercentPerSide) => price * quantity * feePercentPerSide / 100m;
    public static decimal GrossPnl(decimal entry, decimal exit, decimal quantity, SignalDirection direction) => (direction == SignalDirection.Long ? exit - entry : entry - exit) * quantity;
    public static decimal Progress(decimal entry, decimal originalTarget, decimal bestPrice, SignalDirection direction) => direction == SignalDirection.Long ? (bestPrice - entry) / decimal.Abs(originalTarget - entry) * 100m : (entry - bestPrice) / decimal.Abs(originalTarget - entry) * 100m;
    public static decimal LockPercent(decimal progress) => progress >= 100m ? 70m : progress >= 90m ? 60m : progress >= 80m ? 50m : progress >= 70m ? 40m : progress >= 60m ? 30m : progress >= 50m ? 20m : 0m;
    public static decimal TrailingStop(decimal entry, decimal originalTarget, SignalDirection direction, decimal lockPercent) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * lockPercent / 100m : entry - decimal.Abs(originalTarget - entry) * lockPercent / 100m;
    public static decimal ExtendedTarget(decimal entry, decimal originalTarget, SignalDirection direction) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * 1.1m : entry - decimal.Abs(originalTarget - entry) * 1.1m;
}
