using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Services;

// Canonical broker-price representation for protection writes and proof.  A caller
// must already be on the native tick grid; this helper never silently rounds risk.
public static class DemoExecutionProtectionPrices
{
    public static bool TryCanonicalize(decimal price, Mt5ExecutionPositionPayload position, out decimal canonical)
    {
        canonical = price;
        var tick = position.TickSize is > 0m ? position.TickSize : position.PointSize is > 0m ? position.PointSize : null;
        // A requested protection price cannot be safely normalized or compared
        // without the broker's native price increment.  Fail closed instead of
        // treating an unknown grid as permissive.
        if (tick is not > 0m) return false;
        var ticks = price / tick.Value;
        var roundedTicks = decimal.Round(ticks, 0, MidpointRounding.AwayFromZero);
        canonical = roundedTicks * tick.Value;
        // A decimal representation can carry harmless trailing precision; anything
        // beyond that tolerance is an off-grid request and is rejected, not rounded.
        return decimal.Abs(price - canonical) <= tick.Value / 1_000_000m;
    }

    public static bool Equivalent(decimal expected, decimal actual, Mt5ExecutionPositionPayload position) =>
        TryCanonicalize(expected, position, out var canonicalExpected)
        && TryCanonicalize(actual, position, out var canonicalActual)
        && canonicalExpected == canonicalActual;

    public static bool MeetsKnownBrokerDistances(string side, decimal stopLoss, decimal takeProfit, Mt5ExecutionPositionPayload position)
    {
        if (position.Bid is not > 0m || position.Ask is not > 0m || position.PointSize is not > 0m) return true; // EA remains final authority when an exact quote is unavailable.
        var minimum = (position.StopsLevelPoints ?? 0) * position.PointSize.Value;
        var freeze = (position.FreezeLevelPoints ?? 0) * position.PointSize.Value;
        var distance = Math.Max(minimum, freeze);
        return string.Equals(side, "Buy", StringComparison.OrdinalIgnoreCase)
            ? stopLoss < position.Bid && takeProfit > position.Ask && position.Bid.Value - stopLoss >= distance && takeProfit - position.Ask.Value >= distance
            : string.Equals(side, "Sell", StringComparison.OrdinalIgnoreCase)
              && stopLoss > position.Ask && takeProfit < position.Bid && stopLoss - position.Ask.Value >= distance && position.Bid.Value - takeProfit >= distance;
    }
}
