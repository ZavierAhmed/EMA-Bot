namespace EmaBot.Api.Binance;

public sealed class BinanceApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public bool IsRateLimited => StatusCode == StatusCodes.Status429TooManyRequests;
}
