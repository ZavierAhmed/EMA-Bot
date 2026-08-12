namespace EmaBot.Api.Market;

public static class StrategyTimeframes
{
    private static readonly string[] CanonicalValues = ["3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M"];
    private static readonly HashSet<string> Lookup = new(CanonicalValues, StringComparer.Ordinal);

    public static IReadOnlyList<string> Supported => CanonicalValues;
    public static bool IsSupported(string? timeframe) => timeframe is not null && Lookup.Contains(timeframe);

    public static DateTimeOffset Shift(DateTimeOffset value, string timeframe, int count)
    {
        if (timeframe == "1M") return value.AddMonths(count);
        var unit = timeframe[^1];
        var amount = int.Parse(timeframe[..^1]) * count;
        return unit switch
        {
            'm' => value.AddMinutes(amount),
            'h' => value.AddHours(amount),
            'd' => value.AddDays(amount),
            'w' => value.AddDays(7 * amount),
            _ => throw new ArgumentException("Unsupported strategy timeframe.", nameof(timeframe))
        };
    }
}
