using EmaBot.Api.Market;

namespace EmaBot.Api.Strategy.Grid;

public sealed record GridHistoricalBar(Candle Bid, int SpreadPoints, decimal PointSize)
{
    public decimal AskLow => Bid.Low + SpreadPoints * PointSize;
    public decimal AskHigh => Bid.High + SpreadPoints * PointSize;
    public bool IsValid => Bid.IsClosed && Bid.CloseTimeUtc > Bid.OpenTimeUtc && SpreadPoints >= 0 && PointSize > 0m && Bid.Low > 0m
        && Bid.High >= Bid.Low && Bid.Open >= Bid.Low && Bid.Open <= Bid.High && Bid.Close >= Bid.Low && Bid.Close <= Bid.High;
}

public static class GridHistoricalFillRules
{
    public static bool IsTouched(GridRangeLevel level, GridHistoricalBar bar)
        => level.Direction == GridBasketDirection.Long ? bar.AskLow <= level.Price : bar.Bid.High >= level.Price;

    internal static GridBarResult Apply(GridRangeCycle cycle, GridHistoricalBar bar)
    {
        if (!bar.IsValid) throw new ArgumentException("A valid completed Bid bar and captured spread are required.", nameof(bar));
        if (cycle.ExitReason is not null || bar.Bid.OpenTimeUtc < cycle.Snapshot.Time) return new([]);
        var touched = cycle.Levels.Where(l => l.Status == GridLevelStatus.Candidate && IsTouched(l, bar)).ToArray();
        // OHLC does not establish which side was first. Defer an unlocked two-sided
        // bar without inventing a direction. This is explicitly not tick sequencing.
        if (cycle.Direction is null && touched.Select(l => l.Direction).Distinct().Count() > 1)
            return new([], Diagnostic: GridCycleDiagnostics.AmbiguousFirstSide);
        if (cycle.Direction is null && touched.Length > 0) cycle.Direction = touched[0].Direction;
        if (cycle.Direction is not { } direction) return new([]);
        var fills = touched.Where(l => l.Direction == direction).OrderBy(l => l.Number)
            .Select(l => l with { Status = GridLevelStatus.Filled, FillTime = bar.Bid.CloseTimeUtc }).ToArray();
        cycle.Levels = Array.AsReadOnly(cycle.Levels.Select(l => l.Direction != direction
            ? l with { Status = GridLevelStatus.Canceled }
            : fills.FirstOrDefault(f => f.Number == l.Number) ?? l).ToArray());
        // Conservative ordering: ALL touched same-side levels fill at their frozen
        // prices, nearest to deepest, BEFORE stop evaluation. Stop beats TP.
        // On the first fill bar TP is deferred: a prior anchor touch cannot prove a
        // post-entry return. An already-active basket may exit at the anchor.
        var stopTouched = direction == GridBasketDirection.Long ? bar.Bid.Low <= cycle.LongStop : bar.AskHigh >= cycle.ShortStop;
        var tpTouched = direction == GridBasketDirection.Long ? bar.Bid.High >= cycle.Anchor : bar.AskLow <= cycle.Anchor;
        var previouslyActive = cycle.Levels.Any(l => l.Status == GridLevelStatus.Filled && l.FillTime != bar.Bid.CloseTimeUtc);
        GridExitReason? exit = stopTouched ? GridExitReason.EmergencyStop : previouslyActive && tpTouched ? GridExitReason.TakeProfit : null;
        if (exit is not null)
        {
            cycle.ExitReason = exit;
            cycle.ExitPrice = exit == GridExitReason.TakeProfit ? cycle.Anchor : direction == GridBasketDirection.Long ? cycle.LongStop : cycle.ShortStop;
            cycle.ExitTime = bar.Bid.CloseTimeUtc;
            cycle.Levels = Array.AsReadOnly(cycle.Levels.Select(l => l with
                { Status = l.Status == GridLevelStatus.Filled ? GridLevelStatus.Closed : GridLevelStatus.Canceled }).ToArray());
        }
        return new(Array.AsReadOnly(fills), exit);
    }
}
