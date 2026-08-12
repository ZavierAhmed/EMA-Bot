using EmaBot.Api.Market;

namespace EmaBot.Api.Binance;

public interface IBinanceFuturesMarketDataClient
{
    Task<BinanceExchangeInfo> GetExchangeInfoAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BinanceSymbol>> GetTradableUsdtPerpetualSymbolsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc, int? limit, CancellationToken cancellationToken);
}
