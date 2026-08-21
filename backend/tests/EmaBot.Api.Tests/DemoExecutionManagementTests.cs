using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class DemoExecutionManagementTests
{
    [Fact]
    public void ProtectionOperation_IsV2OnlyAndCoordinatorHasNoDirectBridgeDependency()
    {
        Assert.Equal(2, Mt5ExecutionBridgeProtocol.ProtocolVersion);
        Assert.Contains(Mt5ExecutionOperation.ModifyPositionProtection, Mt5ExecutionBridgeProtocol.AllowedWriteOperations);
        var dependencies = typeof(DemoStrategyCoordinator).GetConstructors().SelectMany(item => item.GetParameters()).Select(item => item.ParameterType);
        Assert.DoesNotContain(typeof(IMt5ExecutionBridgeClient), dependencies);
        Assert.Contains("ModifyProtectionAsync", File.ReadAllText(ResolveSource("backend", "src", "EmaBot.Api", "Services", "DemoStrategyCoordinator.cs"))); // B2 delegates through IDemoExecutionService, never the bridge.
    }

    [Fact]
    public async Task SameManagementActionId_IsDurableBeforeOneNativeWrite()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database);
        var service = Service(database, bridge); var id = Guid.NewGuid();

        var first = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        var second = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(first.Id, second.Id); Assert.Equal(DemoExecutionManagementActionState.Applied, first.State); Assert.Equal(1, bridge.ModifyRequests);
        Assert.Equal(1900m, first.RequestedStopLoss); Assert.Equal(2100m, first.RequestedTakeProfit);
        Assert.Equal(1, await database.DemoExecutionManagementActions.CountAsync());
    }

    [Fact]
    public async Task SlOnlyAndTpOnlyRequests_PreserveTheOtherNativeProtection()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database);
        var service = Service(database, bridge);
        var sl = await service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);
        bridge.PositionResult = bridge.PositionResult with { StopLoss = 1900m, TakeProfit = 2100m };
        var tp = await service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, null, 2200m), default);

        Assert.Equal(2100m, sl.RequestedTakeProfit); Assert.Equal(1900m, tp.RequestedStopLoss);
        Assert.Equal(2, bridge.ModifyRequests);
    }

    [Theory]
    [InlineData(1800d, 2100d)]
    [InlineData(1900d, 2050d)]
    public async Task LongLooserProtection_IsRejectedWithoutNativeWrite(double stop, double target)
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database);
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, (decimal)stop, (decimal)target), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Theory]
    [InlineData("Buy", 1890d, 2100d, 1900d, 2200d, true)]
    [InlineData("Sell", 2110d, 1900d, 2100d, 1800d, true)]
    [InlineData("Buy", 1890d, 2100d, 1800d, 2100d, false)]
    [InlineData("Sell", 2110d, 1900d, 2120d, 1900d, false)]
    public async Task MonotonicRules_RespectLongAndShort(string currentSide, double currentStop, double currentTarget, double requestedStop, double requestedTarget, bool accepted)
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSDm", currentSide, .01m, 2000m, null, (decimal)currentStop, (decimal)currentTarget, null, null, null, .01m, .01m) };
        var execution = await AddOpenAsync(database, currentSide);
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, (decimal)requestedStop, (decimal)requestedTarget), default);

        Assert.Equal(accepted ? DemoExecutionManagementActionState.Applied : DemoExecutionManagementActionState.Rejected, action.State);
        Assert.Equal(accepted ? 1 : 0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task EqualProtection_IsAppliedAsNoOpWithoutNativeWrite()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database);
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1890m, 2100m), default);

        Assert.Equal(DemoExecutionManagementActionState.Applied, action.State); Assert.Equal("NoChange", action.ReconciliationSource); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task NullOrZeroProtection_CannotClearEitherNativeProtection()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database); var service = Service(database, bridge);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, null, null), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 0m, 2100m), default));
        Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task BridgeUnavailableBeforeWrite_FailsClosedWithZeroModification()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { IsAvailable = false }; var execution = await AddOpenAsync(database);
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task CreatedManagementAction_RecoveryFailsClosedWithoutWrite()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database); var id = Guid.NewGuid();
        database.DemoExecutionManagementActions.Add(new DemoExecutionManagementAction { ClientManagementActionId = id, DemoExecutionId = execution.Id, Kind = DemoExecutionManagementActionKind.ModifyProtection, State = DemoExecutionManagementActionState.Created, CreatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync();

        var action = await Service(database, bridge).FailClosedManagementActionAsync(id, default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action!.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Theory]
    [InlineData(DemoExecutionState.Closed)]
    [InlineData(DemoExecutionState.Rejected)]
    [InlineData(DemoExecutionState.Cancelled)]
    [InlineData(DemoExecutionState.ReconciliationRequired)]
    public async Task NonOpenExecution_NeverModifies(DemoExecutionState state)
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database); execution.State = state; await database.SaveChangesAsync();
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Theory]
    [InlineData("Ticket")]
    [InlineData("Identifier")]
    [InlineData("Magic")]
    [InlineData("Symbol")]
    [InlineData("Side")]
    public async Task ExactNativeOwnershipMismatch_RejectsWithZeroModification(string mismatch)
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = Mismatch(mismatch) }; var execution = await AddOpenAsync(database);
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task ExplicitRejectionAndAmbiguousWrite_NeverRetry()
    {
        await using var rejectedDb = NewDatabase(); var rejectedBridge = new FakeBridge { ModifyResult = new(false, "Rejected", "denied", null, null, null, null) }; var rejectedExecution = await AddOpenAsync(rejectedDb);
        var rejected = await Service(rejectedDb, rejectedBridge).ModifyProtectionAsync(new(Guid.NewGuid(), rejectedExecution.ClientExecutionId, 1900m, null), default);
        Assert.Equal(DemoExecutionManagementActionState.Rejected, rejected.State); Assert.Equal(1, rejectedBridge.ModifyRequests);

        await using var ambiguousDb = NewDatabase(); var ambiguousBridge = new FakeBridge { ModifyException = new Mt5ExecutionBridgeAmbiguousException("timeout") }; var ambiguousExecution = await AddOpenAsync(ambiguousDb); var service = Service(ambiguousDb, ambiguousBridge); var id = Guid.NewGuid();
        var ambiguous = await service.ModifyProtectionAsync(new(id, ambiguousExecution.ClientExecutionId, 1900m, null), default);
        _ = await service.ModifyProtectionAsync(new(id, ambiguousExecution.ClientExecutionId, 1900m, null), default);
        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, ambiguous.State); Assert.Equal(1, ambiguousBridge.ModifyRequests);
    }

    [Fact]
    public async Task AcceptedWrite_WithDifferentNativeProtection_RequiresReconciliation()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ModifyResult = new(true, "Done", "accepted", 300, 400, 1901m, 2100m) }; var execution = await AddOpenAsync(database); var service = Service(database, bridge); var id = Guid.NewGuid();

        var action = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        _ = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, action.State); Assert.NotEqual(DemoExecutionManagementActionState.Applied, action.State); Assert.Equal(1, bridge.ModifyRequests);
    }

    [Fact]
    public async Task BrokerEquivalentRoundedProtection_ProvesAppliedButMateriallyDifferentDoesNot()
    {
        await using var equivalentDb = NewDatabase(); var equivalentBridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m, null, null, null, .01m, .01m), ModifyResult = new(true, "Done", "accepted", 300, 400, 1900.100000005m, 2100m) }; var equivalentExecution = await AddOpenAsync(equivalentDb);
        var equivalent = await Service(equivalentDb, equivalentBridge).ModifyProtectionAsync(new(Guid.NewGuid(), equivalentExecution.ClientExecutionId, 1900.10m, null), default);
        Assert.Equal(DemoExecutionManagementActionState.Applied, equivalent.State);

        await using var differentDb = NewDatabase(); var differentBridge = new FakeBridge { ModifyResult = new(true, "Done", "accepted", 300, 400, 1900.2m, 2100m) }; var differentExecution = await AddOpenAsync(differentDb);
        var different = await Service(differentDb, differentBridge).ModifyProtectionAsync(new(Guid.NewGuid(), differentExecution.ClientExecutionId, 1900m, null), default);
        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, different.State);
    }

    [Fact]
    public async Task OffGridAndKnownStopsOrFreezeViolations_RejectWithoutNativeWrite()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m, 2000m, 2000.2m, 2, .01m, .01m, 20, 30) }; var execution = await AddOpenAsync(database); var service = Service(database, bridge);
        var offGrid = await service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900.005m, null), default);
        var stopOrFreeze = await service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1999.8m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, offGrid.State); Assert.Equal(DemoExecutionManagementActionState.Rejected, stopOrFreeze.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task MissingTickAndPoint_FailsClosedWithoutNativeWrite()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m) }; var execution = await AddOpenAsync(database);

        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public void ProtectionPriceCanonicalization_MissingTickAndPointFailsClosed()
    {
        var position = new Mt5ExecutionPositionPayload(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m);

        Assert.False(DemoExecutionProtectionPrices.TryCanonicalize(1900m, position, out _));
    }

    [Fact]
    public async Task ManagementValidation_MissingNativeStopClearsCurrentStop()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, null, 2110m, null, null, null, .01m, .01m) }; var execution = await AddOpenAsync(database); execution.CurrentStopLoss = 1890m; execution.CurrentTakeProfit = 2100m; await database.SaveChangesAsync();

        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);
        var persisted = await database.DemoExecutions.SingleAsync();

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Null(persisted.CurrentStopLoss); Assert.Equal(2110m, persisted.CurrentTakeProfit); Assert.NotNull(persisted.ProtectionObservedAtUtc); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task ManagementValidation_MissingNativeTargetClearsCurrentTarget()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1895m, null, null, null, null, .01m, .01m) }; var execution = await AddOpenAsync(database); execution.CurrentStopLoss = 1890m; execution.CurrentTakeProfit = 2100m; await database.SaveChangesAsync();

        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);
        var persisted = await database.DemoExecutions.SingleAsync();

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(1895m, persisted.CurrentStopLoss); Assert.Null(persisted.CurrentTakeProfit); Assert.NotNull(persisted.ProtectionObservedAtUtc); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task ManagementReconciliation_DifferentNativeProtectionRefreshesCurrentValues()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ModifyException = new Mt5ExecutionBridgeAmbiguousException("timeout") }; var execution = await AddOpenAsync(database); var service = Service(database, bridge); var id = Guid.NewGuid();
        _ = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        bridge.ModifyException = null; bridge.PositionResult = bridge.PositionResult with { StopLoss = 1895m, TakeProfit = 2200m };

        var action = await service.ReconcileManagementActionAsync(id, default);
        var persisted = await database.DemoExecutions.SingleAsync();

        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, action!.State); Assert.Equal(1895m, persisted.CurrentStopLoss); Assert.Equal(2200m, persisted.CurrentTakeProfit); Assert.NotNull(persisted.ProtectionObservedAtUtc); Assert.Equal(1, bridge.ModifyRequests);
    }

    [Fact]
    public async Task ManagementReconciliation_MissingProtectionClearsCurrentValue()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ModifyException = new Mt5ExecutionBridgeAmbiguousException("timeout") }; var execution = await AddOpenAsync(database); var service = Service(database, bridge); var id = Guid.NewGuid();
        _ = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        bridge.ModifyException = null; bridge.PositionResult = bridge.PositionResult with { StopLoss = null, TakeProfit = 2200m };

        var action = await service.ReconcileManagementActionAsync(id, default);
        var persisted = await database.DemoExecutions.SingleAsync();

        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, action!.State); Assert.Null(persisted.CurrentStopLoss); Assert.Equal(2200m, persisted.CurrentTakeProfit); Assert.NotNull(persisted.ProtectionObservedAtUtc); Assert.Equal(1, bridge.ModifyRequests);
    }

    [Fact]
    public async Task TrueConcurrentDuplicateManagementAction_HasOneWriteAndOneLedgerRow()
    {
        var name = Guid.NewGuid().ToString(); await using var seed = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(name).Options); var execution = await AddOpenAsync(seed); var bridge = new FakeBridge(); var id = Guid.NewGuid();
        await using var firstDb = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(name).Options); await using var secondDb = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(name).Options);
        var first = Service(firstDb, bridge); var second = Service(secondDb, bridge);

        var results = await Task.WhenAll(Task.Run(() => first.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default)), Task.Run(() => second.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default)));

        Assert.Equal(results[0].Id, results[1].Id); Assert.Equal(1, bridge.ModifyRequests); Assert.Equal(1, await seed.DemoExecutionManagementActions.CountAsync());
    }

    [Fact]
    public async Task ReconciliationProvesAppliedWithoutModificationOrLeavesAmbiguous()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ModifyException = new Mt5ExecutionBridgeAmbiguousException("timeout") }; var execution = await AddOpenAsync(database); var service = Service(database, bridge); var id = Guid.NewGuid();
        _ = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        bridge.ModifyException = null; bridge.PositionResult = bridge.PositionResult with { StopLoss = 1900m };
        var applied = await service.ReconcileManagementActionAsync(id, default);
        Assert.Equal(DemoExecutionManagementActionState.Applied, applied!.State); Assert.Equal(1, bridge.ModifyRequests);

        var second = await service.ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1950m, null), default);
        Assert.Equal(DemoExecutionManagementActionState.Applied, second.State);
    }

    [Fact]
    public async Task AmbiguousWrite_WithDifferentNativeValues_RemainsReconciliationRequiredWithoutRetry()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ModifyException = new Mt5ExecutionBridgeAmbiguousException("timeout") }; var execution = await AddOpenAsync(database); var service = Service(database, bridge); var id = Guid.NewGuid();
        _ = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        bridge.ModifyException = null; bridge.PositionResult = bridge.PositionResult with { StopLoss = 1895m };

        var action = await service.ReconcileManagementActionAsync(id, default);

        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, action!.State); Assert.Equal(1, bridge.ModifyRequests);
    }

    [Fact]
    public async Task ClosedPositionDuringManagementReconciliation_UsesExecutionReconciliationWithoutModification()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ModifyException = new Mt5ExecutionBridgeAmbiguousException("timeout") }; var execution = await AddOpenAsync(database); var service = Service(database, bridge); var id = Guid.NewGuid();
        _ = await service.ModifyProtectionAsync(new(id, execution.ClientExecutionId, 1900m, null), default);
        bridge.ModifyException = null; bridge.PositionResult = new(true, true, null, null, null, null, null, null, null);

        var action = await service.ReconcileManagementActionAsync(id, default);

        Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, action!.State); Assert.NotEqual(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(1, bridge.ModifyRequests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MissingNativeIdentityOnOpenExecution_FailsClosedWithoutModification(bool missingTicket, bool missingIdentifier)
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = await AddOpenAsync(database); if (missingTicket) execution.PositionTicket = null; if (missingIdentifier) execution.PositionIdentifier = null; await database.SaveChangesAsync();
        var action = await Service(database, bridge).ModifyProtectionAsync(new(Guid.NewGuid(), execution.ClientExecutionId, 1900m, null), default);

        Assert.Equal(DemoExecutionManagementActionState.Rejected, action.State); Assert.Equal(0, bridge.ModifyRequests);
    }

    [Fact]
    public async Task RecoveryService_FailsCreatedClosedAndOnlyReconcilesOtherManagementStates()
    {
        var name = Guid.NewGuid().ToString(); var bridge = new FakeBridge(); await using (var seed = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(name).Options))
        {
            var execution = await AddOpenAsync(seed);
            foreach (var state in new[] { DemoExecutionManagementActionState.Created, DemoExecutionManagementActionState.Submitting, DemoExecutionManagementActionState.ReconciliationRequired, DemoExecutionManagementActionState.Applied, DemoExecutionManagementActionState.Rejected })
                seed.DemoExecutionManagementActions.Add(new DemoExecutionManagementAction { ClientManagementActionId = Guid.NewGuid(), DemoExecutionId = execution.Id, Kind = DemoExecutionManagementActionKind.ModifyProtection, State = state, RequestedStopLoss = 1900m, RequestedTakeProfit = 2100m, CreatedAtUtc = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }
        var services = new ServiceCollection(); services.AddDbContext<EmaBotDbContext>(options => options.UseInMemoryDatabase(name)); services.AddSingleton<IMt5ExecutionBridgeClient>(bridge); services.AddScoped<DemoExecutionService>(); services.AddSingleton(TimeProvider.System); services.AddLogging(); services.Configure<DemoExecutionOptions>(options => { options.Enabled = true; options.DemoOnly = true; options.ExpectedAccountFingerprint = "demo-fingerprint"; options.ExpectedServer = "Exness-Demo"; }); await using var provider = services.BuildServiceProvider();
        var recovery = new DemoExecutionRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), bridge, NullLogger<DemoExecutionRecoveryService>.Instance);

        await recovery.StartAsync(default);

        await using var check = provider.CreateAsyncScope(); var states = await check.ServiceProvider.GetRequiredService<EmaBotDbContext>().DemoExecutionManagementActions.OrderBy(item => item.Id).Select(item => item.State).ToListAsync();
        Assert.Equal(DemoExecutionManagementActionState.Rejected, states[0]); Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, states[1]); Assert.Equal(DemoExecutionManagementActionState.ReconciliationRequired, states[2]); Assert.Equal(DemoExecutionManagementActionState.Applied, states[3]); Assert.Equal(DemoExecutionManagementActionState.Rejected, states[4]); Assert.Equal(0, bridge.ModifyRequests);
        await recovery.StopAsync(default);
    }

    private static EmaBotDbContext NewDatabase() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static string ResolveSource(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Could not locate source file.");
    }
    private static DemoExecutionService Service(EmaBotDbContext database, FakeBridge bridge) => new(database, bridge, Options.Create(new DemoExecutionOptions { Enabled = true, DemoOnly = true, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo" }), TimeProvider.System, NullLogger<DemoExecutionService>.Instance);
    private static async Task<DemoExecution> AddOpenAsync(EmaBotDbContext database, string side = "Buy") { var item = new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Open, BrokerSymbol = "XAUUSDm", Side = side, VolumeLots = .01m, MagicNumber = 20260817, CorrelationMarker = "EMA-test", PositionTicket = 300, PositionIdentifier = 400, RequestedStopLoss = side == "Buy" ? 1890m : 2110m, RequestedTakeProfit = side == "Buy" ? 2100m : 1900m, CreatedAtUtc = DateTimeOffset.UtcNow }; database.DemoExecutions.Add(item); await database.SaveChangesAsync(); return item; }
    private static Mt5ExecutionPositionPayload Mismatch(string kind) => kind switch { "Ticket" => new(true, false, 301, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m), "Identifier" => new(true, false, 300, 401, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m), "Magic" => new(true, false, 300, 400, 1, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m), "Symbol" => new(true, false, 300, 400, 20260817, "XAUUSD", "Buy", .01m, 2000m, null, 1890m, 2100m), "Side" => new(true, false, 300, 400, 20260817, "XAUUSDm", "Sell", .01m, 2000m, null, 1890m, 2100m), _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    private sealed class FakeBridge : IMt5ExecutionBridgeClient
    {
        public event Action? Connected { add { } remove { } }
        public bool IsAvailable { get; init; } = true;
        public bool IsConnected => IsAvailable;
        public int ModifyRequests { get; private set; }
        public Exception? ModifyException { get; set; }
        public Mt5ModifyPositionProtectionResultPayload? ModifyResult { get; init; }
        public Mt5ExecutionPositionPayload PositionResult { get; set; } = new(true, false, 300, 400, 20260817, "XAUUSDm", "Buy", .01m, 2000m, null, 1890m, 2100m, null, null, null, .01m, .01m);
        public Mt5ExecutionBridgeStatus GetStatus() => new(true, true, "test", "demo-fingerprint", "Exness-Demo", "Demo", null);
        public Task<Mt5ExecutionEnvelope> SendAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token)
        {
            object response = operation switch
            {
                Mt5ExecutionOperation.GetExecutionAccount => new Mt5ExecutionAccountPayload("demo-fingerprint", "Exness-Demo", "Demo", true, true, true, true),
                Mt5ExecutionOperation.GetPosition => PositionResult,
                Mt5ExecutionOperation.GetExecutionHistory => new Mt5ExecutionHistoryPayload([]),
                Mt5ExecutionOperation.ModifyPositionProtection => Modify((Mt5ModifyPositionProtectionRequest)payload!),
                _ => throw new InvalidOperationException($"Unexpected {operation}")
            };
            return Task.FromResult(Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Response, operation, Guid.NewGuid(), response, TimeProvider.System));
        }
        private Mt5ModifyPositionProtectionResultPayload Modify(Mt5ModifyPositionProtectionRequest request)
        {
            ModifyRequests++; if (ModifyException is not null) throw ModifyException;
            return ModifyResult ?? new(true, "Done", "ok", request.PositionTicket, request.PositionIdentifier, request.StopLoss, request.TakeProfit);
        }
    }
}
