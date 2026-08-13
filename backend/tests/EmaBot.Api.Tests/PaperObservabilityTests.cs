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

public sealed class PaperObservabilityTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory factory;
    public PaperObservabilityTests(EmaBotApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task WarmupInitializesIndicatorAndDecisionWithoutCreatingActionableState()
    {
        var coordinator = Coordinator();
        factory.BinanceClient.Klines = Enumerable.Range(0, 200).Select(index => { var open = DateTimeOffset.UnixEpoch.AddMinutes(index * 3); var close = 100m + index; return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), close - .5m, close + 1m, close - 1m, close, 1m, true); }).ToArray();
        var session = await PersistAsync(PaperSessionStatus.Running);

        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        var symbol = coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"];
        Assert.NotNull(symbol.Indicator);
        Assert.Equal("Warmup", symbol.LastDecision!.Stage);
        Assert.Null(symbol.PendingEntry);
        Assert.Null(symbol.OpenTrade);
        using var scope = factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.SingleAsync(item => item.Id == session.Id);
        Assert.Equal(0, persisted.TotalCrossovers);
        Assert.Equal(0, persisted.LongSignals);
        Assert.Equal(0, persisted.ShortSignals);
        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);
    }

    [Fact]
    public async Task ActiveResponseExposesAllCountersAndStructuredPersistedPendingEntry()
    {
        var session = await PersistAsync(PaperSessionStatus.Interrupted, pending: true, counters: true);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var controller = new PaperSessionsController(services.GetRequiredService<EmaBotDbContext>(), services.GetRequiredService<TradingSettingsService>(), services.GetRequiredService<PaperTradingCoordinator>(), factory.StreamClient, TestMarketProviderCapabilities.WithLiveBars(true), services.GetRequiredService<IInstrumentCatalogProvider>(), services.GetRequiredService<EmaBot.Api.Mt5Bridge.IMt5AccountReader>());

        var detail = Assert.IsType<PaperSessionDetailResponse>(Assert.IsType<OkObjectResult>(await controller.Active(CancellationToken.None)).Value);
        var pending = Assert.IsType<PaperPendingEntryResponse>(detail.Symbols.Single().PendingEntry);
        Assert.Equal("XAUUSDm", detail.Symbols.Single().Symbol);
        Assert.Equal(SignalDirection.Short, pending.Direction);
        Assert.Equal(pending.SignalTimeUtc.AddMilliseconds(1), pending.ExpectedEntryOpenUtc);
        Assert.Equal(101m, pending.StopPrice);
        Assert.Equal("FallbackLookback", pending.StopSource);
        Assert.Equal(1, detail.RejectedByEmaGap);
        Assert.Equal(2, detail.RejectedByStopDistance);
        Assert.Equal(3, detail.RejectedByFees);
        Assert.Equal(4, detail.RejectedByTradingCosts);
        Assert.Equal(5, detail.RejectedByInsufficientMargin);
        Assert.Equal(6, detail.TotalCrossovers);
        Assert.Equal(7, detail.LongSignals);
        Assert.Equal(8, detail.ShortSignals);
    }

    private PaperTradingCoordinator Coordinator() => new(factory.Services.GetRequiredService<IServiceScopeFactory>(), new FixedResolver(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>()), factory.StreamClient, factory.Services.GetRequiredService<EmaSignalEngine>(), NullLogger<PaperTradingCoordinator>.Instance);

    private async Task<PaperSession> PersistAsync(PaperSessionStatus status, bool pending = false, bool counters = false)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        foreach (var existing in await database.PaperSessions.Where(item => item.Status == PaperSessionStatus.Running || item.Status == PaperSessionStatus.Interrupted).ToListAsync()) existing.Status = PaperSessionStatus.Stopped;
        var signal = DateTimeOffset.UnixEpoch;
        var symbol = new PaperSessionSymbol { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm", ContractSize = 100m, VolumeMin = .01m, VolumeMax = 10m, VolumeStep = .01m, PointSize = .01m, TickSize = .01m, TickValueProfit = 1m, TradeMode = InstrumentTradeMode.Full, CommissionPerLotPerSide = 0m, PendingDirection = pending ? SignalDirection.Short : null, PendingCrossoverTimeUtc = pending ? signal : null, PendingSignalTimeUtc = pending ? signal : null, PendingStopPrice = pending ? 101m : null, PendingStopSourceType = pending ? StopSourceType.FallbackLookback : null, PendingStopSourceTimeUtc = pending ? signal : null, PendingSignalOpen = pending ? 100m : null, PendingSignalClose = pending ? 100m : null, PendingSignalEma9 = pending ? 99m : null, PendingSignalEma15 = pending ? 100m : null, PendingSignalEma100 = pending ? 101m : null, PendingSignalGapPercent = pending ? .1m : null, PendingSignalGapState = pending ? GapState.Expanding : null };
        var session = new PaperSession { MarketDataSource = MarketDataSource.Mt5Exness, Interval = "3m", Status = status, CreatedAtUtc = DateTimeOffset.UtcNow, StartedAtUtc = DateTimeOffset.UtcNow, RiskReward = 2m, FixedOrderSizeUsdt = 100m, AccountCurrency = "USD", StartingBalance = 1000m, CurrentBalance = 1000m, Symbols = [symbol] };
        if (counters) { session.RejectedByEmaGap = 1; session.RejectedByStopDistance = 2; session.RejectedByFees = 3; session.RejectedByTradingCosts = 4; session.RejectedByInsufficientMargin = 5; session.TotalCrossovers = 6; session.LongSignals = 7; session.ShortSignals = 8; }
        database.PaperSessions.Add(session); await database.SaveChangesAsync(); return session;
    }

    private sealed class FixedResolver(IHistoricalMarketDataProvider provider) : IHistoricalMarketDataProviderResolver { public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => provider; }
}
