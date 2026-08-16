using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Configuration;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class HistoricalCandleServiceTests
{
    [Fact]
    public async Task Range_UsesClosedCandlesFullyInsideRequestedWindow()
    {
        var start = DateTimeOffset.UnixEpoch;
        var exact = CandleAt(start, 1, true);
        var crossingEnd = new Candle(start.AddMinutes(2), start.AddMinutes(9), 1, 1, 1, 1, 1, true);
        var open = CandleAt(start.AddMinutes(3), 1, false);
        var service = new BinanceHistoricalMarketDataProvider(new PageClient([exact, crossingEnd, open]));

        var result = await service.GetRangeAsync("BTCUSDT", "1w", start, exact.CloseTimeUtc, CancellationToken.None);

        Assert.Equal([exact], result);
    }

    [Theory]
    [InlineData("1w")]
    [InlineData("1M")]
    public async Task Range_ExcludesLongIntervalCandleThatClosesAfterEnd(string interval)
    {
        var start = DateTimeOffset.UnixEpoch;
        var service = new BinanceHistoricalMarketDataProvider(new PageClient([new Candle(start, start.AddDays(7), 1, 1, 1, 1, 1, true)]));

        var result = await service.GetRangeAsync("BTCUSDT", interval, start, start.AddDays(2), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Range_PaginatesDeduplicatesAndUsesCloseTimeCursor()
    {
        var start = DateTimeOffset.UnixEpoch;
        var first = Enumerable.Range(0, 1500).Select(index => CandleAt(start.AddMinutes(index), 1, true)).ToArray();
        var duplicate = first[^1];
        var final = CandleAt(start.AddMinutes(1500), 1, true);
        var client = new PageClient(first, [duplicate, final]);
        var service = new BinanceHistoricalMarketDataProvider(client);

        var result = await service.GetRangeAsync("BTCUSDT", "1m", start, final.CloseTimeUtc, CancellationToken.None);

        Assert.Equal(1501, result.Count);
        Assert.Equal(final.CloseTimeUtc, result[^1].CloseTimeUtc);
        Assert.Equal(first[^1].CloseTimeUtc.AddMilliseconds(1), client.RequestStarts[1]);
        Assert.True(result.Zip(result.Skip(1), (left, right) => left.OpenTimeUtc < right.OpenTimeUtc).All(value => value));
    }

    [Fact]
    public async Task Range_StopsForEmptyPageAndPropagatesRateLimits()
    {
        var start = DateTimeOffset.UnixEpoch;
        var empty = new BinanceHistoricalMarketDataProvider(new PageClient([]));
        Assert.Empty(await empty.GetRangeAsync("BTCUSDT", "1h", start, start.AddHours(1), CancellationToken.None));
        var rateLimited = new BinanceHistoricalMarketDataProvider(new PageClient(new BinanceApiException("slow down", 429)));
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => rateLimited.GetRangeAsync("BTCUSDT", "1h", start, start.AddHours(1), CancellationToken.None));
        Assert.Equal(MarketDataErrorKind.RateLimited, error.Kind);
    }

    [Fact]
    public async Task Range_RejectsOverMaximumAndHonorsCancellation()
    {
        var start = DateTimeOffset.UnixEpoch;
        var tooMany = Enumerable.Range(0, BinanceHistoricalMarketDataProvider.MaximumCandles + 1).Select(index => CandleAt(start.AddSeconds(index), 1, true)).ToArray();
        var service = new BinanceHistoricalMarketDataProvider(new PageClient(tooMany));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetRangeAsync("BTCUSDT", "1m", start, start.AddDays(3), CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetRangeAsync("BTCUSDT", "1m", start, start.AddDays(1), cancellation.Token));
    }

    private static Candle CandleAt(DateTimeOffset open, int minutes, bool closed) => new(open, open.AddMinutes(minutes), 1, 2, 0.5m, 1.5m, 1, closed);

    private sealed class PageClient : IBinanceHistoricalKlineClient
    {
        private readonly Queue<IReadOnlyList<Candle>> _pages = new(); private readonly Exception? _error;
        public List<DateTimeOffset?> RequestStarts { get; } = [];
        public PageClient(params IReadOnlyList<Candle>[] pages) { foreach (var page in pages) _pages.Enqueue(page); }
        public PageClient(Exception error) => _error = error;
        public Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? start, DateTimeOffset? end, int? limit, CancellationToken token)
        {
            RequestStarts.Add(start); if (_error is not null) throw _error;
            return Task.FromResult(_pages.Count == 0 ? (IReadOnlyList<Candle>)[] : _pages.Dequeue());
        }
    }
}

