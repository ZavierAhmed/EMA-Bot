using EmaBot.Api.Market;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5BridgeInstrumentCatalogProvider(IMt5BridgeRequestClient bridge, IMarketProviderCapabilities capabilities) : IInstrumentCatalogProvider
{
    public async Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(Mt5BridgeOperation.GetInstruments, null, cancellationToken);
            return (response.DeserializePayload<IReadOnlyList<Mt5InstrumentCatalogItemPayload>>() ?? throw new MarketDataProviderException("MT5 instrument catalog", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid instrument catalog."))
                .Select(item => Map(item, capabilities.Current.TargetBroker)).ToArray();
        }
        catch (Exception exception) { throw Mt5BridgeProviderErrors.Catalog(exception); }
    }

    public async Task<InstrumentCatalogItem?> GetAsync(string brokerSymbol, CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(Mt5BridgeOperation.GetInstrument, new Mt5GetInstrumentRequest(brokerSymbol), cancellationToken);
            var item = response.DeserializePayload<Mt5InstrumentCatalogItemPayload>();
            return item is null ? throw new MarketDataProviderException("MT5 instrument catalog", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid instrument.") : Map(item, capabilities.Current.TargetBroker);
        }
        catch (Mt5BridgeRemoteException exception) when (exception.Code == "NotFound") { return null; }
        catch (Exception exception) { throw Mt5BridgeProviderErrors.Catalog(exception); }
    }

    internal static InstrumentCatalogItem Map(Mt5InstrumentCatalogItemPayload item, string broker)
    {
        var spec = item.Spec;
        if (string.IsNullOrWhiteSpace(spec.BrokerSymbol) || spec.PointSize <= 0m || spec.ContractSize <= 0m || spec.VolumeMin <= 0m || spec.VolumeMax < spec.VolumeMin || spec.VolumeStep <= 0m)
            throw new MarketDataProviderException("MT5 instrument catalog", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid instrument specification.");
        return new InstrumentCatalogItem(
            new InstrumentSpec(broker, spec.BrokerSymbol, spec.DisplaySymbol, AssetClass.Unknown, spec.Digits, spec.PointSize, spec.ContractSize, spec.VolumeMin, spec.VolumeMax, spec.VolumeStep, spec.CurrencyBase, spec.CurrencyProfit, spec.CurrencyMargin, spec.TickSize, spec.TickValueProfit, spec.TickValueLoss, spec.VolumeLimit, spec.StopsLevelPoints, spec.FreezeLevelPoints, Enum.TryParse<HistoricalChartMode>(spec.ChartMode, false, out var chartMode) ? chartMode : HistoricalChartMode.Unknown),
            item.Description, item.Path, item.IsSelected, item.IsVisible,
            Enum.TryParse<InstrumentTradeMode>(item.TradeMode, false, out var tradeMode) ? tradeMode : InstrumentTradeMode.Unknown);
    }
}

public sealed class Mt5BridgeMarketQuoteProvider(IMt5BridgeRequestClient bridge) : IMarketQuoteProvider
{
    public async Task<MarketQuote> GetQuoteAsync(string brokerSymbol, CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(Mt5BridgeOperation.GetQuote, new Mt5GetInstrumentRequest(brokerSymbol), cancellationToken);
            var quote = response.DeserializePayload<Mt5QuotePayload>() ?? throw new MarketDataProviderException("MT5 market quotes", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid quote.");
            return new MarketQuote(quote.BrokerSymbol, quote.TimeUtc, quote.Bid, quote.Ask, quote.Last, quote.Volume);
        }
        catch (Exception exception) when (exception is not MarketDataProviderException) { throw Mt5BridgeProviderErrors.Quote(exception); }
    }
}

public interface IMt5AccountReader
{
    Task<Mt5AccountPayload> GetAsync(CancellationToken cancellationToken);
}

public sealed class Mt5BridgeAccountReader(IMt5BridgeRequestClient bridge) : IMt5AccountReader
{
    public async Task<Mt5AccountPayload> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(Mt5BridgeOperation.GetAccount, null, cancellationToken);
            return response.DeserializePayload<Mt5AccountPayload>() ?? throw new MarketDataProviderException("MT5 account", MarketDataErrorKind.InvalidResponse, "MT5 returned invalid account information.");
        }
        catch (Exception exception) when (exception is not MarketDataProviderException) { throw Mt5BridgeProviderErrors.Account(exception); }
    }
}

public sealed record Mt5AccountResponse(string Server, string Currency, decimal Balance, decimal Equity, decimal Margin, decimal FreeMargin, decimal MarginLevel, string TradeMode)
{
    public static Mt5AccountResponse From(Mt5AccountPayload account) => new(account.Server, account.Currency, account.Balance, account.Equity, account.Margin, account.FreeMargin, account.MarginLevel, account.TradeMode);
}

internal static class Mt5BridgeProviderErrors
{
    public static MarketDataProviderException Catalog(Exception exception) => Translate("MT5 instrument catalog", exception);
    public static MarketDataProviderException Quote(Exception exception) => Translate("MT5 market quotes", exception);
    public static MarketDataProviderException Account(Exception exception) => Translate("MT5 account", exception);
    public static MarketDataProviderException TradeCalculation(Exception exception) => Translate("MT5 trade calculation", exception);

    internal static MarketDataErrorKind? KindFor(Exception exception) => exception switch
    {
        Mt5BridgeUnavailableException or Mt5BridgeDisconnectedException or IOException => MarketDataErrorKind.Unavailable,
        Mt5BridgeRequestTimeoutException => MarketDataErrorKind.Timeout,
        Mt5BridgeRemoteException remote when remote.Code is "NotFound" or "SymbolUnavailable" or "TerminalUnavailable" => MarketDataErrorKind.Unavailable,
        Mt5BridgeRemoteException => MarketDataErrorKind.InvalidResponse,
        ArgumentException => MarketDataErrorKind.InvalidResponse,
        _ => null
    };

    private static MarketDataProviderException Translate(string provider, Exception exception) => exception switch
    {
        MarketDataProviderException market => market,
        Mt5BridgeUnavailableException or Mt5BridgeDisconnectedException or IOException => new(provider, MarketDataErrorKind.Unavailable, "The MT5 bridge is not connected.", exception),
        Mt5BridgeRequestTimeoutException => new(provider, MarketDataErrorKind.Timeout, "The MT5 bridge request timed out.", exception),
        Mt5BridgeRemoteException remote when remote.Code is "NotFound" or "SymbolUnavailable" or "TerminalUnavailable" => new(provider, MarketDataErrorKind.Unavailable, "The requested MT5 data is unavailable.", exception),
        Mt5BridgeRemoteException remote => new(provider, MarketDataErrorKind.InvalidResponse, remote.Message, exception),
        ArgumentException => new(provider, MarketDataErrorKind.InvalidResponse, "MT5 returned invalid market data.", exception),
        _ => new(provider, MarketDataErrorKind.Unknown, "The MT5 bridge request failed.", exception)
    };
}
