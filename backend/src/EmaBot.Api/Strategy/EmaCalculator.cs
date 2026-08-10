namespace EmaBot.Api.Strategy;

public static class EmaCalculator
{
    // The first EMA is seeded with the SMA of the first complete period; prior positions are null.
    public static IReadOnlyList<decimal?> Calculate(IReadOnlyList<decimal> values, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        var result = Enumerable.Repeat<decimal?>(null, values.Count).ToArray();
        if (values.Count < period) return result;
        var ema = values.Take(period).Average();
        result[period - 1] = ema;
        var multiplier = 2m / (period + 1m);
        for (var index = period; index < values.Count; index++)
        {
            ema = ((values[index] - ema) * multiplier) + ema;
            result[index] = ema;
        }
        return result;
    }
}
