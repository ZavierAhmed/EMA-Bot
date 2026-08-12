namespace EmaBot.Api.Market;

public sealed record MarketProviderCapabilities(
    string HistoricalProvider,
    bool HistoricalResearchConfigured,
    string TargetTerminal,
    string TargetBroker,
    bool InstrumentCatalogConfigured,
    bool QuoteProviderConfigured,
    bool LiveBarProviderConfigured,
    bool ExecutionProviderConfigured,
    IReadOnlyCollection<string> NativeTargetTimeframes);

public interface IMarketProviderCapabilities
{
    MarketProviderCapabilities Current { get; }
}

public sealed class MarketProviderCapabilityService : IMarketProviderCapabilities
{
    public MarketProviderCapabilities Current { get; } = new("Legacy Binance", true, "MetaTrader 5", "Exness", false, false, false, false, Mt5NativeTimeframes.Supported);
}