public sealed class BacktestEngineCorrectiveTests
{
    [Fact]
    public void ExitCandleSignal_IsEligibleButEarlierSignalIsSkipped()
    {
        var candles = Candles(10, 100m); candles[0] = candles[0] with { Low = 90m }; candles[7] = candles[7] with { Low = 90m };
        var calculation = Engine().RunWithEvents(candles, Settings(), [Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 5, SignalDirection.Long, SignalStatus.LongSignal), Event(candles, 6, SignalDirection.Long, SignalStatus.LongSignal), Event(candles, 7, SignalDirection.Long, SignalStatus.LongSignal)]);

        Assert.Equal(2, calculation.Trades.Count);
        Assert.Equal(1, calculation.Diagnostics.SkippedWhilePositionOpen);
        Assert.Equal(candles[8].OpenTimeUtc, calculation.Trades[1].EntryTimeUtc);
    }

    [Theory]
    [InlineData(SignalDirection.Long, true, BacktestExitReason.TakeProfit, 20)]
    [InlineData(SignalDirection.Long, false, BacktestExitReason.StopLoss, 10)]
    [InlineData(SignalDirection.Short, true, BacktestExitReason.TakeProfit, 20)]
    [InlineData(SignalDirection.Short, false, BacktestExitReason.StopLoss, 10)]
    public void FirstExitCandle_RecordsConservativeExcursion(SignalDirection direction, bool target, BacktestExitReason reason, decimal excursion)
    {
        var candles = Candles(8, 100m);
        if (direction == SignalDirection.Long) candles[6] = candles[6] with { High = target ? 120m : 105m, Low = target ? 95m : 90m };
        else candles[6] = candles[6] with { High = target ? 105m : 110m, Low = target ? 80m : 95m };
        var trade = RunOne(candles, direction).Trades.Single();

        Assert.Equal(reason, trade.ExitReason);
        Assert.True(reason == BacktestExitReason.TakeProfit ? trade.MfePrice >= excursion : trade.MaePrice >= excursion);
    }

    [Fact]
    public void SameCandleStopAndTarget_UsesStopFirstAndSetsConflict()
    {
        var candles = Candles(8, 100m); candles[6] = candles[6] with { High = 120m, Low = 90m };
        var trade = RunOne(candles, SignalDirection.Long).Trades.Single();
        Assert.Equal(BacktestExitReason.StopLoss, trade.ExitReason); Assert.True(trade.SameCandleExitConflict);
    }

    [Fact]
    public void EndOfData_UsesFinalCandleRangeForExcursions()
    {
        var candles = Candles(8, 100m); candles[6] = candles[6] with { High = 111m, Low = 95m }; candles[7] = candles[7] with { High = 115m, Low = 94m, Close = 105m };
        var trade = RunOne(candles, SignalDirection.Long).Trades.Single();
        Assert.Equal(BacktestExitReason.EndOfData, trade.ExitReason); Assert.Equal(15m, trade.MfePrice); Assert.Equal(6m, trade.MaePrice);
    }

    [Fact]
    public void TrailingLong_AdvancesOnlyForFollowingCandleAndExtendsTargetOnce()
    {
        var candles = Candles(9, 100m); candles[6] = candles[6] with { High = 110m, Low = 99m }; candles[7] = candles[7] with { High = 114m, Low = 105m }; candles[8] = candles[8] with { High = 109m, Low = 108m };
        var trade = RunOne(candles, SignalDirection.Long, Settings(trailing: true)).Trades.Single();
        Assert.Equal(108m, trade.FinalStopLoss); Assert.Equal(122m, trade.FinalTakeProfit); Assert.True(trade.TakeProfitExtended); Assert.Equal(120m, trade.OriginalTakeProfit); Assert.Equal(BacktestExitReason.TrailingStop, trade.ExitReason);
    }

    [Fact]
    public void TrailingShort_MirrorsLongBehavior()
    {
        var candles = Candles(9, 100m); candles[6] = candles[6] with { High = 101m, Low = 90m }; candles[7] = candles[7] with { High = 95m, Low = 86m }; candles[8] = candles[8] with { High = 92m, Low = 91m };
        var trade = RunOne(candles, SignalDirection.Short, Settings(trailing: true)).Trades.Single();
        Assert.Equal(92m, trade.FinalStopLoss); Assert.Equal(78m, trade.FinalTakeProfit); Assert.True(trade.TakeProfitExtended); Assert.Equal(80m, trade.OriginalTakeProfit); Assert.Equal(BacktestExitReason.TrailingStop, trade.ExitReason);
    }

    [Fact]
    public void FeesAndPnl_AreCalculatedFromFixedUsdtSize()
    {
        var candles = Candles(8, 100m); candles[6] = candles[6] with { High = 120m, Low = 95m };
        var trade = RunOne(candles, SignalDirection.Long, Settings(fee: 0.1m)).Trades.Single();
        Assert.Equal(1m, trade.Quantity); Assert.Equal(100m, trade.EntryNotionalUsdt); Assert.Equal(20m, trade.GrossPnlUsdt); Assert.Equal(0.1m, trade.EntryFeeUsdt); Assert.Equal(0.12m, trade.ExitFeeUsdt); Assert.Equal(19.78m, trade.NetPnlUsdt);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public void FeeAwareTrailingStop_ExitsWithNonNegativeNetPnl(SignalDirection direction)
    {
        var candles = Candles(8, 100m).Select(candle => candle with { High = 100.1m, Low = 99.9m }).ToArray();
        candles[6] = direction == SignalDirection.Long
            ? candles[6] with { Open = 100m, High = 100.1m, Low = 99.95m, Close = 100.05m }
            : candles[6] with { Open = 100m, High = 100.05m, Low = 99.9m, Close = 99.95m };
        var floor = TradeMath.FeeBreakevenPrice(100m, direction, .05m);
        candles[7] = direction == SignalDirection.Long
            ? candles[7] with { Open = 100.15m, High = 100.15m, Low = floor, Close = floor }
            : candles[7] with { Open = 99.85m, High = floor, Low = 99.85m, Close = floor };

        var signalStatus = direction == SignalDirection.Long ? SignalStatus.LongSignal : SignalStatus.ShortSignal;
        var crossoverStatus = direction == SignalDirection.Long ? SignalStatus.BullishCrossover : SignalStatus.BearishCrossover;
        var trade = Engine().RunWithEvents(candles, Settings(trailing: true, fee: .05m), [Event(candles, 5, direction, crossoverStatus), Event(candles, 5, direction, signalStatus)]).Trades.Single();
        Assert.Equal(BacktestExitReason.TrailingStop, trade.ExitReason);
        Assert.True(trade.GrossPnlUsdt > 0m); Assert.True(trade.NetPnlUsdt >= 0m); Assert.Equal(floor, trade.FinalStopLoss);
    }

    [Fact]
    public void SignalWithoutFollowingClosedCandle_IsRecordedAsNoEntry()
    {
        var candles = Candles(6, 100m); candles[0] = candles[0] with { Low = 90m };
        var calculation = Engine().RunWithEvents(candles, Settings(), [Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 5, SignalDirection.Long, SignalStatus.LongSignal)]);
        Assert.Empty(calculation.Trades); Assert.Equal(1, calculation.Diagnostics.NoEntryCandle);
    }

    [Fact]
    public void InvalidStopLoss_IsRejectedBeforePositionOpens()
    {
        var candles = Candles(8, 100m); candles[6] = candles[6] with { Open = 99m };
        var calculation = Engine().RunWithEvents(candles, Settings(), [Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 5, SignalDirection.Long, SignalStatus.LongSignal)]);
        Assert.Empty(calculation.Trades); Assert.Equal(1, calculation.Diagnostics.InvalidStopLoss);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public void OppositeCrossover_ClosesAtFollowingCandleOpenWithoutReversing(SignalDirection direction)
    {
        var candles = Candles(10, 100m);
        candles[0] = direction == SignalDirection.Long ? candles[0] with { Low = 90m } : candles[0] with { High = 110m };
        var opposite = direction == SignalDirection.Long ? SignalDirection.Short : SignalDirection.Long;
        var settings = Settings(); settings.ExitOnOppositeCrossover = true;
        var events = new[]
        {
            Event(candles, 5, direction, direction == SignalDirection.Long ? SignalStatus.BullishCrossover : SignalStatus.BearishCrossover),
            Event(candles, 5, direction, direction == SignalDirection.Long ? SignalStatus.LongSignal : SignalStatus.ShortSignal),
            Event(candles, 6, opposite, opposite == SignalDirection.Long ? SignalStatus.BullishCrossover : SignalStatus.BearishCrossover),
            Event(candles, 6, opposite, opposite == SignalDirection.Long ? SignalStatus.LongSignal : SignalStatus.ShortSignal)
        };

        var calculation = Engine().RunWithEvents(candles, settings, events);

        var trade = Assert.Single(calculation.Trades);
        Assert.Equal(direction, trade.Direction); Assert.Equal(BacktestExitReason.OppositeCrossover, trade.ExitReason);
        Assert.Equal(candles[7].OpenTimeUtc, trade.ExitTimeUtc); Assert.Equal(candles[7].Open, trade.ExitPrice);
    }

    [Fact]
    public void OppositeCrossoverOptionOff_PreservesSkippedWhileOpenBehavior()
    {
        var candles = Candles(10, 100m); candles[0] = candles[0] with { Low = 90m };
        var calculation = Engine().RunWithEvents(candles, Settings(), [
            Event(candles, 5, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 5, SignalDirection.Long, SignalStatus.LongSignal),
            Event(candles, 6, SignalDirection.Short, SignalStatus.BearishCrossover), Event(candles, 6, SignalDirection.Short, SignalStatus.ShortSignal)]);

        var trade = Assert.Single(calculation.Trades);
        Assert.Equal(BacktestExitReason.EndOfData, trade.ExitReason); Assert.Equal(1, calculation.Diagnostics.SkippedWhilePositionOpen);
    }

    [Fact]
    public void ReentryContinuation_RequiresBothFastEmasBeyondEma100()
    {
        var settings = Settings(); settings.UseEma100Filter = true;
        var accepted = new IndicatorSnapshot(DateTimeOffset.UnixEpoch, 106m, 105m, 104m, 100m, 1m, GapState.Expanding, TrendDirection.Up, 105m);
        var rejected = accepted with { Ema15 = 99m };

        foreach (var type in new[] { typeof(BacktestEngine), typeof(PaperTradingCoordinator) })
        {
            var method = type.GetMethod("IsContinuation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Assert.True((bool)method.Invoke(null, [accepted, SignalDirection.Long, settings])!);
            Assert.False((bool)method.Invoke(null, [rejected, SignalDirection.Long, settings])!);
        }
    }

    private static BacktestCalculation RunOne(Candle[] candles, SignalDirection direction, TradingSettings? settings = null)
    {
        candles[0] = direction == SignalDirection.Long ? candles[0] with { Low = 90m } : candles[0] with { High = 110m };
        return Engine().RunWithEvents(candles, settings ?? Settings(), [Event(candles, 5, direction, direction == SignalDirection.Long ? SignalStatus.BullishCrossover : SignalStatus.BearishCrossover), Event(candles, 5, direction, direction == SignalDirection.Long ? SignalStatus.LongSignal : SignalStatus.ShortSignal)]);
    }
    private static BacktestEngine Engine() => new(new EmaSignalEngine());
    private static TradingSettings Settings(bool trailing = false, decimal fee = 0m) => new() { RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = fee, TrailingStopEnabled = trailing };
    private static StrategyEvent Event(Candle[] candles, int index, SignalDirection direction, SignalStatus status) => new(candles[index].CloseTimeUtc, direction, status, new IndicatorSnapshot(candles[index].CloseTimeUtc, candles[index].Close, 1m, 1m, null, null, GapState.Unchanged, TrendDirection.Neutral));
    private static Candle[] Candles(int count, decimal price) => Enumerable.Range(0, count).Select(index => new Candle(DateTimeOffset.UnixEpoch.AddMinutes(index), DateTimeOffset.UnixEpoch.AddMinutes(index + 1).AddMilliseconds(-1), price, 101m, 99m, price, 1m, true)).ToArray();
}

