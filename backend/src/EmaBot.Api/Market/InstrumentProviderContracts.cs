namespace EmaBot.Api.Market;

public enum InstrumentTradeMode { Unknown, Disabled, LongOnly, ShortOnly, CloseOnly, Full }

public sealed record InstrumentCatalogItem(InstrumentSpec Spec, string? Description, string? Path, bool IsSelected, bool IsVisible, InstrumentTradeMode TradeMode);

public interface IInstrumentCatalogProvider
{
    Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken cancellationToken);
    Task<InstrumentCatalogItem?> GetAsync(string brokerSymbol, CancellationToken cancellationToken);
}

public interface IMarketQuoteProvider
{
    Task<MarketQuote> GetQuoteAsync(string brokerSymbol, CancellationToken cancellationToken);
}

public sealed class UnavailableInstrumentCatalogProvider : IInstrumentCatalogProvider
{
    public const string Message = "Instrument catalog is unavailable until the MT5 provider is connected.";
    private static Exception Error() => new MarketDataProviderException("MT5 instrument catalog", MarketDataErrorKind.Unavailable, Message);
    public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken cancellationToken) => Task.FromException<IReadOnlyList<InstrumentCatalogItem>>(Error());
    public Task<InstrumentCatalogItem?> GetAsync(string brokerSymbol, CancellationToken cancellationToken) => Task.FromException<InstrumentCatalogItem?>(Error());
}

public sealed class UnavailableMarketQuoteProvider : IMarketQuoteProvider
{
    public const string Message = "Market quotes are unavailable until the MT5 provider is connected.";
    public Task<MarketQuote> GetQuoteAsync(string brokerSymbol, CancellationToken cancellationToken)
        => Task.FromException<MarketQuote>(new MarketDataProviderException("MT5 market quotes", MarketDataErrorKind.Unavailable, Message));
}
