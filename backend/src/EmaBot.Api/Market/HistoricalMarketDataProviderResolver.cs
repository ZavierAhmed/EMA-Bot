using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Market;

public interface IHistoricalMarketDataProviderResolver
{
    IHistoricalMarketDataProvider Resolve(MarketDataSource source);
}

public sealed class HistoricalMarketDataProviderResolver(
    BinanceHistoricalMarketDataProvider legacyBinance,
    Mt5BridgeHistoricalMarketDataProvider mt5Exness) : IHistoricalMarketDataProviderResolver
{
    public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => source switch
    {
        MarketDataSource.LegacyBinance => legacyBinance,
        MarketDataSource.Mt5Exness => mt5Exness,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported market-data source.")
    };
}
