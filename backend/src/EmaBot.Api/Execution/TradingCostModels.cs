using EmaBot.Api.Strategy;

namespace EmaBot.Api.Execution;

public interface ITradingCostModel
{
    decimal EntryCost(decimal entryPrice, PositionExposure exposure);
    decimal ExitCost(decimal exitPrice, PositionExposure exposure);
    decimal ExpectedNetPnl(decimal entryPrice, decimal exitPrice, PositionExposure exposure, SignalDirection direction);
    decimal BreakEvenExitPrice(decimal entryPrice, PositionExposure exposure, SignalDirection direction);
}

public abstract class TradingCostModel : ITradingCostModel
{
    public abstract decimal EntryCost(decimal entryPrice, PositionExposure exposure);
    public abstract decimal ExitCost(decimal exitPrice, PositionExposure exposure);
    public decimal ExpectedNetPnl(decimal entryPrice, decimal exitPrice, PositionExposure exposure, SignalDirection direction)
        => TradingCostMath.GrossPnl(entryPrice, exitPrice, exposure.Quantity, direction) - EntryCost(entryPrice, exposure) - ExitCost(exitPrice, exposure);
    public abstract decimal BreakEvenExitPrice(decimal entryPrice, PositionExposure exposure, SignalDirection direction);
}

public sealed class PercentageNotionalCostModel(decimal feePercentPerSide) : TradingCostModel
{
    public decimal FeePercentPerSide { get; } = feePercentPerSide >= 0m ? feePercentPerSide : throw new ArgumentOutOfRangeException(nameof(feePercentPerSide));

    public override decimal EntryCost(decimal entryPrice, PositionExposure exposure) => Cost(entryPrice, exposure.Quantity);
    public override decimal ExitCost(decimal exitPrice, PositionExposure exposure) => Cost(exitPrice, exposure.Quantity);

    public override decimal BreakEvenExitPrice(decimal entryPrice, PositionExposure exposure, SignalDirection direction)
    {
        if (FeePercentPerSide == 0m) return entryPrice;
        var rate = FeePercentPerSide / 100m;
        if (rate >= 1m) throw new ArgumentOutOfRangeException(nameof(FeePercentPerSide));
        var price = direction switch
        {
            SignalDirection.Long => entryPrice * (1m + rate) / (1m - rate),
            SignalDirection.Short => entryPrice * (1m - rate) / (1m + rate),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        if (ExpectedNetPnl(entryPrice, price, exposure, direction) < 0m)
        {
            var increment = Math.Max(0.00000000000000000000000001m, decimal.Abs(price) * 0.0000000000000000000000000001m);
            price = direction == SignalDirection.Long ? price + increment : price - increment;
        }
        return price;
    }

    private decimal Cost(decimal price, decimal quantity) => price * quantity * FeePercentPerSide / 100m;
}

public sealed class PerLotCommissionCostModel(decimal commissionPerLotPerSide) : TradingCostModel
{
    public decimal CommissionPerLotPerSide { get; } = commissionPerLotPerSide >= 0m ? commissionPerLotPerSide : throw new ArgumentOutOfRangeException(nameof(commissionPerLotPerSide));

    public override decimal EntryCost(decimal entryPrice, PositionExposure exposure) => Commission(exposure);
    public override decimal ExitCost(decimal exitPrice, PositionExposure exposure) => Commission(exposure);

    public override decimal BreakEvenExitPrice(decimal entryPrice, PositionExposure exposure, SignalDirection direction)
    {
        if (exposure.Quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(exposure), "Quantity must be greater than zero.");
        var roundTripCommission = EntryCost(entryPrice, exposure) + ExitCost(entryPrice, exposure);
        return direction switch
        {
            SignalDirection.Long => entryPrice + roundTripCommission / exposure.Quantity,
            SignalDirection.Short => entryPrice - roundTripCommission / exposure.Quantity,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
    }

    private decimal Commission(PositionExposure exposure) => exposure.Lots is { } lots
        ? lots * CommissionPerLotPerSide
        : throw new InvalidOperationException("Per-lot commission requires an exposure with lots.");
}

public static class TradingCostMath
{
    public static decimal GrossPnl(decimal entryPrice, decimal exitPrice, decimal quantity, SignalDirection direction)
        => (direction == SignalDirection.Long ? exitPrice - entryPrice : entryPrice - exitPrice) * quantity;
}
