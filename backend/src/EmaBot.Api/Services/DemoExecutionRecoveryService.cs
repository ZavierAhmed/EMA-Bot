using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

// Recovery is intentionally reconciliation-only.  It never calls submit, close, or
// protection-modification operations.
public sealed class DemoExecutionRecoveryService(IServiceScopeFactory scopes, IMt5ExecutionBridgeClient bridge, ILogger<DemoExecutionRecoveryService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken token)
    {
        bridge.Connected += OnBridgeConnected;
        if (!bridge.IsConnected) return Task.CompletedTask;
        return RecoverAsync(token);
    }
    public Task StopAsync(CancellationToken token) { bridge.Connected -= OnBridgeConnected; return Task.CompletedTask; }
    private void OnBridgeConnected() => _ = RecoverAsync(CancellationToken.None);
    private async Task RecoverAsync(CancellationToken token)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var ids = await database.DemoExecutions.AsNoTracking().Where(item => item.State == DemoExecutionState.Submitting || item.State == DemoExecutionState.CloseRequested || item.State == DemoExecutionState.ReconciliationRequired).Select(item => item.ClientExecutionId).ToListAsync(token);
            var management = await database.DemoExecutionManagementActions.AsNoTracking().Where(item => item.State == DemoExecutionManagementActionState.Created || item.State == DemoExecutionManagementActionState.Submitting || item.State == DemoExecutionManagementActionState.ReconciliationRequired).Select(item => new { item.ClientManagementActionId, item.State }).ToListAsync(token);
            var service = scope.ServiceProvider.GetRequiredService<DemoExecutionService>();
            foreach (var id in ids) await service.ReconcileAsync(id, token);
            foreach (var action in management)
            {
                if (action.State == DemoExecutionManagementActionState.Created) await service.FailClosedManagementActionAsync(action.ClientManagementActionId, token);
                else await service.ReconcileManagementActionAsync(action.ClientManagementActionId, token);
            }
        }
        catch (Exception exception) when (!token.IsCancellationRequested) { logger.LogWarning(exception, "Demo execution recovery reconciliation did not complete."); }
    }
}
