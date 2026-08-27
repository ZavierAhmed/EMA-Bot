using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class BacktestNativeEconomicsServiceIsolationTests
{
    [Fact]
    public async Task NativeBacktestService_FeePercentPerSideCannotChangeNativeEconomics()
    {
        await using var zeroFee = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m);
        await using var highFee = await NativeHarness.CreateAsync(feePercentPerSide: 5m, commissionPerLotPerSide: 2m);
        var left = await zeroFee.RunAsync(); var right = await highFee.RunAsync();

        Assert.Equal(0m, left.FeePercentPerSide); Assert.Equal(5m, right.FeePercentPerSide); // Snapshot remains diagnostic compatibility data.
        Assert.Equal(BacktestEconomicsMode.Mt5HistoricalBidAsk, left.EconomicsMode); Assert.Equal(left.EconomicsMode, right.EconomicsMode);
        Assert.Equal(left.StartingBalance, right.StartingBalance); Assert.Equal(left.PositionSizingMode, right.PositionSizingMode); Assert.Equal(left.CommissionPerLotPerSide, right.CommissionPerLotPerSide);
        Assert.Equal(left.GrossProfitFactor, right.GrossProfitFactor); Assert.Equal(left.NetProfitFactor, right.NetProfitFactor);
        AssertTradesEqual(left.Trades, right.Trades);
    }

    [Fact]
    public async Task NativeBacktestService_CommissionPerLotChangesNativeEconomics()
    {
        await using var free = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 0m);
        await using var charged = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 5m);
        var freeRun = await free.RunAsync(); var chargedRun = await charged.RunAsync();
        var freeTrade = Assert.Single(freeRun.Trades); var chargedTrade = Assert.Single(chargedRun.Trades);

        Assert.Equal(0m, freeRun.FeePercentPerSide); Assert.Equal(0m, chargedRun.FeePercentPerSide);
        Assert.Equal(0m, freeRun.CommissionPerLotPerSide); Assert.Equal(5m, chargedRun.CommissionPerLotPerSide);
        Assert.Equal(freeTrade.EntryPrice, chargedTrade.EntryPrice); Assert.Equal(freeTrade.ExitPrice, chargedTrade.ExitPrice); Assert.Equal(freeTrade.Lots, chargedTrade.Lots);
        Assert.Equal(freeTrade.GrossPnl, chargedTrade.GrossPnl); Assert.NotEqual(freeTrade.NetPnl, chargedTrade.NetPnl);
        Assert.Equal(0m, freeTrade.RoundTripCommission); Assert.True(chargedTrade.RoundTripCommission > 0m); Assert.True(chargedRun.NetPnlUsdt < freeRun.NetPnlUsdt);
    }

    [Fact]
    public async Task Run_RevalidatesInstrumentAfterReadyPreviewAndNeverFallsBack()
    {
        await using var harness = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m);
        Assert.True((await harness.Service.GetMt5EconomicsPreviewAsync("BTCUSDm", CancellationToken.None)).Ready);
        harness.Catalog.Item = harness.Catalog.Item! with { Spec = harness.Catalog.Item.Spec with { BrokerSymbol = "BTCUSD" } };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        Assert.Contains("exact requested broker symbol", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Database.BacktestRuns); Assert.Empty(harness.Database.BacktestTrades);
        Assert.Empty(harness.Calculator.MarginCalls); Assert.Empty(harness.Calculator.ProfitCalls);
    }

    [Fact]
    public async Task NativeSubmission_WhenNativePrerequisitesFail_NeverCreatesLegacyCompletedRun()
    {
        await using var harness = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m);
        harness.Catalog.Item = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        Assert.Empty(harness.Database.BacktestRuns); Assert.Empty(harness.Database.BacktestTrades);
        Assert.Empty(harness.Calculator.MarginCalls); Assert.Empty(harness.Calculator.ProfitCalls);
    }

    [Fact]
    public async Task NativeBacktestService_AcquiresNativeEconomicsEvidenceOncePerRun()
    {
        await using var shortRun = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m, totalBars: 61);
        await using var longRun = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m, totalBars: 14_400);

        await shortRun.RunAsync(); await longRun.RunAsync();

        Assert.Equal(1, shortRun.Catalog.GetCalls); Assert.Equal(1, shortRun.Account.GetCalls);
        Assert.Equal(1, longRun.Catalog.GetCalls); Assert.Equal(1, longRun.Account.GetCalls);
    }

    [Fact]
    public async Task NativeBacktestService_PersistsFixedLotsSizingProvenanceWithoutChangingExecution()
    {
        await using var harness = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m, sizingMode: PaperPositionSizingMode.FixedLots, paperFixedLots: .01m, paperMarginPerTradePercent: 2m, paperStartingBalance: 100m);

        var run = await harness.RunAsync();

        Assert.Equal(PaperPositionSizingMode.FixedLots, run.NativePositionSizingMode); Assert.Equal(.01m, run.NativeFixedLots); Assert.Equal(2m, run.NativeMarginPerTradePercent); Assert.Equal(100m, run.StartingBalance);
        Assert.All(run.Trades, item => Assert.Equal(PaperPositionSizingMode.FixedLots, item.NativePositionSizingMode));
        Assert.All(run.Trades, item => Assert.Equal(.01m, item.Lots));
    }

    [Fact]
    public async Task NativeBacktestService_PersistsMarginPercentSizingProvenanceWithoutReinterpretingLegacyMode()
    {
        await using var harness = await NativeHarness.CreateAsync(feePercentPerSide: 0m, commissionPerLotPerSide: 2m, sizingMode: PaperPositionSizingMode.MarginPercent, paperFixedLots: .01m, paperMarginPerTradePercent: 2m, paperStartingBalance: 100m);

        var run = await harness.RunAsync();

        Assert.Equal(PositionSizingMode.FixedNotional, run.PositionSizingMode); Assert.Equal(PaperPositionSizingMode.MarginPercent, run.NativePositionSizingMode); Assert.Equal(.01m, run.NativeFixedLots); Assert.Equal(2m, run.NativeMarginPerTradePercent); Assert.Equal(100m, run.StartingBalance);
        Assert.All(run.Trades, item => Assert.Equal(PaperPositionSizingMode.MarginPercent, item.NativePositionSizingMode));
    }

    private static void AssertTradesEqual(IReadOnlyList<BacktestTrade> left, IReadOnlyList<BacktestTrade> right)
    {
        Assert.Equal(left.Count, right.Count);
        foreach (var pair in left.Zip(right))
        {
            Assert.Equal(pair.First.EntryPrice, pair.Second.EntryPrice); Assert.Equal(pair.First.ExitPrice, pair.Second.ExitPrice); Assert.Equal(pair.First.Lots, pair.Second.Lots);
            Assert.Equal(pair.First.GrossPnl, pair.Second.GrossPnl); Assert.Equal(pair.First.NetPnl, pair.Second.NetPnl); Assert.Equal(pair.First.EntryCommission, pair.Second.EntryCommission); Assert.Equal(pair.First.ExitCommission, pair.Second.ExitCommission); Assert.Equal(pair.First.RoundTripCommission, pair.Second.RoundTripCommission);
        }
    }

    private sealed class NativeHarness(EmaBotDbContext database, BacktestService service, MutableCatalog catalog, Account account, RecordingCalculator calculator) : IAsyncDisposable
    {
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public EmaBotDbContext Database { get; } = database;
        public BacktestService Service { get; } = service;
        public MutableCatalog Catalog { get; } = catalog;
        public Account Account { get; } = account;
        public RecordingCalculator Calculator { get; } = calculator;

        public static async Task<NativeHarness> CreateAsync(decimal feePercentPerSide, decimal commissionPerLotPerSide, int totalBars = 61, PaperPositionSizingMode sizingMode = PaperPositionSizingMode.FixedLots, decimal paperFixedLots = .01m, decimal paperMarginPerTradePercent = 10m, decimal paperStartingBalance = 1000m)
        {
            var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            database.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", IsEnabled = true, PaperCommissionPerLotPerSide = commissionPerLotPerSide });
            database.TradingSettings.Add(new TradingSettings { Id = TradingSettings.GlobalId, RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = feePercentPerSide, PaperPositionSizingMode = sizingMode, PaperFixedLots = paperFixedLots, PaperStartingBalance = paperStartingBalance, SimulatedAccountBalanceUsdt = 1000m, PaperMarginPerTradePercent = paperMarginPerTradePercent, MarginPerTradePercent = 10m, Leverage = 5m, MinEmaGapPercent = 0m, MaxStopDistancePercent = 0m, WaitForConfirmationCandle = false, UseEma100Filter = false, UseHtfRegimeFilter = false, TrailingStopEnabled = false });
            await database.SaveChangesAsync();

            var bridge = new TestMt5BridgeRequestClient(); bridge.Responses[Mt5BridgeOperation.GetBarsRange] = Response(Mt5BridgeOperation.GetBarsRange, Bars(totalBars));
            var history = new Mt5BridgeHistoricalMarketDataProvider(bridge); var calculator = new RecordingCalculator(); var catalog = new MutableCatalog(new InstrumentCatalogItem(Spec(), null, null, true, true, InstrumentTradeMode.Full)); var account = new Account();
            var service = new BacktestService(database, history, new TradingSettingsService(database, Options.Create(new TradingDefaultsOptions())), new BacktestEngine(new EmaSignalEngine()), new Mt5HistoricalBacktestEngine(new EmaSignalEngine(), calculator), catalog, account);
            return new(database, service, catalog, account, calculator);
        }

        public Task<BacktestRun> RunAsync() => Service.RunAsync("BTCUSDm", "3m", Start, Start.AddMinutes(3 * 61), CancellationToken.None);
        public ValueTask DisposeAsync() => Database.DisposeAsync();
        private static Mt5BridgeEnvelope Response(Mt5BridgeOperation operation, object payload) => Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, operation, Guid.NewGuid(), payload, TimeProvider.System);
        private static InstrumentSpec Spec() => new("Exness", "BTCUSDm", "BTCUSD", AssetClass.Crypto, 1, .1m, 1m, .01m, 1m, .01m, "BTC", "USD", "USD", .1m, 1m, 1m, null, 0, null, HistoricalChartMode.Bid);
        private static IReadOnlyList<Mt5BarPayload> Bars(int totalBars)
        {
            var values = Enumerable.Range(0, 30).Select(index => 130m - index).Concat(Enumerable.Range(0, 31).Select(index => 101m + index * 2m)).ToList(); while (values.Count < totalBars) values.Add(values[^1]);
            return values.Select((close, index) => { var open = index == 0 ? close : values[index - 1]; return new Mt5BarPayload("BTCUSDm", "3m", Start.AddMinutes(index * 3), open, Math.Max(open, close) + 1m, Math.Min(open, close) - 1m, close, 1, 1, 2, index == values.Count - 1); }).ToArray();
        }
    }

    private sealed class MutableCatalog(InstrumentCatalogItem? item) : IInstrumentCatalogProvider
    {
        public int GetCalls { get; private set; }
        public InstrumentCatalogItem? Item { get; set; } = item;
        public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<InstrumentCatalogItem>>([]);
        public Task<InstrumentCatalogItem?> GetAsync(string brokerSymbol, CancellationToken token) { GetCalls++; return Task.FromResult(Item); }
    }
    private sealed class Account : IMt5AccountReader
    {
        public int GetCalls { get; private set; }
        public Task<Mt5AccountPayload> GetAsync(CancellationToken token) { GetCalls++; return Task.FromResult(new Mt5AccountPayload(1, "test", "USD", 1000m, 1000m, 0m, 1000m, 0m, "Demo")); }
    }
    private sealed class RecordingCalculator : IMt5TradeCalculator
    {
        public List<Mt5CalculateMarginRequest> MarginCalls { get; } = []; public List<Mt5CalculateProfitRequest> ProfitCalls { get; } = [];
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken token) { MarginCalls.Add(request); return Task.FromResult(new Mt5MarginCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, 1m, "USD")); }
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken token) { ProfitCalls.Add(request); var profit = request.Direction == "Long" ? request.ClosePrice - request.OpenPrice : request.OpenPrice - request.ClosePrice; return Task.FromResult(new Mt5ProfitCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, request.ClosePrice, profit, "USD")); }
    }
}
