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
    public void DemoExecutionModel_HasImmutableUniqueClientIdAndNoPaperRelationship()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = database.Model.FindEntityType(typeof(DemoExecution))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(DemoExecution.ClientExecutionId));
        Assert.Empty(entity.GetForeignKeys());
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
        await using var database = NewDatabase(); var bridge = new FakeBridge { PositionResult = new(true, "Done", "open", 123, 456, 0.01m, 2000m, false, 100, 123, false, true) }; var service = Service(database, bridge); var execution = Uncertain(); execution.PositionTicket = 123; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Open, result!.State); Assert.Equal("ExactPositionTicket", result.ReconciliationSource); Assert.Equal(0, bridge.HistoryRequests);
    }

    [Fact]
    public async Task HistoryEntryAndExit_RepairsAmbiguousCloseToClosed()
    {
        await using var database = NewDatabase(); var entry = Evidence(100, 200, 300); var exit = Evidence(100, 201, 300) with { Side = "Sell", IsEntry = false, IsExit = true, EntryType = "Exit", ExecutedAtUtc = DateTimeOffset.UtcNow }; var bridge = new FakeBridge { History = [entry, exit], PositionResult = new(true, "Done", "closed", null, null, null, null, true) }; var service = Service(database, bridge); var execution = Uncertain(); execution.State = DemoExecutionState.ReconciliationRequired; execution.PositionTicket = 300; database.DemoExecutions.Add(execution); await database.SaveChangesAsync();
        var result = await service.ReconcileAsync(execution.ClientExecutionId, default);
        Assert.Equal(DemoExecutionState.Closed, result!.State); Assert.Equal(201, result.ExitDealTicket); Assert.NotNull(result.ClosedAtUtc); Assert.Equal(0, bridge.SubmitCount);
    }

    private static EmaBotDbContext NewDatabase() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DemoExecutionService Service(EmaBotDbContext database, FakeBridge bridge) => new(database, bridge, Options.Create(new DemoExecutionOptions { Enabled = true, DemoOnly = true, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo" }), TimeProvider.System, NullLogger<DemoExecutionService>.Instance);
    private static DemoExecution Uncertain() => new() { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Submitting, ExpectedAccountFingerprint = "demo-fingerprint", ExpectedServer = "Exness-Demo", BrokerSymbol = "XAUUSD", Side = "Buy", VolumeLots = 0.01m, MagicNumber = 20260817, CorrelationMarker = "EMA-fixed", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), SubmittedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30) };
    private static Mt5ExecutionHistoryEvidence Evidence(long order, long deal, long position) => new(order, deal, position, position, "XAUUSD", "Buy", 20260817, "EMA-fixed", 0.01m, 2000m, DateTimeOffset.UtcNow.AddSeconds(-10), "Entry", "HistoryDeal", true, false, false);

    private sealed class FakeBridge : IMt5ExecutionBridgeClient
    {
        public event Action? Connected { add { } remove { } }
        public int SubmitCount { get; private set; }
        public int ExecutionAccountRequests { get; private set; }
        public int HistoryRequests { get; private set; }
        public IReadOnlyList<Mt5ExecutionHistoryEvidence> History { get; init; } = [];
        public Mt5OrderResultPayload PositionResult { get; init; } = new(true, "Done", "open", 123, 456, 0.01m, 1.1m);
        public bool IsConnected => true;
        public Mt5ExecutionBridgeStatus GetStatus() => new(true, true, "test", "demo-fingerprint", "Exness-Demo", "Demo", null);
        public Task<Mt5ExecutionEnvelope> SendAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token)
        {
            object body = operation switch
            {
                Mt5ExecutionOperation.GetExecutionAccount => Account(),
                Mt5ExecutionOperation.OrderCheck => new Mt5OrderCheckPayload(true, "Done", "ok", 1m, 1.1m),
                Mt5ExecutionOperation.SubmitMarketOrder => Submitted(),
                Mt5ExecutionOperation.GetExecutionHistory => HistoryResponse(),
                Mt5ExecutionOperation.GetPosition => PositionResult,
                _ => new Mt5OrderResultPayload(true, "Done", "ok", 123, 456, 0.01m, 1.1m)
            };
            return Task.FromResult(Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Response, operation, Guid.NewGuid(), body, TimeProvider.System));
        }
        private Mt5OrderResultPayload Submitted() { SubmitCount++; return new(true, "Done", "ok", 123, 456, 0.01m, 1.1m); }
        private Mt5ExecutionAccountPayload Account() { ExecutionAccountRequests++; return new("demo-fingerprint", "Exness-Demo", "Demo", true, true); }
        private Mt5ExecutionHistoryPayload HistoryResponse() { HistoryRequests++; return new(History); }
    }
}
