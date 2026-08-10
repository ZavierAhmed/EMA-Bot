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
    public static decimal FeeBreakevenPrice(decimal entry, SignalDirection direction, decimal feePercentPerSide)
    {
        if (feePercentPerSide < 0m) throw new ArgumentOutOfRangeException(nameof(feePercentPerSide));
        if (feePercentPerSide == 0m) return entry;
        var rate = feePercentPerSide / 100m;
        if (rate >= 1m) throw new ArgumentOutOfRangeException(nameof(feePercentPerSide));
        var price = direction switch
        {
            SignalDirection.Long => entry * (1m + rate) / (1m - rate),
            SignalDirection.Short => entry * (1m - rate) / (1m + rate),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        // Decimal division can round the mathematical zero-net price a fraction below zero.
        // Advance one economic decimal unit in the protective direction when that occurs.
        if (ExpectedNetAtTarget(entry, price, 1m, direction, feePercentPerSide) < 0m)
        {
            var increment = Math.Max(0.00000000000000000000000001m, decimal.Abs(price) * 0.0000000000000000000000000001m);
            price = direction == SignalDirection.Long ? price + increment : price - increment;
        }
        return price;
    }
    public static decimal FeeAwareTrailingStop(decimal calculatedTrailingStop, decimal entry, SignalDirection direction, decimal feePercentPerSide)
    {
        var feeBreakeven = FeeBreakevenPrice(entry, direction, feePercentPerSide);
        return direction == SignalDirection.Long ? Math.Max(calculatedTrailingStop, feeBreakeven) : Math.Min(calculatedTrailingStop, feeBreakeven);
    }
    public static decimal GrossPnl(decimal entry, decimal exit, decimal quantity, SignalDirection direction) => (direction == SignalDirection.Long ? exit - entry : entry - exit) * quantity;
    public static decimal Progress(decimal entry, decimal originalTarget, decimal bestPrice, SignalDirection direction) => direction == SignalDirection.Long ? (bestPrice - entry) / decimal.Abs(originalTarget - entry) * 100m : (entry - bestPrice) / decimal.Abs(originalTarget - entry) * 100m;
    public static decimal LockPercent(decimal progress) => progress >= 100m ? 70m : progress >= 90m ? 60m : progress >= 80m ? 50m : progress >= 70m ? 40m : progress >= 60m ? 30m : progress >= 50m ? 20m : 0m;
    public static decimal TrailingStop(decimal entry, decimal originalTarget, SignalDirection direction, decimal lockPercent) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * lockPercent / 100m : entry - decimal.Abs(originalTarget - entry) * lockPercent / 100m;
    public static decimal ExtendedTarget(decimal entry, decimal originalTarget, SignalDirection direction) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * 1.1m : entry - decimal.Abs(originalTarget - entry) * 1.1m;
}
