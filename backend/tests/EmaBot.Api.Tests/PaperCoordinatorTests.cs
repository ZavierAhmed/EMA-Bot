using EmaBot.Api.Binance;
using EmaBot.Api.Data;
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
        await Assert.ThrowsAsync<BinanceApiException>(() => coordinator.StartSessionAsync(failed.Id, false, CancellationToken.None));
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
        await Assert.ThrowsAsync<BinanceApiException>(() => coordinator.StartSessionAsync(session.Id, true, CancellationToken.None));
        Assert.Null(coordinator.GetRuntimeSnapshot());
        Assert.Equal(PaperSessionStatus.Interrupted, await Status(session.Id));
        _factory.BinanceClient.KlinesException = null;
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
        await coordinator.ProcessUpdateForTestAsync(new BinanceKlineUpdate("BTCUSDT", "3m", time, time, time.AddMinutes(3).AddMilliseconds(-1), 100m, 150m, 50m, 101m, 1m, false));
        var trade = coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].OpenTrade!;
        Assert.Equal(90m, trade.CurrentStopLoss);
        Assert.Equal(1m, trade.MfePrice);
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
        await coordinator.ProcessUpdateForTestAsync(new BinanceKlineUpdate("BTCUSDT", "3m", first, first, first.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true));
        var afterGap = first.AddMinutes(6);
        await coordinator.ProcessUpdateForTestAsync(new BinanceKlineUpdate("BTCUSDT", "3m", afterGap, afterGap, afterGap.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true));
        Assert.Equal(2, _factory.BinanceClient.KlineRequests);
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["BTCUSDT"].PendingDirection);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    private async Task<PaperSession> CreateSession(PaperSessionStatus status, DateTimeOffset? pendingSignal = null, bool openTrade = false)
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var symbol = new PaperSessionSymbol { Symbol = "BTCUSDT", PendingDirection = pendingSignal.HasValue ? SignalDirection.Long : null, PendingCrossoverTimeUtc = pendingSignal, PendingSignalTimeUtc = pendingSignal, PendingStopPrice = pendingSignal.HasValue ? 90m : null, PendingStopSourceType = pendingSignal.HasValue ? StopSourceType.FallbackLookback : null, PendingStopSourceTimeUtc = pendingSignal, PendingSignalClose = pendingSignal.HasValue ? 100m : null, PendingSignalEma9 = pendingSignal.HasValue ? 101m : null, PendingSignalEma15 = pendingSignal.HasValue ? 100m : null, PendingSignalGapState = pendingSignal.HasValue ? GapState.Expanding : null };
        var session = new PaperSession { Interval = "3m", Status = status, CreatedAtUtc = DateTimeOffset.UtcNow, StartedAtUtc = DateTimeOffset.UtcNow, RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = 0.1m, Symbols = [symbol] };
        if (openTrade) session.Trades.Add(new PaperTrade { PaperSessionSymbol = symbol, Symbol = "BTCUSDT", Interval = "3m", Status = PaperTradeStatus.Open, Direction = SignalDirection.Long, CrossoverTimeUtc = DateTimeOffset.UnixEpoch, SignalTimeUtc = DateTimeOffset.UnixEpoch, EntryTimeUtc = DateTimeOffset.UnixEpoch, EntryPrice = 100m, Quantity = 1m, EntryNotionalUsdt = 100m, InitialStopLoss = 90m, CurrentStopLoss = 90m, StopSourceType = StopSourceType.FallbackLookback, StopSourceTimeUtc = DateTimeOffset.UnixEpoch, OriginalTakeProfit = 120m, CurrentTakeProfit = 120m, EntryFeeUsdt = 0.1m, TotalFeesUsdt = 0.1m });
        db.PaperSessions.Add(session); await db.SaveChangesAsync(); return session;
    }
    private async Task<PaperSessionStatus> Status(int id) { using var scope = _factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.Where(session => session.Id == id).Select(session => session.Status).SingleAsync(); }
    private static BinanceKlineUpdate Update(DateTimeOffset open, decimal close) => new("BTCUSDT", "3m", open, open, open.AddMinutes(3).AddMilliseconds(-1), close, close, close, close, 1m, false);
}
