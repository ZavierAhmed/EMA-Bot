using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmaBot.Api.Tests;

public sealed class PaperSessionRecoveryTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory factory;

    public PaperSessionRecoveryTests(EmaBotApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task InterruptedSessionWithoutPosition_CanBeEndedWithoutRuntime()
    {
        var coordinator = CreateCoordinator();
        var session = await PersistAsync(PaperSessionStatus.Interrupted);

        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.SingleAsync(item => item.Id == session.Id);
        Assert.Equal(PaperSessionStatus.Stopped, persisted.Status);
        Assert.NotNull(persisted.StoppedAtUtc);
        Assert.Null(coordinator.GetRuntimeSnapshot());
    }

    [Fact]
    public async Task InterruptedSessionWithOpenPosition_CannotBeEndedWithoutExecutableQuote()
    {
        var coordinator = CreateCoordinator();
        var session = await PersistAsync(PaperSessionStatus.Interrupted, openTrade: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StopSessionAsync(session.Id, CancellationToken.None));

        Assert.Contains("Resume the session", exception.Message);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        Assert.Equal(PaperSessionStatus.Interrupted, (await database.PaperSessions.SingleAsync(item => item.Id == session.Id)).Status);
        var trade = await database.PaperTrades.SingleAsync(item => item.PaperSessionId == session.Id);
        Assert.Equal(PaperTradeStatus.Open, trade.Status);
        Assert.Null(trade.ExitPrice);
        Assert.Null(trade.GrossPnl);
        Assert.Null(trade.NetPnl);
    }

    [Fact]
    public async Task Resume_RecreatesRuntimeAndClearsPersistedPendingEntry()
    {
        var coordinator = CreateCoordinator();
        var session = await PersistAsync(PaperSessionStatus.Interrupted, pending: true);

        await coordinator.StartSessionAsync(session.Id, true, CancellationToken.None);

        Assert.Equal(session.Id, coordinator.GetRuntimeSnapshot()!.SessionId);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var persisted = await database.PaperSessions.Include(item => item.Symbols).SingleAsync(item => item.Id == session.Id);
            Assert.Equal(PaperSessionStatus.Running, persisted.Status);
            Assert.Null(persisted.Symbols.Single().PendingDirection);
        }
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Fact]
    public async Task ActiveEndpoint_ExposesPersistedOpenPositionWhenRuntimeIsAbsent()
    {
        var session = await PersistAsync(PaperSessionStatus.Interrupted, openTrade: true);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var controller = new PaperSessionsController(services.GetRequiredService<EmaBotDbContext>(), services.GetRequiredService<TradingSettingsService>(), services.GetRequiredService<PaperTradingCoordinator>(), factory.StreamClient, TestMarketProviderCapabilities.WithLiveBars(true), services.GetRequiredService<IInstrumentCatalogProvider>(), services.GetRequiredService<EmaBot.Api.Mt5Bridge.IMt5AccountReader>());

        var response = await controller.Active(CancellationToken.None);

        var detail = Assert.IsType<PaperSessionDetailResponse>(Assert.IsType<OkObjectResult>(response).Value);
        var trade = Assert.IsType<PaperTradeResponse>(detail.Symbols.Single().OpenTrade);
        Assert.Equal(session.Id, detail.Id);
        Assert.Equal(SignalDirection.Long, trade.Direction);
        Assert.Equal(100.2m, trade.EntryPrice);
        Assert.Equal(.01m, trade.Lots);
        Assert.Equal(99m, trade.CurrentStopLoss);
        Assert.Equal(102.6m, trade.CurrentTakeProfit);
        Assert.Equal(35m, trade.RequiredMargin);
        Assert.Equal(.06m, trade.RoundTripCommission);
    }

    [Fact]
    public async Task EndingInterruptedSession_DoesNotStopDifferentActiveRuntime()
    {
        var coordinator = CreateCoordinator();
        var running = await PersistAsync(PaperSessionStatus.Running);
        var interrupted = await PersistAsync(PaperSessionStatus.Interrupted, normalizeExisting: false);
        await coordinator.StartSessionAsync(running.Id, false, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StopSessionAsync(interrupted.Id, CancellationToken.None));

        Assert.Contains("different paper session", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(running.Id, coordinator.GetRuntimeSnapshot()!.SessionId);
        Assert.Equal(PaperSessionStatus.Interrupted, await StatusAsync(interrupted.Id));
        await coordinator.StopSessionAsync(running.Id, CancellationToken.None);
    }

    [Fact]
    public async Task AlreadyStoppedSessionWithoutRuntime_ReturnsClearInvalidState()
    {
        var coordinator = CreateCoordinator();
        var session = await PersistAsync(PaperSessionStatus.Stopped);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StopSessionAsync(session.Id, CancellationToken.None));

        Assert.Contains("Stopped", exception.Message);
    }

    [Fact]
    public async Task InterruptedLegacySessionWithOpenPosition_ReturnsLegacyLifecycleError()
    {
        var coordinator = CreateCoordinator();
        var session = await PersistAsync(PaperSessionStatus.Interrupted, openTrade: true, source: MarketDataSource.LegacyBinance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StopSessionAsync(session.Id, CancellationToken.None));

        Assert.Contains("Legacy Binance", exception.Message);
        Assert.Equal(PaperSessionStatus.Interrupted, await StatusAsync(session.Id));
    }

    private PaperTradingCoordinator CreateCoordinator() => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new FixedResolver(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>()),
        factory.StreamClient,
        factory.Services.GetRequiredService<EmaSignalEngine>(),
        NullLogger<PaperTradingCoordinator>.Instance);

    private async Task<PaperSession> PersistAsync(PaperSessionStatus status, bool openTrade = false, bool pending = false, bool normalizeExisting = true, MarketDataSource source = MarketDataSource.Mt5Exness)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        foreach (var existing in normalizeExisting ? await database.PaperSessions.Where(item => item.Status == PaperSessionStatus.Running || item.Status == PaperSessionStatus.Interrupted).ToListAsync() : [])
        {
            existing.Status = PaperSessionStatus.Stopped;
            existing.StoppedAtUtc = DateTimeOffset.UtcNow;
        }
        var signal = DateTimeOffset.UnixEpoch;
        var symbol = new PaperSessionSymbol
        {
            Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm", ContractSize = 100m, VolumeMin = .01m, VolumeMax = 10m, VolumeStep = .01m,
            PointSize = .01m, TickSize = .01m, TickValueProfit = 1m, TradeMode = InstrumentTradeMode.Full, CommissionPerLotPerSide = 3m,
            PendingDirection = pending ? SignalDirection.Long : null, PendingCrossoverTimeUtc = pending ? signal : null, PendingSignalTimeUtc = pending ? signal : null,
            PendingStopPrice = pending ? 99m : null, PendingStopSourceType = pending ? StopSourceType.FallbackLookback : null,
            PendingStopSourceTimeUtc = pending ? signal : null, PendingSignalOpen = pending ? 100.2m : null, PendingSignalClose = pending ? 100.2m : null,
            PendingSignalEma9 = pending ? 100.2m : null, PendingSignalEma15 = pending ? 100m : null, PendingSignalGapState = pending ? GapState.Expanding : null
        };
        var session = new PaperSession
        {
            MarketDataSource = source, Interval = "3m", Status = status, CreatedAtUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = DateTimeOffset.UtcNow, RiskReward = 2m, AccountCurrency = "USD", PaperPositionSizingMode = PaperPositionSizingMode.FixedLots,
            PaperFixedLots = .01m, PaperMarginPerTradePercent = 10m, StartingBalance = 1000m, CurrentBalance = 1000m, Symbols = [symbol]
        };
        if (openTrade)
        {
            session.Trades.Add(new PaperTrade
            {
                PaperSessionSymbol = symbol, Symbol = symbol.Symbol, Interval = session.Interval, Status = PaperTradeStatus.Open, Direction = SignalDirection.Long,
                CrossoverTimeUtc = signal, SignalTimeUtc = signal, EntryTimeUtc = signal, EntryPrice = 100.2m, Quantity = 1m,
                InitialStopLoss = 99m, CurrentStopLoss = 99m, StopSourceType = StopSourceType.FallbackLookback, StopSourceTimeUtc = signal,
                OriginalTakeProfit = 102.6m, CurrentTakeProfit = 102.6m, Lots = .01m, RequiredMargin = 35m, MarginUsed = 35m,
                AccountEquityAtEntry = 1000m, RoundTripCommission = .06m
            });
            session.UsedMargin = 35m;
        }
        database.PaperSessions.Add(session);
        await database.SaveChangesAsync();
        return session;
    }

    private async Task<PaperSessionStatus> StatusAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.Where(item => item.Id == id).Select(item => item.Status).SingleAsync();
    }

    private sealed class FixedResolver(IHistoricalMarketDataProvider provider) : IHistoricalMarketDataProviderResolver
    {
        public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => provider;
    }
}
