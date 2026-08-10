namespace EmaBot.Api.Binance;

public sealed record BinanceSymbol(string Symbol, string BaseAsset, string QuoteAsset, string Status, string ContractType);
public sealed record BinanceExchangeInfo(IReadOnlyList<BinanceSymbol> Symbols);
public sealed record Candle(DateTimeOffset OpenTimeUtc, DateTimeOffset CloseTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, bool IsClosed);

public static class BinanceIntervals
{
    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M"
    };

    public static IReadOnlyCollection<string> Supported => Values;
    public static bool IsSupported(string? interval) => interval is not null && Values.Contains(interval);
}

public sealed class BinanceApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public bool IsRateLimited => StatusCode == StatusCodes.Status429TooManyRequests;
}
