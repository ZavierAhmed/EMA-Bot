using System.Diagnostics;
using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class BacktestEnginePerformanceTests
{
    [Fact]
    public void PreCancelledToken_AbortsBeforeEngineWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Engine().Run(Candles(200), Settings(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void IndexedCrossoverAssociation_UsesTheMostRecentMatchingCrossover()
    {
        var candles = Candles(20);
        var events = new[]
        {
            Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover),
            Event(candles, 8, SignalDirection.Long, SignalStatus.BullishCrossover),
            Event(candles, 10, SignalDirection.Long, SignalStatus.LongSignal)
        };

        var trade = Assert.Single(Engine().RunWithEvents(candles, Settings(), events).Trades);

        Assert.Equal(candles[8].CloseTimeUtc, trade.CrossoverTimeUtc);
        Assert.Equal(candles[10].CloseTimeUtc, trade.SignalTimeUtc);
        Assert.Equal(candles[11].OpenTimeUtc, trade.EntryTimeUtc);
    }

    [Fact]
    public void IndexedCrossoverAssociation_PreservesLegacyLastOrDefaultOrderingForUnorderedEvents()
    {
        var candles = Candles(20);
        var events = new[]
        {
            Event(candles, 8, SignalDirection.Long, SignalStatus.BullishCrossover),
            Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover),
            Event(candles, 10, SignalDirection.Long, SignalStatus.LongSignal)
        };

        var trade = Assert.Single(Engine().RunWithEvents(candles, Settings(), events).Trades);

        Assert.Equal(candles[5].CloseTimeUtc, trade.CrossoverTimeUtc);
    }

    [Fact]
    public void IndexedReentry_PreservesSelectedCandleAgeAndEvidence()
    {
        var candles = Candles(12);
        candles[6] = candles[6] with { Open = 100m, High = 106m, Low = 98m, Close = 106m };
        candles[7] = candles[7] with { Open = 106m, High = 107m, Low = 105m, Close = 106m };
        var snapshots = candles.Select((candle, index) => new IndicatorSnapshot(candle.CloseTimeUtc, candle.Close, index == 6 ? 104m : 100m, index == 6 ? 103m : 100m, null, index == 6 ? 1m : null, GapState.Expanding, index == 6 ? TrendDirection.Up : TrendDirection.Neutral, candle.Open)).ToArray();
        var events = new[] { Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 5, SignalDirection.Long, SignalStatus.LongSignal) };
        var settings = Settings(); settings.SameTrendReentryEnabled = true; settings.MaxReentryAgeBars = 3;

        var calculation = Engine().RunWithEvents(candles, settings, events, snapshots);
        var reentry = Assert.Single(calculation.Trades, trade => trade.IsReentry);

        Assert.Equal(2, calculation.Trades.Count);
        Assert.Equal(candles[6].CloseTimeUtc, reentry.SignalTimeUtc);
        Assert.Equal(candles[7].OpenTimeUtc, reentry.EntryTimeUtc);
        Assert.Equal(candles[5].CloseTimeUtc, reentry.TrendRegimeCrossoverTimeUtc);
        Assert.Equal(1, reentry.ReentryAgeBars);
    }

    [Fact]
    public void CancellationDuringLargeExecutionSimulation_IsObserved()
    {
        var candles = Candles(200_000);
        var events = new[] { Event(candles, 20, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 20, SignalDirection.Long, SignalStatus.LongSignal) };
        var settings = Settings(); settings.TrailingStopEnabled = true;
        using var cancellation = new CancellationTokenSource();
        using var timer = new Timer(_ => cancellation.Cancel(), null, TimeSpan.FromMilliseconds(10), Timeout.InfiniteTimeSpan);

        Assert.ThrowsAny<OperationCanceledException>(() => Engine().RunWithEvents(candles, settings, events, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task CancelledServiceRun_DoesNotPersistBacktestRun()
    {
        await using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        database.TradingSettings.Add(new TradingSettings { Id = TradingSettings.GlobalId, RiskReward = 2m, FixedOrderSizeUsdt = 100m, UpdatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync();
        var service = new BacktestService(database, new StaticHistorical(Candles(500)), new TradingSettingsService(database, Options.Create(new TradingDefaultsOptions())), Engine());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync("BTCUSDm", "3m", Candles(1)[0].OpenTimeUtc, Candles(500)[^1].CloseTimeUtc, cancellation.Token));
        Assert.Empty(await database.BacktestRuns.ToListAsync());
    }

    [Fact]
    public void RepresentativeThirtyDayEngineBenchmark_CompletesWithReentryOnAndOff()
    {
        var results = new List<(int Candles, bool Reentry, long Milliseconds)>();
        foreach (var count in new[] { 7_200, 14_400 })
        {
            foreach (var reentry in new[] { false, true })
            {
                var settings = Settings(); settings.SameTrendReentryEnabled = reentry; settings.MaxReentryAgeBars = 6;
                var candles = Candles(count);
                _ = Engine().Run(candles, settings); // Warm-up only; no timing threshold is asserted.
                var samples = Enumerable.Range(0, 3).Select(_ =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var calculation = Engine().Run(candles, settings);
                    stopwatch.Stop();
                    return (Calculation: calculation, Milliseconds: stopwatch.ElapsedMilliseconds);
                }).ToArray();
                var medianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(1);
                var calculation = samples[^1].Calculation;
                results.Add((count, reentry, medianMilliseconds));
                Console.WriteLine($"Backtest engine benchmark: candles={count}; reentry={reentry}; medianElapsedMs={medianMilliseconds}; samplesMs={string.Join(',', samples.Select(sample => sample.Milliseconds))}.");
                Assert.NotNull(calculation);
            }
        }

        Assert.Equal(4, results.Count);
    }

    private static BacktestEngine Engine() => new(new EmaSignalEngine());
    private static TradingSettings Settings() => new() { RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = .05m, WaitForConfirmationCandle = false, UseEma100Filter = false, UseAdaptiveInitialStop = false, MaxStopDistancePercent = 0m, SimulatedAccountBalanceUsdt = 1_000m };
    private static StrategyEvent Event(IReadOnlyList<Candle> candles, int index, SignalDirection direction, SignalStatus status) => new(candles[index].CloseTimeUtc, direction, status, new IndicatorSnapshot(candles[index].CloseTimeUtc, candles[index].Close, direction == SignalDirection.Long ? 101m : 99m, direction == SignalDirection.Long ? 100m : 100m, null, 1m, GapState.Expanding, direction == SignalDirection.Long ? TrendDirection.Up : TrendDirection.Down, candles[index].Open));
    private static Candle[] Candles(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var open = DateTimeOffset.UnixEpoch.AddMinutes(index * 3);
        var phase = index % 80;
        var close = phase < 40 ? 100m + phase * .25m : 110m - (phase - 40) * .25m;
        return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), close - .08m, close + .2m, close - .2m, close, 1m, true);
    }).ToArray();

    private sealed class StaticHistorical(IReadOnlyList<Candle> candles) : IHistoricalMarketDataProvider
    {
        public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) => Task.FromResult(candles);
    }
}
