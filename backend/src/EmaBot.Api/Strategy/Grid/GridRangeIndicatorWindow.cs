using EmaBot.Api.Market;

namespace EmaBot.Api.Strategy.Grid;

// O(50) per candle, O(50) storage. Exact G0 decimal seed/operation order, without
// truncating Wilder history when the rolling range window advances.
public sealed class GridRangeIndicatorWindow
{
    private readonly Queue<Candle> range = new();
    private Candle? previous;
    private int count;
    private decimal atr, tr, plus, minus, dxSeed, adx;

    public GridRangeIndicatorSnapshot Append(Candle candle)
    {
        if (!new GridHistoricalBar(candle, 0, 1m).IsValid || previous is not null && candle.OpenTimeUtc < previous.CloseTimeUtc)
            throw new ArgumentException("Completed, non-overlapping chronological candles are required.", nameof(candle));
        var index = count++;
        var trueRange = previous is null ? candle.High - candle.Low
            : Math.Max(candle.High - candle.Low, Math.Max(Math.Abs(candle.High - previous.Close), Math.Abs(candle.Low - previous.Close)));
        if (index < 14) { atr += trueRange; if (index == 13) atr /= 14m; }
        else atr = (atr * 13m + trueRange) / 14m;
        if (previous is not null)
        {
            var up = candle.High - previous.High; var down = previous.Low - candle.Low;
            var p = up > down && up > 0m ? up : 0m;
            var m = down > up && down > 0m ? down : 0m;
            if (index <= 14) { tr += trueRange; plus += p; minus += m; }
            else { tr = tr - tr / 14m + trueRange; plus = plus - plus / 14m + p; minus = minus - minus / 14m + m; }
            if (index >= 14)
            {
                var dx = tr == 0m || plus + minus == 0m ? 0m : 100m * Math.Abs(plus - minus) / (plus + minus);
                if (index <= 27) { dxSeed += dx; if (index == 27) adx = dxSeed / 14m; }
                else adx = (adx * 13m + dx) / 14m;
            }
        }
        previous = candle;
        range.Enqueue(candle); if (range.Count > 50) range.Dequeue();
        return new(candle.CloseTimeUtc, range.Max(c => c.High), range.Min(c => c.Low), candle.Close,
            index >= 13 ? atr : null, index >= 27 ? adx : null, count);
    }
}
