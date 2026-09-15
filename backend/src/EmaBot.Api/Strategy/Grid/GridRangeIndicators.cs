using EmaBot.Api.Market;

namespace EmaBot.Api.Strategy.Grid;

public static class GridRangeIndicators
{
    // Wilder ADX: seed TR/+DM/-DM with transitions 1..14; seed ADX with DX at
    // indices 14..27. Tied directional moves and a zero DI denominator yield zero.
    // ATR reuses the existing neutral Wilder14 convention (initial bar included).
    public static decimal? Adx14(IReadOnlyList<Candle> candles, int inclusiveIndex)
    {
        if (inclusiveIndex < 27 || inclusiveIndex >= candles.Count) return null;
        decimal tr = 0m, plus = 0m, minus = 0m, seed = 0m, adx = 0m;
        for (var i = 1; i <= inclusiveIndex; i++)
        {
            var c = candles[i]; var previous = candles[i - 1];
            var up = c.High - previous.High; var down = previous.Low - c.Low;
            var p = up > down && up > 0m ? up : 0m;
            var m = down > up && down > 0m ? down : 0m;
            var t = Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - previous.Close), Math.Abs(c.Low - previous.Close)));
            if (i <= 14) { tr += t; plus += p; minus += m; }
            else { tr = tr - tr / 14m + t; plus = plus - plus / 14m + p; minus = minus - minus / 14m + m; }
            if (i < 14) continue;
            var dx = tr == 0m || plus + minus == 0m ? 0m : 100m * Math.Abs(plus - minus) / (plus + minus);
            if (i <= 27) { seed += dx; if (i == 27) adx = seed / 14m; }
            else adx = (adx * 13m + dx) / 14m;
        }
        return adx;
    }

    public static GridRangeQualification Qualify(IReadOnlyList<Candle> candles, DateTimeOffset asOf)
    {
        var closed = candles.Where(c => c.IsClosed && c.CloseTimeUtc <= asOf).OrderBy(c => c.CloseTimeUtc).ToArray();
        var empty = new GridRangeIndicatorSnapshot(asOf, 0m, 0m, 0m, null, null, closed.Length);
        if (closed.Length < 50) return new(empty, GridCycleDiagnostics.InsufficientWarmup);
        if (closed.Any(c => c.CloseTimeUtc <= c.OpenTimeUtc || c.Low <= 0m || c.High < c.Low || c.Open < c.Low || c.Open > c.High || c.Close < c.Low || c.Close > c.High)
            || closed.Select(c => c.CloseTimeUtc).Distinct().Count() != closed.Length)
            return new(empty, GridCycleDiagnostics.InvalidCandles);
        var range = closed.TakeLast(50).ToArray();
        return Evaluate(new(closed[^1].CloseTimeUtc, range.Max(c => c.High), range.Min(c => c.Low), closed[^1].Close,
            AtrCalculator.Wilder14(closed, closed.Length - 1), Adx14(closed, closed.Length - 1), closed.Length));
    }

    public static GridRangeQualification Evaluate(GridRangeIndicatorSnapshot snapshot)
    {
        GridCycleDiagnostics? failure = snapshot.CompletedBars < 50 ? GridCycleDiagnostics.InsufficientWarmup
            : snapshot.Atr is null ? GridCycleDiagnostics.AtrUnavailable
            : snapshot.Atr <= 0m ? GridCycleDiagnostics.InvalidAtr
            : snapshot.Adx is null ? GridCycleDiagnostics.AdxUnavailable
            : snapshot.Adx < 0m || snapshot.Adx > 100m ? GridCycleDiagnostics.AdxUnavailable
            : snapshot.Adx > 20m ? GridCycleDiagnostics.AdxAboveThreshold : null;
        var q = new GridRangeQualification(snapshot, failure);
        if (failure is not null) return q;
        if (snapshot.RangeLow <= 0m || snapshot.RangeHigh < snapshot.RangeLow || q.Anchor <= 0m || q.Spacing <= 0m || q.Anchor - 6m * q.Spacing <= 0m)
            return q with { Failure = GridCycleDiagnostics.InvalidAnchorOrSpacing };
        return Math.Abs(snapshot.Close - q.Anchor) <= q.Spacing * .50m ? q : q with { Failure = GridCycleDiagnostics.CloseOutsideAnchor };
    }
}