public sealed class ResearchSegmentBoundaryTests
{
    [Fact]
    public void Development_ClosesAtSegmentEnd_WithoutUsingFutureTakeProfit()
    {
        var candles = Candles(8); candles[0] = candles[0] with { Low = 90m }; candles[6] = candles[6] with { High = 120m, Close = 120m };
        var events = new[] { Event(candles, 4, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 4, SignalDirection.Long, SignalStatus.LongSignal) };
        var development = new BacktestEngine(new EmaSignalEngine()).RunResearchWithEvents(candles, Settings(), events, candles[0].OpenTimeUtc, candles[5].CloseTimeUtc);

        var trade = Assert.Single(development.Trades);
        Assert.Equal(BacktestExitReason.EndOfData, trade.ExitReason);
        Assert.Equal(candles[5].CloseTimeUtc, trade.ExitTimeUtc);
        Assert.Equal(candles[5].Close, trade.ExitPrice);
    }

    [Fact]
    public void ResearchDiagnostics_ExcludeWarmupAndOtherSegmentEvents()
    {
        var candles = Candles(8);
        var events = new[] { Event(candles, 1, SignalDirection.Long, SignalStatus.BullishCrossover), Event(candles, 4, SignalDirection.Long, SignalStatus.LongSignal), Event(candles, 6, SignalDirection.Short, SignalStatus.ShortSignal), Event(candles, 6, SignalDirection.Short, SignalStatus.RejectedByEmaGap) };
        var engine = new BacktestEngine(new EmaSignalEngine());
        var development = engine.RunResearchWithEvents(candles, Settings(), events, candles[3].OpenTimeUtc, candles[5].CloseTimeUtc);
        var validation = engine.RunResearchWithEvents(candles, Settings(), events, candles[6].OpenTimeUtc, candles[7].CloseTimeUtc);

        Assert.Equal(1, development.Diagnostics.LongSignals); Assert.Equal(0, development.Diagnostics.TotalCrossovers); Assert.Equal(0, development.Diagnostics.ShortSignals);
        Assert.Equal(1, validation.Diagnostics.ShortSignals); Assert.Equal(1, validation.Diagnostics.RejectedByEmaGap); Assert.Equal(0, validation.Diagnostics.TotalCrossovers);
    }

