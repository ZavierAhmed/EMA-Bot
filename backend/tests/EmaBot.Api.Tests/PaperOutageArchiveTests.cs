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

public sealed class PaperOutageArchiveTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory factory;
    public PaperOutageArchiveTests(EmaBotApiFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(MarketDataErrorKind.Unavailable)]
    [InlineData(MarketDataErrorKind.Timeout)]
    public async Task RecoverableMt5StreamOutage_InterruptsAndRecordsEvidence(MarketDataErrorKind kind)
    {
        var session = await PersistSessionAsync(PaperSessionStatus.Running);
        var coordinator = Coordinator(new ThrowingStream(new MarketDataProviderException("MT5", kind, "bridge unavailable")));

        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        await WaitForAsync(async () => await StatusAsync(session.Id) == PaperSessionStatus.Interrupted);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var persisted = await database.PaperSessions.SingleAsync(item => item.Id == session.Id);
        Assert.NotNull(persisted.InterruptedAtUtc);
        Assert.Contains("Resume", persisted.FailureMessage);
        Assert.Equal("SessionInterrupted", await database.PaperDecisionEvents.Where(item => item.PaperSessionId == session.Id).OrderByDescending(item => item.Id).Select(item => item.Stage).FirstAsync());
        Assert.Null(coordinator.GetRuntimeSnapshot());
    }

    [Theory]
    [InlineData(MarketDataErrorKind.InvalidResponse)]
    public async Task NonrecoverableMt5StreamOutage_Faults(MarketDataErrorKind kind)
    {
        var session = await PersistSessionAsync(PaperSessionStatus.Running);
        var coordinator = Coordinator(new ThrowingStream(new MarketDataProviderException("MT5", kind, "malformed")));

        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        await WaitForAsync(async () => await StatusAsync(session.Id) == PaperSessionStatus.Faulted);

        Assert.Null(coordinator.GetRuntimeSnapshot());
    }

    [Fact]
    public async Task GenericStreamException_RemainsFaulted()
    {
        var session = await PersistSessionAsync(PaperSessionStatus.Running);
        var coordinator = Coordinator(new ThrowingStream(new InvalidOperationException("unexpected")));

        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        await WaitForAsync(async () => await StatusAsync(session.Id) == PaperSessionStatus.Faulted);
    }

    [Fact]
    public async Task ArchivedDetailAndPaginatedTrades_AreSessionScoped()
    {
        var archived = await PersistSessionAsync(PaperSessionStatus.Faulted, trades: 65, failure: "Bridge disconnected overnight.");
        var other = await PersistSessionAsync(PaperSessionStatus.Stopped, trades: 2);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            database.PaperDecisionEvents.Add(new PaperDecisionEvent { PaperSessionId = archived.Id, PaperSessionSymbolId = archived.Symbols.Single().Id, TimeUtc = DateTimeOffset.UtcNow, Stage = "CandleEvaluated", Message = "Persisted archive snapshot.", Ema9 = 101m, Ema15 = 100m, Ema100 = 99m, GapPercent = .5m, GapState = GapState.Expanding });
            await database.SaveChangesAsync();
        }
        var controller = Controller(Coordinator(factory.StreamClient));

        var detail = Assert.IsType<PaperSessionDetailResponse>(Assert.IsType<OkObjectResult>(await controller.Get(archived.Id, CancellationToken.None)).Value);
        var first = Assert.IsType<PaperSessionTradeHistoryResponse>(Assert.IsType<OkObjectResult>(await controller.Trades(archived.Id, 1, 20)).Value);
        var second = Assert.IsType<PaperSessionTradeHistoryResponse>(Assert.IsType<OkObjectResult>(await controller.Trades(archived.Id, 2, 20)).Value);

        Assert.Equal(PaperSessionStatus.Faulted, detail.Status);
        Assert.Equal("Bridge disconnected overnight.", detail.FailureMessage);
        Assert.Equal(101m, detail.Symbols.Single().Ema9);
        Assert.Equal("Up", detail.Symbols.Single().Trend);
        Assert.Equal(65, first.Total);
        Assert.Equal(20, first.Items.Count);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.DoesNotContain(first.Items, item => item.Symbol == other.Symbols.Single().Symbol);
        Assert.True(first.Items.Zip(first.Items.Skip(1)).All(pair => pair.First.EntryTimeUtc >= pair.Second.EntryTimeUtc));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task SessionTradesRejectInvalidPageLimits(int page, int pageSize)
    {
        var session = await PersistSessionAsync(PaperSessionStatus.Stopped);
        Assert.IsType<BadRequestObjectResult>(await Controller(Coordinator(factory.StreamClient)).Trades(session.Id, page, pageSize));
    }

    private PaperTradingCoordinator Coordinator(IMarketBarStreamProvider stream) => new(factory.Services.GetRequiredService<IServiceScopeFactory>(), new FixedResolver(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>()), stream, factory.Services.GetRequiredService<EmaSignalEngine>(), NullLogger<PaperTradingCoordinator>.Instance);
    private PaperSessionsController Controller(PaperTradingCoordinator coordinator)
    {
        var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        return new PaperSessionsController(services.GetRequiredService<EmaBotDbContext>(), services.GetRequiredService<TradingSettingsService>(), coordinator, factory.StreamClient, TestMarketProviderCapabilities.WithLiveBars(true), services.GetRequiredService<IInstrumentCatalogProvider>(), services.GetRequiredService<EmaBot.Api.Mt5Bridge.IMt5AccountReader>());
    }

    private async Task<PaperSession> PersistSessionAsync(PaperSessionStatus status, int trades = 0, string? failure = null)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        foreach (var current in await database.PaperSessions.Where(item => item.Status == PaperSessionStatus.Running || item.Status == PaperSessionStatus.Interrupted).ToListAsync()) current.Status = PaperSessionStatus.Stopped;
        var started = DateTimeOffset.UnixEpoch.AddDays((await database.PaperSessions.CountAsync()) + 1);
        var symbol = new PaperSessionSymbol { Symbol = $"XAUUSDm-{Guid.NewGuid():N}", BrokerSymbol = "XAUUSDm", ContractSize = 100m, VolumeMin = .01m, VolumeMax = 10m, VolumeStep = .01m, PointSize = .01m, TickSize = .01m, TickValueProfit = 1m, TradeMode = InstrumentTradeMode.Full, CommissionPerLotPerSide = 0m };
        var session = new PaperSession { MarketDataSource = MarketDataSource.Mt5Exness, Interval = "3m", Status = status, CreatedAtUtc = started, StartedAtUtc = started, FailureMessage = failure, AccountCurrency = "USD", StartingBalance = 1000m, CurrentBalance = 1000m, Symbols = [symbol] };
        for (var index = 0; index < trades; index++) session.Trades.Add(new PaperTrade { PaperSessionSymbol = symbol, Symbol = symbol.Symbol, Interval = "3m", Status = PaperTradeStatus.Closed, Direction = SignalDirection.Long, CrossoverTimeUtc = started, SignalTimeUtc = started, EntryTimeUtc = started.AddMinutes(index), ExitTimeUtc = started.AddMinutes(index + 1), EntryPrice = 100m + index, ExitPrice = 101m + index, Quantity = 1m, InitialStopLoss = 99m, CurrentStopLoss = 99m, StopSourceType = StopSourceType.FallbackLookback, StopSourceTimeUtc = started, OriginalTakeProfit = 102m, CurrentTakeProfit = 102m, GrossPnl = 1m, NetPnl = 1m, NetPnlPercent = .1m });
        database.PaperSessions.Add(session); await database.SaveChangesAsync(); return session;
    }

    private async Task<PaperSessionStatus> StatusAsync(int id) { using var scope = factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.Where(item => item.Id == id).Select(item => item.Status).SingleAsync(); }
    private static async Task WaitForAsync(Func<Task<bool>> condition) { for (var attempt = 0; attempt < 50; attempt++) { if (await condition()) return; await Task.Delay(10); } throw new TimeoutException("The paper stream did not finish in time."); }
    private sealed class ThrowingStream(Exception exception) : IMarketBarStreamProvider { public Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken) => Task.FromException(exception); }
    private sealed class FixedResolver(IHistoricalMarketDataProvider provider) : IHistoricalMarketDataProviderResolver { public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => provider; }
}
