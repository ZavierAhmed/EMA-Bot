using EmaBot.Api.Market;

namespace EmaBot.Api.Binance;

/// <summary>Raw legacy Binance client retained only for historical research candles.</summary>
public interface IBinanceHistoricalKlineClient
{
    Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc, int? limit, CancellationToken cancellationToken);
}
