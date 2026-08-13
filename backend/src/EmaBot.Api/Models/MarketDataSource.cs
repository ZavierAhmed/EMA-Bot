namespace EmaBot.Api.Models;

public enum MarketDataSource
{
    LegacyBinance,
    Mt5Exness
}

public static class MarketDataSourceLabels
{
    public static string For(MarketDataSource source) => source switch
    {
        MarketDataSource.LegacyBinance => "Legacy Binance",
        MarketDataSource.Mt5Exness => "MT5 / Exness",
        _ => source.ToString()
    };
}
