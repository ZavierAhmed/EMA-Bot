namespace EmaBot.Api.Binance;

public interface IBinanceHistoricalCandleService { Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken); }

public sealed class BinanceHistoricalCandleService(IBinanceFuturesMarketDataClient client) : IBinanceHistoricalCandleService
{
    public const int MaximumCandles = 200_000;
    public async Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        if (startUtc >= endUtc) throw new ArgumentException("Start UTC must be before end UTC.");
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
}