    private static TradingSettings Settings() => new() { RiskReward = 2m, FixedOrderSizeUsdt = 100m };
    private static StrategyEvent Event(Candle[] candles, int index, SignalDirection direction, SignalStatus status) => new(candles[index].CloseTimeUtc, direction, status, new IndicatorSnapshot(candles[index].CloseTimeUtc, candles[index].Close, 1m, 1m, null, null, GapState.Unchanged, TrendDirection.Neutral));
    private static Candle[] Candles(int count) => Enumerable.Range(0, count).Select(index => { var time = DateTimeOffset.UnixEpoch.AddMinutes(index); return new Candle(time, time.AddMinutes(1).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true); }).ToArray();
}

public sealed class BacktestServiceCorrectiveTests
{
    [Fact]
    public async Task Run_MetadataUsesOnlyEligibleClosedInRangeCandlesAndSnapshotsSettings()
    {
        var start = DateTimeOffset.UnixEpoch; var used = new Candle(start, start.AddMinutes(1), 100, 101, 99, 100, 1, true);
        var excluded = new Candle(start.AddMinutes(2), start.AddMinutes(5), 100, 101, 99, 100, 1, true);
        await using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        database.TradingSettings.Add(new TradingSettings { Id = 1, RiskReward = 3m, FixedOrderSizeUsdt = 150m, UpdatedAtUtc = start }); await database.SaveChangesAsync();
        var service = new BacktestService(database, new StaticHistorical([used, excluded]), new TradingSettingsService(database, Options.Create(new TradingDefaultsOptions())), new BacktestEngine(new EmaSignalEngine()));
        var run = await service.RunAsync("BTCUSDT", "1m", start, used.CloseTimeUtc, CancellationToken.None);
        database.TradingSettings.Single().RiskReward = 9m; await database.SaveChangesAsync();
        var saved = await service.GetAsync(run.Id, CancellationToken.None);
        Assert.Equal(1, run.CandleCount); Assert.Equal(used.OpenTimeUtc, run.ActualStartUtc); Assert.Equal(used.CloseTimeUtc, run.ActualEndUtc); Assert.Equal(3m, saved!.RiskReward);
    }

