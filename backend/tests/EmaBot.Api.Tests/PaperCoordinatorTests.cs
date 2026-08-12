using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EmaBot.Api.Tests;

public sealed class PaperCoordinatorTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;
    public PaperCoordinatorTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task NewSessionWarmupFailure_CleansRuntimeAndAllowsAnotherStart()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        _factory.BinanceClient.KlinesException = new BinanceApiException("unavailable", 502);
        var failed = await CreateSession(PaperSessionStatus.Running);
        await Assert.ThrowsAsync<MarketDataProviderException>(() => coordinator.StartSessionAsync(failed.Id, false, CancellationToken.None));
        Assert.Null(coordinator.GetRuntimeSnapshot());
        Assert.Equal(PaperSessionStatus.Faulted, await Status(failed.Id));
        _factory.BinanceClient.KlinesException = null;
        var next = await CreateSession(PaperSessionStatus.Running);
        await coordinator.StartSessionAsync(next.Id, false, CancellationToken.None);
        Assert.Equal(next.Id, coordinator.GetRuntimeSnapshot()!.SessionId);
        await coordinator.StopSessionAsync(next.Id, CancellationToken.None);
    }

    [Fact]
    public async Task ResumeWarmupFailure_LeavesSessionInterruptedWithoutActiveRuntime()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        _factory.BinanceClient.KlinesException = new BinanceApiException("unavailable", 502);
        var session = await CreateSession(PaperSessionStatus.Interrupted);
        await Assert.ThrowsAsync<MarketDataProviderException>(() => coordinator.StartSessionAsync(session.Id, true, CancellationToken.None));
        Assert.Null(coordinator.GetRuntimeSnapshot());
        Assert.Equal(PaperSessionStatus.Interrupted, await Status(session.Id));
        _factory.BinanceClient.KlinesException = null;
    }

    [Fact]
    public async Task Resume_DiscardsPersistedPendingReentryButPreservesConsumedRegime()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        var signal = DateTimeOffset.UnixEpoch.AddHours(1).AddMilliseconds(-1);
        var session = await CreateSession(PaperSessionStatus.Interrupted, signal);
        using (var scope = _factory.Services.CreateScope())
        {
            var symbol = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessionSymbols.SingleAsync(item => item.PaperSessionId == session.Id);
            symbol.PendingIsReentry = true; symbol.PendingTrendRegimeCrossoverTimeUtc = DateTimeOffset.UnixEpoch;
            symbol.TrendRegimeDirection = SignalDirection.Long; symbol.TrendRegimeCrossoverTimeUtc = DateTimeOffset.UnixEpoch; symbol.ReentryConsumed = true;
            await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().SaveChangesAsync();
        }

        await coordinator.StartSessionAsync(session.Id, true, CancellationToken.None);
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].PendingDirection);
        using (var scope = _factory.Services.CreateScope())
        {
            var symbol = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessionSymbols.SingleAsync(item => item.PaperSessionId == session.Id);
            Assert.Null(symbol.PendingDirection); Assert.False(symbol.PendingIsReentry);
            Assert.Equal(SignalDirection.Long, symbol.TrendRegimeDirection); Assert.Equal(DateTimeOffset.UnixEpoch, symbol.TrendRegimeCrossoverTimeUtc); Assert.True(symbol.ReentryConsumed);
        }
        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Fact]
    public async Task PendingEntry_EntersOnlyAtExactNextOpenAndExpiresWhenLate()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        var signal = DateTimeOffset.UnixEpoch.AddMinutes(10).AddMilliseconds(-1);
        var exact = await CreateSession(PaperSessionStatus.Running, signal);
        await coordinator.StartSessionAsync(exact.Id, false, CancellationToken.None);
        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m));
        Assert.NotNull(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade);
        await coordinator.StopSessionAsync(exact.Id, CancellationToken.None);

        var late = await CreateSession(PaperSessionStatus.Running, signal);
        await coordinator.StartSessionAsync(late.Id, false, CancellationToken.None);
        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(2), 100m));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade);
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].PendingDirection);
        await coordinator.StopSessionAsync(late.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Management_UsesObservedCloseNotCumulativeKlineHigh()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        var session = await CreateSession(PaperSessionStatus.Running, openTrade: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        var time = DateTimeOffset.UnixEpoch.AddHours(1);
        await coordinator.ProcessUpdateForTestAsync(new MarketBarUpdate("BTCUSDT", "3m", time, time, time.AddMinutes(3).AddMilliseconds(-1), 100m, 150m, 50m, 101m, 1m, false));
        var trade = coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade!;
        Assert.Equal(90m, trade.CurrentStopLoss);
        Assert.Equal(1m, trade.MfePrice);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public async Task FeeAwareTrailingStop_UsesObservedPriceAndPersistsNonNegativeExit(SignalDirection direction)
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        var stop = direction == SignalDirection.Long ? 99.9m : 100.1m;
        var threshold = direction == SignalDirection.Long ? 100.11m : 99.89m;
        var session = await CreateSession(PaperSessionStatus.Running, openTrade: true, direction: direction, entry: 100m, stop: stop, fee: .05m, trailing: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        var time = DateTimeOffset.UnixEpoch.AddHours(3);

        await coordinator.ProcessUpdateForTestAsync(Update(time, threshold));
        var openTrade = coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade!;
        var breakeven = TradeMath.FeeBreakevenPrice(100m, direction, .05m);
        Assert.True(direction == SignalDirection.Long ? openTrade.CurrentStopLoss >= breakeven : openTrade.CurrentStopLoss <= breakeven);
        await coordinator.ProcessUpdateForTestAsync(Update(time.AddSeconds(1), openTrade.CurrentStopLoss));

        using var scope = _factory.Services.CreateScope();
        var trade = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperTrades.SingleAsync(item => item.PaperSessionId == session.Id);
        Assert.Equal(PaperExitReason.TrailingStop, trade.ExitReason); Assert.True(trade.NetPnlUsdt >= 0m);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Fact]
    public async Task InitialStopExit_PersistsSingleReentryRegimeState()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        var session = await CreateSession(PaperSessionStatus.Running, openTrade: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        await coordinator.ProcessUpdateForTestAsync(Update(DateTimeOffset.UnixEpoch.AddHours(1), 90m));

        using var scope = _factory.Services.CreateScope();
        var symbol = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessionSymbols.SingleAsync(item => item.PaperSessionId == session.Id);
        Assert.True(symbol.ReentryEligible);
        Assert.False(symbol.ReentryConsumed);
        Assert.Equal(SignalDirection.Long, symbol.TrendRegimeDirection);
        Assert.Equal(DateTimeOffset.UnixEpoch, symbol.TrendRegimeCrossoverTimeUtc);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public async Task SameExitCandleContinuation_EntersOnePersistedReentryAtFollowingOpen(SignalDirection direction)
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        var entry = 300m; var originalStop = direction == SignalDirection.Long ? 290m : 310m;
        var continuationClose = direction == SignalDirection.Long ? 310m : 290m;
        _factory.BinanceClient.Klines = TrendingWarmup(direction);
        var session = await CreateSession(PaperSessionStatus.Running, openTrade: true, direction: direction, entry: entry, stop: originalStop);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        var exitCandleOpen = _factory.BinanceClient.Klines[^1].CloseTimeUtc.AddMilliseconds(1);

        await coordinator.ProcessUpdateForTestAsync(Kline(exitCandleOpen, entry, originalStop, originalStop, false));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade);
        await coordinator.ProcessUpdateForTestAsync(Kline(exitCandleOpen, entry, continuationClose, direction == SignalDirection.Long ? entry - 1m : entry + 1m, true));

        using (var scope = _factory.Services.CreateScope())
        {
            var symbol = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessionSymbols.SingleAsync(item => item.PaperSessionId == session.Id);
            Assert.False(symbol.ReentryEligible); Assert.True(symbol.ReentryConsumed); Assert.Equal(direction, symbol.PendingDirection); Assert.True(symbol.PendingIsReentry);
            Assert.Equal(DateTimeOffset.UnixEpoch, symbol.PendingTrendRegimeCrossoverTimeUtc);
        }

        var entryOpen = exitCandleOpen.AddMinutes(3);
        var reentryOpen = direction == SignalDirection.Long ? 311m : 289m;
        await coordinator.ProcessUpdateForTestAsync(Kline(entryOpen, reentryOpen, reentryOpen, reentryOpen, false));
        var reentry = coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade!;
        Assert.True(reentry.IsReentry); Assert.Equal(DateTimeOffset.UnixEpoch, reentry.TrendRegimeCrossoverTimeUtc); Assert.Equal(reentryOpen, reentry.EntryPrice);
        Assert.Equal(entry, reentry.SignalOpen); Assert.Equal(continuationClose, reentry.SignalClose); Assert.NotNull(reentry.SignalEma9); Assert.NotNull(reentry.SignalEma15);

        var stopCandleOpen = entryOpen.AddMinutes(3);
        await coordinator.ProcessUpdateForTestAsync(Kline(stopCandleOpen, reentryOpen, reentry.CurrentStopLoss, reentry.CurrentStopLoss, false));
        await coordinator.ProcessUpdateForTestAsync(Kline(stopCandleOpen, reentryOpen, continuationClose, direction == SignalDirection.Long ? reentryOpen - 1m : reentryOpen + 1m, true));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade);
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].PendingDirection);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Fact]
    public async Task ClosedCandleGap_RefetchesHistoryWithoutSchedulingHistoricalSignals()
    {
        var coordinator = _factory.Services.GetRequiredService<PaperTradingCoordinator>();
        _factory.BinanceClient.ResetKlineRequests();
        _factory.BinanceClient.Klines = [new Candle(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true)];
        var session = await CreateSession(PaperSessionStatus.Running);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        var first = DateTimeOffset.UnixEpoch.AddHours(2);
        await coordinator.ProcessUpdateForTestAsync(new MarketBarUpdate("BTCUSDT", "3m", first, first, first.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true));
        var afterGap = first.AddMinutes(6);
        await coordinator.ProcessUpdateForTestAsync(new MarketBarUpdate("BTCUSDT", "3m", afterGap, afterGap, afterGap.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true));
        Assert.Equal(2, _factory.BinanceClient.KlineRequests);
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].PendingDirection);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    private async Task<PaperSession> CreateSession(PaperSessionStatus status, DateTimeOffset? pendingSignal = null, bool openTrade = false, SignalDirection direction = SignalDirection.Long, decimal entry = 100m, decimal? stop = null, decimal fee = .1m, bool trailing = false)
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var symbol = new PaperSessionSymbol { Symbol = "BTCUSDT", PendingDirection = pendingSignal.HasValue ? SignalDirection.Long : null, PendingCrossoverTimeUtc = pendingSignal, PendingSignalTimeUtc = pendingSignal, PendingStopPrice = pendingSignal.HasValue ? 90m : null, PendingStopSourceType = pendingSignal.HasValue ? StopSourceType.FallbackLookback : null, PendingStopSourceTimeUtc = pendingSignal, PendingSignalClose = pendingSignal.HasValue ? 100m : null, PendingSignalEma9 = pendingSignal.HasValue ? 101m : null, PendingSignalEma15 = pendingSignal.HasValue ? 100m : null, PendingSignalGapState = pendingSignal.HasValue ? GapState.Expanding : null };
        var session = new PaperSession { Interval = "3m", Status = status, CreatedAtUtc = DateTimeOffset.UtcNow, StartedAtUtc = DateTimeOffset.UtcNow, RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = fee, TrailingStopEnabled = trailing, Symbols = [symbol] };
        if (openTrade) { var effectiveStop = stop ?? (direction == SignalDirection.Long ? entry - 10m : entry + 10m); var target = direction == SignalDirection.Long ? entry + (entry - effectiveStop) * 2m : entry - (effectiveStop - entry) * 2m; var entryFee = TradeMath.Fee(entry, 1m, fee); session.Trades.Add(new PaperTrade { PaperSessionSymbol = symbol, Symbol = "BTCUSDT", Interval = "3m", Status = PaperTradeStatus.Open, Direction = direction, CrossoverTimeUtc = DateTimeOffset.UnixEpoch, SignalTimeUtc = DateTimeOffset.UnixEpoch, EntryTimeUtc = DateTimeOffset.UnixEpoch, EntryPrice = entry, Quantity = 1m, EntryNotionalUsdt = entry, InitialStopLoss = effectiveStop, CurrentStopLoss = effectiveStop, StopSourceType = StopSourceType.FallbackLookback, StopSourceTimeUtc = DateTimeOffset.UnixEpoch, OriginalTakeProfit = target, CurrentTakeProfit = target, EntryFeeUsdt = entryFee, TotalFeesUsdt = entryFee }); }
        db.PaperSessions.Add(session); await db.SaveChangesAsync(); return session;
    }
    private async Task<PaperSessionStatus> Status(int id) { using var scope = _factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.Where(session => session.Id == id).Select(session => session.Status).SingleAsync(); }
    private static MarketBarUpdate Update(DateTimeOffset open, decimal close) => new("BTCUSDT", "3m", open, open, open.AddMinutes(3).AddMilliseconds(-1), close, close, close, close, 1m, false);
    private static MarketBarUpdate Kline(DateTimeOffset open, decimal candleOpen, decimal close, decimal extreme, bool closed) => new("BTCUSDT", "3m", open, open, open.AddMinutes(3).AddMilliseconds(-1), candleOpen, Math.Max(candleOpen, Math.Max(close, extreme)), Math.Min(candleOpen, Math.Min(close, extreme)), close, 1m, closed);
    private static IReadOnlyList<Candle> TrendingWarmup(SignalDirection direction) => Enumerable.Range(0, 200).Select(index => { var close = direction == SignalDirection.Long ? 100m + index : 500m - index; var open = direction == SignalDirection.Long ? close - .5m : close + .5m; var time = DateTimeOffset.UnixEpoch.AddMinutes(index * 3); return new Candle(time, time.AddMinutes(3).AddMilliseconds(-1), open, Math.Max(open, close) + 1m, Math.Min(open, close) - 1m, close, 1m, true); }).ToArray();
}
