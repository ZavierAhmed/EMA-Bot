namespace EmaBot.Api.Binance;

public sealed record BinanceSymbol(string Symbol, string BaseAsset, string QuoteAsset, string Status, string ContractType);
public sealed record BinanceExchangeInfo(IReadOnlyList<BinanceSymbol> Symbols);

public sealed class BinanceApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public bool IsRateLimited => StatusCode == StatusCodes.Status429TooManyRequests;
}
