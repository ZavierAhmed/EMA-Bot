using System.Net;
using System.Net.Http.Json;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using EmaBot.Api.Execution;
using EmaBot.Api.Market;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EmaBot.Api.Tests;

public sealed class MarketProviderContractsTests
{
    [Fact]
    public void InstrumentSpec_OptionalMt5FieldsRemainNonPersistedAndDoNotChangeVolumeCalculation()
    {
        var spec = new InstrumentSpec("Synthetic", "SYN", "Synthetic", AssetClass.Unknown, 2, .01m, 100m, .01m, 100m, .01m, null, null, null, .01m, 2m, 1.5m, 10m, 20, 5);
        var result = InstrumentVolumeCalculator.Calculate(spec, 50m, 1000m);

        Assert.Equal(.01m, spec.TickSize); Assert.Equal(2m, spec.TickValueProfit); Assert.Equal(1.5m, spec.TickValueLoss); Assert.Equal(10m, spec.VolumeLimit); Assert.Equal(20, spec.StopsLevelPoints); Assert.Equal(5, spec.FreezeLevelPoints);
        Assert.True(result.IsAccepted); Assert.Equal(.20m, result.Lots); Assert.Equal(20m, result.Quantity);
    }

    [Fact]
    public void MarketQuote_ValidatesBidAskAndPreservesUnroundedSpread()
    {
        var quote = new MarketQuote("SYN", DateTimeOffset.UnixEpoch, 100m, 100.25m, 100.20m, 42m);

        Assert.Equal(.25m, quote.Spread); Assert.Equal(100.20m, quote.Last); Assert.Equal(42m, quote.Volume);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketQuote("SYN", DateTimeOffset.UnixEpoch, 0m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketQuote("SYN", DateTimeOffset.UnixEpoch, 1m, 0m));
        Assert.Throws<ArgumentException>(() => new MarketQuote("SYN", DateTimeOffset.UnixEpoch, 2m, 1m));
    }

    [Fact]
    public void Mt5NativeTimeframes_ExcludeCanonicalThreeDayTimeframe()
    {
        var native = new[] { "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "1w", "1M" };

        Assert.Equal(native, Mt5NativeTimeframes.Supported);
        Assert.All(native, timeframe => Assert.True(Mt5NativeTimeframes.IsSupported(timeframe)));
        Assert.True(StrategyTimeframes.IsSupported("3d"));
        Assert.False(Mt5NativeTimeframes.IsSupported("3d"));
    }

    [Fact]
    public async Task UnavailableCatalogAndQuoteProviders_FailWithNeutralAvailabilityErrors()
    {
        var catalog = new UnavailableInstrumentCatalogProvider();
        var quotes = new UnavailableMarketQuoteProvider();

        var catalogError = await Assert.ThrowsAsync<MarketDataProviderException>(() => catalog.GetAvailableAsync(CancellationToken.None));
        var quoteError = await Assert.ThrowsAsync<MarketDataProviderException>(() => quotes.GetQuoteAsync("broker-symbol", CancellationToken.None));
        Assert.Equal(MarketDataErrorKind.Unavailable, catalogError.Kind); Assert.Equal(UnavailableInstrumentCatalogProvider.Message, catalogError.Message);
        Assert.Equal(MarketDataErrorKind.Unavailable, quoteError.Kind); Assert.Equal(UnavailableMarketQuoteProvider.Message, quoteError.Message);
    }

    [Fact]
    public void CurrentCapabilities_AreTruthfulMigrationConfiguration()
    {
        var capabilities = new MarketProviderCapabilityService().Current;

        Assert.Equal("Legacy Binance", capabilities.HistoricalProvider); Assert.True(capabilities.HistoricalResearchConfigured);
        Assert.Equal("MetaTrader 5", capabilities.TargetTerminal); Assert.Equal("Exness", capabilities.TargetBroker);
        Assert.False(capabilities.InstrumentCatalogConfigured); Assert.False(capabilities.QuoteProviderConfigured); Assert.False(capabilities.LiveBarProviderConfigured); Assert.False(capabilities.ExecutionProviderConfigured);
        Assert.Contains("1M", capabilities.NativeTargetTimeframes); Assert.DoesNotContain("3d", capabilities.NativeTargetTimeframes);
    }
}

public sealed class MarketProviderApiTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;
    public MarketProviderApiTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task InstrumentAndQuoteEndpoints_ReturnTruthfulUnavailableResponses()
    {
        using var client = await AdminClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/instruments")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/instruments/broker-symbol")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/instruments/broker-symbol/quote")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/instruments/%20")).StatusCode);
    }

    [Fact]
    public async Task CapabilityEndpoint_ReturnsCurrentMigrationState()
    {
        using var client = await AdminClient();
        var response = await client.GetAsync("/api/market/provider-capabilities");
        var capabilities = await response.Content.ReadFromJsonAsync<MarketProviderCapabilities>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.NotNull(capabilities);
        Assert.Equal("Legacy Binance", capabilities.HistoricalProvider); Assert.True(capabilities.HistoricalResearchConfigured);
        Assert.Equal("MetaTrader 5", capabilities.TargetTerminal); Assert.Equal("Exness", capabilities.TargetBroker);
        Assert.False(capabilities.InstrumentCatalogConfigured); Assert.False(capabilities.QuoteProviderConfigured); Assert.False(capabilities.LiveBarProviderConfigured); Assert.False(capabilities.ExecutionProviderConfigured);
        Assert.Contains("1M", capabilities.NativeTargetTimeframes); Assert.DoesNotContain("3d", capabilities.NativeTargetTimeframes);
    }

    private async Task<HttpClient> AdminClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) };
        request.Headers.Add("X-CSRF-TOKEN", token!.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        return client;
    }
}
