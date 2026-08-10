using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public static class TradeMath
{
    public static decimal InitialTarget(decimal entry, decimal stop, SignalDirection direction, decimal riskReward)
    {
        var risk = decimal.Abs(entry - stop);
        return direction == SignalDirection.Long ? entry + risk * riskReward : entry - risk * riskReward;
    }

    public static decimal Quantity(decimal fixedOrderSizeUsdt, decimal entryPrice) => fixedOrderSizeUsdt / entryPrice;
    public static decimal Fee(decimal price, decimal quantity, decimal feePercentPerSide) => price * quantity * feePercentPerSide / 100m;
    public static decimal GrossPnl(decimal entry, decimal exit, decimal quantity, SignalDirection direction) => (direction == SignalDirection.Long ? exit - entry : entry - exit) * quantity;
    public static decimal Progress(decimal entry, decimal originalTarget, decimal bestPrice, SignalDirection direction) => direction == SignalDirection.Long ? (bestPrice - entry) / decimal.Abs(originalTarget - entry) * 100m : (entry - bestPrice) / decimal.Abs(originalTarget - entry) * 100m;
    public static decimal LockPercent(decimal progress) => progress >= 100m ? 70m : progress >= 90m ? 60m : progress >= 80m ? 50m : progress >= 70m ? 40m : progress >= 60m ? 30m : progress >= 50m ? 20m : 0m;
    public static decimal TrailingStop(decimal entry, decimal originalTarget, SignalDirection direction, decimal lockPercent) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * lockPercent / 100m : entry - decimal.Abs(originalTarget - entry) * lockPercent / 100m;
    public static decimal ExtendedTarget(decimal entry, decimal originalTarget, SignalDirection direction) => direction == SignalDirection.Long ? entry + decimal.Abs(originalTarget - entry) * 1.1m : entry - decimal.Abs(originalTarget - entry) * 1.1m;
}
