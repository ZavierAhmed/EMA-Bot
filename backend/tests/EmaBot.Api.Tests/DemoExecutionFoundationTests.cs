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
    public void DemoExecutionModel_HasImmutableUniqueClientIdAndNoPaperRelationship()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = database.Model.FindEntityType(typeof(DemoExecution))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(DemoExecution.ClientExecutionId));
        Assert.Empty(entity.GetForeignKeys());
    }

    private sealed class FakeBridge : IMt5ExecutionBridgeClient
    {
        public event Action? Connected { add { } remove { } }
        public int SubmitCount { get; private set; }
        public bool IsConnected => true;
        public Mt5ExecutionBridgeStatus GetStatus() => new(true, true, "test", "demo-fingerprint", "Exness-Demo", "Demo", null);
        public Task<Mt5ExecutionEnvelope> SendAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token)
        {
            object body = operation switch
            {
                Mt5ExecutionOperation.GetExecutionAccount => new Mt5ExecutionAccountPayload("demo-fingerprint", "Exness-Demo", "Demo", true, true),
                Mt5ExecutionOperation.OrderCheck => new Mt5OrderCheckPayload(true, "Done", "ok", 1m, 1.1m),
                Mt5ExecutionOperation.SubmitMarketOrder => Submitted(),
                _ => new Mt5OrderResultPayload(true, "Done", "ok", 123, 456, 0.01m, 1.1m)
            };
            return Task.FromResult(Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Response, operation, Guid.NewGuid(), body, TimeProvider.System));
        }
        private Mt5OrderResultPayload Submitted() { SubmitCount++; return new(true, "Done", "ok", 123, 456, 0.01m, 1.1m); }
    }
}
