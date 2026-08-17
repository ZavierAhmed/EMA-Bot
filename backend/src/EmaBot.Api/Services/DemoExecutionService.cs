using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed record DemoExecutionReadiness(bool Ready, string Reason, Mt5ExecutionAccountPayload? Account = null);
public sealed record SubmitDemoOrder(Guid ClientExecutionId, string BrokerSymbol, string Side, decimal VolumeLots, decimal? StopLoss, decimal? TakeProfit);

public sealed class DemoExecutionService(EmaBotDbContext database, IMt5ExecutionBridgeClient bridge, IOptions<DemoExecutionOptions> options, TimeProvider clock, ILogger<DemoExecutionService> logger)
{
    private readonly DemoExecutionOptions _options = options.Value;

    public async Task<DemoExecutionReadiness> ReadinessAsync(CancellationToken token)
    {
        if (!bridge.IsConnected) return new(false, "The MT5 execution bridge v2 is not connected.");
        try
        {
            var response = await bridge.SendAsync(Mt5ExecutionOperation.GetExecutionAccount, null, token);
            var account = response.DeserializePayload<Mt5ExecutionAccountPayload>();
            if (account is null) return new(false, "The MT5 execution bridge returned an invalid account response.");
            if (!string.Equals(account.TradeMode, "Demo", StringComparison.OrdinalIgnoreCase)) return new(false, "MT5 account is not Demo.", account);
            if (!account.AccountTradeAllowed || !account.ExpertTradeAllowed) return new(false, "MT5 account or EA trading is disabled.", account);
            if (!string.Equals(account.AccountFingerprint, _options.ExpectedAccountFingerprint, StringComparison.Ordinal) || !string.Equals(account.Server, _options.ExpectedServer, StringComparison.Ordinal)) return new(false, "MT5 account fingerprint or server does not match the configured Demo target.", account);
            if (!_options.Enabled) return new(false, "Demo execution is disabled.", account);
            if (!_options.DemoOnly) return new(false, "Demo-only execution safety lock is not enabled.", account);
            return new(true, "Demo execution preflight passed.", account);
        }
        catch (Exception exception) when (exception is Mt5ExecutionBridgeException or Mt5ExecutionBridgeUnavailableException or Mt5ExecutionBridgeAmbiguousException)
        { return new(false, "The MT5 execution bridge preflight failed."); }
    }

