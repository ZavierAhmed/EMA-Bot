using EmaBot.Api.Data;
using EmaBot.Api.Controllers;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategyCoordinatorLifecycleTests
{
    [Fact]
    public async Task FormingAndCrossoverCandles_DoNotSubmitUntilSeparateClosedConfirmationAndExactWindow()
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync(waitForConfirmation: true);
        var signal = await harness.DeliverFirstSignalAsync(session);

        Assert.NotNull(signal);
        Assert.Empty(harness.Recorder.Submissions);
        Assert.Equal(1, await harness.CountIntentsAsync());
        Assert.Equal(DemoStrategyIntentStatus.WaitingForEntryWindow, await harness.IntentStatusAsync());

        await harness.DeliverAsync(harness.Forming(signal!.ExpectedEntryOpenUtc, 100m, 100.2m));

        var request = Assert.Single(harness.Recorder.Submissions);
        Assert.Equal("Buy", request.Side);
        Assert.Equal(89.8m, request.StopLoss);
        Assert.Equal(TradeMath.InitialTarget(100.2m, 89.8m, SignalDirection.Long, 2m), request.TakeProfit); // Ask and the session's 1:2 target.
        Assert.Equal(1, harness.Recorder.PersistedIntentObservedAtSubmit);
        Assert.Equal(1, await harness.CountIntentsAsync());
    }

    [Fact]
    public async Task BearishConfirmation_UsesBidAndSubmitsProtectedShortOnce()
    {
        await using var harness = new Harness(enabled: true, shortSetup: true);
        var session = await harness.CreateAndStartAsync(waitForConfirmation: true);
        var signal = await harness.DeliverFirstSignalAsync(session);
        Assert.NotNull(signal);
        Assert.Equal(SignalDirection.Short, signal!.Direction);
        await harness.DeliverAsync(harness.Forming(signal.ExpectedEntryOpenUtc, 99.8m, 100.2m));
        var request = Assert.Single(harness.Recorder.Submissions);
        Assert.Equal("Sell", request.Side);
        Assert.True(request.StopLoss > 99.8m);
        Assert.True(request.TakeProfit < 99.8m);
    }

    [Fact]
    public async Task SessionRiskRewardSnapshot_DrivesSubmittedTargetNotTwoR()
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync(riskReward: 3m);
        var signal = await harness.DeliverFirstSignalAsync(session);
        await harness.DeliverAsync(harness.Forming(signal!.ExpectedEntryOpenUtc, 100m, 100.2m));
        var request = Assert.Single(harness.Recorder.Submissions);
        var threeR = TradeMath.InitialTarget(100.2m, request.StopLoss!.Value, SignalDirection.Long, 3m);
        var twoR = TradeMath.InitialTarget(100.2m, request.StopLoss.Value, SignalDirection.Long, 2m);
        Assert.Equal(threeR, request.TakeProfit);
        Assert.NotEqual(twoR, request.TakeProfit);
    }

    [Theory]
    [InlineData(DemoExecutionState.Closed)]
    [InlineData(DemoExecutionState.Rejected)]
    [InlineData(DemoExecutionState.Cancelled)]
    public async Task TerminalLinkedExecution_AllowsLaterIndependentIntentAndSubmit(DemoExecutionState state)
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync();
        await harness.AddLinkedExecutionAsync(session, state, "Buy");
        var prior = (await harness.FirstIntentAsync())!;
        var signal = await harness.DeliverFirstSignalAsync(session);
        Assert.NotEqual(prior.ClientExecutionId, signal!.ClientExecutionId);
        await harness.DeliverAsync(harness.Forming(signal.ExpectedEntryOpenUtc, 100m, 100.2m));
        Assert.Single(harness.Recorder.Submissions);
        Assert.Equal(state, await harness.ExecutionStateAsync(prior.ClientExecutionId));
    }

    [Fact]
    public async Task PriorStoppedSessionOpenExecution_BlocksNewSessionForSameBrokerSymbol()
    {
        await using var harness = new Harness(enabled: true);
        var prior = await harness.CreateSessionAsync(DemoStrategySessionStatus.Stopped);
        await harness.AddLinkedExecutionAsync(prior, DemoExecutionState.Open, "Buy");
        var session = await harness.CreateAndStartAsync();

        var intent = await harness.DeliverFirstSignalAsync(session);

        Assert.Equal(DemoStrategyIntentStatus.Blocked, intent!.Status);
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Theory]
    [InlineData(DemoExecutionState.Closed)]
    [InlineData(DemoExecutionState.Rejected)]
    [InlineData(DemoExecutionState.Cancelled)]
    public async Task PriorStoppedSessionTerminalExecution_DoesNotBlockNewSession(DemoExecutionState state)
    {
        await using var harness = new Harness(enabled: true);
        var prior = await harness.CreateSessionAsync(DemoStrategySessionStatus.Stopped);
        await harness.AddLinkedExecutionAsync(prior, state, "Buy");
        var session = await harness.CreateAndStartAsync();
        var intent = await harness.DeliverFirstSignalAsync(session);

        Assert.Equal(DemoStrategyIntentStatus.WaitingForEntryWindow, intent!.Status);
        await harness.DeliverAsync(harness.Forming(intent.ExpectedEntryOpenUtc, 100m, 100.2m));
        Assert.Single(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task ManualUnlinkedOpenExecution_BlocksNewStrategyExposureForSameBrokerSymbol()
    {
        await using var harness = new Harness(enabled: true);
        await harness.AddExistingExecutionAsync(Guid.NewGuid(), DemoExecutionState.Open);
        var session = await harness.CreateAndStartAsync();

        var intent = await harness.DeliverFirstSignalAsync(session);

        Assert.Equal(DemoStrategyIntentStatus.Blocked, intent!.Status);
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task OppositeShortSignalWhileOpen_IsDeferredWithoutSubmitOrClose()
    {
        await using var harness = new Harness(enabled: true, shortSetup: true);
        var session = await harness.CreateAndStartAsync();
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        var deferred = await harness.DeliverFirstSignalAsync(session);
        Assert.Equal(DemoStrategyIntentStatus.Blocked, deferred!.Status);
        Assert.Contains("OppositeSignalDeferred", deferred.Reason);
        Assert.Empty(harness.Recorder.Submissions);
        Assert.Equal(0, harness.Recorder.CloseCalls);
    }

    [Fact]
    public async Task BrokerSideStopOrTargetClosure_ReconcilesThenAllowsLaterExactWindowEntry()
    {
        await using var harness = new Harness(enabled: true) { Recorder = { ReconcileToClosed = true } };
        var session = await harness.CreateAndStartAsync();
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        var old = (await harness.FirstIntentAsync())!;
        var signal = await harness.DeliverFirstSignalAsync(session);
        Assert.Equal(1, harness.Recorder.ReconcileCalls);
        Assert.NotEqual(old.ClientExecutionId, signal!.ClientExecutionId);
        Assert.Equal(DemoStrategyIntentStatus.WaitingForEntryWindow, signal.Status);
        Assert.Empty(harness.Recorder.Submissions);
        await harness.DeliverAsync(harness.Forming(signal.ExpectedEntryOpenUtc, 100m, 100.2m));
        Assert.Single(harness.Recorder.Submissions);
        Assert.Equal(DemoExecutionState.Closed, await harness.ExecutionStateAsync(old.ClientExecutionId));
        Assert.Equal(0, harness.Recorder.CloseCalls);
    }

    [Fact]
    public async Task ExposureAppearingAfterSignal_BlocksAtExactEntryWindowWithoutSubmit()
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync();
        var intent = await harness.DeliverFirstSignalAsync(session);
        await harness.AddExistingExecutionAsync(Guid.NewGuid(), DemoExecutionState.Open);

        await harness.DeliverAsync(harness.Forming(intent!.ExpectedEntryOpenUtc, 100m, 100.2m));

        Assert.Equal(DemoStrategyIntentStatus.Blocked, await harness.IntentStatusAsync());
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task ExposureReconciledClosedAtExactEntryWindow_SubmitsOnce()
    {
        await using var harness = new Harness(enabled: true) { Recorder = { ReconcileToClosed = true } };
        var session = await harness.CreateAndStartAsync();
        var intent = await harness.DeliverFirstSignalAsync(session);
        await harness.AddExistingExecutionAsync(Guid.NewGuid(), DemoExecutionState.Open);

        await harness.DeliverAsync(harness.Forming(intent!.ExpectedEntryOpenUtc, 100m, 100.2m));

        Assert.Single(harness.Recorder.Submissions);
        Assert.Equal(DemoStrategyIntentStatus.ExecutionLinked, await harness.IntentStatusAsync());
    }

    [Fact]
    public async Task StreamGapResync_DoesNotCreateHistoricalIntent()
    {
        await using var harness = new Harness(enabled: true);
        await harness.CreateAndStartAsync();
        var historicalSignal = harness.LiveCandleAt(111);
        await harness.DeliverAsync(harness.Closed(historicalSignal));
        Assert.Equal(0, await harness.CountIntentsAsync());
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public void AutomationController_HasOnlySessionLifecycleAndObservationActions()
    {
        var actions = typeof(DemoStrategySessionsController).GetMethods().Where(method => method.DeclaringType == typeof(DemoStrategySessionsController)).Select(method => method.Name).ToArray();
        Assert.All(actions, action => Assert.DoesNotContain(action, new[] { "ForceTrade", "BuyNow", "SellNow", "TestSignal", "InjectSignal", "Submit" }));
        Assert.Contains("Create", actions); Assert.Contains("Start", actions); Assert.Contains("Stop", actions); Assert.Contains("Resume", actions); Assert.Contains("Runtime", actions);
    }

    [Fact]
    public void AutomationUsesMinimalExecutionInterface_NotDirectBrokerBridge()
    {
        var dependencies = typeof(DemoStrategyCoordinator).GetConstructors().SelectMany(item => item.GetParameters()).Select(item => item.ParameterType).ToArray();
        Assert.DoesNotContain(typeof(EmaBot.Api.Mt5Bridge.IMt5ExecutionBridgeClient), dependencies);
        Assert.Contains(typeof(IDemoExecutionService), typeof(DemoExecutionService).GetInterfaces());
    }

    [Fact]
    public async Task DuplicateClosedCandleAndNearConcurrentEntryUpdates_SubmitAtMostOnce()
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync(waitForConfirmation: true);
        var signal = await harness.DeliverFirstSignalAsync(session);
        await Task.WhenAll(
            harness.DeliverAsync(harness.Closed(signal!.ExpectedEntryOpenUtc.AddMinutes(3), 100m)),
            harness.DeliverAsync(harness.Forming(signal.ExpectedEntryOpenUtc, 100m, 100.2m)),
            harness.DeliverAsync(harness.Forming(signal.ExpectedEntryOpenUtc, 100m, 100.2m)));

        Assert.Equal(1, await harness.CountIntentsAsync());
        Assert.Single(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task MissedWindowAndDisabledOrUnreadableGate_NeverLateSubmit()
    {
        await using var missed = new Harness(enabled: true);
        var session = await missed.CreateAndStartAsync();
        await missed.AddPendingAsync(session, expected: missed.Start.AddMinutes(3));
        await missed.DeliverAsync(missed.Forming(missed.Start.AddMinutes(6), 100m, 100.2m));
        Assert.Equal(DemoStrategyIntentStatus.Expired, await missed.IntentStatusAsync());
        await missed.DeliverAsync(missed.Forming(missed.Start.AddMinutes(9), 100m, 100.2m));
        Assert.Empty(missed.Recorder.Submissions);

        await using var disabled = new Harness(enabled: false);
        session = await disabled.CreateAndStartAsync();
        await disabled.AddPendingAsync(session, expected: disabled.Start.AddMinutes(3));
        await disabled.DeliverAsync(disabled.Forming(disabled.Start.AddMinutes(3), 100m, 100.2m));
        Assert.Equal(DemoStrategyIntentStatus.Blocked, await disabled.IntentStatusAsync());
        Assert.Empty(disabled.Recorder.Submissions);

        await using var unreadable = new Harness(enabled: true) { Recorder = { Ready = false } };
        session = await unreadable.CreateAndStartAsync();
        await unreadable.AddPendingAsync(session, expected: unreadable.Start.AddMinutes(3));
        await unreadable.DeliverAsync(unreadable.Forming(unreadable.Start.AddMinutes(3), 100m, 100.2m));
        unreadable.Recorder.Ready = true;
        await unreadable.DeliverAsync(unreadable.Forming(unreadable.Start.AddMinutes(6), 100m, 100.2m));
        Assert.Equal(DemoStrategyIntentStatus.Blocked, await unreadable.IntentStatusAsync());
        Assert.Empty(unreadable.Recorder.Submissions);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.Disabled, SignalDirection.Long, false)]
    [InlineData(InstrumentTradeMode.CloseOnly, SignalDirection.Long, false)]
    [InlineData(InstrumentTradeMode.Unknown, SignalDirection.Long, false)]
    [InlineData(InstrumentTradeMode.LongOnly, SignalDirection.Short, false)]
    [InlineData(InstrumentTradeMode.ShortOnly, SignalDirection.Long, false)]
    [InlineData(InstrumentTradeMode.LongOnly, SignalDirection.Long, true)]
    [InlineData(InstrumentTradeMode.ShortOnly, SignalDirection.Short, true)]
    [InlineData(InstrumentTradeMode.Full, SignalDirection.Long, true)]
    public async Task TradeMode_AllowsOnlyPermittedStrategyDirection(InstrumentTradeMode tradeMode, SignalDirection direction, bool allowed)
    {
        await using var harness = new Harness(enabled: true, tradeMode: tradeMode);
        var session = await harness.CreateAndStartAsync();
        await harness.AddPendingAsync(session, harness.Start.AddMinutes(3), stop: direction == SignalDirection.Long ? 99m : 101m, direction: direction);

        await harness.DeliverAsync(harness.Forming(harness.Start.AddMinutes(3), 100m, 100.2m));

        Assert.Equal(allowed ? DemoStrategyIntentStatus.ExecutionLinked : DemoStrategyIntentStatus.Blocked, await harness.IntentStatusAsync());
        Assert.Equal(allowed ? 1 : 0, harness.Recorder.Submissions.Count);
    }

    [Theory]
    [InlineData(0.001, 10d, null, "below")]
    [InlineData(11d, 10d, null, "maximum")]
    [InlineData(6d, 10d, 5d, "limit")]
    [InlineData(0.015, 10d, null, "step")]
    public async Task InvalidFixedLots_RejectWithoutSubmit(double lots, double maximum, double? limit, string reason)
    {
        await using var harness = new Harness(enabled: true, spec: Harness.Specification with { VolumeMax = (decimal)maximum, VolumeLimit = limit is null ? null : (decimal)limit.Value });
        var session = await harness.CreateAndStartAsync(fixedLots: (decimal)lots);
        await harness.AddPendingAsync(session, expected: harness.Start.AddMinutes(3));
        await harness.DeliverAsync(harness.Forming(harness.Start.AddMinutes(3), 100m, 100.2m));
        Assert.Equal(DemoStrategyIntentStatus.Rejected, await harness.IntentStatusAsync());
        Assert.Contains(reason, await harness.IntentReasonAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Theory]
    [InlineData(101d, 99d, "wrong side")]
    [InlineData(99.99d, 98d, "stop-level")]
    public async Task InvalidStopOrBrokerStopsLevel_RejectWithoutSubmit(double stop, double target, string reason)
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync();
        await harness.AddPendingAsync(session, expected: harness.Start.AddMinutes(3), stop: (decimal)stop, target: (decimal)target);
        await harness.DeliverAsync(harness.Forming(harness.Start.AddMinutes(3), 100m, 100.2m));
        Assert.Equal(DemoStrategyIntentStatus.Rejected, await harness.IntentStatusAsync());
        Assert.Contains(reason, await harness.IntentReasonAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Theory]
    [InlineData(DemoExecutionState.PreflightPassed)]
    [InlineData(DemoExecutionState.Open)]
    [InlineData(DemoExecutionState.BrokerAccepted)]
    [InlineData(DemoExecutionState.PartiallyFilled)]
    [InlineData(DemoExecutionState.Submitting)]
    [InlineData(DemoExecutionState.CloseRequested)]
    [InlineData(DemoExecutionState.ReconciliationRequired)]
    public async Task UnresolvedLinkedExecution_SuppressesSameSymbolIntent(DemoExecutionState state)
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync();
        await harness.AddLinkedExecutionAsync(session, state, "Buy");
        var signal = await harness.DeliverFirstSignalAsync(session);
        Assert.NotNull(signal);
        Assert.Contains(await harness.IntentStatusesAsync(), item => item == DemoStrategyIntentStatus.Blocked);
        Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task StopAndResumeFailClosed_ExpireUnlinkedButRelinkExistingWithoutWrites()
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateAndStartAsync();
        await harness.AddPendingAsync(session, expected: harness.Start.AddMinutes(3));
        await harness.Coordinator.StopSessionAsync(session.Id, default);
        Assert.Equal(DemoStrategySessionStatus.Stopped, await harness.SessionStatusAsync(session.Id));
        Assert.Equal(DemoStrategyIntentStatus.Expired, await harness.IntentStatusAsync());
        Assert.Equal(0, harness.Recorder.CloseCalls);

        var interrupted = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted);
        var id = Guid.NewGuid();
        await harness.AddPendingAsync(interrupted, expected: harness.Start.AddMinutes(3), id: id);
        await harness.AddExistingExecutionAsync(id, DemoExecutionState.Submitting);
        await harness.Coordinator.StartSessionAsync(interrupted.Id, true, default);
        Assert.Empty(harness.Recorder.Submissions);
        Assert.Contains(await harness.IntentStatusesAsync(interrupted.Id), item => item == DemoStrategyIntentStatus.ExecutionLinked);
        await harness.Coordinator.StopSessionAsync(interrupted.Id, default);
    }

    [Fact]
    public async Task CallerCancellationAfterStart_DoesNotStopRuntimeButExplicitStopDoes()
    {
        await using var harness = new Harness(enabled: true);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Created);
        using var caller = new CancellationTokenSource();
        await harness.Coordinator.StartSessionAsync(session.Id, false, caller.Token);

        caller.Cancel();
        await Task.Delay(25);

        Assert.Equal(session.Id, harness.Coordinator.GetRuntimeSnapshot()!.SessionId);
        Assert.Equal(DemoStrategySessionStatus.Running, await harness.SessionStatusAsync(session.Id));
        await harness.Coordinator.StopSessionAsync(session.Id, default);
        Assert.Null(harness.Coordinator.GetRuntimeSnapshot());
        Assert.Equal(DemoStrategySessionStatus.Stopped, await harness.SessionStatusAsync(session.Id));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public static readonly InstrumentSpec Specification = new("MT5", "XAUUSDm", "XAUUSDm", AssetClass.Commodity, 2, .01m, 100m, .01m, 10m, .01m, "XAU", "USD", "USD", VolumeLimit: 5m, StopsLevelPoints: 10);
        public DateTimeOffset Start { get; } = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
        public Recorder Recorder { get; } = new();
        public DemoStrategyCoordinator Coordinator { get; }
        private readonly ServiceProvider provider;
        private readonly History history;
        private readonly IReadOnlyList<Candle> liveCandles;
        private readonly string databaseName = Guid.NewGuid().ToString();

        public Harness(bool enabled, InstrumentSpec? spec = null, bool shortSetup = false, InstrumentTradeMode tradeMode = InstrumentTradeMode.Full)
        {
            liveCandles = Series(Start, shortSetup);
            history = new History(liveCandles.Take(100).ToArray());
            var services = new ServiceCollection();
            services.AddDbContext<EmaBotDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddSingleton(Recorder);
            services.AddScoped<IDemoExecutionService, RecordingExecutionService>();
            provider = services.BuildServiceProvider();
            Coordinator = new DemoStrategyCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), new Resolver(history), new Stream(), new Catalog(spec ?? Specification, tradeMode), new EmaSignalEngine(), Options.Create(new DemoStrategyAutomationOptions { Enabled = enabled, FixedLots = .01m }), NullLogger<DemoStrategyCoordinator>.Instance);
        }

        public async Task<DemoStrategySession> CreateAndStartAsync(bool waitForConfirmation = true, decimal fixedLots = .01m, decimal riskReward = 2m)
        {
            var session = await CreateSessionAsync(DemoStrategySessionStatus.Created, waitForConfirmation, fixedLots, riskReward);
            await Coordinator.StartSessionAsync(session.Id, false, default);
            return session;
        }
        public async Task<DemoStrategySession> CreateSessionAsync(DemoStrategySessionStatus status, bool waitForConfirmation = true, decimal fixedLots = .01m, decimal riskReward = 2m)
        {
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var session = new DemoStrategySession { Interval = "3m", Status = status, CreatedAtUtc = Start, FixedLots = fixedLots, RiskReward = riskReward, WaitForConfirmationCandle = waitForConfirmation, Symbols = [new DemoStrategySessionSymbol { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm" }] };
            db.DemoStrategySessions.Add(session); await db.SaveChangesAsync(); return session;
        }
        public async Task<DemoStrategyIntent?> DeliverFirstSignalAsync(DemoStrategySession session)
        {
            var priorCount = await CountIntentsAsync();
            foreach (var candle in liveCandles.Skip(100))
            {
                await DeliverAsync(Closed(candle));
                if (await CountIntentsAsync() > priorCount) return await LatestIntentAsync();
            }
            return null;
        }
        public MarketBarUpdate Closed(DateTimeOffset open, decimal close) => new("XAUUSDm", "3m", open.AddMinutes(3), open, open.AddMinutes(3).AddMilliseconds(-1), close, close + .2m, close - .2m, close, 1m, true);
        public MarketBarUpdate Closed(Candle candle) => new("XAUUSDm", "3m", candle.CloseTimeUtc.AddMilliseconds(1), candle.OpenTimeUtc, candle.CloseTimeUtc, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, true);
        public MarketBarUpdate Forming(DateTimeOffset open, decimal bid, decimal ask) => new("XAUUSDm", "3m", open, open, open.AddMinutes(3).AddMilliseconds(-1), bid, bid, bid, bid, 1m, false, bid, ask);
        public Task DeliverAsync(MarketBarUpdate update) => Coordinator.ProcessUpdateForTestAsync(update);
        public Candle LiveCandleAt(int index) => liveCandles[index];
        public async Task AddPendingAsync(DemoStrategySession session, DateTimeOffset expected, decimal stop = 99m, decimal? target = null, Guid? id = null, SignalDirection direction = SignalDirection.Long)
        {
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await db.DemoStrategySessionSymbols.SingleAsync(item => item.DemoStrategySessionId == session.Id);
            db.DemoStrategyIntents.Add(new DemoStrategyIntent { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = symbol.Id, Direction = direction, CrossoverTimeUtc = expected.AddMinutes(-6), SignalTimeUtc = expected.AddMilliseconds(-1), ExpectedEntryOpenUtc = expected, SignalOpen = 100m, SignalClose = 100m, SignalGapState = GapState.Unchanged, StructuralStopLoss = stop, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = expected.AddMinutes(-3), IntendedTakeProfit = target, IntendedVolumeLots = session.FixedLots, ClientExecutionId = id ?? Guid.NewGuid(), Status = DemoStrategyIntentStatus.WaitingForEntryWindow, CreatedAtUtc = Start });
            await db.SaveChangesAsync();
        }
        public async Task AddLinkedExecutionAsync(DemoStrategySession session, DemoExecutionState state, string side)
        {
            var id = Guid.NewGuid(); await AddExistingExecutionAsync(id, state, side); await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var execution = await db.DemoExecutions.SingleAsync(item => item.ClientExecutionId == id); var symbol = await db.DemoStrategySessionSymbols.SingleAsync(item => item.DemoStrategySessionId == session.Id);
            db.DemoStrategyIntents.Add(new DemoStrategyIntent { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = symbol.Id, Direction = SignalDirection.Long, CrossoverTimeUtc = Start, SignalTimeUtc = Start.AddMilliseconds(-1), ExpectedEntryOpenUtc = Start, SignalOpen = 100m, SignalClose = 100m, SignalGapState = GapState.Unchanged, StructuralStopLoss = 99m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = Start, IntendedVolumeLots = .01m, ClientExecutionId = id, Status = DemoStrategyIntentStatus.ExecutionLinked, DemoExecutionId = execution.Id, CreatedAtUtc = Start }); await db.SaveChangesAsync();
        }
        public async Task AddExistingExecutionAsync(Guid id, DemoExecutionState state, string side = "Buy") { await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); db.DemoExecutions.Add(new DemoExecution { ClientExecutionId = id, State = state, BrokerSymbol = "XAUUSDm", Side = side, VolumeLots = .01m, CorrelationMarker = "EMA-test", CreatedAtUtc = Start }); await db.SaveChangesAsync(); }
        public async Task<int> CountIntentsAsync() { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.CountAsync(); }
        public async Task<DemoStrategyIntent?> FirstIntentAsync(int? sessionId = null) { await using var scope = provider.CreateAsyncScope(); var query = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.AsNoTracking(); if (sessionId is not null) query = query.Where(item => item.DemoStrategySessionId == sessionId); return await query.OrderBy(item => item.Id).FirstOrDefaultAsync(); }
        public async Task<DemoStrategyIntent?> LatestIntentAsync() { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.AsNoTracking().OrderByDescending(item => item.Id).FirstOrDefaultAsync(); }
        public async Task<DemoStrategyIntentStatus> IntentStatusAsync() => (await FirstIntentAsync())!.Status;
        public async Task<string?> IntentReasonAsync() => (await FirstIntentAsync())!.Reason;
        public async Task<IReadOnlyList<DemoStrategyIntentStatus>> IntentStatusesAsync(int? sessionId = null) { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.Where(item => sessionId == null || item.DemoStrategySessionId == sessionId).Select(item => item.Status).ToListAsync(); }
        public async Task<DemoStrategySessionStatus> SessionStatusAsync(int id) { await using var scope = provider.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategySessions.SingleAsync(item => item.Id == id)).Status; }
        public async Task<DemoExecutionState> ExecutionStateAsync(Guid id) { await using var scope = provider.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoExecutions.SingleAsync(item => item.ClientExecutionId == id)).State; }
        public async ValueTask DisposeAsync() { await Coordinator.StopAsync(default); await provider.DisposeAsync(); }

        private static IReadOnlyList<Candle> Series(DateTimeOffset start, bool shortSetup)
        {
            return Enumerable.Range(0, 125).Select(index => { var close = index < 100 ? 100m : index < 110 ? (shortSetup ? 110m : 90m) : (shortSetup ? 90m : 110m); var open = start.AddMinutes(index * 3); var candleOpen = index >= 110 ? 100m : close; return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), candleOpen, close + .2m, close - .2m, close, 1m, true); }).ToArray();
        }
    }

    private sealed class Recorder { public bool Ready { get; set; } = true; public bool ReconcileToClosed { get; set; } public List<SubmitDemoOrder> Submissions { get; } = []; public int PersistedIntentObservedAtSubmit { get; set; } public int CloseCalls { get; set; } public int ReconcileCalls { get; set; } }
    private sealed class RecordingExecutionService(EmaBotDbContext database, Recorder recorder) : IDemoExecutionService
    {
        public Task<DemoExecutionReadiness> ReadinessAsync(CancellationToken token) => Task.FromResult(new DemoExecutionReadiness(recorder.Ready, recorder.Ready ? "ready" : "not ready"));
        public async Task<DemoExecution> SubmitAsync(SubmitDemoOrder request, CancellationToken token) { recorder.PersistedIntentObservedAtSubmit = await database.DemoStrategyIntents.CountAsync(item => item.ClientExecutionId == request.ClientExecutionId, token); recorder.Submissions.Add(request); var execution = new DemoExecution { ClientExecutionId = request.ClientExecutionId, State = DemoExecutionState.Open, BrokerSymbol = request.BrokerSymbol, Side = request.Side, VolumeLots = request.VolumeLots, RequestedStopLoss = request.StopLoss, RequestedTakeProfit = request.TakeProfit, CorrelationMarker = "EMA-test", CreatedAtUtc = DateTimeOffset.UtcNow }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync(token); return execution; }
        public async Task<DemoExecution?> ReconcileAsync(Guid id, CancellationToken token) { recorder.ReconcileCalls++; var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token); if (execution is not null && recorder.ReconcileToClosed) { execution.State = DemoExecutionState.Closed; execution.ExitDealTicket = 9001; execution.EntryDealTicket ??= 8001; execution.PositionIdentifier ??= 7001; execution.FilledVolumeLots ??= execution.VolumeLots; execution.ClosedVolumeLots = execution.FilledVolumeLots; await database.SaveChangesAsync(token); } return execution; }
        public Task<DemoExecution?> CloseAsync(Guid id, CancellationToken token) { recorder.CloseCalls++; return database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token); }
        public Task<DemoExecution?> GetAsync(Guid id, CancellationToken token) => database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
    }
    private sealed class History(IReadOnlyList<Candle> candles) : IHistoricalMarketDataProvider { public IReadOnlyList<Candle> Candles { get; } = candles; public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string timeframe, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) => Task.FromResult(Candles); public Task<IReadOnlyList<Candle>> GetLatestAsync(string symbol, string timeframe, int count, CancellationToken token) => Task.FromResult<IReadOnlyList<Candle>>(Candles.Take(count).ToArray()); }
    private sealed class Resolver(History history) : IHistoricalMarketDataProviderResolver { public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => history; }
    private sealed class Stream : IMarketBarStreamProvider { public async Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> update, Action<string>? state, CancellationToken token) { state?.Invoke("Connected"); await Task.Delay(Timeout.InfiniteTimeSpan, token); } }
    private sealed class Catalog(InstrumentSpec spec, InstrumentTradeMode tradeMode) : IInstrumentCatalogProvider { public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<InstrumentCatalogItem>>([]); public Task<InstrumentCatalogItem?> GetAsync(string symbol, CancellationToken token) => Task.FromResult<InstrumentCatalogItem?>(new InstrumentCatalogItem(spec, null, null, true, true, tradeMode)); }
}
