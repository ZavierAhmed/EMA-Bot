using EmaBot.Api.Binance;

namespace EmaBot.Api.Tests;

public sealed class TestBinanceClient : IBinanceFuturesMarketDataClient
{
    private static readonly IReadOnlyList<BinanceSymbol> Symbols = [new("BTCUSDT", "BTC", "USDT", "TRADING", "PERPETUAL")];
    public Task<BinanceExchangeInfo> GetExchangeInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new BinanceExchangeInfo(Symbols));
    public Task<IReadOnlyList<BinanceSymbol>> GetTradableUsdtPerpetualSymbolsAsync(CancellationToken cancellationToken) => Task.FromResult(Symbols);
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

public sealed class TestBinanceStreamClient : IBinanceFuturesStreamClient
{
    public IReadOnlyList<string> LastSymbols { get; private set; } = [];
    public string? LastInterval { get; private set; }
    public async Task StreamAsync(IReadOnlyCollection<string> symbols, string interval, Func<BinanceKlineUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken)
    {
        LastSymbols = symbols.ToArray(); LastInterval = interval; onStateChange?.Invoke("Connected");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
