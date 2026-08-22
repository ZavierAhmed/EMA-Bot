using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class DemoExecutionFoundationTests
{
    [Fact]
    public void ExecutionSafetyOptions_AreDisabledByDefault_AndCannotEnableWithoutDemoTarget()
    {
        Assert.Empty(DemoExecutionOptions.Validate(new DemoExecutionOptions()));
        Assert.NotEmpty(DemoExecutionOptions.Validate(new DemoExecutionOptions { Enabled = true }));
        Assert.NotEmpty(DemoExecutionOptions.Validate(new DemoExecutionOptions { Enabled = true, DemoOnly = false, ExpectedAccountFingerprint = "safe-fingerprint", ExpectedServer = "Demo" }));
        Assert.Empty(DemoExecutionOptions.Validate(new DemoExecutionOptions { Enabled = true, DemoOnly = true, ExpectedAccountFingerprint = "safe-fingerprint", ExpectedServer = "Demo" }));
        Assert.NotEmpty(Mt5ExecutionBridgeOptions.Validate(new Mt5ExecutionBridgeOptions { Enabled = true }));
        Assert.NotEmpty(DemoExecutionOptions.Validate(new DemoExecutionOptions { Enabled = true, DemoOnly = true, ExpectedAccountFingerprint = "safe-fingerprint", ExpectedServer = "Demo", CorrelationPrefix = "TOOLONG" }));
    }

    [Fact]
    public void NewMarkers_AreBrokerSafe_AndLegacyMarkersUseTheirPhysicalBrokerPrefix()
    {
        var id = Guid.Parse("f05c2344-bd5e-48c5-91d0-b3c7fb26cb51");
        var marker = DemoExecutionMarker.Generate("EMA", id);

        Assert.Equal("EMA-f05c2344bd5e48c591d0b3c7fb2", marker);
        Assert.Equal(31, marker.Length);
        Assert.Equal("EMA-f05c2344bd5e48c591d0b3c7fb2", DemoExecutionMarker.BrokerMarker("EMA-f05c2344bd5e48c591d0b3c7fb26cb51"));
    }

    [Fact]
    public async Task DuplicateClientExecutionId_ReturnsPersistedIntentWithoutSecondSubmit()
    {
        await using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var bridge = new FakeBridge();
        var options = Options.Create(new DemoExecutionOptions { Enabled = true, DemoOnly = true, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo" });
        var service = new DemoExecutionService(database, bridge, options, TimeProvider.System, NullLogger<DemoExecutionService>.Instance);
        var id = Guid.NewGuid();
        var first = await service.SubmitAsync(new(id, "XAUUSD", "Buy", 0.01m, null, null), default);
        var second = await service.SubmitAsync(new(id, "XAUUSD", "Buy", 0.01m, null, null), default);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, bridge.SubmitCount);
        Assert.Equal(200, first.EntryDealTicket);
        Assert.Equal(DemoExecutionState.Open, first.State);
        Assert.Equal(1, await database.DemoExecutions.CountAsync());
    }

    [Fact]
    public async Task DisabledDemoExecution_StillReadOnlyValidatesTheMatchingAccount()
    {
        await using var database = NewDatabase();
        var bridge = new FakeBridge();
        var options = Options.Create(new DemoExecutionOptions { Enabled = false, DemoOnly = true, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo" });
        var service = new DemoExecutionService(database, bridge, options, TimeProvider.System, NullLogger<DemoExecutionService>.Instance);

        var readiness = await service.ReadinessAsync(CancellationToken.None);

        Assert.False(readiness.Ready);
        Assert.Equal("Demo execution is disabled.", readiness.Reason);
        Assert.Equal("Demo", readiness.Account!.TradeMode);
        Assert.True(readiness.Account.AccountTradeAllowed);
        Assert.True(readiness.Account.ExpertTradeAllowed);
        Assert.Equal(1, bridge.ExecutionAccountRequests);
        Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task Readiness_WhenEaExecutionDisabled_IsFalse()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { AccountResult = new("demo-fingerprint", "Exness-Demo", "Demo", true, true, false, false) };

        var readiness = await Service(database, bridge).ReadinessAsync(default);

        Assert.False(readiness.Ready); Assert.Equal("MT5 EA Demo execution is disabled.", readiness.Reason);
    }

    [Fact]
    public async Task Readiness_WhenEaSafetyGateFails_IsFalse()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { AccountResult = new("demo-fingerprint", "Exness-Demo", "Demo", true, true, true, false) };

        var readiness = await Service(database, bridge).ReadinessAsync(default);

        Assert.False(readiness.Ready); Assert.Equal("MT5 EA Demo execution safety gate failed.", readiness.Reason);
    }

    [Fact]
    public async Task Readiness_WhenBothDotNetAndEaGatesPass_IsTrue()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge();

        var readiness = await Service(database, bridge).ReadinessAsync(default);

        Assert.True(readiness.Ready); Assert.Equal("Demo execution preflight passed.", readiness.Reason); Assert.NotNull(readiness.Account); Assert.Equal("E11.7A1A1", readiness.Account.EaBuildId); Assert.True(readiness.Account.SupportsExactProtectionReadback); Assert.True(readiness.Account.SupportsNativeExitReason);
    }

    [Fact]
    public async Task Readiness_WhenProtectionReadbackCapabilityIsMissing_FailsClosed()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { AccountResult = new("demo-fingerprint", "Exness-Demo", "Demo", true, true, true, true, "E11.7A1A1", false, true) };
        var readiness = await Service(database, bridge).ReadinessAsync(default);

        Assert.False(readiness.Ready); Assert.Equal("MT5 execution EA does not support the required broker-evidence capabilities.", readiness.Reason); Assert.NotNull(readiness.Account);
    }

    [Fact]
    public async Task Readiness_WhenNativeExitCapabilityIsMissing_FailsClosed()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { AccountResult = new("demo-fingerprint", "Exness-Demo", "Demo", true, true, true, true, "E11.7A1A1", true, false) };
        var readiness = await Service(database, bridge).ReadinessAsync(default);

        Assert.False(readiness.Ready); Assert.Equal("MT5 execution EA does not support the required broker-evidence capabilities.", readiness.Reason); Assert.NotNull(readiness.Account);
    }

    [Fact]
    public void DemoExecutionModel_HasImmutableUniqueClientIdAndNoPaperRelationship()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = database.Model.FindEntityType(typeof(DemoExecution))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(DemoExecution.ClientExecutionId));
        Assert.Empty(entity.GetForeignKeys());
    }

    [Fact]
    public void DemoExecutionModel_PersistsNullableBrokerPnlEvidence()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = database.Model.FindEntityType(typeof(DemoExecution))!;
        var currency = entity.FindProperty(nameof(DemoExecution.BrokerAccountCurrency))!;
        Assert.True(currency.IsNullable); Assert.Equal(16, currency.GetMaxLength());

        foreach (var name in new[]
        {
            nameof(DemoExecution.BrokerEntryProfit), nameof(DemoExecution.BrokerEntryCommission), nameof(DemoExecution.BrokerEntrySwap), nameof(DemoExecution.BrokerEntryFee),
            nameof(DemoExecution.BrokerCurrentProfit), nameof(DemoExecution.BrokerCurrentSwap),
            nameof(DemoExecution.BrokerHistoryProfit), nameof(DemoExecution.BrokerHistoryCommission), nameof(DemoExecution.BrokerHistorySwap), nameof(DemoExecution.BrokerHistoryFee)
        })
        {
            var property = entity.FindProperty(name)!;
            Assert.True(property.IsNullable); Assert.Equal(18, property.GetPrecision()); Assert.Equal(8, property.GetScale());
        }

        Assert.True(entity.FindProperty(nameof(DemoExecution.BrokerEntryPnlObservedAtUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DemoExecution.BrokerCurrentPnlObservedAtUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DemoExecution.BrokerHistoryPnlObservedAtUtc))!.IsNullable);
    }

    [Fact]
    public void PaperCoordinator_HasNoExecutionSubmissionDependency()
    {
        var constructorTypes = typeof(PaperTradingCoordinator).GetConstructors().SelectMany(item => item.GetParameters()).Select(item => item.ParameterType);
        Assert.DoesNotContain(typeof(DemoExecutionService), constructorTypes);
        Assert.DoesNotContain(typeof(IMt5ExecutionBridgeClient), constructorTypes);
    }

    [Fact]
    public async Task MissingTicket_ReconcilesFromOneDeterministicHistoryMatch()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { History = [Evidence(100, 200, 300)] }; var service = Service(database, bridge);
        var execution = Uncertain(); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var recovered = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Open, recovered!.State); Assert.Equal(300, recovered.PositionTicket); Assert.Equal(200, recovered.EntryDealTicket); Assert.Equal("BoundedHistory", recovered.ReconciliationSource); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task ExactPositionRead_MissingStopClearsPreviouslyObservedCurrentStop()
    {
        await using var database = NewDatabase(); var execution = Uncertain(); execution.PositionTicket = 300; execution.PositionIdentifier = 400; execution.CurrentStopLoss = 1890m; execution.CurrentTakeProfit = 2100m; var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSD", "Buy", .01m, 2000m, null, null, 2100m) }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Null(result!.CurrentStopLoss); Assert.Equal(2100m, result.CurrentTakeProfit); Assert.NotNull(result.ProtectionObservedAtUtc);
    }

    [Fact]
    public async Task ExactPositionRead_MissingTargetClearsPreviouslyObservedCurrentTarget()
    {
        await using var database = NewDatabase(); var execution = Uncertain(); execution.PositionTicket = 300; execution.PositionIdentifier = 400; execution.CurrentStopLoss = 1890m; execution.CurrentTakeProfit = 2100m; var bridge = new FakeBridge { PositionResult = new(true, false, 300, 400, 20260817, "XAUUSD", "Buy", .01m, 2000m, null, 1890m, null) }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(1890m, result!.CurrentStopLoss); Assert.Null(result.CurrentTakeProfit); Assert.NotNull(result.ProtectionObservedAtUtc);
    }

    [Fact]
    public async Task MissingTicket_WithNoHistory_RemainsReconciliationRequired()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var service = Service(database, bridge); var execution = Uncertain(); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Contains("No deterministic", result.ReconciliationNote); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task MultipleHistoryPositions_AreNeverGuessed()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { History = [Evidence(100, 200, 300), Evidence(101, 201, 301)] }; var service = Service(database, bridge); var execution = Uncertain(); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Contains("More than one", result.ReconciliationNote);
    }

    [Fact]
    public async Task WrongMagicOrMarker_IsNotAdopted()
    {
        await using var database = NewDatabase(); var execution = Uncertain(); var wrongMagic = Evidence(100, 200, 300) with { MagicNumber = 99 }; var wrongMarker = Evidence(101, 201, 301) with { CorrelationMarker = "other" }; var bridge = new FakeBridge { History = [wrongMagic, wrongMarker] }; var service = Service(database, bridge); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State);
    }

    [Fact]
    public async Task PartialHistoryFill_RemainsPartiallyFilled()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { History = [Evidence(100, 200, 300) with { ExecutedVolumeLots = 0.005m, IsPartial = true }] }; var service = Service(database, bridge); var execution = Uncertain(); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.PartiallyFilled, result!.State); Assert.Equal(0.005m, result.FilledVolumeLots);
    }

    [Fact]
    public async Task KnownExactTicket_ReconcilesWithoutHistorySearch()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 123, 456, 20260817, "XAUUSD", "Buy", 0.01m, 2000m) }; var service = Service(database, bridge); var execution = Uncertain(); execution.PositionTicket = 123; execution.PositionIdentifier = 456; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal("ExactPositionTicket", result.ReconciliationSource); Assert.Equal(0, bridge.HistoryRequests);
    }

    [Fact]
    public async Task HistoryEntryAndExit_RepairsAmbiguousCloseToClosed()
    {
        await using var database = NewDatabase(); var entry = Evidence(100, 200, 300); var exit = Evidence(100, 201, 300) with { Side = "Sell", IsEntry = false, IsExit = true, EntryType = "Exit", ExecutedAtUtc = DateTimeOffset.UtcNow }; var bridge = new FakeBridge { History = [entry, exit], PositionResult = new(true, true, null, null, null, null, null, null, null) }; var service = Service(database, bridge); var execution = Uncertain(); execution.State = DemoExecutionState.ReconciliationRequired; execution.PositionTicket = 300; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal(201, result.ExitDealTicket); Assert.NotNull(result.ClosedAtUtc); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task ManuallyClosedKnownEntryDeal_ReconcilesFromNativeHistory_AndNeverResurrectsOpen()
    {
        await using var database = NewDatabase();
        var id = Guid.Parse("f05c2344-bd5e-48c5-91d0-b3c7fb26cb51");
        var entryAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var bridge = new FakeBridge
        {
            ExactDeal = new(1604880873, 1702319499, 1702319499, null, "XAUUSDm", "Buy", 20260817, 0.01m, 2400m, entryAt, true, false, false),
            PositionHistory = [
                new(1604880873, 1702319499, 1702319499, "XAUUSDm", "Buy", 20260817, 0.01m, 2400m, entryAt, "Entry", true, false),
                new(1604880874, 1702319500, 1702319499, "XAUUSDm", "Sell", 0, 0.01m, 2401m, entryAt.AddMinutes(1), "Exit", false, true)]
        };
        var execution = new DemoExecution { ClientExecutionId = id, State = DemoExecutionState.ReconciliationRequired, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo", BrokerSymbol = "XAUUSDm", Side = "Buy", VolumeLots = 0.01m, MagicNumber = 20260817, CorrelationMarker = "EMA-f05c2344bd5e48c591d0b3c7fb26cb51", CreatedAtUtc = entryAt.AddMinutes(-1), SubmittedAtUtc = entryAt, OrderTicket = 1702319499, EntryDealTicket = 1604880873, DealTicket = 1604880873, PositionIdentifier = 1702319499 };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(id, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal(1604880873, result.EntryDealTicket); Assert.Equal(1604880874, result.ExitDealTicket); Assert.Equal("NativePositionHistory", result.ReconciliationSource); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task LegacyTruncatedMarker_RemainsRecoverableOnlyThroughStrictBoundedHistory()
    {
        await using var database = NewDatabase(); var execution = Uncertain(); execution.CorrelationMarker = "EMA-f05c2344bd5e48c591d0b3c7fb26cb51"; var physical = DemoExecutionMarker.BrokerMarker(execution.CorrelationMarker); var bridge = new FakeBridge { History = [Evidence(100, 200, 300) with { CorrelationMarker = physical }] }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal(physical, bridge.LastHistoryRequest!.CorrelationMarker); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task CloseResult_SetsExitDealWithoutOverwritingEntryDeal()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { CloseResult = new(true, "Done", "closed", 201, 0.01m, 2001m, true) }; var execution = Uncertain(); execution.State = DemoExecutionState.Open; execution.PositionTicket = 300; execution.PositionIdentifier = 300; execution.EntryDealTicket = 200; execution.DealTicket = 200; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).CloseAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal(200, result.EntryDealTicket); Assert.Equal(201, result.ExitDealTicket);
    }

    [Fact]
    public async Task ExactEntryReconciliation_PrefersEntryDealTicketOverLegacyDealTicket()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { ExactDeal = new(200, 100, 300, 301, "XAUUSD", "Buy", 20260817, 0.01m, 2000m, DateTimeOffset.UtcNow.AddMinutes(-1), true, false, true), PositionResult = new(true, false, 301, 300, 20260817, "XAUUSD", "Buy", 0.01m, 2000m, StopLoss: 1990m, TakeProfit: 2010m) }; var execution = Uncertain(); execution.EntryDealTicket = 200; execution.DealTicket = 999; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal(200, bridge.LastExactDealRequest!.ExactDealTicket); Assert.Equal(200, result.EntryDealTicket);
    }

    [Fact]
    public async Task ExactOpenPositionReadback_PersistsBrokerNativeProtection()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 123456, 123456, 20260817, "BTCUSDm", "Buy", .01m, 78693.81m, StopLoss: 78569.64m, TakeProfit: 78876.42m) }; var execution = Uncertain(); execution.BrokerSymbol = "BTCUSDm"; execution.PositionTicket = 123456; execution.PositionIdentifier = 123456; execution.FilledVolumeLots = .01m; execution.RequestedStopLoss = 78000m; execution.RequestedTakeProfit = 79000m; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal(78569.64m, result.CurrentStopLoss); Assert.Equal(78876.42m, result.CurrentTakeProfit); Assert.NotNull(result.ProtectionObservedAtUtc); Assert.Equal("ExactPositionTicket", result.ReconciliationSource); Assert.Equal(123456, result.PositionTicket); Assert.Equal(123456, result.PositionIdentifier);
    }

    [Fact]
    public async Task ExactEntryAndOpenPosition_PersistSignedBrokerPnlEvidence()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt) with { PositionTicket = 123456, IsPositionOpen = true, Profit = 0m, Commission = -0.35m, Swap = 0m, Fee = -0.02m, AccountCurrency = "USD" }, PositionResult = new(true, false, 123456, 300, 20260817, "XAUUSD", "Buy", .01m, 2000m, CurrentProfit: -12.34m, CurrentSwap: -0.56m, AccountCurrency: "USD") }; var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal("USD", result.BrokerAccountCurrency); Assert.Equal(0m, result.BrokerEntryProfit); Assert.Equal(-0.35m, result.BrokerEntryCommission); Assert.Equal(0m, result.BrokerEntrySwap); Assert.Equal(-0.02m, result.BrokerEntryFee); Assert.NotNull(result.BrokerEntryPnlObservedAtUtc); Assert.Equal(-12.34m, result.BrokerCurrentProfit); Assert.Equal(-0.56m, result.BrokerCurrentSwap); Assert.NotNull(result.BrokerCurrentPnlObservedAtUtc);
    }

    [Fact]
    public async Task ExactOpenPosition_WhenPnlFieldsAreMissing_DoesNotErasePriorBrokerPnl()
    {
        await using var database = NewDatabase(); var priorObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1); var bridge = new FakeBridge { PositionResult = new(true, false, 123456, 123456, 20260817, "BTCUSDm", "Buy", .01m, 78693.81m) }; var execution = Uncertain(); execution.BrokerSymbol = "BTCUSDm"; execution.PositionTicket = 123456; execution.PositionIdentifier = 123456; execution.FilledVolumeLots = .01m; execution.BrokerAccountCurrency = "USD"; execution.BrokerCurrentProfit = 5.25m; execution.BrokerCurrentSwap = -0.10m; execution.BrokerCurrentPnlObservedAtUtc = priorObservedAt; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal(5.25m, result.BrokerCurrentProfit); Assert.Equal(-0.10m, result.BrokerCurrentSwap); Assert.Equal(priorObservedAt, result.BrokerCurrentPnlObservedAtUtc);
    }

    [Fact]
    public async Task ExactOpenPosition_WithConflictingCurrency_DoesNotOverwriteBrokerMoney()
    {
        await using var database = NewDatabase(); var priorObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1); var bridge = new FakeBridge { PositionResult = new(true, false, 123456, 123456, 20260817, "BTCUSDm", "Buy", .01m, 78693.81m, CurrentProfit: 99m, CurrentSwap: 1m, AccountCurrency: "EUR") }; var execution = Uncertain(); execution.BrokerSymbol = "BTCUSDm"; execution.PositionTicket = 123456; execution.PositionIdentifier = 123456; execution.FilledVolumeLots = .01m; execution.BrokerAccountCurrency = "USD"; execution.BrokerCurrentProfit = 5m; execution.BrokerCurrentSwap = -0.10m; execution.BrokerCurrentPnlObservedAtUtc = priorObservedAt; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal("USD", result.BrokerAccountCurrency); Assert.Equal(5m, result.BrokerCurrentProfit); Assert.Equal(-0.10m, result.BrokerCurrentSwap); Assert.Equal(priorObservedAt, result.BrokerCurrentPnlObservedAtUtc);
    }

    [Fact]
    public async Task ExactOpenPositionReadback_WhenStopLossMissing_ClearsStaleProtection()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 123456, 123456, 20260817, "BTCUSDm", "Buy", .01m, 78693.81m, StopLoss: null, TakeProfit: 78876.42m) }; var execution = Uncertain(); execution.BrokerSymbol = "BTCUSDm"; execution.PositionTicket = 123456; execution.PositionIdentifier = 123456; execution.FilledVolumeLots = .01m; execution.RequestedStopLoss = 77000m; execution.RequestedTakeProfit = 80000m; execution.CurrentStopLoss = 78000m; execution.CurrentTakeProfit = 79000m; execution.ProtectionObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Null(result.CurrentStopLoss); Assert.Equal(78876.42m, result.CurrentTakeProfit); Assert.NotNull(result.ProtectionObservedAtUtc); Assert.Equal("ExactPositionTicket", result.ReconciliationSource);
    }

    [Fact]
    public async Task ExactOpenPositionReadback_WhenTakeProfitMissing_ClearsStaleProtection()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 123456, 123456, 20260817, "BTCUSDm", "Buy", .01m, 78693.81m, StopLoss: 78569.64m, TakeProfit: null) }; var execution = Uncertain(); execution.BrokerSymbol = "BTCUSDm"; execution.PositionTicket = 123456; execution.PositionIdentifier = 123456; execution.FilledVolumeLots = .01m; execution.RequestedStopLoss = 77000m; execution.RequestedTakeProfit = 80000m; execution.CurrentStopLoss = 78000m; execution.CurrentTakeProfit = 79000m; execution.ProtectionObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1); database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal(78569.64m, result.CurrentStopLoss); Assert.Null(result.CurrentTakeProfit); Assert.NotNull(result.ProtectionObservedAtUtc); Assert.Equal("ExactPositionTicket", result.ReconciliationSource);
    }

    [Fact]
    public async Task ExactOpenPositionReadback_WhenPositionIdentifierMismatches_DoesNotAdoptProtection()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, false, 123456, 654321, 20260817, "BTCUSDm", "Buy", .01m, 78693.81m, StopLoss: 78569.64m, TakeProfit: 78876.42m) }; var execution = Uncertain(); execution.BrokerSymbol = "BTCUSDm"; execution.PositionTicket = 123456; execution.PositionIdentifier = 123456; execution.FilledVolumeLots = .01m; execution.CurrentStopLoss = 78000m; execution.CurrentTakeProfit = 79000m; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.NotEqual(78569.64m, result.CurrentStopLoss); Assert.NotEqual(78876.42m, result.CurrentTakeProfit);
    }

    [Fact]
    public async Task PositionTicketWithoutIdentifier_NeverCallsNativeGetPosition()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = Uncertain(); execution.PositionTicket = 300; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Equal(0, bridge.PositionRequests); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task CloseWithoutPositiveIdentifier_IsRejectedBeforeBridgeCall()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge(); var execution = Uncertain(); execution.State = DemoExecutionState.Open; execution.PositionTicket = 300; execution.PositionIdentifier = 0; execution.EntryDealTicket = 200; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).CloseAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Equal(0, bridge.CloseRequests); Assert.Equal(200, result.EntryDealTicket);
    }

    [Theory]
    [InlineData("Order")]
    [InlineData("Magic")]
    [InlineData("Symbol")]
    [InlineData("Side")]
    [InlineData("NotEntry")]
    [InlineData("ZeroVolume")]
    [InlineData("ExcessVolume")]
    public async Task ExactEntryEvidenceViolation_RemainsReconciliationRequired(string violation)
    {
        await using var database = NewDatabase(); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; var bridge = new FakeBridge { ExactDeal = InvalidExactDeal(violation) }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task ExactEntryWithoutLivePositionOrFullExit_RemainsReconciliationRequired()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt)] }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.NotEqual(DemoExecutionState.Open, result.State); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task NativePositionHistoryExitWithAnotherIdentifier_IsIgnoredAndCannotClose()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), new(201, 101, 999, "XAUUSD", "Sell", 0, 0.01m, 2001m, entryAt.AddSeconds(1), "Exit", false, true)] }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Null(result.ExitDealTicket); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task PartialManualExit_RemainsReconciliationRequired()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), new(201, 101, 300, "XAUUSD", "Sell", 0, 0.005m, 2001m, entryAt.AddSeconds(1), "Exit", false, true)] }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task FirstExactExitReason_PersistsWithoutConflict()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), "SL")] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("SL", result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted); Assert.Equal(201, result.ExitDealTicket);
    }

    [Fact]
    public async Task FirstExactTpExitReason_PersistsWithoutConflict()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), "TP")] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("TP", result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted); Assert.Equal(201, result.ExitDealTicket); Assert.Equal("NativePositionHistory", result.ReconciliationSource);
    }

    [Fact]
    public async Task ExactClosedPositionHistory_PersistsSignedBrokerPnlTotals()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.BrokerCurrentProfit = 7m; execution.BrokerCurrentSwap = -0.2m; execution.BrokerCurrentPnlObservedAtUtc = entryAt;
        var entry = PositionHistoryEntry(entryAt) with { Profit = 0m, Commission = -0.35m, Swap = 0m, Fee = -0.02m }; var exit1 = PositionHistoryExit(entryAt.AddSeconds(1), "SL") with { ExecutedVolumeLots = .005m, Profit = 8m, Commission = -0.20m, Swap = -0.04m, Fee = 0m }; var exit2 = PositionHistoryExit(entryAt.AddSeconds(2), "SL") with { DealTicket = 202, OrderTicket = 102, ExecutedVolumeLots = .005m, Profit = 12.50m, Commission = -0.15m, Swap = -0.06m, Fee = -0.01m }; var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [entry, exit1, exit2], PositionHistoryAccountCurrency = "USD" };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("USD", result.BrokerAccountCurrency); Assert.Equal(20.50m, result.BrokerHistoryProfit); Assert.Equal(-0.70m, result.BrokerHistoryCommission); Assert.Equal(-0.10m, result.BrokerHistorySwap); Assert.Equal(-0.03m, result.BrokerHistoryFee); Assert.NotNull(result.BrokerHistoryPnlObservedAtUtc); Assert.Equal(0m, result.BrokerEntryProfit); Assert.Equal(-0.35m, result.BrokerEntryCommission); Assert.Equal(0m, result.BrokerEntrySwap); Assert.Equal(-0.02m, result.BrokerEntryFee); Assert.Null(result.BrokerCurrentProfit); Assert.Null(result.BrokerCurrentSwap); Assert.Null(result.BrokerCurrentPnlObservedAtUtc);
    }

    [Fact]
    public async Task ExactClosedPositionHistory_WithIncompletePnlEvidence_StillClosesWithoutInventingMoney()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.BrokerCurrentProfit = 7m; execution.BrokerCurrentSwap = -0.2m; execution.BrokerCurrentPnlObservedAtUtc = entryAt;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt) with { Profit = 0m, Commission = -0.35m, Swap = 0m, Fee = -0.02m }, PositionHistoryExit(entryAt.AddSeconds(1), "TP") with { Profit = 20.50m, Commission = -0.35m, Swap = -0.10m, Fee = null }], PositionHistoryAccountCurrency = "USD" };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("TP", result.NativeExitReason); Assert.Null(result.BrokerHistoryProfit); Assert.Null(result.BrokerHistoryCommission); Assert.Null(result.BrokerHistorySwap); Assert.Null(result.BrokerHistoryFee); Assert.Null(result.BrokerHistoryPnlObservedAtUtc); Assert.Null(result.BrokerCurrentProfit); Assert.Null(result.BrokerCurrentSwap); Assert.Null(result.BrokerCurrentPnlObservedAtUtc);
    }

    [Fact]
    public async Task ExactExitAtRequestedStopPrice_WithoutNativeReason_DoesNotInferSl()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.RequestedStopLoss = 78569.64m; execution.RequestedTakeProfit = 78876.42m;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), null) with { ExecutionPrice = 78569.64m }] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("NativePositionHistory", result.ReconciliationSource); Assert.Equal(201, result.ExitDealTicket); Assert.Null(result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Fact]
    public async Task ExactNativeExitReason_IsDurablyPersistedAcrossDatabaseReload()
    {
        var name = Guid.NewGuid().ToString(); var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(name).Options; await using (var database = new EmaBotDbContext(options)) { var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), "SL")] }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync(); var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default); Assert.Equal(DemoExecutionState.Closed, result!.State); }
        await using var fresh = new EmaBotDbContext(options); var persisted = await fresh.DemoExecutions.SingleAsync(item => item.EntryDealTicket == 200);
        Assert.Equal(DemoExecutionState.Closed, persisted.State); Assert.Equal("SL", persisted.NativeExitReason); Assert.False(persisted.NativeExitReasonConflicted); Assert.Equal(201, persisted.ExitDealTicket); Assert.Equal("NativePositionHistory", persisted.ReconciliationSource); Assert.NotNull(persisted.ClosedAtUtc);
    }

    [Fact]
    public async Task ExactBrokerPnlHistory_IsDurablyPersistedAcrossDatabaseReload()
    {
        var name = Guid.NewGuid().ToString(); var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(name).Options; await using (var database = new EmaBotDbContext(options)) { var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt) with { Profit = 0m, Commission = -0.35m, Swap = 0m, Fee = -0.02m }, PositionHistoryExit(entryAt.AddSeconds(1), "TP") with { Profit = 20.50m, Commission = -0.35m, Swap = -0.10m, Fee = -0.01m }], PositionHistoryAccountCurrency = "USD" }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync(); var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default); Assert.Equal(DemoExecutionState.Closed, result!.State); }
        await using var fresh = new EmaBotDbContext(options); var persisted = await fresh.DemoExecutions.SingleAsync(item => item.EntryDealTicket == 200);
        Assert.Equal("USD", persisted.BrokerAccountCurrency); Assert.Equal(20.50m, persisted.BrokerHistoryProfit); Assert.Equal(-0.70m, persisted.BrokerHistoryCommission); Assert.Equal(-0.10m, persisted.BrokerHistorySwap); Assert.Equal(-0.03m, persisted.BrokerHistoryFee); Assert.NotNull(persisted.BrokerHistoryPnlObservedAtUtc);
    }

    [Fact]
    public async Task LaterNullReason_DoesNotEraseProvenReasonOrCreateConflict()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.NativeExitReason = "SL";
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), null)] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("SL", result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Fact]
    public async Task LaterSameExactReason_DoesNotCreateConflict()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.NativeExitReason = "SL";
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), "SL")] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("SL", result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Fact]
    public async Task LaterDifferentExactReason_PreservesAuditValueButMarksConflict()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.NativeExitReason = "SL";
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), "TP")] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("SL", result.NativeExitReason); Assert.True(result.NativeExitReasonConflicted); Assert.Contains("closure", result.ReconciliationNote, StringComparison.OrdinalIgnoreCase); Assert.Contains("unusable", result.ReconciliationNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConflictFlag_IsSticky()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100; execution.NativeExitReason = "SL"; execution.NativeExitReasonConflicted = true;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), null)] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal("SL", result.NativeExitReason); Assert.True(result.NativeExitReasonConflicted);
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Mobile")]
    [InlineData("Web")]
    public async Task ExactPositionHistory_ManualExitWithDifferentMagic_PersistsNativeReason(string nativeReason)
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), PositionHistoryExit(entryAt.AddSeconds(1), nativeReason) with { MagicNumber = 0 }] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal(nativeReason, result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Theory]
    [InlineData("WrongPosition")]
    [InlineData("WrongSymbol")]
    public async Task ExactPositionHistory_MismatchedOwnershipNeverSetsReason(string mismatch)
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100;
        var exit = PositionHistoryExit(entryAt.AddSeconds(1), "Client");
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt), PositionHistory = [PositionHistoryEntry(entryAt), mismatch == "WrongPosition" ? exit with { PositionIdentifier = 999 } : exit with { BrokerSymbol = "OTHERm" }] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.NotEqual(DemoExecutionState.Closed, result!.State); Assert.Null(result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Fact]
    public async Task BoundedHistory_StillRequiresItsExistingStrictOwnership()
    {
        await using var database = NewDatabase(); var execution = Uncertain(); execution.EntryDealTicket = null; execution.DealTicket = null;
        var at = DateTimeOffset.UtcNow.AddSeconds(-10);
        var bridge = new FakeBridge { History = [Evidence(100, 200, 300), Evidence(101, 201, 300) with { Side = "Sell", MagicNumber = 0, ExecutedAtUtc = at.AddSeconds(1), EntryType = "Exit", IsEntry = false, IsExit = true, NativeReason = "Client" }] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.NotEqual("Client", result!.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Fact]
    public async Task EntryDealNativeReason_NeverBecomesExitReason()
    {
        await using var database = NewDatabase(); var entryAt = DateTimeOffset.UtcNow.AddMinutes(-1); var execution = Uncertain(); execution.EntryDealTicket = 200; execution.OrderTicket = 100;
        var bridge = new FakeBridge { ExactDeal = ExactDeal(entryAt) with { NativeReason = "SL" }, PositionHistory = [PositionHistoryEntry(entryAt)] };
        database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.NotEqual(DemoExecutionState.Closed, result!.State); Assert.Null(result.NativeExitReason); Assert.False(result.NativeExitReasonConflicted);
    }

    [Theory]
    [InlineData("SL", false, true)]
    [InlineData("SL", true, false)]
    [InlineData("TP", false, false)]
    [InlineData("Client", false, false)]
    [InlineData(null, false, false)]
    public void ReentryEvidence_RequiresUnconflictedStopLossOnly(string? nativeReason, bool conflicted, bool expected)
    {
        var execution = Uncertain(); execution.NativeExitReason = nativeReason; execution.NativeExitReasonConflicted = conflicted;
        Assert.Equal(expected, DemoStrategyReentryEvidence.IsEligibleExitReason(execution));
    }

    [Fact]
    public async Task AcceptedButInconclusiveClose_RemainsCloseRequested()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { CloseResult = new(true, "DonePartial", "partial", 201, 0.005m, 2001m, false) }; var execution = Uncertain(); execution.State = DemoExecutionState.Open; execution.PositionTicket = 300; execution.PositionIdentifier = 300; execution.EntryDealTicket = 200; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).CloseAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.CloseRequested, result!.State); Assert.Equal(200, result.EntryDealTicket); Assert.Equal(201, result.ExitDealTicket);
    }

    [Fact]
    public async Task RejectedClose_RemainsRecoverableAndNeverBecomesRejected()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { CloseResult = new(false, "Rejected", "close denied", null, null, null) }; var execution = Uncertain(); execution.State = DemoExecutionState.Open; execution.PositionTicket = 300; execution.PositionIdentifier = 301; execution.EntryDealTicket = 200; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).CloseAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.NotEqual(DemoExecutionState.Rejected, result.State); Assert.Equal(200, result.EntryDealTicket); Assert.Equal(300, result.PositionTicket); Assert.Equal(301, result.PositionIdentifier); Assert.Equal("Rejected", result.BrokerRetcode); Assert.Equal("close denied", result.BrokerMessage); Assert.Equal(1, bridge.CloseRequests); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task TransportFailureAfterSubmitting_RemainsReconciliationRequired()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { SubmitException = new Mt5ExecutionBridgeUnavailableException("disconnected") }; var service = Service(database, bridge); var execution = await service.SubmitAsync(new(Guid.NewGuid(), "XAUUSD", "Buy", 0.01m, null, null), default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, execution.State); Assert.Equal(1, bridge.SubmitCount);
    }

    [Fact]
    public async Task CloseBridgeUnavailable_RequiresReconciliationAndNeverRetries()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { CloseException = new Mt5ExecutionBridgeUnavailableException("disconnected") }; var execution = Uncertain(); execution.State = DemoExecutionState.Open; execution.PositionTicket = 300; execution.PositionIdentifier = 301; execution.EntryDealTicket = 200; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).CloseAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Equal(1, bridge.CloseRequests); Assert.Equal(0, bridge.SubmitCount); Assert.Equal(200, result.EntryDealTicket); Assert.Equal(300, result.PositionTicket); Assert.Equal(301, result.PositionIdentifier);
    }

    [Fact]
    public async Task SubmitResultWithTicketButNoIdentifier_DoesNotBecomeOpen()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { SubmitResult = new(true, "Done", "accepted", 100, 200, null, 123, 0.01m, 2000m, false, true) }; var service = Service(database, bridge);

        var execution = await service.SubmitAsync(new(Guid.NewGuid(), "XAUUSD", "Buy", 0.01m, null, null), default);

        Assert.Equal(DemoExecutionState.BrokerAccepted, execution.State); Assert.Equal(123, execution.PositionTicket); Assert.Null(execution.PositionIdentifier); Assert.Equal(1, bridge.SubmitCount);
    }

    [Fact]
    public async Task OrderCheckDemoSafetyGate_IsRejectedBeforeSubmitAndNotReconciliationRequired()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { OrderCheckException = new Mt5ExecutionBridgeRejectedException("DemoSafetyGate", "EA demo execution is disabled.", false) };

        var execution = await Service(database, bridge).SubmitAsync(new(Guid.NewGuid(), "XAUUSD", "Buy", 0.01m, null, null), default);

        Assert.Equal(DemoExecutionState.Rejected, execution.State); Assert.NotEqual(DemoExecutionState.ReconciliationRequired, execution.State); Assert.Equal("DemoSafetyGate", execution.BrokerRetcode); Assert.Equal("EA demo execution is disabled.", execution.BrokerMessage); Assert.Null(execution.PreflightAtUtc); Assert.Null(execution.SubmittedAtUtc); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task ExplicitOrderCheckBridgeRejection_NeverCallsSubmitMarketOrder()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { OrderCheckException = new Mt5ExecutionBridgeRejectedException("OwnershipRejected", "preflight denied", false) };

        var execution = await Service(database, bridge).SubmitAsync(new(Guid.NewGuid(), "XAUUSD", "Buy", 0.01m, null, null), default);

        Assert.Equal(DemoExecutionState.Rejected, execution.State); Assert.Equal("OwnershipRejected", execution.BrokerRetcode); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task ExplicitCloseBridgeRejection_RemainsRecoverableAndNeverBecomesRejected()
    {
        await using var database = NewDatabase(); var bridge = new FakeBridge { CloseException = new Mt5ExecutionBridgeRejectedException("DemoSafetyGate", "EA gate rejected close.", false) }; var execution = Uncertain(); execution.State = DemoExecutionState.Open; execution.PositionTicket = 300; execution.PositionIdentifier = 301; execution.EntryDealTicket = 200; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).CloseAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.NotEqual(DemoExecutionState.Rejected, result.State); Assert.Equal(200, result.EntryDealTicket); Assert.Equal(300, result.PositionTicket); Assert.Equal(301, result.PositionIdentifier); Assert.Equal("DemoSafetyGate", result.BrokerRetcode); Assert.Equal(1, bridge.CloseRequests); Assert.Equal(0, bridge.SubmitCount);
    }

    [Fact]
    public async Task SimilarButNotExactLegacyPhysicalMarker_IsRejected()
    {
        await using var database = NewDatabase(); var execution = Uncertain(); execution.CorrelationMarker = "EMA-f05c2344bd5e48c591d0b3c7fb26cb51"; var physical = DemoExecutionMarker.BrokerMarker(execution.CorrelationMarker); var nearMatch = physical[..^1] + "9"; var bridge = new FakeBridge { History = [Evidence(100, 200, 300) with { CorrelationMarker = nearMatch }] }; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();

        var result = await Service(database, bridge).ReconcileAsync(execution.ClientExecutionId, default);

        Assert.Equal(DemoExecutionState.ReconciliationRequired, result!.State); Assert.Equal(0, bridge.SubmitCount);
    }

    private static EmaBotDbContext NewDatabase() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DemoExecutionService Service(EmaBotDbContext database, FakeBridge bridge) => new(database, bridge, Options.Create(new DemoExecutionOptions { Enabled = true, DemoOnly = true, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo" }), TimeProvider.System, NullLogger<DemoExecutionService>.Instance);
    private static DemoExecution Uncertain() => new() { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Submitting, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo", BrokerSymbol = "XAUUSD", Side = "Buy", VolumeLots = 0.01m, MagicNumber = 20260817, CorrelationMarker = "EMA-fixed", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), SubmittedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30) };
    private static Mt5ExecutionHistoryEvidence Evidence(long order, long deal, long position) => new(order, deal, position, position, "XAUUSD", "Buy", 20260817, "EMA-fixed", 0.01m, 2000m, DateTimeOffset.UtcNow.AddSeconds(-10), "Entry", "HistoryDeal", true, false, false);
    private static Mt5ExactDealPayload ExactDeal(DateTimeOffset at) => new(200, 100, 300, null, "XAUUSD", "Buy", 20260817, 0.01m, 2000m, at, true, false, false);
    private static Mt5PositionHistoryDeal PositionHistoryEntry(DateTimeOffset at) => new(200, 100, 300, "XAUUSD", "Buy", 20260817, 0.01m, 2000m, at, "Entry", true, false);
    private static Mt5PositionHistoryDeal PositionHistoryExit(DateTimeOffset at, string? nativeReason) => new(201, 101, 300, "XAUUSD", "Sell", 20260817, 0.01m, 2001m, at, "Exit", false, true, NativeReason: nativeReason);
    private static Mt5ExactDealPayload InvalidExactDeal(string violation)
    {
        var valid = ExactDeal(DateTimeOffset.UtcNow.AddMinutes(-1)) with { PositionTicket = 301, IsPositionOpen = true };
        return violation switch
        {
            "Order" => valid with { OrderTicket = 101 },
            "Magic" => valid with { MagicNumber = 1 },
            "Symbol" => valid with { BrokerSymbol = "XAUUSDm" },
            "Side" => valid with { Side = "Sell" },
            "NotEntry" => valid with { IsEntry = false, IsExit = true },
            "ZeroVolume" => valid with { ExecutedVolumeLots = 0m },
            "ExcessVolume" => valid with { ExecutedVolumeLots = 0.02m },
            _ => throw new ArgumentOutOfRangeException(nameof(violation))
        };
    }

    private sealed class FakeBridge : IMt5ExecutionBridgeClient
    {
        public event Action? Connected { add { } remove { } }
        public int SubmitCount { get; private set; }
        public int ExecutionAccountRequests { get; private set; }
        public int HistoryRequests { get; private set; }
        public int PositionRequests { get; private set; }
        public int CloseRequests { get; private set; }
        public IReadOnlyList<Mt5ExecutionHistoryEvidence> History { get; init; } = [];
        public Mt5ExecutionPositionPayload PositionResult { get; init; } = new(true, false, 123, 456, 20260817, "XAUUSD", "Buy", 0.01m, 1.1m);
        public Mt5ExactDealPayload? ExactDeal { get; init; }
        public IReadOnlyList<Mt5PositionHistoryDeal> PositionHistory { get; init; } = [];
        public string? PositionHistoryAccountCurrency { get; init; }
        public Mt5ClosePositionResultPayload CloseResult { get; init; } = new(true, "Done", "closed", 789, 0.01m, 1.1m, true);
        public Mt5SubmitOrderResultPayload SubmitResult { get; init; } = new(true, "Done", "ok", 100, 200, 123, 123, 0.01m, 1.1m, false, true);
        public Mt5ExecutionAccountPayload AccountResult { get; init; } = new("demo-fingerprint", "Exness-Demo", "Demo", true, true, true, true, "E11.7A1A1", true, true);
        public Exception? OrderCheckException { get; init; }
        public Exception? SubmitException { get; init; }
        public Exception? CloseException { get; init; }
        public Mt5ExecutionHistoryRequest? LastHistoryRequest { get; private set; }
        public Mt5ExactDealRequest? LastExactDealRequest { get; private set; }
        public bool IsConnected => true;
        public Mt5ExecutionBridgeStatus GetStatus() => new(true, true, "test", "demo-fingerprint", "Exness-Demo", "Demo", null);
        public Task<Mt5ExecutionEnvelope> SendAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token)
        {
            object body = operation switch
            {
                Mt5ExecutionOperation.GetExecutionAccount => Account(),
                Mt5ExecutionOperation.OrderCheck => OrderCheck(),
                Mt5ExecutionOperation.SubmitMarketOrder => Submitted(),
                Mt5ExecutionOperation.GetExecutionHistory => HistoryResponse((Mt5ExecutionHistoryRequest)payload!),
                Mt5ExecutionOperation.GetPosition => PositionResponse(),
                Mt5ExecutionOperation.GetExactDeal => ExactDealResponse((Mt5ExactDealRequest)payload!),
                Mt5ExecutionOperation.GetPositionHistory => new Mt5PositionHistoryPayload(((Mt5PositionHistoryRequest)payload!).PositionIdentifier, PositionHistory, PositionHistoryAccountCurrency),
                Mt5ExecutionOperation.ClosePosition => CloseResponse(),
                _ => throw new InvalidOperationException($"Unexpected operation {operation}.")
            };
            return Task.FromResult(Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Response, operation, Guid.NewGuid(), body, TimeProvider.System));
        }
        private Mt5SubmitOrderResultPayload Submitted() { SubmitCount++; if (SubmitException is not null) throw SubmitException; return SubmitResult; }
        private Mt5OrderCheckPayload OrderCheck() { if (OrderCheckException is not null) throw OrderCheckException; return new(true, "Done", "ok", 1m, 1.1m); }
        private Mt5ExecutionAccountPayload Account() { ExecutionAccountRequests++; return AccountResult; }
        private Mt5ExecutionHistoryPayload HistoryResponse(Mt5ExecutionHistoryRequest request) { HistoryRequests++; LastHistoryRequest = request; return new(History); }
        private Mt5ExactDealPayload ExactDealResponse(Mt5ExactDealRequest request) { LastExactDealRequest = request; return ExactDeal ?? throw new Mt5ExecutionBridgeException("No exact deal fixture configured."); }
        private Mt5ExecutionPositionPayload PositionResponse() { PositionRequests++; return PositionResult; }
        private Mt5ClosePositionResultPayload CloseResponse() { CloseRequests++; if (CloseException is not null) throw CloseException; return CloseResult; }
    }
}
