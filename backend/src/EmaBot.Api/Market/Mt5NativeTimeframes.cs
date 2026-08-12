namespace EmaBot.Api.Market;

public static class Mt5NativeTimeframes
{
    private static readonly string[] NativeValues = ["3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "1w", "1M"];
    private static readonly HashSet<string> Lookup = new(NativeValues, StringComparer.Ordinal);

    public static IReadOnlyList<string> Supported => NativeValues;
    public static bool IsSupported(string? timeframe) => timeframe is not null && Lookup.Contains(timeframe);
}
