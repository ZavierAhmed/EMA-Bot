namespace EmaBot.Api.Market;

public interface IHistoricalMarketDataProvider
{
    Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string timeframe, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<Candle>> GetLatestAsync(string symbol, string timeframe, int count, CancellationToken cancellationToken)
        => throw new NotSupportedException("This market-data provider does not support latest-bar retrieval.");
}

public sealed record MarketBarUpdate(
    string Symbol,
    string Timeframe,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset OpenTimeUtc,
    DateTimeOffset CloseTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    bool IsClosed);

public interface IMarketBarStreamProvider
{
    Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken);
}