    private sealed class StaticHistorical(IReadOnlyList<Candle> candles) : IHistoricalMarketDataProvider
    { public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token) => Task.FromResult(candles); }
}

public sealed class BacktestApiCorrectiveTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;
    public BacktestApiCorrectiveTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PostAndGet_ReturnSerializableDtoWithoutNavigationCyclesAndPersistOnce()
    {
        _factory.BinanceClient.Klines = ApiCandles();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Login(client); await EnsureEnabledSymbol();
        var response = await Post(client, "/api/backtests", new BacktestRequest("BTCUSDT", "3m", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(105)));
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<BacktestRunDetailResponse>(json, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.NotNull(detail); Assert.NotEmpty(detail.Trades); Assert.DoesNotContain("\"backtestRun\":", json, StringComparison.OrdinalIgnoreCase); Assert.Equal("Completed", detail.Status.ToString());
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        Assert.Equal(1, await db.BacktestRuns.CountAsync()); Assert.Equal(detail.Trades.Count, await db.BacktestTrades.CountAsync());
        var list = JsonSerializer.Deserialize<List<BacktestRunSummaryResponse>>(await (await client.GetAsync("/api/backtests")).Content.ReadAsStringAsync(), JsonOptions); Assert.Single(list!);
        var loaded = JsonSerializer.Deserialize<BacktestRunDetailResponse>(await (await client.GetAsync($"/api/backtests/{detail.Id}")).Content.ReadAsStringAsync(), JsonOptions); Assert.NotNull(loaded); Assert.NotEmpty(loaded.Trades);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(client, $"/api/backtests/{detail.Id}")).StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.BacktestRuns.CountAsync()); Assert.Equal(0, await db.BacktestTrades.CountAsync());
    }

    [Fact]
    public async Task BacktestEndpoints_RejectUnauthenticatedAndInvalidRequests()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/backtests", new BacktestRequest("BTCUSDT", "3m", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1)))).StatusCode);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/backtests", new BacktestRequest("MISSINGUSDT", "3m", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/backtests", new BacktestRequest("BTCUSDT", "3m", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch))).StatusCode);
    }

    private async Task EnsureEnabledSymbol()
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        if (!await db.MonitoredSymbols.AnyAsync(symbol => symbol.Symbol == "BTCUSDT")) { db.MonitoredSymbols.Add(new MonitoredSymbol { Source = MarketDataSource.Mt5Exness, Symbol = "BTCUSDT", BaseAsset = "BTC", QuoteAsset = "USDT", IsEnabled = true }); await db.SaveChangesAsync(); }
    }
    private static async Task Login(HttpClient client)
    { var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) }; request.Headers.Add("X-CSRF-TOKEN", token!.Token); Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode); }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    { var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"); using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", token!.Token); return await client.SendAsync(request); }
    private static async Task<HttpResponseMessage> Delete(HttpClient client, string path)
    { var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery"); using var request = new HttpRequestMessage(HttpMethod.Delete, path); request.Headers.Add("X-CSRF-TOKEN", token!.Token); return await client.SendAsync(request); }
    private static IReadOnlyList<Candle> ApiCandles() => Enumerable.Range(0, 104).Select(index => { var close = index == 100 ? 110m : index > 100 ? 111m : 100m; var open = index == 103 ? 111m : 100m; var time = DateTimeOffset.UnixEpoch.AddMinutes(index); return new Candle(time, time.AddMinutes(1).AddMilliseconds(-1), open, index == 103 ? 112m : close, 99m, close, 1m, true); }).ToArray();
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
