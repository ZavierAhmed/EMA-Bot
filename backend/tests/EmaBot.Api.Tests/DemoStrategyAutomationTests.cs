using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategyAutomationTests
{
    private static readonly InstrumentSpec Spec = new("MT5", "XAUUSDm", "XAUUSDm", AssetClass.Commodity, 2, .01m, 100m, .01m, 10m, .01m, "XAU", "USD", "USD", VolumeLimit: 5m, StopsLevelPoints: 10);

    [Fact]
    public void AutomationDefaultsFailClosedWithConservativeFixedLots()
    {
        var options = new DemoStrategyAutomationOptions();
        Assert.False(options.Enabled);
        Assert.Equal(.01m, options.FixedLots);
        Assert.True(new DemoStrategyAutomationOptionsValidator().Validate(null, new DemoStrategyAutomationOptions()).Succeeded);
    }

    [Theory]
    [InlineData(0.001, "below")]
    [InlineData(11, "exceed")]
    [InlineData(0.015, "step")]
    public void InvalidFixedLotsAreRejectedWithoutRounding(decimal lots, string expected)
    {
        var failure = DemoStrategyExecutionRules.ValidateFixedLots(Spec, lots);
        Assert.NotNull(failure);
        Assert.Contains(expected, failure!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidFixedLotsAndBrokerLimitAreAcceptedOrRejectedExactly()
    {
        Assert.Null(DemoStrategyExecutionRules.ValidateFixedLots(Spec, .01m));
        Assert.Null(DemoStrategyExecutionRules.ValidateFixedLots(Spec, 5m));
        Assert.Contains("limit", DemoStrategyExecutionRules.ValidateFixedLots(Spec, 6m)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntryUsesAskForLongAndBidForShort()
    {
        Assert.Equal(100.2m, DemoStrategyExecutionRules.EntryPrice(SignalDirection.Long, 100m, 100.2m));
        Assert.Equal(100m, DemoStrategyExecutionRules.EntryPrice(SignalDirection.Short, 100m, 100.2m));
    }

    [Fact]
    public void StopAndTargetValidationRequiresCorrectSideAndBrokerStopsLevel()
    {
        Assert.False(DemoStrategyExecutionRules.StopAndTargetMeetBrokerMinimum(Spec, SignalDirection.Long, 100.2m, 100m, 100.2m, 100.1m, 102m));
        Assert.False(DemoStrategyExecutionRules.StopAndTargetMeetBrokerMinimum(Spec, SignalDirection.Short, 100m, 100m, 100.2m, 100.1m, 98m));
        Assert.True(DemoStrategyExecutionRules.StopAndTargetMeetBrokerMinimum(Spec, SignalDirection.Long, 100.2m, 100m, 100.2m, 99.8m, 100.5m));
    }

    [Fact]
    public void TargetUsesCanonicalRiskRewardMath()
    {
        var entry = DemoStrategyExecutionRules.EntryPrice(SignalDirection.Long, 100m, 100.2m)!.Value;
        Assert.Equal(100.6m, TradeMath.InitialTarget(entry, 100m, SignalDirection.Long, 2m));
    }

    [Fact]
    public void ModelHasDurableClientIdAndIdempotencyIndexes()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var intent = database.Model.FindEntityType(typeof(DemoStrategyIntent));
        Assert.NotNull(intent);
        Assert.Contains(intent!.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(DemoStrategyIntent.ClientExecutionId)]));
        Assert.Contains(intent.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(DemoStrategyIntent.DemoStrategySessionId), nameof(DemoStrategyIntent.DemoStrategySessionSymbolId), nameof(DemoStrategyIntent.SignalTimeUtc), nameof(DemoStrategyIntent.Direction)]));
    }

    [Fact]
    public void CoordinatorCannotDirectlyDependOnExecutionBridgeClient()
    {
        var dependencies = typeof(DemoStrategyCoordinator).GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType);
        Assert.DoesNotContain(typeof(IMt5ExecutionBridgeClient), dependencies);
    }

    [Fact]
    public void IntentStateMachineIncludesFailClosedRestartAndSubmissionStates()
    {
        var states = Enum.GetValues<DemoStrategyIntentStatus>();
        Assert.Contains(DemoStrategyIntentStatus.WaitingForEntryWindow, states);
        Assert.Contains(DemoStrategyIntentStatus.Blocked, states);
        Assert.Contains(DemoStrategyIntentStatus.Expired, states);
        Assert.Contains(DemoStrategyIntentStatus.ReconciliationRequired, states);
    }
}
