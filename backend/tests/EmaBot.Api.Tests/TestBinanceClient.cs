using EmaBot.Api.Binance;
using EmaBot.Api.Mt5Bridge;

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

public sealed class TestMt5BridgeRequestClient : IMt5BridgeRequestClient
{
    public bool IsConnected { get; set; } = true;
    public Mt5BridgeStatus Status { get; set; } = new(true, Mt5BridgeProtocol.ProtocolVersion, "test-bridge", Mt5BridgeConnectionState.Connected, Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, "test", "terminal", "Synthetic MT5", "Synthetic", 1, "Synthetic-Server", "USD", "Demo", null);
    public Dictionary<Mt5BridgeOperation, Mt5BridgeEnvelope> Responses { get; } = [];
    public Exception? Exception { get; set; }
    public Mt5BridgeOperation? LastOperation { get; private set; }
    public object? LastPayload { get; private set; }
    public Mt5BridgeStatus GetStatus() => Status;
    public Task<Mt5BridgeEnvelope> SendAsync(Mt5BridgeOperation operation, object? payload, CancellationToken cancellationToken)
    {
        LastOperation = operation; LastPayload = payload;
        if (Exception is not null) return Task.FromException<Mt5BridgeEnvelope>(Exception);
        return Responses.TryGetValue(operation, out var response) ? Task.FromResult(response) : Task.FromException<Mt5BridgeEnvelope>(new Mt5BridgeUnavailableException("The MT5 bridge is not connected."));
    }
}
