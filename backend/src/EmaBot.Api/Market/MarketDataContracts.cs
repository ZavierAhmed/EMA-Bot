namespace EmaBot.Api.Market;

public enum MarketDataErrorKind { Unavailable, Timeout, RateLimited, InvalidResponse, Unknown }

public sealed class MarketDataProviderException(string provider, MarketDataErrorKind kind, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Provider { get; } = provider;
    public MarketDataErrorKind Kind { get; } = kind;
}

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

public sealed class UnavailableMarketBarStreamProvider : IMarketBarStreamProvider
{
    public const string Message = "Live market-bar streaming is unavailable until the MT5 provider is configured.";

    public Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException(Message));
}
