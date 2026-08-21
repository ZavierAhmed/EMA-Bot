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

    [Fact]
    public async Task B2_CurrentSessionExactOpenExecution_AttachesAndCombines70PercentTierAndExtension()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        var modification = Assert.Single(harness.Recorder.Modifications);
        Assert.Equal(104m, modification.StopLoss); Assert.Equal(111m, modification.TakeProfit);
        var management = await harness.ManagementAsync();
        Assert.NotNull(management); Assert.Equal(DemoStrategyPositionManagementState.Active, management!.State); Assert.Equal(40m, management.HighestAppliedLockPercent); Assert.Equal(DemoStrategyTargetExtensionState.Applied, management.TakeProfitExtensionState);
    }

    [Fact]
    public async Task ManagementDisabledFromStart_DoesNotAccumulateActionableBest()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: false);
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Empty(harness.Recorder.Modifications);
        var management = await harness.ManagementAsync();
        Assert.NotNull(management); Assert.Equal(0m, management!.BestFavorableProgressPercent); Assert.Null(management.BestFavorablePrice);
    }

    [Fact]
    public async Task B2_ManualExecutionIsNeverAttachedOrModified()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        await harness.AddExistingExecutionAsync(Guid.NewGuid(), DemoExecutionState.Open);
        await harness.CreateAndStartAsync(trailingStopEnabled: true);

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Empty(harness.Recorder.Modifications); Assert.Null(await harness.ManagementAsync());
    }

    [Fact]
    public async Task WrongBrokerSymbol_LinkedExecutionNeverAttachesOrWrites()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy", brokerSymbol: "OTHERm");

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Null(await harness.ManagementAsync());
    }

    [Fact]
    public async Task MultiplePlausibleCurrentExecutions_FailsClosedWithoutWrite()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, await harness.ManagementRowsAsync());
    }

    [Fact]
    public async Task ClosedFirstExecution_SecondExecutionSameSessionSymbolGetsOwnManagementRow()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Closed, "Buy");
        await harness.AddHistoricalClosedManagementAsync(session);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 105m, 105.2m));

        Assert.Equal(2, await harness.ManagementRowsAsync()); Assert.Single(harness.Recorder.Modifications);
    }

    [Fact]
    public async Task DisabledPeriodHigherPrice_NotReplayedAfterReenable()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: false);
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 110m, 110.2m));
        harness.Automation.ManagementEnabled = true;

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1).AddMinutes(3), 105.5m, 105.7m));

        var modification = Assert.Single(harness.Recorder.Modifications);
        Assert.Equal(102m, modification.StopLoss); // only the current 55% quote created the 20% tier.
        Assert.Equal(20m, (await harness.ManagementAsync())!.HighestAppliedLockPercent);
    }

    [Fact]
    public async Task PendingActionReconciliationRejected_ClearsPendingWithoutRetryAndAllowsHigherTier()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true) { Recorder = { ModifyState = DemoExecutionManagementActionState.ReconciliationRequired, ReconciledActionState = DemoExecutionManagementActionState.Rejected } };
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 105m, 105.2m));
        var pending = (await harness.ManagementAsync())!;
        Assert.NotNull(pending.PendingProtectionActionId);

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1).AddMinutes(3), 106m, 106.2m));
        var rejected = (await harness.ManagementAsync())!;
        Assert.Null(rejected.PendingProtectionActionId); Assert.Equal(20m, rejected.HighestAttemptedLockPercent); Assert.Equal(DemoStrategyPositionManagementState.Active, rejected.State); Assert.Single(harness.Recorder.Modifications);

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1).AddMinutes(6), 106m, 106.2m));
        Assert.Equal(2, harness.Recorder.Modifications.Count); Assert.Equal(30m, (await harness.ManagementAsync())!.HighestAttemptedLockPercent);
    }

    [Fact]
    public async Task PendingOppositeClose_FirstLaterLongBid_ClosesExactlyOnce()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true) { Recorder = { CloseResultState = DemoExecutionState.Closed } };
        var session = await harness.CreateAndStartAsync(); await harness.AddPendingOppositeManagementAsync(session, "Buy");

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Equal(1, harness.Recorder.CloseCalls); Assert.True(harness.Recorder.CloseObservedPersistedRequest); Assert.Empty(harness.Recorder.Modifications);
        var management = (await harness.ManagementAsync())!; Assert.Equal(DemoStrategyPositionManagementState.Closed, management.State); Assert.Equal(DemoStrategyOppositeCloseState.Closed, management.OppositeCloseState);
    }

    [Fact]
    public async Task ConfirmedOppositeSignal_PersistsPendingButDoesNotCloseOnClosedCandle()
    {
        await using var harness = new Harness(enabled: true, shortSetup: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(exitOnOppositeCrossover: true);
        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 100m, 100.2m)); // attach management only.

        var signal = await harness.DeliverFirstSignalAsync(session);

        Assert.NotNull(signal); Assert.Equal(SignalDirection.Short, signal!.Direction); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Equal(DemoStrategyOppositeCloseState.Pending, (await harness.ManagementAsync())!.OppositeCloseState);
    }

    [Fact]
    public async Task OppositeFeatureDisabledOrGateOffAtSignal_NeverQueuesHistoricalClose()
    {
        await using var disabledFeature = new Harness(enabled: true, shortSetup: true, managementEnabled: true); var featureSession = await disabledFeature.CreateAndStartAsync(exitOnOppositeCrossover: false); await disabledFeature.AddLinkedExecutionAsync(featureSession, DemoExecutionState.Open, "Buy"); await disabledFeature.DeliverAsync(disabledFeature.Forming(disabledFeature.Start.AddHours(1), 100m, 100.2m)); await disabledFeature.DeliverFirstSignalAsync(featureSession);
        Assert.NotEqual(DemoStrategyOppositeCloseState.Pending, (await disabledFeature.ManagementAsync())!.OppositeCloseState); Assert.Equal(0, disabledFeature.Recorder.CloseCalls);

        await using var gateOff = new Harness(enabled: true, shortSetup: true, managementEnabled: false); var gateSession = await gateOff.CreateAndStartAsync(exitOnOppositeCrossover: true); await gateOff.AddLinkedExecutionAsync(gateSession, DemoExecutionState.Open, "Buy"); await gateOff.DeliverAsync(gateOff.Forming(gateOff.Start.AddHours(1), 100m, 100.2m)); await gateOff.DeliverFirstSignalAsync(gateSession); gateOff.Automation.ManagementEnabled = true; await gateOff.DeliverAsync(gateOff.Forming(gateOff.Start.AddHours(2), 107m, 107.2m));
        Assert.NotEqual(DemoStrategyOppositeCloseState.Pending, (await gateOff.ManagementAsync())!.OppositeCloseState); Assert.Equal(0, gateOff.Recorder.CloseCalls);
    }

    [Fact]
    public async Task PendingOppositeClose_FirstLaterShortAskAndMissingRequiredQuoteBehaveExactly()
    {
        await using var missing = new Harness(enabled: true, managementEnabled: true); var missingSession = await missing.CreateAndStartAsync(); await missing.AddPendingOppositeManagementAsync(missingSession, "Sell");
        await missing.DeliverAsync(missing.Live(missing.Start.AddHours(1), 100m, null));
        Assert.Equal(0, missing.Recorder.CloseCalls); Assert.Equal(DemoStrategyOppositeCloseState.Pending, (await missing.ManagementAsync())!.OppositeCloseState);

        await using var shortHarness = new Harness(enabled: true, managementEnabled: true) { Recorder = { CloseResultState = DemoExecutionState.Closed } }; var session = await shortHarness.CreateAndStartAsync(); await shortHarness.AddPendingOppositeManagementAsync(session, "Sell");
        await shortHarness.DeliverAsync(shortHarness.Live(shortHarness.Start.AddHours(1), null, 93m));
        Assert.Equal(1, shortHarness.Recorder.CloseCalls); Assert.Equal(DemoStrategyOppositeCloseState.Closed, (await shortHarness.ManagementAsync())!.OppositeCloseState);
    }

    [Fact]
    public async Task AmbiguousOppositeClose_IsNeverRetriedAndReconciliationCanClose()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true) { Recorder = { CloseResultState = DemoExecutionState.CloseRequested } };
        var session = await harness.CreateAndStartAsync(); await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1).AddMinutes(3), 107m, 107.2m));
        Assert.Equal(1, harness.Recorder.CloseCalls); var ambiguous = (await harness.ManagementAsync())!; Assert.Equal(DemoStrategyPositionManagementState.CloseRequested, ambiguous.State); Assert.Equal(DemoStrategyOppositeCloseState.ReconciliationRequired, ambiguous.OppositeCloseState);

        harness.Recorder.ReconcileToClosed = true;
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1).AddMinutes(6), 107m, 107.2m));
        Assert.Equal(1, harness.Recorder.CloseCalls); Assert.Equal(DemoStrategyPositionManagementState.Closed, (await harness.ManagementAsync())!.State);
    }

    [Fact]
    public async Task PendingOppositeClose_TakesPrecedenceAndConcurrentUpdatesCloseOnce()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true) { Recorder = { CloseResultState = DemoExecutionState.Closed } };
        var session = await harness.CreateAndStartAsync(trailingStopEnabled: true); await harness.AddPendingOppositeManagementAsync(session, "Buy");
        var update = harness.Forming(harness.Start.AddHours(1), 107m, 107.2m);
        await Task.WhenAll(harness.DeliverAsync(update), harness.DeliverAsync(update));

        Assert.Equal(1, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Modifications);
    }

    [Fact]
    public async Task StopSession_WithPendingOppositeClose_BlocksWithoutClosing()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(); await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.Coordinator.StopSessionAsync(session.Id, default);

        Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Modifications); Assert.Equal(DemoStrategyOppositeCloseState.Blocked, (await harness.ManagementAsync())!.OppositeCloseState);
    }

    [Fact]
    public async Task ResumeReconcilesAlreadyClosedManagementWithoutWriteAndNewExecutionCanBeManaged()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted, trailingStopEnabled: true); await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.SetOnlyExecutionStateAsync(DemoExecutionState.Closed);
        await harness.Coordinator.StartSessionAsync(session.Id, true, default);
        Assert.Equal(DemoStrategyPositionManagementState.Closed, (await harness.ManagementAsync())!.State);

        await harness.AddLinkedExecutionAsync(session, DemoExecutionState.Open, "Buy");
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 105m, 105.2m));
        Assert.Equal(2, await harness.ManagementRowsAsync()); Assert.Single(harness.Recorder.Modifications);
    }

    [Fact]
    public async Task ResumeExactOpenManagement_PreservesDurableProgressAndIssuesOnlyReadsUntilNewBid()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted, trailingStopEnabled: true);
        await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.UpdateManagementAsync(item =>
        {
            item.State = DemoStrategyPositionManagementState.Active;
            item.OppositeCloseState = DemoStrategyOppositeCloseState.None;
            item.OriginalEntryPrice = 100m; item.OriginalTakeProfit = 110m;
            item.BestFavorablePrice = 105m; item.BestFavorableProgressPercent = 50m;
            item.HighestAttemptedLockPercent = 20m; item.HighestAppliedLockPercent = 20m;
            item.TakeProfitExtensionState = DemoStrategyTargetExtensionState.NotAttempted;
        });

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);

        var recovered = (await harness.ManagementAsync())!;
        Assert.Equal(DemoStrategyPositionManagementState.Active, recovered.State);
        Assert.Equal(100m, recovered.OriginalEntryPrice); Assert.Equal(110m, recovered.OriginalTakeProfit);
        Assert.Equal(50m, recovered.BestFavorableProgressPercent); Assert.Equal(20m, recovered.HighestAppliedLockPercent);
        Assert.Equal(1, harness.Recorder.ReconcileCalls); Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Submissions);

        // A downtime candle, including its high/low/close, is not a management quote.
        await harness.DeliverAsync(new MarketBarUpdate("XAUUSDm", "3m", harness.Start.AddHours(1), harness.Start.AddHours(1), harness.Start.AddHours(1).AddMinutes(3).AddMilliseconds(-1), 109m, 120m, 80m, 119m, 1m, true));
        Assert.Equal(50m, (await harness.ManagementAsync())!.BestFavorableProgressPercent);
        Assert.Empty(harness.Recorder.Modifications);

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(2), 106m, 106.2m));
        Assert.Single(harness.Recorder.Modifications);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ResumeRecovery_IsReadOnlyWhenEitherManagementGateIsOff(bool enabled, bool managementEnabled)
    {
        await using var harness = new Harness(enabled: enabled, managementEnabled: managementEnabled);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted, trailingStopEnabled: true);
        await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.UpdateManagementAsync(item => { item.State = DemoStrategyPositionManagementState.Active; item.OppositeCloseState = DemoStrategyOppositeCloseState.None; });

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Equal(DemoStrategyPositionManagementState.Active, (await harness.ManagementAsync())!.State);
        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task ResumePendingProtection_ReconcilesSameActionWithoutReplacementOrWrite()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true) { Recorder = { ReconciledActionState = DemoExecutionManagementActionState.Applied } };
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted, trailingStopEnabled: true);
        await harness.AddPendingOppositeManagementAsync(session, "Buy");
        var actionId = Guid.NewGuid();
        await harness.UpdateManagementAsync(item => { item.State = DemoStrategyPositionManagementState.ProtectionReconciliationRequired; item.OppositeCloseState = DemoStrategyOppositeCloseState.None; item.PendingProtectionActionId = actionId; item.PendingProtectionLockPercent = 40m; });

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);

        var management = (await harness.ManagementAsync())!;
        Assert.Equal(DemoStrategyPositionManagementState.Active, management.State); Assert.Null(management.PendingProtectionActionId); Assert.Equal(40m, management.HighestAppliedLockPercent);
        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task ResumeAmbiguousPendingProtection_RetainsSameActionAndRemainsFailClosed()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted, trailingStopEnabled: true);
        await harness.AddPendingOppositeManagementAsync(session, "Buy");
        var actionId = Guid.NewGuid();
        await harness.UpdateManagementAsync(item => { item.OppositeCloseState = DemoStrategyOppositeCloseState.None; item.PendingProtectionActionId = actionId; item.PendingProtectionLockPercent = 40m; });

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        var management = (await harness.ManagementAsync())!;
        Assert.Equal(DemoStrategyPositionManagementState.ProtectionReconciliationRequired, management.State); Assert.Equal(actionId, management.PendingProtectionActionId);
        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Submissions);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResumeWithoutBothPositiveNativeIdentifiers_RemainsSuspendedWithoutWrite(bool missingTicket, bool missingIdentifier)
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted);
        await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.UpdateManagementAsync(item => { item.State = DemoStrategyPositionManagementState.Active; item.OppositeCloseState = DemoStrategyOppositeCloseState.None; });
        await harness.UpdateExecutionAsync(item => { if (missingTicket) item.PositionTicket = null; if (missingIdentifier) item.PositionIdentifier = null; });

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);

        Assert.Equal(DemoStrategyPositionManagementState.SuspendedAfterRestart, (await harness.ManagementAsync())!.State);
        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Submissions);
    }

    [Fact]
    public async Task ResumePendingOppositeClose_PreservesDirectiveAndFirstNewShortAskClosesOnce()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true) { Recorder = { CloseResultState = DemoExecutionState.Closed } };
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted);
        await harness.AddPendingOppositeManagementAsync(session, "Sell");

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);
        Assert.Equal(DemoStrategyOppositeCloseState.Pending, (await harness.ManagementAsync())!.OppositeCloseState); Assert.Equal(0, harness.Recorder.CloseCalls);

        await harness.DeliverAsync(harness.Live(harness.Start.AddHours(1), null, 93m));
        Assert.Equal(1, harness.Recorder.CloseCalls); Assert.Equal(DemoStrategyPositionManagementState.Closed, (await harness.ManagementAsync())!.State);
    }

    [Theory]
    [InlineData(DemoStrategyOppositeCloseState.CloseRequested)]
    [InlineData(DemoStrategyOppositeCloseState.ReconciliationRequired)]
    public async Task ResumePreviouslyAttemptedOppositeClose_ReconcilesOnlyAndNeverResubmits(DemoStrategyOppositeCloseState closeState)
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateSessionAsync(DemoStrategySessionStatus.Interrupted);
        await harness.AddPendingOppositeManagementAsync(session, "Buy");
        await harness.UpdateManagementAsync(item => { item.State = DemoStrategyPositionManagementState.CloseRequested; item.OppositeCloseState = closeState; });

        await harness.Coordinator.StartSessionAsync(session.Id, true, default);
        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Empty(harness.Recorder.Modifications); Assert.Equal(DemoStrategyPositionManagementState.CloseRequested, (await harness.ManagementAsync())!.State);
    }

    [Fact]
    public async Task AlreadyReconciledClosedExecution_MarksExistingManagementClosedWithoutWrite()
    {
        await using var harness = new Harness(enabled: true, managementEnabled: true);
        var session = await harness.CreateAndStartAsync(); await harness.AddPendingOppositeManagementAsync(session, "Buy"); await harness.SetOnlyExecutionStateAsync(DemoExecutionState.Closed);

        await harness.DeliverAsync(harness.Forming(harness.Start.AddHours(1), 107m, 107.2m));

        Assert.Empty(harness.Recorder.Modifications); Assert.Equal(0, harness.Recorder.CloseCalls); Assert.Equal(DemoStrategyPositionManagementState.Closed, (await harness.ManagementAsync())!.State);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public static readonly InstrumentSpec Specification = new("MT5", "XAUUSDm", "XAUUSDm", AssetClass.Commodity, 2, .01m, 100m, .01m, 10m, .01m, "XAU", "USD", "USD", VolumeLimit: 5m, StopsLevelPoints: 10);
        public DateTimeOffset Start { get; } = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
        public Recorder Recorder { get; } = new();
        public DemoStrategyAutomationOptions Automation { get; }
        public DemoStrategyCoordinator Coordinator { get; }
        private readonly ServiceProvider provider;
        private readonly History history;
        private readonly IReadOnlyList<Candle> liveCandles;
        private readonly string databaseName = Guid.NewGuid().ToString();

        public Harness(bool enabled, InstrumentSpec? spec = null, bool shortSetup = false, InstrumentTradeMode tradeMode = InstrumentTradeMode.Full, bool managementEnabled = false)
        {
            liveCandles = Series(Start, shortSetup);
            history = new History(liveCandles.Take(100).ToArray());
            var services = new ServiceCollection();
            services.AddDbContext<EmaBotDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddSingleton(Recorder);
            services.AddScoped<IDemoExecutionService, RecordingExecutionService>();
            provider = services.BuildServiceProvider();
            Automation = new DemoStrategyAutomationOptions { Enabled = enabled, ManagementEnabled = managementEnabled, FixedLots = .01m };
            Coordinator = new DemoStrategyCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), new Resolver(history), new Stream(), new Catalog(spec ?? Specification, tradeMode), new EmaSignalEngine(), Options.Create(Automation), NullLogger<DemoStrategyCoordinator>.Instance);
        }

        public async Task<DemoStrategySession> CreateAndStartAsync(bool waitForConfirmation = true, decimal fixedLots = .01m, decimal riskReward = 2m, bool trailingStopEnabled = false, bool exitOnOppositeCrossover = false)
        {
            var session = await CreateSessionAsync(DemoStrategySessionStatus.Created, waitForConfirmation, fixedLots, riskReward, trailingStopEnabled, exitOnOppositeCrossover);
            await Coordinator.StartSessionAsync(session.Id, false, default);
            return session;
        }
        public async Task<DemoStrategySession> CreateSessionAsync(DemoStrategySessionStatus status, bool waitForConfirmation = true, decimal fixedLots = .01m, decimal riskReward = 2m, bool trailingStopEnabled = false, bool exitOnOppositeCrossover = false)
        {
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var session = new DemoStrategySession { Interval = "3m", Status = status, CreatedAtUtc = Start, FixedLots = fixedLots, RiskReward = riskReward, WaitForConfirmationCandle = waitForConfirmation, TrailingStopEnabled = trailingStopEnabled, ExitOnOppositeCrossover = exitOnOppositeCrossover, Symbols = [new DemoStrategySessionSymbol { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm" }] };
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
        public MarketBarUpdate Live(DateTimeOffset open, decimal? bid, decimal? ask) => new("XAUUSDm", "3m", open, open, open.AddMinutes(3).AddMilliseconds(-1), bid ?? ask ?? 100m, bid ?? ask ?? 100m, bid ?? ask ?? 100m, bid ?? ask ?? 100m, 1m, false, bid, ask);
        public Task DeliverAsync(MarketBarUpdate update) => Coordinator.ProcessUpdateForTestAsync(update);
        public Candle LiveCandleAt(int index) => liveCandles[index];
        public async Task AddPendingAsync(DemoStrategySession session, DateTimeOffset expected, decimal stop = 99m, decimal? target = null, Guid? id = null, SignalDirection direction = SignalDirection.Long)
        {
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await db.DemoStrategySessionSymbols.SingleAsync(item => item.DemoStrategySessionId == session.Id);
            db.DemoStrategyIntents.Add(new DemoStrategyIntent { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = symbol.Id, Direction = direction, CrossoverTimeUtc = expected.AddMinutes(-6), SignalTimeUtc = expected.AddMilliseconds(-1), ExpectedEntryOpenUtc = expected, SignalOpen = 100m, SignalClose = 100m, SignalGapState = GapState.Unchanged, StructuralStopLoss = stop, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = expected.AddMinutes(-3), IntendedTakeProfit = target, IntendedVolumeLots = session.FixedLots, ClientExecutionId = id ?? Guid.NewGuid(), Status = DemoStrategyIntentStatus.WaitingForEntryWindow, CreatedAtUtc = Start });
            await db.SaveChangesAsync();
        }
        public async Task AddLinkedExecutionAsync(DemoStrategySession session, DemoExecutionState state, string side, string brokerSymbol = "XAUUSDm")
        {
            var id = Guid.NewGuid(); await AddExistingExecutionAsync(id, state, side, brokerSymbol); await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var execution = await db.DemoExecutions.SingleAsync(item => item.ClientExecutionId == id); var symbol = await db.DemoStrategySessionSymbols.SingleAsync(item => item.DemoStrategySessionId == session.Id);
            db.DemoStrategyIntents.Add(new DemoStrategyIntent { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = symbol.Id, Direction = SignalDirection.Long, CrossoverTimeUtc = Start, SignalTimeUtc = Start.AddMilliseconds(-1), ExpectedEntryOpenUtc = Start, SignalOpen = 100m, SignalClose = 100m, SignalGapState = GapState.Unchanged, StructuralStopLoss = 99m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = Start, IntendedVolumeLots = .01m, ClientExecutionId = id, Status = DemoStrategyIntentStatus.ExecutionLinked, DemoExecutionId = execution.Id, CreatedAtUtc = Start }); await db.SaveChangesAsync();
        }
        public async Task AddHistoricalClosedManagementAsync(DemoStrategySession session)
        {
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var intent = await db.DemoStrategyIntents.Include(item => item.DemoExecution).SingleAsync(item => item.DemoStrategySessionId == session.Id && item.DemoExecution!.State == DemoExecutionState.Closed);
            db.DemoStrategyPositionManagement.Add(new DemoStrategyPositionManagement { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = intent.DemoStrategySessionSymbolId, DemoStrategyIntentId = intent.Id, DemoExecutionId = intent.DemoExecutionId!.Value, State = DemoStrategyPositionManagementState.Closed, OriginalEntryPrice = 100m, OriginalStopLoss = 90m, OriginalTakeProfit = 110m, TakeProfitExtensionState = DemoStrategyTargetExtensionState.NotAttempted, OppositeCloseState = DemoStrategyOppositeCloseState.Closed, CreatedAtUtc = Start, UpdatedAtUtc = Start });
            await db.SaveChangesAsync();
        }
        public async Task AddPendingOppositeManagementAsync(DemoStrategySession session, string side)
        {
            await AddLinkedExecutionAsync(session, DemoExecutionState.Open, side);
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var intent = await db.DemoStrategyIntents.Include(item => item.DemoExecution).SingleAsync(item => item.DemoStrategySessionId == session.Id && item.DemoExecution!.State == DemoExecutionState.Open);
            db.DemoStrategyPositionManagement.Add(new DemoStrategyPositionManagement { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = intent.DemoStrategySessionSymbolId, DemoStrategyIntentId = intent.Id, DemoExecutionId = intent.DemoExecutionId!.Value, State = DemoStrategyPositionManagementState.ClosePending, OriginalEntryPrice = 100m, OriginalStopLoss = side == "Buy" ? 90m : 110m, OriginalTakeProfit = side == "Buy" ? 110m : 90m, TakeProfitExtensionState = DemoStrategyTargetExtensionState.NotAttempted, OppositeCloseState = DemoStrategyOppositeCloseState.Pending, OppositeSignalDirection = side == "Buy" ? SignalDirection.Short : SignalDirection.Long, OppositeSignalTimeUtc = Start, CreatedAtUtc = Start, UpdatedAtUtc = Start });
            await db.SaveChangesAsync();
        }
        public async Task AddExistingExecutionAsync(Guid id, DemoExecutionState state, string side = "Buy", string brokerSymbol = "XAUUSDm") { await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); db.DemoExecutions.Add(new DemoExecution { ClientExecutionId = id, State = state, BrokerSymbol = brokerSymbol, Side = side, VolumeLots = .01m, RequestedStopLoss = side == "Buy" ? 90m : 110m, RequestedTakeProfit = side == "Buy" ? 110m : 90m, CurrentStopLoss = side == "Buy" ? 90m : 110m, CurrentTakeProfit = side == "Buy" ? 110m : 90m, AverageFillPrice = 100m, PositionTicket = 300, PositionIdentifier = 400, CorrelationMarker = "EMA-test", CreatedAtUtc = Start }); await db.SaveChangesAsync(); }
        public async Task<int> CountIntentsAsync() { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.CountAsync(); }
        public async Task<DemoStrategyIntent?> FirstIntentAsync(int? sessionId = null) { await using var scope = provider.CreateAsyncScope(); var query = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.AsNoTracking(); if (sessionId is not null) query = query.Where(item => item.DemoStrategySessionId == sessionId); return await query.OrderBy(item => item.Id).FirstOrDefaultAsync(); }
        public async Task<DemoStrategyIntent?> LatestIntentAsync() { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.AsNoTracking().OrderByDescending(item => item.Id).FirstOrDefaultAsync(); }
        public async Task<DemoStrategyIntentStatus> IntentStatusAsync() => (await FirstIntentAsync())!.Status;
        public async Task<string?> IntentReasonAsync() => (await FirstIntentAsync())!.Reason;
        public async Task<IReadOnlyList<DemoStrategyIntentStatus>> IntentStatusesAsync(int? sessionId = null) { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyIntents.Where(item => sessionId == null || item.DemoStrategySessionId == sessionId).Select(item => item.Status).ToListAsync(); }
        public async Task<DemoStrategySessionStatus> SessionStatusAsync(int id) { await using var scope = provider.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategySessions.SingleAsync(item => item.Id == id)).Status; }
        public async Task<DemoExecutionState> ExecutionStateAsync(Guid id) { await using var scope = provider.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoExecutions.SingleAsync(item => item.ClientExecutionId == id)).State; }
        public async Task<DemoStrategyPositionManagement?> ManagementAsync() { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyPositionManagement.AsNoTracking().SingleOrDefaultAsync(); }
        public async Task<int> ManagementRowsAsync() { await using var scope = provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoStrategyPositionManagement.CountAsync(); }
        public async Task SetOnlyExecutionStateAsync(DemoExecutionState state) { await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var execution = await db.DemoExecutions.SingleAsync(); execution.State = state; await db.SaveChangesAsync(); }
        public async Task UpdateExecutionAsync(Action<DemoExecution> update) { await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var execution = await db.DemoExecutions.SingleAsync(); update(execution); await db.SaveChangesAsync(); }
        public async Task UpdateManagementAsync(Action<DemoStrategyPositionManagement> update) { await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var management = await db.DemoStrategyPositionManagement.SingleAsync(); update(management); await db.SaveChangesAsync(); }
        public async ValueTask DisposeAsync() { await Coordinator.StopAsync(default); await provider.DisposeAsync(); }

        private static IReadOnlyList<Candle> Series(DateTimeOffset start, bool shortSetup)
        {
            return Enumerable.Range(0, 125).Select(index => { var close = index < 100 ? 100m : index < 110 ? (shortSetup ? 110m : 90m) : (shortSetup ? 90m : 110m); var open = start.AddMinutes(index * 3); var candleOpen = index >= 110 ? 100m : close; return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), candleOpen, close + .2m, close - .2m, close, 1m, true); }).ToArray();
        }
    }

    private sealed class Recorder { public bool Ready { get; set; } = true; public bool ReconcileToClosed { get; set; } public DemoExecutionState? CloseResultState { get; set; } public bool CloseObservedPersistedRequest { get; set; } public DemoExecutionManagementActionState ModifyState { get; set; } = DemoExecutionManagementActionState.Applied; public DemoExecutionManagementActionState? ReconciledActionState { get; set; } public List<SubmitDemoOrder> Submissions { get; } = []; public List<ModifyDemoProtection> Modifications { get; } = []; public int PersistedIntentObservedAtSubmit { get; set; } public int CloseCalls { get; set; } public int ReconcileCalls { get; set; } }
    private sealed class RecordingExecutionService(EmaBotDbContext database, Recorder recorder) : IDemoExecutionService
    {
        public Task<DemoExecutionReadiness> ReadinessAsync(CancellationToken token) => Task.FromResult(new DemoExecutionReadiness(recorder.Ready, recorder.Ready ? "ready" : "not ready"));
        public async Task<DemoExecution> SubmitAsync(SubmitDemoOrder request, CancellationToken token) { recorder.PersistedIntentObservedAtSubmit = await database.DemoStrategyIntents.CountAsync(item => item.ClientExecutionId == request.ClientExecutionId, token); recorder.Submissions.Add(request); var execution = new DemoExecution { ClientExecutionId = request.ClientExecutionId, State = DemoExecutionState.Open, BrokerSymbol = request.BrokerSymbol, Side = request.Side, VolumeLots = request.VolumeLots, RequestedStopLoss = request.StopLoss, RequestedTakeProfit = request.TakeProfit, CorrelationMarker = "EMA-test", CreatedAtUtc = DateTimeOffset.UtcNow }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync(token); return execution; }
        public async Task<DemoExecution?> ReconcileAsync(Guid id, CancellationToken token) { recorder.ReconcileCalls++; var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token); if (execution is not null && recorder.ReconcileToClosed) { execution.State = DemoExecutionState.Closed; execution.ExitDealTicket = 9001; execution.EntryDealTicket ??= 8001; execution.PositionIdentifier ??= 7001; execution.FilledVolumeLots ??= execution.VolumeLots; execution.ClosedVolumeLots = execution.FilledVolumeLots; await database.SaveChangesAsync(token); } return execution; }
        public async Task<DemoExecution?> CloseAsync(Guid id, CancellationToken token) { recorder.CloseCalls++; recorder.CloseObservedPersistedRequest = await database.DemoStrategyPositionManagement.AnyAsync(item => item.OppositeCloseState == DemoStrategyOppositeCloseState.CloseRequested && item.OppositeCloseRequestedAtUtc != null, token); var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token); if (execution is not null && recorder.CloseResultState is { } state) { execution.State = state; await database.SaveChangesAsync(token); } return execution; }
        public Task<DemoExecutionManagementAction> ModifyProtectionAsync(ModifyDemoProtection request, CancellationToken token) { recorder.Modifications.Add(request); return Task.FromResult(new DemoExecutionManagementAction { ClientManagementActionId = request.ClientManagementActionId, State = recorder.ModifyState }); }
        public Task<DemoExecutionManagementAction?> ReconcileManagementActionAsync(Guid clientManagementActionId, CancellationToken token) => Task.FromResult(recorder.ReconciledActionState is { } state ? new DemoExecutionManagementAction { ClientManagementActionId = clientManagementActionId, State = state } : null);
        public Task<DemoExecutionManagementAction?> FailClosedManagementActionAsync(Guid clientManagementActionId, CancellationToken token) => Task.FromResult<DemoExecutionManagementAction?>(null);
        public Task<DemoExecution?> GetAsync(Guid id, CancellationToken token) => database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
    }
    private sealed class History(IReadOnlyList<Candle> candles) : IHistoricalMarketDataProvider { public IReadOnlyList<Candle> Candles { get; } = candles; public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string timeframe, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) => Task.FromResult(Candles); public Task<IReadOnlyList<Candle>> GetLatestAsync(string symbol, string timeframe, int count, CancellationToken token) => Task.FromResult<IReadOnlyList<Candle>>(Candles.Take(count).ToArray()); }
    private sealed class Resolver(History history) : IHistoricalMarketDataProviderResolver { public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => history; }
    private sealed class Stream : IMarketBarStreamProvider { public async Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> update, Action<string>? state, CancellationToken token) { state?.Invoke("Connected"); await Task.Delay(Timeout.InfiniteTimeSpan, token); } }
    private sealed class Catalog(InstrumentSpec spec, InstrumentTradeMode tradeMode) : IInstrumentCatalogProvider { public Task<IReadOnlyList<InstrumentCatalogItem>> GetAvailableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<InstrumentCatalogItem>>([]); public Task<InstrumentCatalogItem?> GetAsync(string symbol, CancellationToken token) => Task.FromResult<InstrumentCatalogItem?>(new InstrumentCatalogItem(spec, null, null, true, true, tradeMode)); }
}
