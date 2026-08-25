using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmaBot.Api.Tests;

public sealed class BacktestOptimizerCompatibilityTests
{
    [Fact]
    public async Task Optimizer_RemainsOnLegacyCompatibilityEconomics()
    {
        await using var harness = await Harness.CreateAsync(.05m);
        var run = await harness.Service.StartAsync(Request([1.1m]), CancellationToken.None);
        var completed = await harness.WaitForTerminalAsync(run.Id);
        var candidate = Assert.Single(await harness.CandidatesAsync(run.Id));
        var settings = StrategyOptimizationService.Settings(candidate, completed, harness.Baseline);
        var expected = new BacktestEngine(new EmaSignalEngine()).RunResearch(Candles, settings, completed.RequestedStartUtc, completed.RequestedEndUtc);

        Assert.Equal(StrategyOptimizationStatus.Completed, completed.Status); Assert.Equal(expected.Trades.Sum(trade => trade.GrossPnlUsdt), candidate.Full.GrossPnlUsdt); Assert.Equal(expected.Trades.Sum(trade => trade.TotalFeesUsdt), candidate.Full.TotalFeesUsdt); Assert.Equal(expected.Trades.Sum(trade => trade.NetPnlUsdt), candidate.Full.NetPnlUsdt);
    }

    [Fact]
    public async Task Optimizer_FeePercentPerSideRemainsLegacyInput()
    {
        await using var low = await Harness.CreateAsync(.05m); await using var high = await Harness.CreateAsync(.10m);
        var lowRun = await low.Service.StartAsync(Request([1.1m]), CancellationToken.None); var highRun = await high.Service.StartAsync(Request([1.1m]), CancellationToken.None);
        var lowCompleted = await low.WaitForTerminalAsync(lowRun.Id); var highCompleted = await high.WaitForTerminalAsync(highRun.Id);
        var lowCandidate = Assert.Single(await low.CandidatesAsync(lowRun.Id)); var highCandidate = Assert.Single(await high.CandidatesAsync(highRun.Id));

        Assert.Equal(.05m, lowCompleted.FeePercentPerSide); Assert.Equal(.10m, highCompleted.FeePercentPerSide);
        Assert.Equal(lowCandidate.Full.GrossPnlUsdt, highCandidate.Full.GrossPnlUsdt); Assert.True(highCandidate.Full.TotalFeesUsdt > lowCandidate.Full.TotalFeesUsdt); Assert.True(highCandidate.Full.NetPnlUsdt < lowCandidate.Full.NetPnlUsdt);
    }

    [Fact]
    public async Task Optimizer_B1DoesNotChangeCompatibilityRankingOrScores()
    {
        await using var harness = await Harness.CreateAsync(.05m);
        var run = await harness.Service.StartAsync(Request([1.1m, 1.5m]), CancellationToken.None);
        var completed = await harness.WaitForTerminalAsync(run.Id);
        var candidates = (await harness.CandidatesAsync(run.Id)).OrderBy(candidate => candidate.RiskReward).ToArray();

        Assert.Equal(2, completed.CandidateCount); Assert.Equal([1.1m, 1.5m], candidates.Select(candidate => candidate.RiskReward)); Assert.Null(completed.RecommendedCandidateId);
        foreach (var candidate in candidates)
        {
            var settings = StrategyOptimizationService.Settings(candidate, completed, harness.Baseline);
            var expected = new BacktestEngine(new EmaSignalEngine()).RunResearch(Candles, settings, completed.RequestedStartUtc, completed.RequestedEndUtc);
            Assert.Equal(expected.Trades.Count, candidate.Full.TotalTrades); Assert.Equal(expected.Trades.Sum(trade => trade.GrossPnlUsdt), candidate.Full.GrossPnlUsdt); Assert.Equal(expected.Trades.Sum(trade => trade.TotalFeesUsdt), candidate.Full.TotalFeesUsdt); Assert.Equal(expected.Trades.Sum(trade => trade.NetPnlUsdt), candidate.Full.NetPnlUsdt); Assert.Null(candidate.RobustRank);
        }
    }

