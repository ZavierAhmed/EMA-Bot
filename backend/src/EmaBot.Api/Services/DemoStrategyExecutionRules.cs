using EmaBot.Api.Market;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

public static class DemoStrategyExecutionRules
{
    public static bool AllowsDirection(InstrumentTradeMode tradeMode, SignalDirection direction) => tradeMode switch
    {
        InstrumentTradeMode.Full => direction is SignalDirection.Long or SignalDirection.Short,
        InstrumentTradeMode.LongOnly => direction == SignalDirection.Long,
        InstrumentTradeMode.ShortOnly => direction == SignalDirection.Short,
        _ => false
    };

    public static string? ValidateFixedLots(InstrumentSpec spec, decimal lots)
    {
        if (lots <= 0m) return "Configured fixed lots must be greater than zero.";
        if (spec.VolumeMin <= 0m || spec.VolumeMax < spec.VolumeMin || spec.VolumeStep <= 0m)
            return "MT5 instrument volume constraints are invalid or unavailable.";
        if (lots < spec.VolumeMin) return $"Configured fixed lots {lots} are below MT5 minimum {spec.VolumeMin}.";
        if (lots > spec.VolumeMax) return $"Configured fixed lots {lots} exceed MT5 maximum {spec.VolumeMax}.";
        if (spec.VolumeLimit is > 0m && lots > spec.VolumeLimit) return $"Configured fixed lots {lots} exceed MT5 volume limit {spec.VolumeLimit}.";
        var steps = (lots - spec.VolumeMin) / spec.VolumeStep;
        return steps != decimal.Truncate(steps) ? $"Configured fixed lots {lots} do not match MT5 volume step {spec.VolumeStep}." : null;
    }

    public static decimal? EntryPrice(SignalDirection direction, decimal? bid, decimal? ask) => direction switch
    {
        SignalDirection.Long when ask is > 0m => ask,
        SignalDirection.Short when bid is > 0m => bid,
        _ => null
    };

    public static bool StopAndTargetMeetBrokerMinimum(InstrumentSpec spec, SignalDirection direction, decimal entry, decimal bid, decimal ask, decimal stop, decimal target)
    {
        if (direction == SignalDirection.Long && stop >= entry || direction == SignalDirection.Short && stop <= entry) return false;
        if (spec.StopsLevelPoints is not > 0 || spec.PointSize <= 0m) return true;
        var minimum = spec.StopsLevelPoints.Value * spec.PointSize;
        return direction == SignalDirection.Long
            ? bid - stop >= minimum && target - ask >= minimum
            : stop - ask >= minimum && bid - target >= minimum;
    }
}
