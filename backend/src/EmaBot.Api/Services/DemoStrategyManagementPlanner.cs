using EmaBot.Api.Market;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

// B2 planning is intentionally separate from B1 validation: it aligns only
// strategy-generated mathematical objectives and never rounds a caller's manual
// request.  B1 remains the final native ownership/grid/stops authority.
public static class DemoStrategyManagementPlanner
{
    public static decimal? ExecutableManagementPrice(SignalDirection direction, decimal? bid, decimal? ask) => direction switch
    {
        SignalDirection.Long when bid is > 0m => bid,
        SignalDirection.Short when ask is > 0m => ask,
        _ => null
    };

    public static decimal? Align(decimal price, SignalDirection direction, InstrumentSpec specification)
    {
        var tick = specification.TickSize is > 0m ? specification.TickSize : specification.PointSize is > 0m ? specification.PointSize : null;
        if (tick is not > 0m || price <= 0m) return null;
        var units = price / tick.Value;
        return direction == SignalDirection.Long
            ? decimal.Ceiling(units) * tick.Value
            : decimal.Floor(units) * tick.Value;
    }

    public static decimal NextBest(SignalDirection direction, decimal? existing, decimal quote) => direction == SignalDirection.Long
        ? Math.Max(existing ?? quote, quote)
        : Math.Min(existing ?? quote, quote);

    public static decimal Progress(decimal entry, decimal originalTarget, decimal best, SignalDirection direction) =>
        Math.Max(0m, TradeMath.Progress(entry, originalTarget, best, direction));
}