    public async Task<DemoExecution> SubmitAsync(SubmitDemoOrder request, CancellationToken token)
    {
        if (request.ClientExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(request.BrokerSymbol) || request.VolumeLots <= 0 || !IsSide(request.Side)) throw new ArgumentException("The execution request is invalid.");
        var existing = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == request.ClientExecutionId, token);
        if (existing is not null) return existing; // idempotency: never submit a second order for this key.
        var now = clock.GetUtcNow();
        var marker = $"{_options.CorrelationPrefix}-{request.ClientExecutionId:N}";
        var execution = new DemoExecution { ClientExecutionId = request.ClientExecutionId, State = DemoExecutionState.Created, ExpectedAccountFingerprint = _options.ExpectedAccountFingerprint, ExpectedServer = _options.ExpectedServer, BrokerSymbol = request.BrokerSymbol.Trim(), Side = request.Side, VolumeLots = request.VolumeLots, RequestedStopLoss = request.StopLoss, RequestedTakeProfit = request.TakeProfit, MagicNumber = _options.MagicNumber, CorrelationMarker = marker, CreatedAtUtc = now };
        database.DemoExecutions.Add(execution);
        try { await database.SaveChangesAsync(token); } // durable intent exists before any bridge I/O.
        catch (DbUpdateException)
        {
            database.Entry(execution).State = EntityState.Detached;
            return await database.DemoExecutions.SingleAsync(item => item.ClientExecutionId == request.ClientExecutionId, token);
        }
        var readiness = await ReadinessAsync(token);
        if (!readiness.Ready) return await Reject(execution, readiness.Reason, token);
        var order = ToOrder(execution);
        try
        {
            var preflight = (await bridge.SendAsync(Mt5ExecutionOperation.OrderCheck, order, token)).DeserializePayload<Mt5OrderCheckPayload>();
            if (preflight is not { Accepted: true }) return await Reject(execution, preflight?.Message ?? "MT5 order preflight rejected the request.", token, preflight?.Retcode);
            execution.State = DemoExecutionState.PreflightPassed; execution.PreflightAtUtc = clock.GetUtcNow(); execution.BrokerRetcode = preflight.Retcode; execution.BrokerMessage = preflight.Message;
            await database.SaveChangesAsync(token);
            execution.State = DemoExecutionState.Submitting; execution.SubmittedAtUtc = clock.GetUtcNow();
            await database.SaveChangesAsync(token); // state is persisted before the potentially ambiguous write.
            var result = (await bridge.SendAsync(Mt5ExecutionOperation.SubmitMarketOrder, order, token)).DeserializePayload<Mt5OrderResultPayload>();
            if (result is not { Accepted: true }) return await Reject(execution, result?.Message ?? "MT5 rejected the order.", token, result?.Retcode);
            ApplyBrokerResult(execution, result, false);
            await database.SaveChangesAsync(token);
            return execution;
        }
        catch (Exception exception) when (IsAmbiguous(exception))
        { return await RequireReconciliationAsync(execution, "Submit result is ambiguous; no automatic retry will occur.", token); }
    }

    public async Task<DemoExecution?> ReconcileAsync(Guid id, CancellationToken token)
    {
        var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
        if (execution is null) return null;
        try
        {
            if (execution.PositionTicket is { } positionTicket)
            {
                var result = (await bridge.SendAsync(Mt5ExecutionOperation.GetPosition, new Mt5ExecutionPositionRequest(positionTicket, execution.MagicNumber, execution.CorrelationMarker), token)).DeserializePayload<Mt5OrderResultPayload>();
                if (result is null || result.PositionTicket is not null && result.PositionTicket != positionTicket) return await RequireReconciliationAsync(execution, "MT5 returned invalid exact-ticket reconciliation evidence.", token);
                if (!result.IsClosed)
                {
                    ApplyBrokerResult(execution, result, false); execution.ReconciledAtUtc = clock.GetUtcNow(); execution.ReconciliationSource = "ExactPositionTicket"; execution.ReconciliationNote = "Reconciled from exact owned broker position ticket.";
                    await database.SaveChangesAsync(token); return execution;
                }
            }
            return await ReconcileFromHistoryAsync(execution, token);
        }
        catch (Exception exception) when (IsAmbiguous(exception)) { return await RequireReconciliationAsync(execution, "Reconciliation is inconclusive.", token); }
    }

    public async Task<DemoExecution?> CloseAsync(Guid id, CancellationToken token)
    {
        var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
        if (execution is null) return null;
        if (execution.PositionTicket is null || execution.State is not (DemoExecutionState.Open or DemoExecutionState.PartiallyFilled or DemoExecutionState.BrokerAccepted)) return await RequireReconciliationAsync(execution, "A known open broker ticket is required for an exact close.", token);
        execution.State = DemoExecutionState.CloseRequested; await database.SaveChangesAsync(token);
        try
        {
            var result = (await bridge.SendAsync(Mt5ExecutionOperation.ClosePosition, ToOrder(execution) with { PositionTicket = execution.PositionTicket }, token)).DeserializePayload<Mt5OrderResultPayload>();
            if (result is not { Accepted: true }) return await Reject(execution, result?.Message ?? "MT5 rejected the close.", token, result?.Retcode);
            ApplyBrokerResult(execution, result, true); await database.SaveChangesAsync(token); return execution;
        }
        catch (Exception exception) when (IsAmbiguous(exception)) { return await RequireReconciliationAsync(execution, "Close result is ambiguous; no automatic retry will occur.", token); }
    }

    public Task<DemoExecution?> GetAsync(Guid id, CancellationToken token) => database.DemoExecutions.AsNoTracking().SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
    private async Task<DemoExecution> ReconcileFromHistoryAsync(DemoExecution execution, CancellationToken token)
    {
        var from = execution.SubmittedAtUtc ?? execution.CreatedAtUtc;
        var to = clock.GetUtcNow();
        var request = new Mt5ExecutionHistoryRequest(execution.ClientExecutionId.ToString("D"), execution.MagicNumber, execution.CorrelationMarker, execution.BrokerSymbol, execution.Side, execution.VolumeLots, from.AddMinutes(-5).ToUnixTimeSeconds(), to.AddMinutes(5).ToUnixTimeSeconds(), execution.PositionTicket);
        var payload = (await bridge.SendAsync(Mt5ExecutionOperation.GetExecutionHistory, request, token)).DeserializePayload<Mt5ExecutionHistoryPayload>();
        var evidence = payload?.Evidence.Where(item => IsStrictMatch(execution, item, from, to)).ToArray() ?? [];
        if (evidence.Length == 0) return await RequireReconciliationAsync(execution, "No deterministic broker execution evidence was found; automatic resubmission is prohibited.", token);
        var matches = evidence.GroupBy(item => item.PositionIdentifier ?? item.PositionTicket ?? item.OrderTicket ?? item.DealTicket).Where(group => group.Key is not null).ToArray();
        if (matches.Length != 1) return await RequireReconciliationAsync(execution, "More than one plausible broker execution match was found; automatic adoption is prohibited.", token);
        var match = matches[0].OrderBy(item => item.ExecutedAtUtc).ToArray();
        var entries = match.Where(item => item.IsEntry).ToArray();
        if (entries.Length == 0) return await RequireReconciliationAsync(execution, "Broker history did not contain a deterministic owned entry deal.", token);
        var entry = entries[0]; var exits = match.Where(item => item.IsExit).ToArray(); var filledVolume = entries.Sum(item => item.ExecutedVolumeLots); var closedVolume = exits.Sum(item => item.ExecutedVolumeLots);
        if (filledVolume > execution.VolumeLots || closedVolume > filledVolume) return await RequireReconciliationAsync(execution, "Broker history volumes are incompatible with the persisted execution intent.", token);
        execution.OrderTicket = entry.OrderTicket ?? execution.OrderTicket;
        execution.DealTicket = entry.DealTicket ?? execution.DealTicket;
        execution.EntryDealTicket = entry.DealTicket ?? execution.EntryDealTicket;
        execution.PositionIdentifier = entry.PositionIdentifier ?? execution.PositionIdentifier;
        execution.PositionTicket = entry.PositionTicket ?? execution.PositionTicket;
        execution.FilledVolumeLots = filledVolume;
        execution.AverageFillPrice = WeightedAverage(entries);
        execution.BrokerExecutedAtUtc = entry.ExecutedAtUtc;
        execution.ReconciledAtUtc = clock.GetUtcNow(); execution.ReconciliationSource = "BoundedHistory";
        if (exits.Length > 0 && closedVolume >= execution.FilledVolumeLots)
        {
            var exit = exits[^1]; execution.ExitDealTicket = exit.DealTicket; execution.ClosedVolumeLots = closedVolume; execution.AverageClosePrice = WeightedAverage(exits); execution.BrokerClosedAtUtc = exit.ExecutedAtUtc; execution.ClosedAtUtc = exit.ExecutedAtUtc; execution.State = DemoExecutionState.Closed; execution.ReconciliationNote = "Bounded broker history conclusively matched owned entry and exit deals.";
        }
        else if (execution.FilledVolumeLots < execution.VolumeLots)
        {
            execution.State = DemoExecutionState.PartiallyFilled; execution.ReconciliationNote = "Bounded broker history recovered a deterministic partial entry fill.";
        }
        else if (execution.PositionTicket is not null)
        {
            execution.State = DemoExecutionState.Open; execution.ReconciliationNote = "Bounded broker history recovered a deterministic owned entry.";
        }
        else return await RequireReconciliationAsync(execution, "Broker history found an owned deal but no safe position identifier; manual reconciliation is required.", token);
        await database.SaveChangesAsync(token); return execution;
    }
    private static bool IsStrictMatch(DemoExecution execution, Mt5ExecutionHistoryEvidence item, DateTimeOffset from, DateTimeOffset to) => item.MagicNumber == execution.MagicNumber && string.Equals(item.CorrelationMarker, execution.CorrelationMarker, StringComparison.Ordinal) && string.Equals(item.BrokerSymbol, execution.BrokerSymbol, StringComparison.Ordinal) && item.ExecutedAtUtc >= from.AddMinutes(-5) && item.ExecutedAtUtc <= to.AddMinutes(5) && (item.IsExit || string.Equals(item.Side, execution.Side, StringComparison.OrdinalIgnoreCase)) && item.ExecutedVolumeLots > 0m && item.ExecutedVolumeLots <= execution.VolumeLots;
    private static decimal? WeightedAverage(IEnumerable<Mt5ExecutionHistoryEvidence> values) { var rows = values.ToArray(); var total = rows.Sum(item => item.ExecutedVolumeLots); return total == 0m ? null : rows.Where(item => item.ExecutionPrice is not null).Sum(item => item.ExecutionPrice!.Value * item.ExecutedVolumeLots) / total; }
    private async Task<DemoExecution> Reject(DemoExecution item, string message, CancellationToken token, string? retcode = null) { item.State = DemoExecutionState.Rejected; item.BrokerMessage = message; item.BrokerRetcode = retcode; await database.SaveChangesAsync(token); return item; }
    private async Task<DemoExecution> RequireReconciliationAsync(DemoExecution item, string note, CancellationToken token) { item.State = DemoExecutionState.ReconciliationRequired; item.ReconciliationNote = note; await database.SaveChangesAsync(token); logger.LogWarning("Demo execution {ClientExecutionId} requires reconciliation: {Reason}", item.ClientExecutionId, note); return item; }
    private void ApplyBrokerResult(DemoExecution item, Mt5OrderResultPayload result, bool closing) { item.PositionTicket = result.PositionTicket ?? item.PositionTicket; item.PositionIdentifier = result.PositionIdentifier ?? item.PositionIdentifier; item.OrderTicket = result.OrderTicket ?? item.OrderTicket; item.DealTicket = result.DealTicket ?? item.DealTicket; item.EntryDealTicket ??= closing ? null : result.DealTicket; item.FilledVolumeLots = result.FilledVolumeLots ?? item.FilledVolumeLots; item.AverageFillPrice = result.AverageFillPrice ?? item.AverageFillPrice; item.BrokerRetcode = result.Retcode; item.BrokerMessage = result.Message; item.BrokerAcceptedAtUtc ??= clock.GetUtcNow(); item.State = result.IsClosed ? DemoExecutionState.Closed : closing ? DemoExecutionState.CloseRequested : result.IsPartial || result.FilledVolumeLots is { } filled && filled > 0m && filled < item.VolumeLots ? DemoExecutionState.PartiallyFilled : result.IsPositionOpen && result.PositionTicket is not null ? DemoExecutionState.Open : DemoExecutionState.BrokerAccepted; if (item.State == DemoExecutionState.Closed) item.ClosedAtUtc = clock.GetUtcNow(); }
    private static Mt5OrderRequest ToOrder(DemoExecution item) => new(item.ClientExecutionId.ToString("D"), item.BrokerSymbol, item.Side, item.VolumeLots, item.RequestedStopLoss, item.RequestedTakeProfit, item.MagicNumber, item.CorrelationMarker, item.PositionTicket);
    private static bool IsSide(string side) => string.Equals(side, "Buy", StringComparison.OrdinalIgnoreCase) || string.Equals(side, "Sell", StringComparison.OrdinalIgnoreCase);
    private static bool IsAmbiguous(Exception exception) => exception is TimeoutException or IOException or Mt5ExecutionBridgeException;
}
