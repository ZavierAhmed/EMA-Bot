using System.Collections.Concurrent;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmaBot.Api.Tests;

public sealed class StrategyOptimizerFinalizationLifecycleTests
{
    [Fact]
    public async Task Finalization_StaysRunningUntilDetachedTradesArePersisted()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        await using var harness = await Harness.CreateAsync(finalization: async (_, _, _, _, _) => { if (Interlocked.Increment(ref calls) == 1) { firstEntered.TrySetResult(); await releaseFirst.Task; } else { secondEntered.TrySetResult(); await releaseSecond.Task; } });
        var run = await harness.Service.StartAsync(Request(), CancellationToken.None);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var active = await harness.RunAsync(run.Id);
        Assert.Equal(StrategyOptimizationStatus.Running, active.Status);
        Assert.Equal(active.TotalWork, active.CompletedWork);
        Assert.Equal("FinalizingTopCandidates", harness.Service.RuntimeStatusFor(run.Id, active.Status).Phase);
        Assert.Equal(2, harness.Service.RuntimeStatusFor(run.Id, active.Status).FinalizationTotalMarkets);
        Assert.Equal(0, await harness.TradeCountAsync(run.Id));

        releaseFirst.TrySetResult(); await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, harness.Service.RuntimeStatusFor(run.Id, StrategyOptimizationStatus.Running).FinalizationCompletedMarkets); Assert.Equal(0, await harness.TradeCountAsync(run.Id));
        releaseSecond.TrySetResult();
        var completed = await harness.WaitForTerminalAsync(run.Id);
        Assert.Equal(StrategyOptimizationStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.True(await harness.TradeCountAsync(run.Id) > 0);
        Assert.Equal("Completed", harness.Service.RuntimeStatusFor(run.Id, completed.Status).Phase);
    }

    [Fact]
    public async Task CancellationDuringFinalization_CancelsAndLeavesNoPartialTrades()
    {
        StrategyOptimizationService? service = null;
        var firstMarket = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await Harness.CreateAsync(finalization: async (runId, _, _, _, _) =>
        {
            if (firstMarket.TrySetResult()) Assert.True(await service!.CancelAsync(runId, CancellationToken.None));
        });
        service = harness.Service;
        var run = await service.StartAsync(Request(), CancellationToken.None);

        await firstMarket.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelled = await harness.WaitForTerminalAsync(run.Id);
        Assert.Equal(StrategyOptimizationStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAtUtc);
        Assert.Equal(0, await harness.TradeCountAsync(run.Id));
        Assert.Equal("Cancelled", service.RuntimeStatusFor(run.Id, cancelled.Status).Phase);
    }

    [Fact]
    public async Task FinalizationTrades_MatchBacktestResearchOutput()
    {
        await using var harness = await Harness.CreateAsync();
        var run = await harness.Service.StartAsync(Request(), CancellationToken.None);
        var completed = await harness.WaitForTerminalAsync(run.Id);
        Assert.Equal(StrategyOptimizationStatus.Completed, completed.Status);

        var candidate = await harness.CandidateAsync(run.Id);
        var settings = Settings(candidate, completed);
        var expected = Frames.SelectMany(frame => new BacktestEngine(new EmaSignalEngine()).RunResearch(Candles, settings, completed.RequestedStartUtc, completed.RequestedEndUtc).Trades.Select(trade => new { Frame = frame, trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.GrossPnlUsdt, trade.TotalFeesUsdt, trade.NetPnlUsdt, trade.NetRMultiple, trade.ExitReason })).ToArray();
        var actual = await harness.TradesAsync(run.Id);
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Length, actual.Count);
        foreach (var pair in expected.Zip(actual.OrderBy(trade => trade.Timeframe).ThenBy(trade => trade.EntryTimeUtc)))
        {
            Assert.Equal(candidate.Id, pair.Second.StrategyOptimizationCandidateId); Assert.Equal(pair.First.Frame, pair.Second.Timeframe); Assert.Equal(pair.First.Direction, pair.Second.Direction); Assert.Equal(pair.First.EntryTimeUtc, pair.Second.EntryTimeUtc); Assert.Equal(pair.First.ExitTimeUtc, pair.Second.ExitTimeUtc); Assert.Equal(pair.First.EntryPrice, pair.Second.EntryPrice); Assert.Equal(pair.First.ExitPrice, pair.Second.ExitPrice); Assert.Equal(pair.First.GrossPnlUsdt, pair.Second.GrossPnlUsdt); Assert.Equal(pair.First.TotalFeesUsdt, pair.Second.TotalFeesUsdt); Assert.Equal(pair.First.NetPnlUsdt, pair.Second.NetPnlUsdt); Assert.Equal(pair.First.NetRMultiple, pair.Second.NetRMultiple); Assert.Equal(pair.First.ExitReason, pair.Second.ExitReason);
        }
    }

    [Fact]
    public async Task RuntimeStatus_ExposesEachActivePhaseAndRestartCleansActiveState()
    {
        var gates = new ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        TaskCompletionSource Gate(string phase) => gates.GetOrAdd(phase, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        var reached = new ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        TaskCompletionSource Reached(string phase) => reached.GetOrAdd(phase, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        await using var harness = await Harness.CreateAsync(phase: async (_, name, _) => { Reached(name).TrySetResult(); await Gate(name).Task; });
        var run = await harness.Service.StartAsync(Request(), CancellationToken.None);

        foreach (var phase in new[] { "Queued", "LoadingHistoricalData", "EvaluatingCandidates", "FinalizingTopCandidates" })
        {
            await Reached(phase).Task.WaitAsync(TimeSpan.FromSeconds(5));
            var persisted = await harness.RunAsync(run.Id);
            Assert.Equal(phase, harness.Service.RuntimeStatusFor(run.Id, persisted.Status).Phase);
            Gate(phase).TrySetResult();
        }
        var completed = await harness.WaitForTerminalAsync(run.Id);
        Assert.Equal("Completed", harness.Service.RuntimeStatusFor(run.Id, completed.Status).Phase);

        var interrupted = await harness.AddRunAsync(StrategyOptimizationStatus.Running);
        await harness.Service.MarkRunningAsInterruptedAsync(CancellationToken.None);
        var restarted = await harness.RunAsync(interrupted.Id);
        Assert.Equal(StrategyOptimizationStatus.Interrupted, restarted.Status); Assert.NotNull(restarted.CompletedAtUtc); Assert.Equal("Interrupted", harness.Service.RuntimeStatusFor(interrupted.Id, restarted.Status).Phase);
    }

    [Fact]
    public async Task TerminalCancellationIsRejected_AndFailedRunsLoseTransientState()
    {
        await using var harness = await Harness.CreateAsync();
        foreach (var status in new[] { StrategyOptimizationStatus.Completed, StrategyOptimizationStatus.Cancelled, StrategyOptimizationStatus.Failed })
        {
            var terminal = await harness.AddRunAsync(status);
            Assert.False(await harness.Service.CancelAsync(terminal.Id, CancellationToken.None));
            Assert.Equal(status.ToString(), harness.Service.RuntimeStatusFor(terminal.Id, status).Phase);
        }

        await using var failing = await Harness.CreateAsync(phase: (_, phase, _) => phase == "EvaluatingCandidates" ? Task.FromException(new InvalidOperationException("test failure")) : Task.CompletedTask);
        var run = await failing.Service.StartAsync(Request(), CancellationToken.None);
        var failed = await failing.WaitForTerminalAsync(run.Id);
        Assert.Equal(StrategyOptimizationStatus.Failed, failed.Status); Assert.NotNull(failed.CompletedAtUtc); Assert.Equal("Failed", failing.Service.RuntimeStatusFor(run.Id, failed.Status).Phase);
    }

    private static readonly string[] Frames = ["3m", "5m"];
    private static readonly Candle[] Candles = Enumerable.Range(0, 130).Select(index => { var close = index < 100 ? 100m : 100m + index - 99; var open = index < 100 ? 100m : close - 1m; var time = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index * 3); return new Candle(time, time.AddMinutes(3).AddMilliseconds(-1), open, close + 1m, open - 1m, close, 1m, true); }).ToArray();
    private static StrategyOptimizerStartRequest Request() => new(["BTCUSDT"], Frames, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 30, 23, 59, 59, 999, TimeSpan.Zero), new([1.1m], [0m], [0m], [false], [false], [false]), 1);
    private static TradingSettings Settings(StrategyOptimizationCandidate candidate, StrategyOptimizationRun run) => new() { Id = 1, RiskReward = candidate.RiskReward, MinEmaGapPercent = candidate.MinEmaGapPercent, MaxStopDistancePercent = candidate.MaxStopDistancePercent, WaitForConfirmationCandle = candidate.WaitForConfirmationCandle, UseEma100Filter = candidate.UseEma100Filter, TrailingStopEnabled = candidate.TrailingStopEnabled, FixedOrderSizeUsdt = run.FixedOrderSizeUsdt, SimulatedAccountBalanceUsdt = run.SimulatedAccountBalanceUsdt, MarginPerTradePercent = run.MarginPerTradePercent, Leverage = run.Leverage, FeePercentPerSide = run.FeePercentPerSide, PositionSizingMode = run.PositionSizingMode };

    private sealed class Harness(DbContextOptions<EmaBotDbContext> options, StrategyOptimizationService service) : IAsyncDisposable
    {
        public StrategyOptimizationService Service { get; } = service;
        public static async Task<Harness> CreateAsync(Func<int, string, CancellationToken, Task>? phase = null, Func<int, int, string, string, CancellationToken, Task>? finalization = null)
        {
            var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using (var db = new EmaBotDbContext(options)) { await db.Database.EnsureCreatedAsync(); db.MonitoredSymbols.Add(new MonitoredSymbol { Symbol = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", IsEnabled = true }); db.TradingSettings.Add(new TradingSettings { Id = 1, RiskReward = 1.1m, FixedOrderSizeUsdt = 100m, MinEmaGapPercent = 0m, MaxStopDistancePercent = 0m, SimulatedAccountBalanceUsdt = 1000m, MarginPerTradePercent = 10m, Leverage = 5m, FeePercentPerSide = .05m, WaitForConfirmationCandle = false, UseEma100Filter = false, TrailingStopEnabled = false }); await db.SaveChangesAsync(); }
            var service = new StrategyOptimizationService(new TestScopeFactory(options, new StaticHistorical(Candles)), new BacktestEngine(new EmaSignalEngine()), new FixedClock(), NullLogger<StrategyOptimizationService>.Instance) { ExecutionPhaseEnteredAsync = phase, FinalizationMarketCompletedAsync = finalization };
            return new(options, service);
        }
        public async Task<StrategyOptimizationRun> RunAsync(int id) { await using var db = new EmaBotDbContext(options); return await db.StrategyOptimizationRuns.AsNoTracking().SingleAsync(run => run.Id == id); }
        public async Task<StrategyOptimizationRun> WaitForTerminalAsync(int id) { for (var attempt = 0; attempt < 250; attempt++) { var run = await RunAsync(id); if (run.Status is StrategyOptimizationStatus.Completed or StrategyOptimizationStatus.Cancelled or StrategyOptimizationStatus.Failed) return run; await Task.Delay(20); } throw new TimeoutException("Optimizer run did not reach a terminal state."); }
        public async Task<int> TradeCountAsync(int runId) { await using var db = new EmaBotDbContext(options); return await db.StrategyOptimizationTrades.CountAsync(trade => trade.StrategyOptimizationRunId == runId); }
        public async Task<List<StrategyOptimizationTrade>> TradesAsync(int runId) { await using var db = new EmaBotDbContext(options); return await db.StrategyOptimizationTrades.AsNoTracking().Where(trade => trade.StrategyOptimizationRunId == runId).ToListAsync(); }
        public async Task<StrategyOptimizationCandidate> CandidateAsync(int runId) { await using var db = new EmaBotDbContext(options); return await db.StrategyOptimizationCandidates.AsNoTracking().SingleAsync(candidate => candidate.StrategyOptimizationRunId == runId); }
        public async Task<StrategyOptimizationRun> AddRunAsync(StrategyOptimizationStatus status) { await using var db = new EmaBotDbContext(options); var run = new StrategyOptimizationRun { Status = status, CreatedAtUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero) }; db.StrategyOptimizationRuns.Add(run); await db.SaveChangesAsync(); return run; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class TestScopeFactory(DbContextOptions<EmaBotDbContext> options, IHistoricalMarketDataProvider historical) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestScope(options, historical);
        private sealed class TestScope : IServiceScope, IServiceProvider
        {
            private readonly EmaBotDbContext database;
            private readonly IHistoricalMarketDataProvider historical;
            public TestScope(DbContextOptions<EmaBotDbContext> options, IHistoricalMarketDataProvider historical) { database = new EmaBotDbContext(options); this.historical = historical; }
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(EmaBotDbContext) ? database : serviceType == typeof(IHistoricalMarketDataProvider) ? historical : null;
            public void Dispose() => database.Dispose();
        }
    }
    private sealed class StaticHistorical(IReadOnlyList<Candle> candles) : IHistoricalMarketDataProvider { public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken) => Task.FromResult(candles); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero); }
}
