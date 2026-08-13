using EmaBot.Api.Binance;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class MarketDataSourceE9Tests
{
    [Fact]
    public void Model_PersistsSourceAsStringAndUsesSourceSymbolUniqueness()
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var database = new EmaBotDbContext(options);
        var entity = database.Model.FindEntityType(typeof(MonitoredSymbol))!;

        Assert.Equal(typeof(string), entity.FindProperty(nameof(MonitoredSymbol.Source))!.GetProviderClrType());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(MonitoredSymbol.Source), nameof(MonitoredSymbol.Symbol)]));
        Assert.Equal(64, entity.FindProperty(nameof(MonitoredSymbol.Symbol))!.GetMaxLength());
    }

    [Fact]
    public async Task AddMt5Symbol_PreservesExactBrokerCasingAndRejectsWrongCaseOrDuplicate()
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new EmaBotDbContext(options); await database.Database.EnsureCreatedAsync();
        var controller = new SymbolsController(database, new Catalog());

        var created = Assert.IsType<CreatedAtActionResult>((await controller.Add(new AddMonitoredSymbolRequest("XAUUSDm"), CancellationToken.None)).Result);
        var response = Assert.IsType<MonitoredSymbolResponse>(created.Value);
        Assert.Equal(MarketDataSource.Mt5Exness, response.Source); Assert.Equal("XAUUSDm", response.Symbol); Assert.Equal("Gold vs US Dollar", response.DisplayName);
        Assert.IsType<BadRequestObjectResult>((await controller.Add(new AddMonitoredSymbolRequest("XAUUSDM"), CancellationToken.None)).Result);
        Assert.IsType<ConflictObjectResult>((await controller.Add(new AddMonitoredSymbolRequest("XAUUSDm"), CancellationToken.None)).Result);
        Assert.Equal("XAUUSDm", (await database.MonitoredSymbols.SingleAsync()).Symbol);
    }

    [Fact]
    public void Resolver_RoutesOnlyByPersistedSource()
    {
        var legacy = new BinanceHistoricalMarketDataProvider(new TestBinanceClient());
        var mt5 = new Mt5BridgeHistoricalMarketDataProvider(new TestMt5BridgeRequestClient());
        var resolver = new HistoricalMarketDataProviderResolver(legacy, mt5);

        Assert.Same(legacy, resolver.Resolve(MarketDataSource.LegacyBinance));
        Assert.Same(mt5, resolver.Resolve(MarketDataSource.Mt5Exness));
    }

    private sealed class Catalog : IInstrumentCatalogProvider
    {
        private static readonly InstrumentCatalogItem Gold = new(new InstrumentSpec("Exness", "XAUUSDm", "XAUUSDm", AssetClass.Unknown, 2, .01m, 100m, .01m, 100m, .01m, "XAU", "USD", "USD", .01m, 1m, 1m, null, 0, 0), "Gold vs US Dollar", null, true, true, InstrumentTradeMode.Full);
        public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstrumentCatalogItem>>([Gold]);
        public Task<InstrumentCatalogItem?> GetAsync(string brokerSymbol, CancellationToken cancellationToken) => Task.FromResult<InstrumentCatalogItem?>(string.Equals(brokerSymbol, Gold.Spec.BrokerSymbol, StringComparison.Ordinal) ? Gold : null);
    }
}
