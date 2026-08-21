using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed record DemoExecutionReadiness(bool Ready, string Reason, Mt5ExecutionAccountPayload? Account = null);
public sealed record SubmitDemoOrder(Guid ClientExecutionId, string BrokerSymbol, string Side, decimal VolumeLots, decimal? StopLoss, decimal? TakeProfit);

public interface IDemoExecutionService
{
    Task<DemoExecutionReadiness> ReadinessAsync(CancellationToken token);
    Task<DemoExecution> SubmitAsync(SubmitDemoOrder request, CancellationToken token);
    Task<DemoExecution?> ReconcileAsync(Guid id, CancellationToken token);
    Task<DemoExecution?> CloseAsync(Guid id, CancellationToken token);
    Task<DemoExecution?> GetAsync(Guid id, CancellationToken token);
}

public sealed class DemoExecutionService(EmaBotDbContext database, IMt5ExecutionBridgeClient bridge, IOptions<DemoExecutionOptions> options, TimeProvider clock, ILogger<DemoExecutionService> logger) : IDemoExecutionService
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
            if (!account.DemoExecutionEnabled) return new(false, "MT5 EA Demo execution is disabled.", account);
            if (!account.DemoExecutionAllowed) return new(false, "MT5 EA Demo execution safety gate failed.", account);
            return new(true, "Demo execution preflight passed.", account);
        }
        catch (Exception exception) when (exception is Mt5ExecutionBridgeException or Mt5ExecutionBridgeRejectedException or Mt5ExecutionBridgeUnavailableException or Mt5ExecutionBridgeAmbiguousException)
        { return new(false, "The MT5 execution bridge preflight failed."); }
    }

    public async Task<DemoExecution> SubmitAsync(SubmitDemoOrder request, CancellationToken token)
    {
        if (request.ClientExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(request.BrokerSymbol) || request.VolumeLots <= 0 || !IsSide(request.Side)) throw new ArgumentException("The execution request is invalid.");
        var existing = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == request.ClientExecutionId, token);
        if (existing is not null) return existing; // idempotency: never submit a second order for this key.
        var now = clock.GetUtcNow();
        var marker = DemoExecutionMarker.Generate(_options.CorrelationPrefix, request.ClientExecutionId);
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
            Mt5OrderCheckPayload? preflight;
            try { preflight = (await bridge.SendAsync(Mt5ExecutionOperation.OrderCheck, order, token)).DeserializePayload<Mt5OrderCheckPayload>(); }
            catch (Mt5ExecutionBridgeRejectedException exception) { return await Reject(execution, exception.Message, token, exception.Code); }
            if (preflight is not { Accepted: true }) return await Reject(execution, preflight?.Message ?? "MT5 order preflight rejected the request.", token, preflight?.Retcode);
            execution.State = DemoExecutionState.PreflightPassed; execution.PreflightAtUtc = clock.GetUtcNow(); execution.BrokerRetcode = preflight.Retcode; execution.BrokerMessage = preflight.Message;
            await database.SaveChangesAsync(token);
            execution.State = DemoExecutionState.Submitting; execution.SubmittedAtUtc = clock.GetUtcNow();
            await database.SaveChangesAsync(token); // state is persisted before the potentially ambiguous write.
            var result = (await bridge.SendAsync(Mt5ExecutionOperation.SubmitMarketOrder, order, token)).DeserializePayload<Mt5SubmitOrderResultPayload>();
            if (result is not { Accepted: true }) return await Reject(execution, result?.Message ?? "MT5 rejected the order.", token, result?.Retcode);
            ApplySubmitResult(execution, result);
            await database.SaveChangesAsync(token);
            return execution;
        }
        catch (Mt5ExecutionBridgeRejectedException exception) { return await Reject(execution, exception.Message, token, exception.Code); }
        catch (Exception exception) when (IsAmbiguous(exception))
        { return await RequireReconciliationAsync(execution, "Submit result is ambiguous; no automatic retry will occur.", token); }
    }

    // Reconciliation precedence is broker-native identity first: the persisted exact
    // entry deal, then the exact owned position ticket, and only then the bounded
    // marker-based history fallback for submissions that never received native tickets.
    public async Task<DemoExecution?> ReconcileAsync(Guid id, CancellationToken token)
    {
        var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
        if (execution is null) return null;
        try
        {
            if ((execution.EntryDealTicket ?? execution.DealTicket) is { } entryDealTicket) return await ReconcileFromExactEntryDealAsync(execution, entryDealTicket, token);
            if (execution.PositionTicket is { } positionTicket && execution.PositionIdentifier is > 0) return await ReconcileFromExactPositionAsync(execution, positionTicket, execution.PositionIdentifier.Value, token);
            return await ReconcileFromHistoryAsync(execution, token);
        }
        catch (Mt5ExecutionBridgeRejectedException exception) { return await RequireReconciliationAsync(execution, $"MT5 rejected reconciliation evidence ({exception.Code}); no state was inferred.", token); }
        catch (Exception exception) when (IsAmbiguous(exception)) { return await RequireReconciliationAsync(execution, "Reconciliation is inconclusive.", token); }
    }

    public async Task<DemoExecution?> CloseAsync(Guid id, CancellationToken token)
    {
        var execution = await database.DemoExecutions.SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);
        if (execution is null) return null;
        if (execution.PositionTicket is null || execution.PositionIdentifier is not > 0 || execution.State is not (DemoExecutionState.Open or DemoExecutionState.PartiallyFilled or DemoExecutionState.BrokerAccepted)) return await RequireReconciliationAsync(execution, "Known native position ticket and identifier are required for an exact close.", token);
        execution.State = DemoExecutionState.CloseRequested; await database.SaveChangesAsync(token);
        try
        {
            // Exact-ticket close ownership is native: ticket, identifier, magic, symbol and
            // original side.  The correlation marker is deliberately NOT an ownership key.
            var request = new Mt5ClosePositionRequest(execution.PositionTicket.Value, execution.PositionIdentifier.Value, execution.MagicNumber, execution.BrokerSymbol, execution.Side);
            var result = (await bridge.SendAsync(Mt5ExecutionOperation.ClosePosition, request, token)).DeserializePayload<Mt5ClosePositionResultPayload>();
            if (result is not { Accepted: true })
            {
                execution.BrokerMessage = result?.Message ?? "MT5 rejected the close.";
                execution.BrokerRetcode = result?.Retcode;
                return await RequireReconciliationAsync(execution, "MT5 rejected the close; the position may still be open and no automatic close retry will occur.", token);
            }
            ApplyCloseResult(execution, result); await database.SaveChangesAsync(token); return execution;
        }
        catch (Mt5ExecutionBridgeRejectedException exception)
        {
            execution.BrokerMessage = exception.Message;
            execution.BrokerRetcode = exception.Code;
            return await RequireReconciliationAsync(execution, "MT5 rejected the close; the position may still be open and no automatic close retry will occur.", token);
        }
        catch (Exception exception) when (IsAmbiguous(exception)) { return await RequireReconciliationAsync(execution, "Close result is ambiguous; no automatic retry will occur.", token); }
    }

    public Task<DemoExecution?> GetAsync(Guid id, CancellationToken token) => database.DemoExecutions.AsNoTracking().SingleOrDefaultAsync(item => item.ClientExecutionId == id, token);

    // Trusted ownership chain: persisted exact entry deal -> DEAL_POSITION_ID ->
    // current position by identifier.  A truncated broker comment never invalidates
    // otherwise exact native identity.
    private async Task<DemoExecution> ReconcileFromExactEntryDealAsync(DemoExecution execution, long entryDealTicket, CancellationToken token)
    {
        var request = new Mt5ExactDealRequest(entryDealTicket, execution.MagicNumber, execution.BrokerSymbol, execution.Side, execution.VolumeLots, execution.OrderTicket);
        var payload = (await bridge.SendAsync(Mt5ExecutionOperation.GetExactDeal, request, token)).DeserializePayload<Mt5ExactDealPayload>();
        if (payload is null) return await RequireReconciliationAsync(execution, "The exact entry deal response was invalid.", token);
        if (payload.DealTicket != entryDealTicket) return await RequireReconciliationAsync(execution, "The broker entry deal ticket did not match the persisted exact ticket.", token);
        if (execution.OrderTicket is { } expectedOrder && payload.OrderTicket != expectedOrder) return await RequireReconciliationAsync(execution, "The broker entry order ticket did not match the persisted order ticket.", token);
        if (payload.MagicNumber != execution.MagicNumber) return await RequireReconciliationAsync(execution, "The exact entry deal magic number did not match the persisted magic number.", token);
        if (!string.Equals(payload.BrokerSymbol, execution.BrokerSymbol, StringComparison.Ordinal)) return await RequireReconciliationAsync(execution, "The exact entry deal symbol did not match the persisted symbol.", token);
        if (!string.Equals(payload.Side, execution.Side, StringComparison.OrdinalIgnoreCase)) return await RequireReconciliationAsync(execution, "The exact entry deal side did not match the persisted original side.", token);
        if (!payload.IsEntry) return await RequireReconciliationAsync(execution, "The exact deal exists but is not an entry deal.", token);
        if (payload.ExecutedVolumeLots <= 0m || payload.ExecutedVolumeLots > execution.VolumeLots) return await RequireReconciliationAsync(execution, "The exact entry deal volume is incompatible with the persisted execution intent.", token);
        if (payload.PositionIdentifier is not > 0) return await RequireReconciliationAsync(execution, "The exact entry deal has no native position identifier.", token);
        execution.OrderTicket = payload.OrderTicket ?? execution.OrderTicket;
        execution.EntryDealTicket = payload.DealTicket;
        execution.DealTicket ??= payload.DealTicket;
        execution.PositionIdentifier = payload.PositionIdentifier;
        execution.FilledVolumeLots = payload.ExecutedVolumeLots;
        execution.AverageFillPrice = payload.ExecutionPrice ?? execution.AverageFillPrice;
        execution.BrokerExecutedAtUtc = payload.ExecutedAtUtc;
        execution.ReconciledAtUtc = clock.GetUtcNow(); execution.ReconciliationSource = "ExactEntryDeal";
        if (payload.IsPositionOpen && payload.PositionTicket is > 0)
        {
            execution.PositionTicket = payload.PositionTicket;
            execution.State = execution.FilledVolumeLots < execution.VolumeLots ? DemoExecutionState.PartiallyFilled : DemoExecutionState.Open;
            execution.ReconciliationNote = "Reconciled from the exact native entry deal with exactly one live native position match.";
            await database.SaveChangesAsync(token); return execution;
        }
        // The entry deal is proven but no current position was proven open, so native
        // position history by the trusted identifier decides between Closed and
        // ReconciliationRequired.  It can never become Open on this path.
        return await ReconcilePositionHistoryAsync(execution, payload.PositionIdentifier!.Value, payload.DealTicket, token);
    }

    private async Task<DemoExecution> ReconcileFromExactPositionAsync(DemoExecution execution, long positionTicket, long positionIdentifier, CancellationToken token)
    {
        var request = new Mt5ExecutionPositionRequest(positionTicket, positionIdentifier, execution.MagicNumber, execution.BrokerSymbol, execution.Side);
        var payload = (await bridge.SendAsync(Mt5ExecutionOperation.GetPosition, request, token)).DeserializePayload<Mt5ExecutionPositionPayload>();
        if (payload is null) return await RequireReconciliationAsync(execution, "MT5 returned invalid exact-ticket reconciliation evidence.", token);
        if (payload.PositionTicket is not null && payload.PositionTicket != positionTicket) return await RequireReconciliationAsync(execution, "MT5 returned a different exact position ticket.", token);
        if (!payload.Accepted) return await RequireReconciliationAsync(execution, "MT5 rejected exact-ticket reconciliation evidence.", token);
        if (!payload.IsClosed && payload.PositionTicket is not null)
        {
            if (payload.PositionIdentifier is not > 0 || payload.MagicNumber != execution.MagicNumber || !string.Equals(payload.BrokerSymbol, execution.BrokerSymbol, StringComparison.Ordinal) || !string.Equals(payload.Side, execution.Side, StringComparison.OrdinalIgnoreCase) || payload.VolumeLots is not > 0m)
                return await RequireReconciliationAsync(execution, "MT5 exact-ticket evidence failed native ownership checks.", token);
            if (payload.PositionIdentifier != positionIdentifier)
                return await RequireReconciliationAsync(execution, "MT5 returned a different native position identifier.", token);
            execution.PositionIdentifier = payload.PositionIdentifier;
            execution.FilledVolumeLots = payload.VolumeLots;
            execution.AverageFillPrice = payload.OpenPrice ?? execution.AverageFillPrice;
            execution.ReconciledAtUtc = clock.GetUtcNow(); execution.ReconciliationSource = "ExactPositionTicket"; execution.ReconciliationNote = "Reconciled from the exact owned broker position ticket with native ownership fields.";
            execution.State = execution.FilledVolumeLots < execution.VolumeLots ? DemoExecutionState.PartiallyFilled : DemoExecutionState.Open;
            await database.SaveChangesAsync(token); return execution;
        }
        return await ReconcileFromHistoryAsync(execution, token);
    }

    // Native position history by the exact trusted PositionIdentifier.  This is the
    // path that recovers a position manually closed outside EMA-Bot: once exact entry
    // ownership is proven, a manual exit may carry a different magic or comment and is
    // still associated through its exact native DEAL_POSITION_ID.
    private async Task<DemoExecution> ReconcilePositionHistoryAsync(DemoExecution execution, long positionIdentifier, long entryDealTicket, CancellationToken token)
    {
        var from = (execution.BrokerExecutedAtUtc ?? execution.SubmittedAtUtc ?? execution.CreatedAtUtc).AddMinutes(-5);
        var to = clock.GetUtcNow().AddMinutes(5);
        var request = new Mt5PositionHistoryRequest(positionIdentifier, entryDealTicket, execution.MagicNumber, execution.BrokerSymbol, execution.Side, execution.VolumeLots, from.ToUnixTimeSeconds(), to.ToUnixTimeSeconds());
        var payload = (await bridge.SendAsync(Mt5ExecutionOperation.GetPositionHistory, request, token)).DeserializePayload<Mt5PositionHistoryPayload>();
        if (payload is null || payload.PositionIdentifier != positionIdentifier) return await RequireReconciliationAsync(execution, "The native position history response was invalid.", token);
        var deals = payload.Deals.Where(item => item.PositionIdentifier == positionIdentifier).ToArray();
        var entries = deals.Where(item => item.DealTicket == entryDealTicket).ToArray();
        if (entries.Length != 1) return await RequireReconciliationAsync(execution, "Native position history did not contain the proven exact entry deal.", token);
        var entry = entries[0];
        if (entry.MagicNumber != execution.MagicNumber || !string.Equals(entry.BrokerSymbol, execution.BrokerSymbol, StringComparison.Ordinal) || !string.Equals(entry.Side, execution.Side, StringComparison.OrdinalIgnoreCase) || !entry.IsEntry || entry.ExecutedVolumeLots <= 0m || entry.ExecutedVolumeLots > execution.VolumeLots)
            return await RequireReconciliationAsync(execution, "Native position history entry evidence failed strict entry ownership checks.", token);
        var filled = entry.ExecutedVolumeLots;
        var oppositeSide = string.Equals(execution.Side, "Buy", StringComparison.OrdinalIgnoreCase) ? "Sell" : "Buy";
        // Exit deals must belong to the exact PositionIdentifier and symbol, occur after
        // the proven entry, carry valid volume, and be opposite-direction where the
        // deal semantics make direction meaningful.  Magic and comment are not required.
        var exits = deals.Where(item => item.IsExit && item.DealTicket != entryDealTicket && string.Equals(item.BrokerSymbol, execution.BrokerSymbol, StringComparison.Ordinal) && item.ExecutedAtUtc > entry.ExecutedAtUtc && item.ExecutedVolumeLots > 0m && (string.Equals(item.EntryType, "OutBy", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Side, oppositeSide, StringComparison.OrdinalIgnoreCase))).OrderBy(item => item.ExecutedAtUtc).ToArray();
        var closedVolume = exits.Sum(item => item.ExecutedVolumeLots);
        if (closedVolume > filled) return await RequireReconciliationAsync(execution, "Native position history exit volume exceeds the proven entry volume.", token);
        if (exits.Length > 0 && closedVolume == filled)
        {
            var exit = exits[^1];
            execution.ExitDealTicket = exit.DealTicket; execution.ClosedVolumeLots = closedVolume; execution.AverageClosePrice = WeightedAverage(exits);
            execution.BrokerClosedAtUtc = exit.ExecutedAtUtc; execution.ClosedAtUtc = exit.ExecutedAtUtc;
            execution.State = DemoExecutionState.Closed; execution.ReconciliationSource = "NativePositionHistory"; execution.ReconciliationNote = "Exact native entry and identifier ownership proven; exit deals by exact PositionIdentifier conclusively balance the filled entry volume.";
            await database.SaveChangesAsync(token); return execution;
        }
        return await RequireReconciliationAsync(execution, "No current position exists and native position history could not conclusively establish full closure; the execution can never become Open on this path.", token);
    }

    private async Task<DemoExecution> ReconcileFromHistoryAsync(DemoExecution execution, CancellationToken token)
    {
        var from = execution.SubmittedAtUtc ?? execution.CreatedAtUtc;
        var to = clock.GetUtcNow();
        var request = new Mt5ExecutionHistoryRequest(execution.ClientExecutionId.ToString("D"), execution.MagicNumber, DemoExecutionMarker.BrokerMarker(execution.CorrelationMarker), execution.BrokerSymbol, execution.Side, execution.VolumeLots, from.AddMinutes(-5).ToUnixTimeSeconds(), to.AddMinutes(5).ToUnixTimeSeconds(), execution.PositionTicket);
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
        execution.EntryDealTicket = entry.DealTicket ?? execution.EntryDealTicket;
        execution.DealTicket ??= entry.DealTicket;
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
    private static bool IsStrictMatch(DemoExecution execution, Mt5ExecutionHistoryEvidence item, DateTimeOffset from, DateTimeOffset to) => item.MagicNumber == execution.MagicNumber && DemoExecutionMarker.MatchesPersistedMarker(execution.CorrelationMarker, item.CorrelationMarker) && string.Equals(item.BrokerSymbol, execution.BrokerSymbol, StringComparison.Ordinal) && item.ExecutedAtUtc >= from.AddMinutes(-5) && item.ExecutedAtUtc <= to.AddMinutes(5) && (item.IsExit || string.Equals(item.Side, execution.Side, StringComparison.OrdinalIgnoreCase)) && item.ExecutedVolumeLots > 0m && item.ExecutedVolumeLots <= execution.VolumeLots;
    private static decimal? WeightedAverage(IEnumerable<Mt5ExecutionHistoryEvidence> values) { var rows = values.ToArray(); var total = rows.Sum(item => item.ExecutedVolumeLots); return total == 0m ? null : rows.Where(item => item.ExecutionPrice is not null).Sum(item => item.ExecutionPrice!.Value * item.ExecutedVolumeLots) / total; }
    private async Task<DemoExecution> Reject(DemoExecution item, string message, CancellationToken token, string? retcode = null) { item.State = DemoExecutionState.Rejected; item.BrokerMessage = message; item.BrokerRetcode = retcode; await database.SaveChangesAsync(token); return item; }
    private async Task<DemoExecution> RequireReconciliationAsync(DemoExecution item, string note, CancellationToken token) { item.State = DemoExecutionState.ReconciliationRequired; item.ReconciliationNote = note; await database.SaveChangesAsync(token); logger.LogWarning("Demo execution {ClientExecutionId} requires reconciliation: {Reason}", item.ClientExecutionId, note); return item; }
    private void ApplySubmitResult(DemoExecution item, Mt5SubmitOrderResultPayload result) { item.OrderTicket = result.OrderTicket ?? item.OrderTicket; item.EntryDealTicket = result.EntryDealTicket ?? item.EntryDealTicket; item.DealTicket ??= result.EntryDealTicket; item.PositionIdentifier = result.PositionIdentifier ?? item.PositionIdentifier; item.PositionTicket = result.PositionTicket ?? item.PositionTicket; item.FilledVolumeLots = result.FilledVolumeLots ?? item.FilledVolumeLots; item.AverageFillPrice = result.AverageFillPrice ?? item.AverageFillPrice; item.BrokerRetcode = result.Retcode; item.BrokerMessage = result.Message; item.BrokerAcceptedAtUtc ??= clock.GetUtcNow(); item.State = result.IsPositionOpen && result.PositionTicket is > 0 && result.PositionIdentifier is > 0 ? DemoExecutionState.Open : result.IsPartial || result.FilledVolumeLots is { } filled && filled > 0m && filled < item.VolumeLots ? DemoExecutionState.PartiallyFilled : DemoExecutionState.BrokerAccepted; }
    private void ApplyCloseResult(DemoExecution item, Mt5ClosePositionResultPayload result) { item.ExitDealTicket = result.ExitDealTicket ?? item.ExitDealTicket; item.ClosedVolumeLots = result.ClosedVolumeLots ?? item.ClosedVolumeLots; item.AverageClosePrice = result.AverageClosePrice ?? item.AverageClosePrice; item.BrokerRetcode = result.Retcode; item.BrokerMessage = result.Message; if (result.IsClosed) { item.State = DemoExecutionState.Closed; item.BrokerClosedAtUtc ??= clock.GetUtcNow(); item.ClosedAtUtc ??= clock.GetUtcNow(); } }
    private static Mt5OrderRequest ToOrder(DemoExecution item) => new(item.ClientExecutionId.ToString("D"), item.BrokerSymbol, item.Side, item.VolumeLots, item.RequestedStopLoss, item.RequestedTakeProfit, item.MagicNumber, item.CorrelationMarker, item.PositionTicket);
    private static decimal? WeightedAverage(IEnumerable<Mt5PositionHistoryDeal> values) { var rows = values.ToArray(); var total = rows.Sum(item => item.ExecutedVolumeLots); return total == 0m ? null : rows.Where(item => item.ExecutionPrice is not null).Sum(item => item.ExecutionPrice!.Value * item.ExecutedVolumeLots) / total; }
    private static bool IsSide(string side) => string.Equals(side, "Buy", StringComparison.OrdinalIgnoreCase) || string.Equals(side, "Sell", StringComparison.OrdinalIgnoreCase);
    private static bool IsAmbiguous(Exception exception) => exception is TimeoutException or IOException or Mt5ExecutionBridgeException or Mt5ExecutionBridgeUnavailableException;
}
