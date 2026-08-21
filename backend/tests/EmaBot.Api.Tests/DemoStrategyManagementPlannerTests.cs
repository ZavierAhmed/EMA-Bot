using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategyManagementPlannerTests
{
    private static readonly InstrumentSpec Tick = new("MT5", "XAUUSDm", "XAUUSDm", AssetClass.Commodity, 2, .01m, 100m, .01m, 10m, .01m, "XAU", "USD", "USD", TickSize: .05m);

    [Fact]
    public void DefaultsKeepB2ManagementFailClosed()
    {
        var options = new DemoStrategyAutomationOptions();
        Assert.False(options.Enabled); Assert.False(options.ManagementEnabled);
    }

    [Fact]
    public void ManagementUsesOnlyExecutableLongBidAndShortAsk()
    {
        Assert.Equal(100m, DemoStrategyManagementPlanner.ExecutableManagementPrice(SignalDirection.Long, 100m, 100.2m));
        Assert.Equal(100.2m, DemoStrategyManagementPlanner.ExecutableManagementPrice(SignalDirection.Short, 100m, 100.2m));
        Assert.Null(DemoStrategyManagementPlanner.ExecutableManagementPrice(SignalDirection.Long, null, 100.2m));
        Assert.Null(DemoStrategyManagementPlanner.ExecutableManagementPrice(SignalDirection.Short, 100m, null));
    }

    [Fact]
    public void BestProgressUsesActualFillAndNeverRegresses()
    {
        var best = DemoStrategyManagementPlanner.NextBest(SignalDirection.Long, 107m, 105m);
        Assert.Equal(107m, best); Assert.Equal(70m, DemoStrategyManagementPlanner.Progress(100m, 110m, best, SignalDirection.Long));
        Assert.Equal(20m, TradeMath.LockPercent(50m)); Assert.Equal(70m, TradeMath.LockPercent(100m));
    }

    [Fact]
    public void TargetExtensionAndTrailingUseImmutableOriginalTarget()
    {
        Assert.Equal(111m, TradeMath.ExtendedTarget(100m, 110m, SignalDirection.Long));
        Assert.Equal(104m, TradeMath.TrailingStop(100m, 110m, SignalDirection.Long, 40m));
        Assert.Equal(89m, TradeMath.ExtendedTarget(100m, 90m, SignalDirection.Short));
        Assert.Equal(96m, TradeMath.TrailingStop(100m, 90m, SignalDirection.Short, 40m));
    }

    [Fact]
    public void GeneratedPricesAlignConservativelyByDirection()
    {
        Assert.Equal(100.05m, DemoStrategyManagementPlanner.Align(100.01m, SignalDirection.Long, Tick));
        Assert.Equal(100.00m, DemoStrategyManagementPlanner.Align(100.01m, SignalDirection.Short, Tick));
        var pointOnly = Tick with { TickSize = null, PointSize = .1m };
        Assert.Equal(100.1m, DemoStrategyManagementPlanner.Align(100.01m, SignalDirection.Long, pointOnly));
        Assert.Null(DemoStrategyManagementPlanner.Align(100.01m, SignalDirection.Long, Tick with { TickSize = null, PointSize = 0m }));
    }

    [Fact]
    public void ManagementModelHasOneRowPerExecutionAndExplicitNoRetryStates()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = database.Model.FindEntityType(typeof(DemoStrategyPositionManagement));
        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(DemoStrategyPositionManagement.DemoExecutionId)]));
        Assert.Contains(DemoStrategyPositionManagementState.ProtectionReconciliationRequired, Enum.GetValues<DemoStrategyPositionManagementState>());
        Assert.Contains(DemoStrategyPositionManagementState.SuspendedAfterRestart, Enum.GetValues<DemoStrategyPositionManagementState>());
    }

    [Fact]
    public void SessionCreationSnapshotsTrailingAndOppositeExitSettings()
    {
        var settings = new TradingSettings { TrailingStopEnabled = true, ExitOnOppositeCrossover = true };
        var session = new DemoStrategySession { Interval = "3m", TrailingStopEnabled = settings.TrailingStopEnabled, ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover };
        settings.TrailingStopEnabled = false; settings.ExitOnOppositeCrossover = false;

        Assert.True(session.TrailingStopEnabled); Assert.True(session.ExitOnOppositeCrossover);
        var source = FindSource("backend", "src", "EmaBot.Api", "Controllers", "DemoStrategySessionsController.cs");
        Assert.Contains("TrailingStopEnabled = settings.TrailingStopEnabled", source);
        Assert.Contains("ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover", source);
    }

    [Fact]
    public void CurrentMigrationSnapshot_ConstructsAndMapsManagementSessionRelationship()
    {
        var snapshotType = typeof(EmaBotDbContext).Assembly.GetType("EmaBot.Api.Migrations.EmaBotDbContextModelSnapshot", throwOnError: true)!;
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(Activator.CreateInstance(snapshotType, nonPublic: true));
        var management = snapshot.Model.FindEntityType(typeof(DemoStrategyPositionManagement));

        Assert.NotNull(management);
        var relationship = Assert.Single(management!.GetForeignKeys(), item => item.PrincipalEntityType.Name == typeof(DemoStrategySession).FullName);
        Assert.Equal(nameof(DemoStrategyPositionManagement.DemoStrategySessionId), Assert.Single(relationship.Properties).Name);
        Assert.Equal(DeleteBehavior.Cascade, relationship.DeleteBehavior);
        Assert.Equal(nameof(DemoStrategySession.PositionManagement), relationship.PrincipalToDependent!.Name);
        Assert.Equal(nameof(DemoStrategyPositionManagement.DemoStrategySession), relationship.DependentToPrincipal!.Name);
    }

    private static string FindSource(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("Could not locate source file.");
    }
}
