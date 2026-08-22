using System.Text.Json;
using EmaBot.Api.Controllers;
using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategySessionsControllerTests
{
    [Fact]
    public async Task Safety_MapsMarketDataAndExecutionGatesSeparately()
    {
        var (controller, _) = CreateController(new DemoExecutionReadiness(true, "ready", Account()), new FakeBridge(enabled: false));
        var result = Assert.IsType<OkObjectResult>((await controller.Safety(CancellationToken.None)).Result);
        var safety = Assert.IsType<DemoAutomationSafetyResponse>(result.Value);

        Assert.False(safety.MarketDataBridgeEnabled);
        Assert.Equal("Connected", safety.MarketDataConnectionState);
        Assert.True(safety.ExecutionBridgeEnabled);
        Assert.True(safety.DotNetDemoExecutionEnabled);
        Assert.True(safety.EaDemoExecutionEnabled);
        Assert.True(safety.EaDemoExecutionAllowed);
    }

    [Fact]
    public async Task Safety_ReportsAccountAndExpertTradeAllowedWithoutIdentityFields()
    {
        var (controller, _) = CreateController(new DemoExecutionReadiness(false, "not ready", Account()), new FakeBridge(enabled: true));
        var result = Assert.IsType<OkObjectResult>((await controller.Safety(CancellationToken.None)).Result);
        var safety = Assert.IsType<DemoAutomationSafetyResponse>(result.Value);
        var json = JsonSerializer.Serialize(safety);

        Assert.True(safety.AccountTradeAllowed);
        Assert.True(safety.ExpertTradeAllowed);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Current_ReturnsRunningBeforeInterrupted()
    {
        var (controller, database) = CreateController();
        database.DemoStrategySessions.AddRange(Session(DemoStrategySessionStatus.Interrupted, 1), Session(DemoStrategySessionStatus.Running, 2)); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await controller.Current(CancellationToken.None));
        Assert.Equal("Running", Assert.IsType<DemoStrategySessionResponse>(result.Value).Status);
    }

    [Fact]
    public async Task Current_ReturnsInterruptedWhenNoRunning()
    {
        var (controller, database) = CreateController(); database.DemoStrategySessions.Add(Session(DemoStrategySessionStatus.Interrupted, 1)); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await controller.Current(CancellationToken.None));
        Assert.Equal("Interrupted", Assert.IsType<DemoStrategySessionResponse>(result.Value).Status);
    }

    [Fact]
    public async Task Current_ReturnsNoContentWhenNoActiveSession()
    {
        var (controller, _) = CreateController(); Assert.IsType<NoContentResult>(await controller.Current(CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsNewestFirstAndClampsTakeToSupportedRange()
    {
        var (controller, database) = CreateController();
        for (var index = 0; index < 101; index++) database.DemoStrategySessions.Add(Session(DemoStrategySessionStatus.Stopped, index)); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>((await controller.List(1000, CancellationToken.None)).Result); var list = Assert.IsAssignableFrom<IReadOnlyList<DemoStrategySessionSummaryResponse>>(result.Value);
        Assert.Equal(100, list.Count); Assert.True(list[0].CreatedAtUtc > list[1].CreatedAtUtc);
    }

    [Fact]
    public async Task Detail_ReturnsImmutableSessionSnapshot()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.AutomationEnabledAtCreation = false; session.MinEmaGapPercent = 1.2m; session.MaxStopDistancePercent = 3.4m; database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)); var detail = Assert.IsType<DemoStrategySessionResponse>(result.Value);
        Assert.False(detail.AutomationEnabledAtCreation); Assert.Equal(1.2m, detail.MinEmaGapPercent); Assert.Equal(3.4m, detail.MaxStopDistancePercent);
    }

    [Fact]
    public async Task Detail_IncludesLinkedSafeExecutionAndManagementActions()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); var symbol = session.Symbols.Single(); var execution = new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Open, BrokerSymbol = "XAUUSDm", Side = "Buy", VolumeLots = .01m, MagicNumber = 1, EntryDealTicket = 2, CreatedAtUtc = DateTimeOffset.UtcNow, ManagementActions = [new() { ClientManagementActionId = Guid.NewGuid(), Kind = DemoExecutionManagementActionKind.ModifyProtection, State = DemoExecutionManagementActionState.Applied, CreatedAtUtc = DateTimeOffset.UtcNow }] }; var intent = new DemoStrategyIntent { Direction = SignalDirection.Long, ClientExecutionId = execution.ClientExecutionId, Status = DemoStrategyIntentStatus.ExecutionLinked, CrossoverTimeUtc = DateTimeOffset.UtcNow, SignalTimeUtc = DateTimeOffset.UtcNow, ExpectedEntryOpenUtc = DateTimeOffset.UtcNow, StructuralStopLoss = 1m, StopSourceTimeUtc = DateTimeOffset.UtcNow, IntendedVolumeLots = .01m, DemoExecution = execution }; symbol.Intents.Add(intent); database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)); var linked = Assert.Single(Assert.Single(Assert.IsType<DemoStrategySessionResponse>(result.Value).Symbols).RecentIntents).Execution;
        Assert.NotNull(linked); Assert.Equal(2, linked.EntryDealTicket); Assert.Single(linked.ManagementActions);
    }

    [Fact]
    public async Task Detail_IncludesPositionManagementAndB3BFields()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); var symbol = session.Symbols.Single(); var execution = new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Open, BrokerSymbol = "XAUUSDm", Side = "Buy", VolumeLots = .01m, CreatedAtUtc = DateTimeOffset.UtcNow }; var intent = new DemoStrategyIntent { Direction = SignalDirection.Long, ClientExecutionId = execution.ClientExecutionId, Status = DemoStrategyIntentStatus.ExecutionLinked, CrossoverTimeUtc = DateTimeOffset.UtcNow, SignalTimeUtc = DateTimeOffset.UtcNow, ExpectedEntryOpenUtc = DateTimeOffset.UtcNow, StructuralStopLoss = 1m, StopSourceTimeUtc = DateTimeOffset.UtcNow, IntendedVolumeLots = .01m, IsReentry = true, ReentryAgeBars = 3, DemoExecution = execution }; symbol.Intents.Add(intent); session.PositionManagement.Add(new DemoStrategyPositionManagement { DemoStrategySessionSymbol = symbol, DemoStrategyIntent = intent, DemoExecution = execution, State = DemoStrategyPositionManagementState.Active, OriginalEntryPrice = 1m, OriginalStopLoss = .9m, OriginalTakeProfit = 1.1m, TakeProfitExtensionState = DemoStrategyTargetExtensionState.Applied, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }); database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)); var response = Assert.IsType<DemoStrategySessionResponse>(result.Value); Assert.True(Assert.Single(response.Symbols).RecentIntents.Single().IsReentry); Assert.Equal(3, response.Symbols.Single().RecentIntents.Single().ReentryAgeBars); Assert.Equal("Applied", Assert.Single(response.Symbols.Single().Management).TakeProfitExtensionState);
    }

    [Fact]
    public async Task Detail_SerializationDoesNotExposeExpectedAccountFingerprintOrExpectedServer()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); database.DemoStrategySessions.Add(session); await database.SaveChangesAsync(); var result = Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)); var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("expectedAccountFingerprint", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("expectedServer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithEnabledExactMt5Symbol_CreatesSession()
    {
        var (controller, database) = CreateController(); database.MonitoredSymbols.Add(new MonitoredSymbol { Symbol = "BTCUSDm", Source = MarketDataSource.Mt5Exness, IsEnabled = true }); await database.SaveChangesAsync();
        var result = Assert.IsType<CreatedAtActionResult>((await controller.Create(new("3m", ["BTCUSDm"], 100m), CancellationToken.None)).Result); var response = Assert.IsType<DemoStrategySessionResponse>(result.Value);
        Assert.Equal("3m", response.Interval); Assert.Equal("BTCUSDm", Assert.Single(response.Symbols).BrokerSymbol); Assert.True(response.AutomationEnabledAtCreation); Assert.Equal(.01m, response.FixedLots); Assert.NotNull(await database.DemoStrategySessions.FindAsync(response.Id));
    }

    [Fact]
    public async Task Create_RequiresExactOrdinalMt5Symbol()
    {
        var (controller, database) = CreateController(); database.MonitoredSymbols.Add(new MonitoredSymbol { Symbol = "BTCUSDm", Source = MarketDataSource.Mt5Exness, IsEnabled = true }); await database.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>((await controller.Create(new("3m", ["btcusdm"], 100m), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Create_RequiresPositiveInitialAllocation()
    {
        var (controller, database) = CreateController(); database.MonitoredSymbols.Add(new MonitoredSymbol { Symbol = "BTCUSDm", Source = MarketDataSource.Mt5Exness, IsEnabled = true }); await database.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>((await controller.Create(new("3m", ["BTCUSDm"], 0m), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Create_OvernightAllocationModeRequiresExactlyOneSymbol()
    {
        var (controller, database) = CreateController(); database.MonitoredSymbols.AddRange(new MonitoredSymbol { Symbol = "BTCUSDm", Source = MarketDataSource.Mt5Exness, IsEnabled = true }, new MonitoredSymbol { Symbol = "XAUUSDm", Source = MarketDataSource.Mt5Exness, IsEnabled = true }); await database.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>((await controller.Create(new("3m", ["BTCUSDm", "XAUUSDm"], 100m), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task InitialAllocation100_NoHistory_HasBalance100()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.InitialAllocation = 100m; database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)); var response = Assert.IsType<DemoStrategySessionResponse>(result.Value);
        Assert.Empty(await database.DemoExecutions.ToListAsync());
        Assert.Equal(100m, response.InitialAllocation); Assert.Equal(0m, response.Budget.RealizedPnl); Assert.Equal(0m, response.Budget.UnrealizedPnl); Assert.Equal(100m, response.Budget.Balance); Assert.Equal(100m, response.Budget.Equity); Assert.True(response.Budget.EvidenceReady);
    }

    [Fact]
    public async Task ClosedExactLoss_ReducesNextBalance()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.InitialAllocation = 100m; var execution = AddClosedExecutionEvidence(session, -25m); database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var response = Assert.IsType<DemoStrategySessionResponse>(Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)).Value); var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);
        Assert.True(evidence.Available); Assert.Equal(-25m, evidence.Amount); Assert.Equal("USD", response.Budget.AccountCurrency); Assert.Equal(-25m, response.Budget.RealizedPnl); Assert.Equal(0m, response.Budget.UnrealizedPnl); Assert.Equal(75m, response.Budget.Balance); Assert.Equal(75m, response.Budget.Equity); Assert.True(response.Budget.EvidenceReady);
    }

    [Fact]
    public async Task ClosedExactProfit_IncreasesNextBalance()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.InitialAllocation = 100m; var execution = AddClosedExecutionEvidence(session, 20m); database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var response = Assert.IsType<DemoStrategySessionResponse>(Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)).Value); var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);
        Assert.True(evidence.Available); Assert.Equal(20m, evidence.Amount); Assert.Equal("USD", response.Budget.AccountCurrency); Assert.Equal(20m, response.Budget.RealizedPnl); Assert.Equal(0m, response.Budget.UnrealizedPnl); Assert.Equal(120m, response.Budget.Balance); Assert.Equal(120m, response.Budget.Equity); Assert.True(response.Budget.EvidenceReady);
    }

    [Fact]
    public async Task IncompleteClosedBrokerEvidence_FailsClosed()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.InitialAllocation = 100m; var execution = AddClosedExecutionEvidence(session, -25m); execution.BrokerHistoryFee = null; database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution); var response = Assert.IsType<DemoStrategySessionResponse>(Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)).Value);
        Assert.False(evidence.Available); Assert.False(response.Budget.EvidenceReady); Assert.NotNull(response.Budget.Reason); Assert.Null(response.Budget.RealizedPnl); Assert.Null(response.Budget.Balance); Assert.Null(response.Budget.Equity);
    }

    [Fact]
    public async Task OtherSessionExecution_DoesNotAffectBudget()
    {
        var (controller, database) = CreateController(); var first = Session(DemoStrategySessionStatus.Created, 1); first.InitialAllocation = 100m; var second = Session(DemoStrategySessionStatus.Created, 2); second.InitialAllocation = 100m; AddClosedExecutionEvidence(second, 20m); database.DemoStrategySessions.AddRange(first, second); await database.SaveChangesAsync();
        var response = Assert.IsType<DemoStrategySessionResponse>(Assert.IsType<OkObjectResult>(await controller.Get(first.Id, CancellationToken.None)).Value);
        Assert.Equal(0m, response.Budget.RealizedPnl); Assert.Equal(100m, response.Budget.Balance); Assert.Equal(100m, response.Budget.Equity); Assert.True(response.Budget.EvidenceReady);
    }

    [Fact]
    public async Task RejectedNoFillExecution_CountsAsZero()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.InitialAllocation = 100m; AddRejectedExecution(session); database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();

        var response = Assert.IsType<DemoStrategySessionResponse>(Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)).Value);

        Assert.Equal(0m, response.Budget.RealizedPnl); Assert.Equal(100m, response.Budget.Balance); Assert.Equal(100m, response.Budget.Equity); Assert.True(response.Budget.EvidenceReady);
    }

    [Fact]
    public async Task RejectedWithFillEvidence_FailsClosed()
    {
        var (controller, database) = CreateController(); var session = Session(DemoStrategySessionStatus.Created, 1); session.InitialAllocation = 100m; var execution = AddRejectedExecution(session); execution.FilledVolumeLots = .01m; database.DemoStrategySessions.Add(session); await database.SaveChangesAsync();

        var response = Assert.IsType<DemoStrategySessionResponse>(Assert.IsType<OkObjectResult>(await controller.Get(session.Id, CancellationToken.None)).Value);

        Assert.False(response.Budget.EvidenceReady); Assert.Contains("ambiguous", response.Budget.Reason, StringComparison.OrdinalIgnoreCase); Assert.Null(response.Budget.Balance); Assert.Null(response.Budget.Equity);
    }

    private static (DemoStrategySessionsController Controller, EmaBotDbContext Database) CreateController(DemoExecutionReadiness? readiness = null, IMt5BridgeRequestClient? bridge = null)
    {
        var context = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = Options.Create(new DemoStrategyAutomationOptions { Enabled = true, ManagementEnabled = true, FixedLots = .01m });
        var coordinator = new DemoStrategyCoordinator(null!, null!, null!, null!, null!, options, null!);
        var settings = new TradingSettingsService(context, Options.Create(new TradingDefaultsOptions()));
        return (new DemoStrategySessionsController(context, settings, coordinator, options, Options.Create(new DemoExecutionOptions { Enabled = true, DemoOnly = true }), Options.Create(new Mt5ExecutionBridgeOptions { Enabled = true }), new FakeExecutionService(readiness ?? new(false, "not ready")), bridge ?? new FakeBridge(true)), context);
    }
    private static DemoExecution AddClosedExecutionEvidence(DemoStrategySession session, decimal brokerHistoryAmount, string currency = "USD")
    {
        var at = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero); var execution = new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Closed, BrokerSymbol = session.Symbols.Single().BrokerSymbol, Side = "Buy", VolumeLots = .01m, BrokerAccountCurrency = currency, BrokerHistoryProfit = brokerHistoryAmount, BrokerHistoryCommission = 0m, BrokerHistorySwap = 0m, BrokerHistoryFee = 0m, BrokerHistoryPnlObservedAtUtc = at, CreatedAtUtc = at };
        session.Symbols.Single().Intents.Add(new DemoStrategyIntent { Direction = SignalDirection.Long, ClientExecutionId = execution.ClientExecutionId, Status = DemoStrategyIntentStatus.ExecutionLinked, CrossoverTimeUtc = at, SignalTimeUtc = at, ExpectedEntryOpenUtc = at, StructuralStopLoss = 1m, StopSourceTimeUtc = at, IntendedVolumeLots = .01m, DemoExecution = execution }); return execution;
    }
    private static DemoExecution AddRejectedExecution(DemoStrategySession session)
    {
        var at = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero); var execution = new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Rejected, BrokerSymbol = session.Symbols.Single().BrokerSymbol, Side = "Buy", VolumeLots = .01m, BrokerRetcode = "rejected", BrokerMessage = "test rejection", CreatedAtUtc = at };
        session.Symbols.Single().Intents.Add(new DemoStrategyIntent { Direction = SignalDirection.Long, ClientExecutionId = execution.ClientExecutionId, Status = DemoStrategyIntentStatus.Rejected, CrossoverTimeUtc = at, SignalTimeUtc = at, ExpectedEntryOpenUtc = at, StructuralStopLoss = 1m, StopSourceTimeUtc = at, IntendedVolumeLots = .01m, DemoExecution = execution }); return execution;
    }

    private static DemoStrategySession Session(DemoStrategySessionStatus status, int offset) => new() { Interval = "3m", Status = status, CreatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(offset), FixedLots = .01m, RiskReward = 2m, Symbols = [new() { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm" }] };

    private static Mt5ExecutionAccountPayload Account() => new("not-exported", "not-exported", "Demo", true, true, true, true);

    private sealed class FakeExecutionService(DemoExecutionReadiness readiness) : IDemoExecutionService
    {
        public Task<DemoExecutionReadiness> ReadinessAsync(CancellationToken token) => Task.FromResult(readiness);
        public Task<DemoExecution> SubmitAsync(SubmitDemoOrder request, CancellationToken token) => throw new NotSupportedException();
        public Task<DemoExecution?> ReconcileAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<DemoExecution?> CloseAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<DemoExecutionManagementAction> ModifyProtectionAsync(ModifyDemoProtection request, CancellationToken token) => throw new NotSupportedException();
        public Task<DemoExecutionManagementAction?> ReconcileManagementActionAsync(Guid clientManagementActionId, CancellationToken token) => throw new NotSupportedException();
        public Task<DemoExecutionManagementAction?> FailClosedManagementActionAsync(Guid clientManagementActionId, CancellationToken token) => throw new NotSupportedException();
        public Task<DemoExecution?> GetAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class FakeBridge(bool enabled) : IMt5BridgeRequestClient
    {
        public bool IsConnected => true;
        public Mt5BridgeStatus GetStatus() => new(enabled, 1, "safe", Mt5BridgeConnectionState.Connected, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        public Task<Mt5BridgeEnvelope> SendAsync(Mt5BridgeOperation operation, object? payload, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
