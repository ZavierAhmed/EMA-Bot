using System.Text.Json;
using EmaBot.Api.Controllers;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class Mt5BridgeProviderTests
{
    [Fact]
    public async Task CatalogMapping_PreservesMt5SpecificationAndSymbolSpelling()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.GetInstruments] = Response(Mt5BridgeOperation.GetInstruments, new[] { Item("EURUSD.pro") });
        var provider = new Mt5BridgeInstrumentCatalogProvider(bridge, new TestCapabilities(true));
        var catalog = await provider.GetAvailableAsync(CancellationToken.None);
        var item = Assert.Single(catalog);
        Assert.Equal("Exness", item.Spec.Broker); Assert.Equal("EURUSD.pro", item.Spec.BrokerSymbol); Assert.Equal(100000m, item.Spec.ContractSize); Assert.Equal(.01m, item.Spec.VolumeMin); Assert.Equal(100m, item.Spec.VolumeMax); Assert.Equal(.01m, item.Spec.VolumeStep);
        Assert.Equal(.00001m, item.Spec.TickSize); Assert.Equal(1.25m, item.Spec.TickValueProfit); Assert.Equal(1.2m, item.Spec.TickValueLoss); Assert.Equal(50m, item.Spec.VolumeLimit); Assert.Equal(15, item.Spec.StopsLevelPoints); Assert.Equal(5, item.Spec.FreezeLevelPoints);
        Assert.Equal("EUR", item.Spec.CurrencyBase); Assert.Equal("USD", item.Spec.CurrencyProfit); Assert.Equal("USD", item.Spec.CurrencyMargin); Assert.Equal(InstrumentTradeMode.Full, item.TradeMode); Assert.Equal("Escaped \"description\"", item.Description); Assert.Equal("Forex\\Major", item.Path);
    }

    [Fact]
    public async Task QuoteMapping_PreservesBidAskTimestampAndRejectsInvalidPayload()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.GetQuote] = Response(Mt5BridgeOperation.GetQuote, new Mt5QuotePayload("exact.symbol", timestamp, 100m, 100.2m, 100.1m, 9m));
        var provider = new Mt5BridgeMarketQuoteProvider(bridge);
        var quote = await provider.GetQuoteAsync("exact.symbol", CancellationToken.None);
        Assert.Equal("exact.symbol", quote.BrokerSymbol); Assert.Equal(timestamp, quote.TimeUtc); Assert.Equal(100m, quote.Bid); Assert.Equal(100.2m, quote.Ask); Assert.Equal(.2m, quote.Spread);
        bridge.Responses[Mt5BridgeOperation.GetQuote] = Response(Mt5BridgeOperation.GetQuote, new Mt5QuotePayload("exact.symbol", timestamp, 0m, 1m, null, null));
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => provider.GetQuoteAsync("exact.symbol", CancellationToken.None));
        Assert.Equal(MarketDataErrorKind.InvalidResponse, error.Kind);
    }

    [Fact]
    public async Task ConnectedControllersReturnReadOnlyDataAndSanitizeAccountLogin()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.GetInstruments] = Response(Mt5BridgeOperation.GetInstruments, new[] { Item("XAUUSD.a") });
        bridge.Responses[Mt5BridgeOperation.GetInstrument] = Response(Mt5BridgeOperation.GetInstrument, Item("XAUUSD.a"));
        bridge.Responses[Mt5BridgeOperation.GetQuote] = Response(Mt5BridgeOperation.GetQuote, new Mt5QuotePayload("XAUUSD.a", DateTimeOffset.UnixEpoch, 100m, 100.2m, null, null));
        bridge.Responses[Mt5BridgeOperation.GetAccount] = Response(Mt5BridgeOperation.GetAccount, new Mt5AccountPayload(987654321, "Exness-Demo", "USD", 1000m, 1010m, 12m, 998m, 8416m, "Demo"));
        var capabilities = new TestCapabilities(true);
        var controller = new InstrumentsController(new Mt5BridgeInstrumentCatalogProvider(bridge, capabilities), new Mt5BridgeMarketQuoteProvider(bridge));
        var accountController = new Mt5AccountController(new Mt5BridgeAccountReader(bridge));
        Assert.IsType<OkObjectResult>((await controller.GetAvailable(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Get("XAUUSD.a", CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetQuote("XAUUSD.a", CancellationToken.None)).Result);
        var account = Assert.IsType<OkObjectResult>((await accountController.Get(CancellationToken.None)).Result).Value;
        var serialized = JsonSerializer.Serialize(account, Mt5BridgeProtocol.JsonOptions);
        Assert.DoesNotContain("987654321", serialized); Assert.DoesNotContain("login", serialized, StringComparison.OrdinalIgnoreCase); Assert.Contains("Exness-Demo", serialized);
    }

    [Fact]
    public async Task DisconnectedBridgeTranslatesToUnavailableData()
    {
        var bridge = new TestMt5BridgeRequestClient { IsConnected = false, Exception = new Mt5BridgeDisconnectedException("disconnected") };
        var catalog = new Mt5BridgeInstrumentCatalogProvider(bridge, new TestCapabilities(true));
        var quotes = new Mt5BridgeMarketQuoteProvider(bridge);
        var account = new Mt5BridgeAccountReader(bridge);
        Assert.Equal(MarketDataErrorKind.Unavailable, (await Assert.ThrowsAsync<MarketDataProviderException>(() => catalog.GetAvailableAsync(CancellationToken.None))).Kind);
        Assert.Equal(MarketDataErrorKind.Unavailable, (await Assert.ThrowsAsync<MarketDataProviderException>(() => quotes.GetQuoteAsync("exact.symbol", CancellationToken.None))).Kind);
        Assert.Equal(MarketDataErrorKind.Unavailable, (await Assert.ThrowsAsync<MarketDataProviderException>(() => account.GetAsync(CancellationToken.None))).Kind);
        var accountResult = await new Mt5AccountController(account).Get(CancellationToken.None);
        Assert.Equal(503, Assert.IsType<ObjectResult>(accountResult.Result).StatusCode);
    }

    [Fact]
    public void CapabilitiesRemainFalseWhenDisabledAndEnableOnlyReadOnlyProviders()
    {
        var disabled = new MarketProviderCapabilityService().Current;
        var enabled = new MarketProviderCapabilityService(Options.Create(new Mt5BridgeOptions { Enabled = true, HandshakeSecret = new string('s', 32) })).Current;
        Assert.False(disabled.InstrumentCatalogConfigured); Assert.False(disabled.QuoteProviderConfigured);
        Assert.True(enabled.InstrumentCatalogConfigured); Assert.True(enabled.QuoteProviderConfigured); Assert.False(enabled.LiveBarProviderConfigured); Assert.False(enabled.ExecutionProviderConfigured);
    }

    private static Mt5BridgeEnvelope Response(Mt5BridgeOperation operation, object payload) => Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, operation, Guid.NewGuid(), payload, TimeProvider.System);
    private static Mt5InstrumentCatalogItemPayload Item(string symbol) => new(new Mt5InstrumentSpecPayload(symbol, symbol, "Unknown", 5, .00001m, 100000m, .01m, 100m, .01m, .00001m, 1.25m, 1.2m, 50m, 15, 5, "EUR", "USD", "USD"), "Escaped \"description\"", "Forex\\Major", true, true, "Full");

    private sealed class TestCapabilities(bool bridgeEnabled) : IMarketProviderCapabilities
    {
        public MarketProviderCapabilities Current { get; } = new("Legacy Binance", true, "MetaTrader 5", "Exness", bridgeEnabled, bridgeEnabled, false, false, Mt5NativeTimeframes.Supported);
    }
}
