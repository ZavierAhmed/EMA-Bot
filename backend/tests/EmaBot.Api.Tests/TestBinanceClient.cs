using EmaBot.Api.Binance;

namespace EmaBot.Api.Tests;

public sealed class TestBinanceClient : IBinanceFuturesMarketDataClient
{
    private static readonly IReadOnlyList<BinanceSymbol> Symbols = [new("BTCUSDT", "BTC", "USDT", "TRADING", "PERPETUAL")];
    public Task<BinanceExchangeInfo> GetExchangeInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new BinanceExchangeInfo(Symbols));
    public Task<IReadOnlyList<BinanceSymbol>> GetTradableUsdtPerpetualSymbolsAsync(CancellationToken cancellationToken) => Task.FromResult(Symbols);
    public IReadOnlyList<Candle> Klines { get; set; } = [];
    public Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc, int? limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Klines);
    }
}
