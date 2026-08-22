using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Controllers;

public sealed record CreateDemoStrategySessionRequest(string Interval, IReadOnlyList<string> Symbols, decimal InitialAllocation = 0m);
public sealed record DemoExecutionManagementActionResponse(int Id, Guid ClientManagementActionId, string Kind, string State, decimal? RequestedStopLoss, decimal? RequestedTakeProfit, decimal? ObservedBeforeStopLoss, decimal? ObservedBeforeTakeProfit, decimal? AppliedStopLoss, decimal? AppliedTakeProfit, string? BrokerRetcode, string? BrokerMessage, DateTimeOffset CreatedAtUtc, DateTimeOffset? SubmittedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? ReconciledAtUtc, string? ReconciliationNote, string? ReconciliationSource);
public sealed record DemoExecutionSummaryResponse(int Id, Guid ClientExecutionId, string State, string Provider, string BrokerSymbol, string Side, decimal VolumeLots, decimal? RequestedStopLoss, decimal? RequestedTakeProfit, decimal? CurrentStopLoss, decimal? CurrentTakeProfit, DateTimeOffset? ProtectionObservedAtUtc, long MagicNumber, long? PositionTicket, long? PositionIdentifier, long? OrderTicket, long? EntryDealTicket, long? ExitDealTicket, string? NativeExitReason, bool NativeExitReasonConflicted, decimal? FilledVolumeLots, decimal? AverageFillPrice, decimal? ClosedVolumeLots, decimal? AverageClosePrice, DateTimeOffset? BrokerExecutedAtUtc, DateTimeOffset? BrokerClosedAtUtc, string? BrokerRetcode, string? BrokerMessage, DateTimeOffset CreatedAtUtc, DateTimeOffset? PreflightAtUtc, DateTimeOffset? SubmittedAtUtc, DateTimeOffset? BrokerAcceptedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset? ReconciledAtUtc, string? ReconciliationNote, string? ReconciliationSource, IReadOnlyList<DemoExecutionManagementActionResponse> ManagementActions);
public sealed record DemoStrategyManagementResponse(int Id, int DemoStrategyIntentId, int DemoExecutionId, string State, decimal OriginalEntryPrice, decimal OriginalStopLoss, decimal OriginalTakeProfit, decimal? BestFavorablePrice, decimal BestFavorableProgressPercent, string TakeProfitExtensionState, DateTimeOffset? TargetExtensionAppliedAtUtc, decimal HighestAttemptedLockPercent, decimal HighestAppliedLockPercent, Guid? PendingProtectionActionId, decimal? PendingProtectionLockPercent, bool PendingProtectionExtendsTarget, decimal? PendingDesiredStopLoss, decimal? PendingDesiredTakeProfit, DateTimeOffset? OppositeSignalTimeUtc, SignalDirection? OppositeSignalDirection, string OppositeCloseState, DateTimeOffset? OppositeCloseRequestedAtUtc, DateTimeOffset? LastManagedAtUtc, string? LastReason, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record DemoStrategyIntentResponse(int Id, string Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, DateTimeOffset ExpectedEntryOpenUtc, decimal StructuralStopLoss, decimal? IntendedTakeProfit, decimal IntendedVolumeLots, Guid ClientExecutionId, string Status, int? DemoExecutionId, bool IsReentry, int? ReentrySourceDemoExecutionId, DateTimeOffset? TrendRegimeCrossoverTimeUtc, int? ReentryAgeBars, string? Reason, DemoExecutionSummaryResponse? Execution);
public sealed record DemoStrategySymbolResponse(string Symbol, string BrokerSymbol, DateTimeOffset? LastProcessedClosedCandleUtc, DateTimeOffset? LastMarketEventUtc, SignalDirection? TrendRegimeDirection, DateTimeOffset? TrendRegimeCrossoverTimeUtc, bool ReentryEligible, bool ReentryConsumed, int? ReentrySourceDemoExecutionId, DateTimeOffset? ReentryEligibleAtUtc, string? ReentryReason, IReadOnlyList<DemoStrategyIntentResponse> RecentIntents, IReadOnlyList<DemoStrategyManagementResponse> Management);
public sealed record DemoStrategySessionSummaryResponse(int Id, string Interval, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? StoppedAtUtc, DateTimeOffset? InterruptedAtUtc, string? FailureMessage, int SymbolCount, IReadOnlyList<string> Symbols, bool AutomationEnabledAtCreation, bool TrailingStopEnabled, bool ExitOnOppositeCrossover, bool SameTrendReentryEnabled, decimal FixedLots, decimal RiskReward, bool NewEntriesPaused, DateTimeOffset? NewEntriesPausedAtUtc, decimal InitialAllocation);
public sealed record DemoAutomationSafetyResponse(bool MarketDataBridgeEnabled, string MarketDataConnectionState, bool ExecutionBridgeEnabled, bool ExecutionReady, string ExecutionReason, string? AccountMode, bool? AccountTradeAllowed, bool? ExpertTradeAllowed, bool DotNetDemoExecutionEnabled, bool DemoOnlyLockEnabled, bool? EaDemoExecutionEnabled, bool? EaDemoExecutionAllowed, string? EaBuildId, bool? SupportsExactProtectionReadback, bool? SupportsNativeExitReason, bool? SupportsBrokerPnlEvidence, bool AutomationEnabled, bool ManagementEnabled, decimal FixedLots);
public sealed record DemoStrategyBudgetResponse(decimal InitialAllocation, string? AccountCurrency, decimal? RealizedPnl, decimal? UnrealizedPnl, decimal? Balance, decimal? Equity, bool EvidenceReady, string? Reason);
public sealed record DemoStrategySessionResponse(int Id, string Interval, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? StoppedAtUtc, DateTimeOffset? InterruptedAtUtc, string? FailureMessage, bool AutomationEnabled, bool ManagementEnabled, bool AutomationEnabledAtCreation, decimal FixedLots, decimal RiskReward, decimal MinEmaGapPercent, decimal MaxStopDistancePercent, bool WaitForConfirmationCandle, bool UseEma100Filter, bool UseAdaptiveInitialStop, bool TrailingStopEnabled, bool ExitOnOppositeCrossover, bool SameTrendReentryEnabled, int MaxReentryAgeBars, bool NewEntriesPaused, DateTimeOffset? NewEntriesPausedAtUtc, decimal InitialAllocation, DemoStrategyBudgetResponse Budget, IReadOnlyList<DemoStrategySymbolResponse> Symbols, DemoStrategyRuntimeSnapshot? Runtime);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/demo-strategy-sessions")]
public sealed class DemoStrategySessionsController(EmaBotDbContext database, TradingSettingsService settingsService, DemoStrategyCoordinator coordinator, IOptions<DemoStrategyAutomationOptions> automation, IOptions<DemoExecutionOptions> execution, IOptions<Mt5ExecutionBridgeOptions> bridgeOptions, IDemoExecutionService executions, IMt5BridgeRequestClient bridge) : ControllerBase
{
    [HttpGet("safety")]
    public async Task<ActionResult<DemoAutomationSafetyResponse>> Safety(CancellationToken token)
    {
        var readiness = await executions.ReadinessAsync(token); var status = bridge.GetStatus(); var account = readiness.Account;
        return Ok(new DemoAutomationSafetyResponse(status.Enabled, status.ConnectionState.ToString(), bridgeOptions.Value.Enabled, readiness.Ready, readiness.Reason, account?.TradeMode, account?.AccountTradeAllowed, account?.ExpertTradeAllowed, execution.Value.Enabled, execution.Value.DemoOnly, account?.DemoExecutionEnabled, account?.DemoExecutionAllowed, account?.EaBuildId, account?.SupportsExactProtectionReadback, account?.SupportsNativeExitReason, account?.SupportsBrokerPnlEvidence, automation.Value.Enabled, automation.Value.ManagementEnabled, automation.Value.FixedLots));
    }
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken token)
    {
        var id = await database.DemoStrategySessions.AsNoTracking().Where(item => item.Status == DemoStrategySessionStatus.Running || item.Status == DemoStrategySessionStatus.Interrupted).OrderBy(item => item.Status == DemoStrategySessionStatus.Running ? 0 : 1).ThenByDescending(item => item.CreatedAtUtc).Select(item => (int?)item.Id).FirstOrDefaultAsync(token);
        return id is null ? NoContent() : Ok(await GetResponseAsync(id.Value, token));
    }
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DemoStrategySessionSummaryResponse>>> List([FromQuery] int take = 20, CancellationToken token = default)
    {
        take = Math.Clamp(take, 1, 100); var sessions = await database.DemoStrategySessions.AsNoTracking().Include(item => item.Symbols).OrderByDescending(item => item.CreatedAtUtc).Take(take).ToListAsync(token);
        return Ok(sessions.Select(item => new DemoStrategySessionSummaryResponse(item.Id, item.Interval, item.Status.ToString(), item.CreatedAtUtc, item.StartedAtUtc, item.StoppedAtUtc, item.InterruptedAtUtc, item.FailureMessage, item.Symbols.Count, item.Symbols.Select(symbol => symbol.BrokerSymbol).ToArray(), item.AutomationEnabledAtCreation, item.TrailingStopEnabled, item.ExitOnOppositeCrossover, item.SameTrendReentryEnabled, item.FixedLots, item.RiskReward, item.NewEntriesPaused, item.NewEntriesPausedAtUtc, item.InitialAllocation)).ToArray());
    }
    [HttpPost]
    public async Task<ActionResult<DemoStrategySessionResponse>> Create(CreateDemoStrategySessionRequest request, CancellationToken token)
    {
        var symbols = request.Symbols?.Select(item => item.Trim()).Where(item => item.Length > 0).ToArray() ?? [];
        if (!Mt5NativeTimeframes.IsSupported(request.Interval) || symbols.Length == 0) return BadRequest(new ApiMessage("Use a native MT5 interval and select at least one enabled MT5 symbol."));
        if (request.InitialAllocation <= 0m) return BadRequest(new ApiMessage("Initial allocation must be greater than zero."));
        if (symbols.Length != 1) return BadRequest(new ApiMessage("Overnight allocation mode currently supports exactly one Demo symbol."));
        if (symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length) return BadRequest(new ApiMessage("Symbols must not contain duplicates."));
        if (await database.DemoStrategySessions.AnyAsync(item => item.Status == DemoStrategySessionStatus.Running || item.Status == DemoStrategySessionStatus.Interrupted, token)) return Conflict(new ApiMessage("Stop or resume the existing Demo strategy session before creating another."));
        var requestedSymbols = symbols.ToHashSet(StringComparer.Ordinal);
        var monitoredCandidates = await database.MonitoredSymbols.Where(item => item.IsEnabled && item.Source == MarketDataSource.Mt5Exness).ToListAsync(token);
        var monitored = monitoredCandidates.Where(item => requestedSymbols.Contains(item.Symbol)).ToList();
        if (monitored.Count != symbols.Length) return BadRequest(new ApiMessage("Every selected symbol must be an enabled exact MT5 instrument."));
        var settings = await settingsService.GetAsync(token);
        if (settings.UseHtfRegimeFilter) return BadRequest(new ApiMessage("HTF Regime Filter is currently supported for historical backtesting only."));
        var now = DateTimeOffset.UtcNow;
        var session = new DemoStrategySession
        {
            Interval = request.Interval, Status = DemoStrategySessionStatus.Created, CreatedAtUtc = now, InitialAllocation = request.InitialAllocation,
            AutomationEnabledAtCreation = automation.Value.Enabled, FixedLots = automation.Value.FixedLots,
            RiskReward = settings.RiskReward, MinEmaGapPercent = settings.MinEmaGapPercent, MaxStopDistancePercent = settings.MaxStopDistancePercent,
            WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, UseAdaptiveInitialStop = settings.UseAdaptiveInitialStop,
            TrailingStopEnabled = settings.TrailingStopEnabled, ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover,
            SameTrendReentryEnabled = settings.SameTrendReentryEnabled, MaxReentryAgeBars = settings.MaxReentryAgeBars,
            Symbols = monitored.Select(item => new DemoStrategySessionSymbol { Symbol = item.Symbol, BrokerSymbol = item.Symbol }).ToList()
        };
        database.DemoStrategySessions.Add(session);
        await database.SaveChangesAsync(token);
        return CreatedAtAction(nameof(Get), new { id = session.Id }, ToResponse(session));
    }

    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken token)
    {
        try { await coordinator.StartSessionAsync(id, false, token); return Ok(await GetResponseAsync(id, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("MT5 historical market data is unavailable; observation was not started.")); }
    }

    [HttpPost("{id:int}/resume")]
    public async Task<IActionResult> Resume(int id, CancellationToken token)
    {
        try { await coordinator.StartSessionAsync(id, true, token); return Ok(await GetResponseAsync(id, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("MT5 historical market data is unavailable; observation was not started.")); }
    }

    [HttpPost("{id:int}/stop")]
    public async Task<IActionResult> Stop(int id, CancellationToken token)
    {
        try { await coordinator.StopSessionAsync(id, token); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
    }

    [HttpPost("{id:int}/pause-new-entries")]
    public async Task<IActionResult> PauseNewEntries(int id, CancellationToken token)
    {
        try { await coordinator.PauseNewEntriesAsync(id, token); return Ok(await GetResponseAsync(id, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
    }

    [HttpPost("{id:int}/resume-new-entries")]
    public async Task<IActionResult> ResumeNewEntries(int id, CancellationToken token)
    {
        try { await coordinator.ResumeNewEntriesAsync(id, token); return Ok(await GetResponseAsync(id, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken token) => await GetResponseAsync(id, token) is { } response ? Ok(response) : NotFound(new ApiMessage("Demo strategy session not found."));
    [HttpGet("{id:int}/runtime")]
    public async Task<IActionResult> Runtime(int id, CancellationToken token)
    {
        if (!await database.DemoStrategySessions.AnyAsync(item => item.Id == id, token)) return NotFound(new ApiMessage("Demo strategy session not found."));
        var runtime = coordinator.GetRuntimeSnapshot();
        return Ok(runtime?.SessionId == id ? runtime : null);
    }

    private async Task<DemoStrategySessionResponse?> GetResponseAsync(int id, CancellationToken token)
    {
        var session = await database.DemoStrategySessions.AsNoTracking().Include(item => item.Symbols).ThenInclude(item => item.Intents).ThenInclude(item => item.DemoExecution).ThenInclude(item => item!.ManagementActions).Include(item => item.PositionManagement).SingleOrDefaultAsync(item => item.Id == id, token);
        return session is null ? null : ToResponse(session);
    }
    private DemoStrategySessionResponse ToResponse(DemoStrategySession session)
    {
        var runtime = coordinator.GetRuntimeSnapshot(); if (runtime?.SessionId != session.Id) runtime = null;
        var budget = Budget(session);
        return new(session.Id, session.Interval, session.Status.ToString(), session.CreatedAtUtc, session.StartedAtUtc, session.StoppedAtUtc, session.InterruptedAtUtc, session.FailureMessage, automation.Value.Enabled, automation.Value.ManagementEnabled, session.AutomationEnabledAtCreation, session.FixedLots, session.RiskReward, session.MinEmaGapPercent, session.MaxStopDistancePercent, session.WaitForConfirmationCandle, session.UseEma100Filter, session.UseAdaptiveInitialStop, session.TrailingStopEnabled, session.ExitOnOppositeCrossover, session.SameTrendReentryEnabled, session.MaxReentryAgeBars, session.NewEntriesPaused, session.NewEntriesPausedAtUtc, session.InitialAllocation, budget,
            session.Symbols.Select(symbol => new DemoStrategySymbolResponse(symbol.Symbol, symbol.BrokerSymbol, symbol.LastProcessedClosedCandleUtc, symbol.LastMarketEventUtc, symbol.TrendRegimeDirection, symbol.TrendRegimeCrossoverTimeUtc, symbol.ReentryEligible, symbol.ReentryConsumed, symbol.ReentrySourceDemoExecutionId, symbol.ReentryEligibleAtUtc, symbol.ReentryReason, symbol.Intents.OrderByDescending(item => item.CreatedAtUtc).Take(25).Select(intent => new DemoStrategyIntentResponse(intent.Id, intent.Direction.ToString(), intent.CrossoverTimeUtc, intent.SignalTimeUtc, intent.ExpectedEntryOpenUtc, intent.StructuralStopLoss, intent.IntendedTakeProfit, intent.IntendedVolumeLots, intent.ClientExecutionId, intent.Status.ToString(), intent.DemoExecutionId, intent.IsReentry, intent.ReentrySourceDemoExecutionId, intent.TrendRegimeCrossoverTimeUtc, intent.ReentryAgeBars, intent.Reason, intent.DemoExecution is null ? null : Execution(intent.DemoExecution))).ToArray(), session.PositionManagement.Where(item => item.DemoStrategySessionSymbolId == symbol.Id).Select(Management).ToArray())).ToArray(), runtime);
    }
    private static DemoStrategyBudgetResponse Budget(DemoStrategySession session)
    {
        var executions = session.Symbols.SelectMany(symbol => symbol.Intents).Select(intent => intent.DemoExecution).Where(execution => execution is not null).Cast<DemoExecution>().DistinctBy(execution => execution.Id).ToArray();
        string? currency = null; decimal realized = 0m; decimal unrealized = 0m;
        foreach (var execution in executions)
        {
            if (execution.State is DemoExecutionState.Rejected or DemoExecutionState.Cancelled)
            {
                if (DemoStrategyBudgetEvidencePolicy.IsConclusiveNoFillTerminal(execution)) continue;
                return new(session.InitialAllocation, null, null, null, null, null, false, "Rejected/cancelled execution has ambiguous broker exposure or monetary evidence.");
            }
            var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);
            if (!evidence.Available || evidence.Amount is null || string.IsNullOrWhiteSpace(evidence.AccountCurrency)) return new(session.InitialAllocation, null, null, null, null, null, false, evidence.Reason);
            var current = evidence.AccountCurrency.Trim();
            if (currency is not null && !string.Equals(currency, current, StringComparison.Ordinal)) return new(session.InitialAllocation, null, null, null, null, null, false, "Broker account currencies conflict.");
            currency = current;
            if (execution.State == DemoExecutionState.Closed) realized += evidence.Amount.Value; else unrealized += evidence.Amount.Value;
        }
        var balance = session.InitialAllocation + realized;
        return new(session.InitialAllocation, currency, realized, unrealized, balance, balance + unrealized, true, null);
    }
    private static DemoExecutionSummaryResponse Execution(DemoExecution item) => new(item.Id, item.ClientExecutionId, item.State.ToString(), item.Provider, item.BrokerSymbol, item.Side, item.VolumeLots, item.RequestedStopLoss, item.RequestedTakeProfit, item.CurrentStopLoss, item.CurrentTakeProfit, item.ProtectionObservedAtUtc, item.MagicNumber, item.PositionTicket, item.PositionIdentifier, item.OrderTicket, item.EntryDealTicket, item.ExitDealTicket, item.NativeExitReason, item.NativeExitReasonConflicted, item.FilledVolumeLots, item.AverageFillPrice, item.ClosedVolumeLots, item.AverageClosePrice, item.BrokerExecutedAtUtc, item.BrokerClosedAtUtc, item.BrokerRetcode, item.BrokerMessage, item.CreatedAtUtc, item.PreflightAtUtc, item.SubmittedAtUtc, item.BrokerAcceptedAtUtc, item.ClosedAtUtc, item.ReconciledAtUtc, item.ReconciliationNote, item.ReconciliationSource, item.ManagementActions.OrderByDescending(action => action.CreatedAtUtc).Take(25).Select(action => new DemoExecutionManagementActionResponse(action.Id, action.ClientManagementActionId, action.Kind.ToString(), action.State.ToString(), action.RequestedStopLoss, action.RequestedTakeProfit, action.ObservedBeforeStopLoss, action.ObservedBeforeTakeProfit, action.AppliedStopLoss, action.AppliedTakeProfit, action.BrokerRetcode, action.BrokerMessage, action.CreatedAtUtc, action.SubmittedAtUtc, action.CompletedAtUtc, action.ReconciledAtUtc, action.ReconciliationNote, action.ReconciliationSource)).ToArray());
    private static DemoStrategyManagementResponse Management(DemoStrategyPositionManagement item) => new(item.Id, item.DemoStrategyIntentId, item.DemoExecutionId, item.State.ToString(), item.OriginalEntryPrice, item.OriginalStopLoss, item.OriginalTakeProfit, item.BestFavorablePrice, item.BestFavorableProgressPercent, item.TakeProfitExtensionState.ToString(), item.TargetExtensionAppliedAtUtc, item.HighestAttemptedLockPercent, item.HighestAppliedLockPercent, item.PendingProtectionActionId, item.PendingProtectionLockPercent, item.PendingProtectionExtendsTarget, item.PendingDesiredStopLoss, item.PendingDesiredTakeProfit, item.OppositeSignalTimeUtc, item.OppositeSignalDirection, item.OppositeCloseState.ToString(), item.OppositeCloseRequestedAtUtc, item.LastManagedAtUtc, item.LastReason, item.CreatedAtUtc, item.UpdatedAtUtc);
}
