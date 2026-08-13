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
    public MarketProviderCapabilityService() : this(false) { }
    public MarketProviderCapabilityService(Microsoft.Extensions.Options.IOptions<EmaBot.Api.Mt5Bridge.Mt5BridgeOptions> options) : this(options.Value.Enabled) { }
    private MarketProviderCapabilityService(bool bridgeEnabled)
    {
        Current = new("MT5 / Exness", bridgeEnabled, "MetaTrader 5", "Exness", bridgeEnabled, bridgeEnabled, false, false, Mt5NativeTimeframes.Supported);
    }
    public MarketProviderCapabilities Current { get; }
}
