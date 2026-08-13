using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmaBot.Api.Tests;

public sealed class Mt5PaperQuoteSideTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory factory;

    public Mt5PaperQuoteSideTests(EmaBotApiFactory factory) => this.factory = factory;

    [Fact]
    public void EntryAndExitExecutablePrices_UseOppositeQuoteSidesAndRejectMissingQuotes()
    {
        Assert.Equal(100.2m, PaperTradingCoordinator.EntryExecutablePrice(SignalDirection.Long, 100m, 100.2m));
        Assert.Equal(100m, PaperTradingCoordinator.EntryExecutablePrice(SignalDirection.Short, 100m, 100.2m));
        Assert.Equal(100m, PaperTradingCoordinator.ExitExecutablePrice(SignalDirection.Long, 100m, 100.2m));
        Assert.Equal(100.2m, PaperTradingCoordinator.ExitExecutablePrice(SignalDirection.Short, 100m, 100.2m));
        Assert.Null(PaperTradingCoordinator.EntryExecutablePrice(SignalDirection.Long, 100m, 0m));
        Assert.Null(PaperTradingCoordinator.ExitExecutablePrice(SignalDirection.Short, 100m, 0m));
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public async Task PendingEntry_UsesEntryExecutableQuoteAndCalculatorRequest(SignalDirection direction)
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(1).AddMilliseconds(-1);
        var session = await CreateSessionAsync(direction, pendingSignal: signal);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        const decimal bid = 100m;
        const decimal ask = 100.2m;
        var expectedEntry = direction == SignalDirection.Long ? ask : bid;

        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), bid, ask));

        var trade = coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade!;
        Assert.Equal(expectedEntry, trade.EntryPrice);
        Assert.Equal(.06m, trade.RoundTripCommission);
        Assert.Equal("XAUUSDm", calculator.MarginRequests.Single().BrokerSymbol);
        var targetRequest = calculator.ProfitRequests.Single();
        Assert.Equal(direction.ToString(), targetRequest.Direction);
        Assert.Equal(expectedEntry, targetRequest.OpenPrice);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public async Task InitialStopExit_UsesExitExecutableQuote(SignalDirection direction)
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var session = await CreateSessionAsync(direction, openTrade: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        var bid = direction == SignalDirection.Long ? 98.9m : 101.1m;
        var ask = direction == SignalDirection.Long ? 99.1m : 101.3m;
        var expectedExit = direction == SignalDirection.Long ? bid : ask;

        await coordinator.ProcessUpdateForTestAsync(Update(DateTimeOffset.UnixEpoch.AddHours(2), bid, ask));

        using var scope = factory.Services.CreateScope();
        var trade = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperTrades.SingleAsync(item => item.PaperSessionId == session.Id);
        Assert.Equal(PaperExitReason.InitialStopLoss, trade.ExitReason);
        Assert.Equal(expectedExit, trade.ExitPrice);
        Assert.Equal(expectedExit, calculator.ProfitRequests.Single().ClosePrice);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public async Task TakeProfitExit_UsesExitExecutableQuoteAndObservedCloseInCalculator(SignalDirection direction)
    {
        var bid = direction == SignalDirection.Long ? 102.7m : 97.7m;
        var ask = direction == SignalDirection.Long ? 102.9m : 97.9m;
        var expectedExit = direction == SignalDirection.Long ? bid : ask;
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var session = await CreateSessionAsync(direction, openTrade: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        await coordinator.ProcessUpdateForTestAsync(Update(DateTimeOffset.UnixEpoch.AddHours(2), bid, ask));

        using var scope = factory.Services.CreateScope();
        var trade = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperTrades.SingleAsync(item => item.PaperSessionId == session.Id);
        Assert.Equal(PaperTradeStatus.Closed, trade.Status);
        Assert.Equal(PaperExitReason.TakeProfit, trade.ExitReason);
        Assert.Equal(expectedExit, trade.ExitPrice);
        Assert.Equal(expectedExit, calculator.ProfitRequests.Last().ClosePrice);
        Assert.Equal("XAUUSDm", calculator.ProfitRequests.Last().BrokerSymbol);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ClosedMt5Bar_DoesNotManageOpenTradeUntilLiveQuoteArrives()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var session = await CreateSessionAsync(SignalDirection.Long, openTrade: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        var time = DateTimeOffset.UnixEpoch.AddHours(3);

        await coordinator.ProcessUpdateForTestAsync(Update(time, 102.7m, 102.9m, closed: true));
        Assert.NotNull(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        await coordinator.ProcessUpdateForTestAsync(Update(time.AddSeconds(1), 102.7m, 102.9m));

        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SessionStop_ClosesLongAtBidRatherThanAsk()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var session = await CreateSessionAsync(SignalDirection.Long, openTrade: true);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);
        await coordinator.ProcessUpdateForTestAsync(Update(DateTimeOffset.UnixEpoch.AddHours(4), 101m, 101.2m));

        await coordinator.StopSessionAsync(session.Id, CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var trade = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperTrades.SingleAsync(item => item.PaperSessionId == session.Id);
        Assert.Equal(PaperExitReason.SessionStopped, trade.ExitReason);
        Assert.Equal(101m, trade.ExitPrice);
        Assert.Equal(101m, calculator.ProfitRequests.Last().ClosePrice);
    }

    [Fact]
    public async Task ConfirmedZeroCommission_AllowsEntryAndPersistsNoTradingCost()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(5).AddMilliseconds(-1);
        var session = await CreateSessionAsync(SignalDirection.Long, pendingSignal: signal);
        await ConfigureAsync(session.Id, (_, symbol) => symbol.CommissionPerLotPerSide = 0m);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));

        var trade = coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade!;
        Assert.Equal(0m, trade.RoundTripCommission);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MissingCommission_FaultsInsteadOfTreatingItAsZero()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(6).AddMilliseconds(-1);
        var session = await CreateSessionAsync(SignalDirection.Long, pendingSignal: signal);
        await ConfigureAsync(session.Id, (_, symbol) => symbol.CommissionPerLotPerSide = null);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));

        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        Assert.Equal(PaperSessionStatus.Faulted, await SessionStatusAsync(session.Id));
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MarginPercentSizing_NormalizesVolumeDownToBrokerStep()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(7).AddMilliseconds(-1);
        var session = await CreateSessionAsync(SignalDirection.Long, pendingSignal: signal);
        await ConfigureAsync(session.Id, (value, _) => { value.PaperPositionSizingMode = PaperPositionSizingMode.MarginPercent; value.PaperMarginPerTradePercent = 10m; });
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));

        Assert.Equal(.02m, coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade!.Lots);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InsufficientMargin_RejectsEntryWithoutCreatingTrade()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(8).AddMilliseconds(-1);
        var session = await CreateSessionAsync(SignalDirection.Long, pendingSignal: signal);
        await ConfigureAsync(session.Id, (value, _) => value.CurrentBalance = 10m);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));

        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        using var scope = factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.SingleAsync(item => item.Id == session.Id);
        Assert.Equal(1, persisted.RejectedByInsufficientMargin);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DirectionAndStopsLevelGuards_RejectInvalidMt5Entries()
    {
        var calculator = new TestCalculator();
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(9).AddMilliseconds(-1);
        var blocked = await CreateSessionAsync(SignalDirection.Short, pendingSignal: signal);
        await ConfigureAsync(blocked.Id, (_, symbol) => symbol.TradeMode = InstrumentTradeMode.LongOnly);
        await coordinator.StartSessionAsync(blocked.Id, false, CancellationToken.None);
        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        await coordinator.StopAsync(CancellationToken.None);

        coordinator = CreateCoordinator(calculator);
        var invalidStop = await CreateSessionAsync(SignalDirection.Long, pendingSignal: signal);
        await ConfigureAsync(invalidStop.Id, (_, symbol) => symbol.StopsLevelPoints = 200);
        await coordinator.StartSessionAsync(invalidStop.Id, false, CancellationToken.None);
        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.SingleAsync(item => item.Id == invalidStop.Id)).InvalidStopLoss);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CalculatorFailure_FaultsSessionWithoutCreatingTrade()
    {
        var calculator = new TestCalculator { ThrowOnMargin = true };
        var coordinator = CreateCoordinator(calculator);
        var signal = DateTimeOffset.UnixEpoch.AddHours(10).AddMilliseconds(-1);
        var session = await CreateSessionAsync(SignalDirection.Long, pendingSignal: signal);
        await coordinator.StartSessionAsync(session.Id, false, CancellationToken.None);

        await coordinator.ProcessUpdateForTestAsync(Update(signal.AddMilliseconds(1), 100m, 100.2m));

        Assert.Equal(PaperSessionStatus.Faulted, await SessionStatusAsync(session.Id));
        Assert.Null(coordinator.GetRuntimeSnapshot()!.Symbols["XAUUSDm"].OpenTrade);
        await coordinator.StopAsync(CancellationToken.None);
    }

    private PaperTradingCoordinator CreateCoordinator(TestCalculator calculator) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new FixedResolver(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>()),
        factory.StreamClient,
        factory.Services.GetRequiredService<EmaSignalEngine>(),
        NullLogger<PaperTradingCoordinator>.Instance,
        calculator);

    private async Task<PaperSession> CreateSessionAsync(SignalDirection direction, DateTimeOffset? pendingSignal = null, bool openTrade = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var entry = direction == SignalDirection.Long ? 100.2m : 100m;
        var stop = direction == SignalDirection.Long ? 99m : 101m;
        var target = direction == SignalDirection.Long ? 102.6m : 98m;
        var symbol = new PaperSessionSymbol
        {
            Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm", ContractSize = 100m, VolumeMin = .01m, VolumeMax = 10m, VolumeStep = .01m,
            PointSize = .01m, TickSize = .01m, TickValueProfit = 1m, StopsLevelPoints = 0, TradeMode = InstrumentTradeMode.Full,
            CommissionPerLotPerSide = 3m,
            PendingDirection = pendingSignal.HasValue ? direction : null, PendingCrossoverTimeUtc = pendingSignal, PendingSignalTimeUtc = pendingSignal,
            PendingStopPrice = pendingSignal.HasValue ? stop : null, PendingStopSourceType = pendingSignal.HasValue ? StopSourceType.FallbackLookback : null,
            PendingStopSourceTimeUtc = pendingSignal, PendingSignalOpen = pendingSignal.HasValue ? entry : null, PendingSignalClose = pendingSignal.HasValue ? entry : null,
            PendingSignalEma9 = pendingSignal.HasValue ? entry : null, PendingSignalEma15 = pendingSignal.HasValue ? entry : null,
            PendingSignalGapState = pendingSignal.HasValue ? GapState.Expanding : null
        };
        var session = new PaperSession
        {
            MarketDataSource = MarketDataSource.Mt5Exness, Interval = "3m", Status = PaperSessionStatus.Running, CreatedAtUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = DateTimeOffset.UtcNow, RiskReward = 2m, AccountCurrency = "USD", PaperPositionSizingMode = PaperPositionSizingMode.FixedLots,
            PaperFixedLots = .01m, PaperMarginPerTradePercent = 10m, StartingBalance = 1000m, CurrentBalance = 1000m, Symbols = [symbol]
        };
        if (openTrade)
        {
            session.Trades.Add(new PaperTrade
            {
                PaperSessionSymbol = symbol, Symbol = symbol.Symbol, Interval = session.Interval, Status = PaperTradeStatus.Open, Direction = direction,
                CrossoverTimeUtc = DateTimeOffset.UnixEpoch, SignalTimeUtc = DateTimeOffset.UnixEpoch, EntryTimeUtc = DateTimeOffset.UnixEpoch,
                EntryPrice = entry, Quantity = 1m, InitialStopLoss = stop, CurrentStopLoss = stop, StopSourceType = StopSourceType.FallbackLookback,
                StopSourceTimeUtc = DateTimeOffset.UnixEpoch, OriginalTakeProfit = target, CurrentTakeProfit = target, Lots = .01m,
                RequiredMargin = 35m, MarginUsed = 35m, AccountEquityAtEntry = 1000m, RoundTripCommission = .06m
            });
            session.UsedMargin = 35m;
        }
        db.PaperSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private static MarketBarUpdate Update(DateTimeOffset time, decimal bid, decimal ask, bool closed = false)
        => new("XAUUSDm", "3m", time, time, time.AddMinutes(3).AddMilliseconds(-1), bid, ask, bid, bid, 1m, closed, bid, ask);

    private async Task ConfigureAsync(int sessionId, Action<PaperSession, PaperSessionSymbol> configure)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var session = await db.PaperSessions.Include(item => item.Symbols).SingleAsync(item => item.Id == sessionId);
        configure(session, session.Symbols.Single());
        await db.SaveChangesAsync();
    }

    private async Task<PaperSessionStatus> SessionStatusAsync(int sessionId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.Where(item => item.Id == sessionId).Select(item => item.Status).SingleAsync();
    }

    private sealed class FixedResolver(IHistoricalMarketDataProvider provider) : IHistoricalMarketDataProviderResolver
    {
        public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => provider;
    }

    private sealed class TestCalculator : IMt5TradeCalculator
    {
        public bool ThrowOnMargin { get; set; }
        public List<Mt5CalculateMarginRequest> MarginRequests { get; } = [];
        public List<Mt5CalculateProfitRequest> ProfitRequests { get; } = [];

        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken)
        {
            MarginRequests.Add(request);
            if (ThrowOnMargin) throw new MarketDataProviderException("MT5 trade calculation", MarketDataErrorKind.Unavailable, "calculator unavailable");
            return Task.FromResult(new Mt5MarginCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, 35m, "USD"));
        }

        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken)
        {
            ProfitRequests.Add(request);
            return Task.FromResult(new Mt5ProfitCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, request.ClosePrice, 10m, "USD"));
        }
    }
}