    [Fact]
    public void Optimizer_PreservesLegacySizingLeverageAndNeverInjectsNativeEngine()
    {
        var baseline = new TradingSettings { FixedOrderSizeUsdt = 250m, SimulatedAccountBalanceUsdt = 1200m, MarginPerTradePercent = 12m, Leverage = 7m, FeePercentPerSide = .05m, PositionSizingMode = PositionSizingMode.MarginPercent };
        var candidate = new StrategyOptimizationCandidate { RiskReward = 1.1m, MinEmaGapPercent = 0m, MaxStopDistancePercent = 0m, WaitForConfirmationCandle = false, UseEma100Filter = false, UseHtfRegimeFilter = false, TrailingStopEnabled = false };
        var run = new StrategyOptimizationRun { FixedOrderSizeUsdt = 250m, SimulatedAccountBalanceUsdt = 1200m, MarginPerTradePercent = 12m, Leverage = 7m, FeePercentPerSide = .05m, PositionSizingMode = PositionSizingMode.MarginPercent };
        var settings = StrategyOptimizationService.Settings(candidate, run, baseline);

        Assert.Equal(250m, settings.FixedOrderSizeUsdt); Assert.Equal(1200m, settings.SimulatedAccountBalanceUsdt); Assert.Equal(12m, settings.MarginPerTradePercent); Assert.Equal(7m, settings.Leverage); Assert.Equal(.05m, settings.FeePercentPerSide); Assert.Equal(PositionSizingMode.MarginPercent, settings.PositionSizingMode);
        Assert.DoesNotContain(typeof(StrategyOptimizationService).GetConstructors().SelectMany(constructor => constructor.GetParameters()), parameter => parameter.ParameterType == typeof(Mt5HistoricalBacktestEngine));
    }

    private static readonly DateTimeOffset Start = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Candle[] Candles = Enumerable.Range(0, 130).Select(index => { var close = index < 100 ? 100m : 100m + index - 99; var open = index < 100 ? 100m : close - 1m; var time = Start.AddMinutes(index * 3); return new Candle(time, time.AddMinutes(3).AddMilliseconds(-1), open, close + 1m, open - 1m, close, 1m, true); }).ToArray();
    private static StrategyOptimizerStartRequest Request(IReadOnlyList<decimal> riskRewards) => new(["BTCUSDm"], ["3m"], Start, new DateTimeOffset(2026, 7, 30, 23, 59, 59, 999, TimeSpan.Zero), new(riskRewards, [0m], [0m], [false], [false], [false]), 1);

    private sealed class Harness(DbContextOptions<EmaBotDbContext> options, StrategyOptimizationService service, TradingSettings baseline) : IAsyncDisposable
    {
        public StrategyOptimizationService Service { get; } = service; public TradingSettings Baseline { get; } = baseline;
        public static async Task<Harness> CreateAsync(decimal fee)
        {
            var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var baseline = new TradingSettings { Id = TradingSettings.GlobalId, RiskReward = 1.1m, FixedOrderSizeUsdt = 100m, MinEmaGapPercent = 0m, MaxStopDistancePercent = 0m, SimulatedAccountBalanceUsdt = 1000m, MarginPerTradePercent = 10m, Leverage = 5m, FeePercentPerSide = fee, WaitForConfirmationCandle = false, UseEma100Filter = false, TrailingStopEnabled = false };
            await using (var database = new EmaBotDbContext(options)) { database.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", IsEnabled = true }); database.TradingSettings.Add(baseline); await database.SaveChangesAsync(); }
            return new(options, new StrategyOptimizationService(new ScopeFactory(options), new BacktestEngine(new EmaSignalEngine()), new FixedClock(), NullLogger<StrategyOptimizationService>.Instance), baseline);
        }
        public async Task<StrategyOptimizationRun> WaitForTerminalAsync(int id) { for (var attempt = 0; attempt < 250; attempt++) { await using var database = new EmaBotDbContext(options); var run = await database.StrategyOptimizationRuns.AsNoTracking().SingleAsync(item => item.Id == id); if (run.Status is StrategyOptimizationStatus.Completed or StrategyOptimizationStatus.Cancelled or StrategyOptimizationStatus.Failed) return run; await Task.Delay(20); } throw new TimeoutException("Optimizer run did not reach a terminal state."); }
        public async Task<List<StrategyOptimizationCandidate>> CandidatesAsync(int id) { await using var database = new EmaBotDbContext(options); return await database.StrategyOptimizationCandidates.AsNoTracking().Where(item => item.StrategyOptimizationRunId == id).ToListAsync(); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ScopeFactory(DbContextOptions<EmaBotDbContext> options) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(options);
        private sealed class Scope(DbContextOptions<EmaBotDbContext> options) : IServiceScope, IServiceProvider
        {
            private readonly EmaBotDbContext database = new(options);
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(EmaBotDbContext) ? database : serviceType == typeof(IHistoricalMarketDataProvider) ? new StaticHistorical() : null;
            public void Dispose() => database.Dispose();
        }
    }
    private sealed class StaticHistorical : IHistoricalMarketDataProvider { public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string timeframe, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) => Task.FromResult<IReadOnlyList<Candle>>(Candles); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero); }
}
