using EmaBot.Api.Configuration;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class BacktestNativeEconomicsPreviewTests
{
    [Fact]
    public async Task Preview_ReadyOnlyWhenCompleteNativeEvidenceIsAvailable()
    {
        await using var database = Database();
        database.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", IsEnabled = true, PaperCommissionPerLotPerSide = 2.5m });
        await database.SaveChangesAsync();
        var service = Service(database, Spec());

        var preview = await service.GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);

        Assert.True(preview.Ready); Assert.Equal("BTCUSDm", preview.BrokerSymbol); Assert.Equal("USD", preview.AccountCurrency);
        Assert.Equal(PaperPositionSizingMode.FixedLots, preview.SizingMode); Assert.Equal(.01m, preview.FixedLots);
        Assert.Equal(2.5m, preview.CommissionPerLotPerSide); Assert.Equal(Mt5HistoricalBacktestEngine.SpreadModel, preview.HistoricalSpreadModel);
    }

    [Fact]
    public async Task Preview_ReadyResponseContainsCompleteNativeEconomicsEvidence()
    {
        await using var database = await SeedAsync();

        var preview = await Service(database, Spec()).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);

        Assert.True(preview.Ready); Assert.Null(preview.Reason); Assert.Equal("BTCUSDm", preview.BrokerSymbol); Assert.Equal("USD", preview.AccountCurrency); Assert.Equal(1000m, preview.StartingBalance);
        Assert.Equal(PaperPositionSizingMode.FixedLots, preview.SizingMode); Assert.Equal(.01m, preview.FixedLots); Assert.Equal(10m, preview.MarginPerTradePercent); Assert.Equal(0m, preview.CommissionPerLotPerSide);
        Assert.Equal(Mt5HistoricalBacktestEngine.SpreadModel, preview.HistoricalSpreadModel); Assert.Equal(HistoricalChartMode.Bid.ToString(), preview.ChartMode); Assert.Equal(.1m, preview.PointSize); Assert.Equal(1m, preview.ContractSize); Assert.Equal(.01m, preview.VolumeMin); Assert.Equal(1m, preview.VolumeMax); Assert.Equal(.01m, preview.VolumeStep); Assert.Equal(0, preview.StopsLevelPoints);
    }

    [Fact]
    public async Task Preview_ZeroCommissionIsReadyButNullCommissionFailsClosed()
    {
        await using var database = Database();
        database.MonitoredSymbols.AddRange(
            new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "ZEROm", IsEnabled = true, PaperCommissionPerLotPerSide = 0m },
            new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "NULLm", IsEnabled = true, PaperCommissionPerLotPerSide = null });
        await database.SaveChangesAsync();
        var zero = await Service(database, Spec() with { BrokerSymbol = "ZEROm" }).GetMt5EconomicsPreviewAsync("ZEROm", CancellationToken.None);
        var missing = await Service(database, Spec() with { BrokerSymbol = "NULLm" }).GetMt5EconomicsPreviewAsync("NULLm", CancellationToken.None);

        Assert.True(zero.Ready); Assert.Equal(0m, zero.CommissionPerLotPerSide);
        Assert.False(missing.Ready); Assert.Contains("commission", missing.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0d, .01d, .01d, 1d)]
    [InlineData(.1d, 0d, .01d, 1d)]
    [InlineData(.1d, .01d, 0d, 1d)]
    [InlineData(.1d, .01d, .01d, 0d)]
    public async Task Preview_MissingNativeInstrumentEconomicsFailsClosed(double point, double min, double step, double contract)
    {
        await using var database = Database();
        database.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", IsEnabled = true, PaperCommissionPerLotPerSide = 0m });
        await database.SaveChangesAsync();
        var spec = Spec() with { PointSize = (decimal)point, VolumeMin = (decimal)min, VolumeStep = (decimal)step, ContractSize = (decimal)contract };

        var preview = await Service(database, spec).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);

        Assert.False(preview.Ready);
        Assert.Contains("economics", preview.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HistoricalChartMode.Last)]
    [InlineData(HistoricalChartMode.Unknown)]
    public async Task Preview_NonBidChartModesFailClosed(HistoricalChartMode mode)
    {
        await using var database = await SeedAsync();
        var preview = await Service(database, Spec() with { HistoricalChartMode = mode }).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);
        Assert.False(preview.Ready); Assert.Equal(mode.ToString(), preview.ChartMode);
    }

    [Fact]
    public async Task Preview_NullCommissionDisabledAndNonMt5SymbolsFailClosed()
    {
        await using var database = Database();
        database.MonitoredSymbols.AddRange(
            new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "NULLm", IsEnabled = true, PaperCommissionPerLotPerSide = null },
            new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "DISABLEDm", IsEnabled = false, PaperCommissionPerLotPerSide = 0m },
            new MonitoredSymbol { Source = MarketDataSource.LegacyBinance, Symbol = "LEGACY", IsEnabled = true, PaperCommissionPerLotPerSide = 0m });
        await database.SaveChangesAsync();
        var service = Service(database, Spec());
        foreach (var symbol in new[] { "NULLm", "DISABLEDm", "LEGACY" }) Assert.False((await service.GetMt5EconomicsPreviewAsync(symbol, CancellationToken.None)).Ready);
    }

    [Fact]
    public async Task Preview_InstrumentMissingAndAccountCurrencyMissingFailClosed()
    {
        await using var database = await SeedAsync();
        var missing = await Service(database, Spec(), missing: true).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);
        Assert.False(missing.Ready); Assert.Contains("specification", missing.Reason!, StringComparison.OrdinalIgnoreCase);
        var noCurrency = await Service(database, Spec(), currency: "").GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);
        Assert.False(noCurrency.Ready); Assert.Contains("currency", noCurrency.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_CatalogUnavailable_FailsClosed()
    {
        await using var database = await SeedAsync();
        var preview = await Service(database, Spec(), catalogUnavailable: true).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);
        Assert.False(preview.Ready); Assert.Contains("specification", preview.Reason!, StringComparison.OrdinalIgnoreCase); Assert.Null(preview.ContractSize);
    }

    [Fact]
    public async Task Preview_AccountUnavailable_FailsClosed()
    {
        await using var database = await SeedAsync();
        var preview = await Service(database, Spec(), accountUnavailable: true).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);
        Assert.False(preview.Ready); Assert.Contains("account", preview.Reason!, StringComparison.OrdinalIgnoreCase); Assert.Null(preview.AccountCurrency); Assert.Null(preview.StartingBalance);
    }

    [Fact]
    public async Task Preview_ExactBrokerSymbolMismatch_FailsClosed()
    {
        await using var database = await SeedAsync();
        var preview = await Service(database, Spec() with { BrokerSymbol = "BTCUSD" }).GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None);
        Assert.False(preview.Ready); Assert.Contains("exact requested", preview.Reason!, StringComparison.OrdinalIgnoreCase); Assert.Equal("BTCUSDm", preview.BrokerSymbol);
    }

    [Fact]
    public async Task Run_RevalidatesCommissionAfterReadyPreviewAndNeverFallsBack()
    {
        await using var database = await SeedAsync();
        var service = Service(database, Spec());
        Assert.True((await service.GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None)).Ready);
        var monitored = await database.MonitoredSymbols.SingleAsync(item => item.Symbol == "BTCUSDm");
        monitored.PaperCommissionPerLotPerSide = null; await database.SaveChangesAsync();
        var controller = new BacktestsController(database, service, Options.Create(new BacktestRequestTimeoutOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Run(new BacktestRequest("BTCUSDm", "3m", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow), CancellationToken.None));
        Assert.Empty(database.BacktestRuns);
    }

    private static async Task<EmaBotDbContext> SeedAsync()
    {
        var database = Database(); database.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", IsEnabled = true, PaperCommissionPerLotPerSide = 0m }); await database.SaveChangesAsync(); return database;
    }
    private static BacktestService Service(EmaBotDbContext database, InstrumentSpec spec, bool missing = false, string currency = "USD", bool catalogUnavailable = false, bool accountUnavailable = false)
    {
        var bridge = new TestMt5BridgeRequestClient();
        var history = new Mt5BridgeHistoricalMarketDataProvider(bridge);
        return new BacktestService(database, history,
            new TradingSettingsService(database, Options.Create(new TradingDefaultsOptions())),
            new BacktestEngine(new EmaSignalEngine()), new Mt5HistoricalBacktestEngine(new EmaSignalEngine(), new Calculator()),
            new Catalog(missing ? null : new InstrumentCatalogItem(spec, null, null, true, true, InstrumentTradeMode.Full), catalogUnavailable), new Account(currency, accountUnavailable));
    }
    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static InstrumentSpec Spec() => new("Exness", "BTCUSDm", "BTCUSD", AssetClass.Crypto, 1, .1m, 1m, .01m, 1m, .01m, "BTC", "USD", "USD", .1m, 1m, 1m, null, 0, null, HistoricalChartMode.Bid);
    private sealed class Catalog(InstrumentCatalogItem? item, bool unavailable = false) : IInstrumentCatalogProvider
    {
        public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstrumentCatalogItem>>([]);
        public Task<InstrumentCatalogItem?> GetAsync(string brokerSymbol, CancellationToken cancellationToken) => unavailable ? throw new InvalidOperationException("test catalog unavailable") : Task.FromResult(item);
    }
    private sealed class Account(string currency, bool unavailable = false) : IMt5AccountReader
    {
        public Task<Mt5AccountPayload> GetAsync(CancellationToken cancellationToken) => unavailable ? throw new InvalidOperationException("test account unavailable") : Task.FromResult(new Mt5AccountPayload(1, "test", currency, 1000m, 1000m, 0m, 1000m, 0m, "Demo"));
    }
    private sealed class Calculator : IMt5TradeCalculator
    {
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
