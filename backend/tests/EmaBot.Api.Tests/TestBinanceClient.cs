using EmaBot.Api.Binance;

namespace EmaBot.Api.Tests;

public sealed class TestBinanceClient : IBinanceHistoricalKlineClient
{
    public IReadOnlyList<Candle> Klines { get; set; } = [];
    public Exception? KlinesException { get; set; }
    public int KlineRequests { get; private set; }
    public void ResetKlineRequests() => KlineRequests = 0;
    public Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc, int? limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); KlineRequests++; if (KlinesException is not null) throw KlinesException;
        return Task.FromResult(Klines);
    }
}

public sealed class TestBinanceStreamClient : IMarketBarStreamProvider
{
    public IReadOnlyList<string> LastSymbols { get; private set; } = [];
    public string? LastInterval { get; private set; }
    public async Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken)
    {
        LastSymbols = symbols.ToArray(); LastInterval = timeframe; onStateChange?.Invoke("Connected");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

public sealed class TestMarketProviderCapabilities(MarketProviderCapabilities current) : IMarketProviderCapabilities
{
    public MarketProviderCapabilities Current { get; } = current;
    public static TestMarketProviderCapabilities WithLiveBars(bool configured) => new(new MarketProviderCapabilities("Legacy Binance", true, "MetaTrader 5", "Exness", false, false, configured, false, Mt5NativeTimeframes.Supported));
}
