using EmaBot.Api.Market;

namespace EmaBot.Api.Binance;

/// <summary>Legacy Binance historical research provider. It is not a live or execution adapter.</summary>
public sealed class BinanceHistoricalMarketDataProvider(IBinanceHistoricalKlineClient client) : IHistoricalMarketDataProvider
{
    public const int MaximumCandles = 200_000;

    public async Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc) throw new ArgumentException("Start UTC must be before end UTC.");
        try
        {
            var all = new SortedDictionary<DateTimeOffset, Candle>(); var cursor = startUtc;
            while (cursor <= endUtc)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await client.GetKlinesAsync(symbol, interval, cursor, endUtc, 1500, cancellationToken);
                if (page.Count == 0) break;
                foreach (var candle in page.Where(candle => candle.OpenTimeUtc >= startUtc && candle.CloseTimeUtc <= endUtc && candle.IsClosed)) all[candle.OpenTimeUtc] = candle;
                if (all.Count > MaximumCandles) throw new ArgumentException($"Backtests cannot exceed {MaximumCandles:N0} candles.");
                var last = page.MaxBy(candle => candle.CloseTimeUtc)!;
                var next = last.CloseTimeUtc.AddMilliseconds(1);
                if (next <= cursor || last.CloseTimeUtc >= endUtc || page.Count < 1500) break;
                cursor = next;
            }
            return all.Values.ToArray();
        }
        catch (BinanceApiException exception) { throw Translate(exception); }
    }

    public async Task<IReadOnlyList<Candle>> GetLatestAsync(string symbol, string timeframe, int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > 1500) throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 1500.");
        try
        {
            return (await client.GetKlinesAsync(symbol, timeframe, null, null, count, cancellationToken)).Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
        }
        catch (BinanceApiException exception) { throw Translate(exception); }
    }

    private static MarketDataProviderException Translate(BinanceApiException exception) => new("Legacy Binance historical research", exception.StatusCode switch
    {
        StatusCodes.Status429TooManyRequests => MarketDataErrorKind.RateLimited,
        StatusCodes.Status504GatewayTimeout => MarketDataErrorKind.Timeout,
        >= 500 => MarketDataErrorKind.Unavailable,
        _ => MarketDataErrorKind.InvalidResponse
    }, exception.Message, exception);
}
