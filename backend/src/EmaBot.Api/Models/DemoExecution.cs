namespace EmaBot.Api.Models;

// This is deliberately separate from PaperTrade.  Paper remains a simulator;
// these records are an auditable broker-intent and reconciliation ledger.
public enum DemoExecutionState
{
    Created, PreflightPassed, Submitting, BrokerAccepted, PartiallyFilled, Open,
    CloseRequested, Closed, Rejected, Cancelled, ReconciliationRequired
}

public sealed class DemoExecution
{
    public int Id { get; set; }
    public Guid ClientExecutionId { get; set; }
    public DemoExecutionState State { get; set; }
    public string Provider { get; set; } = "MT5";
    public string ExpectedAccountFingerprint { get; set; } = string.Empty;
    public string ExpectedServer { get; set; } = string.Empty;
    public string BrokerSymbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal VolumeLots { get; set; }
    public decimal? RequestedStopLoss { get; set; }
    public decimal? RequestedTakeProfit { get; set; }
    // Broker-derived protection only.  Requested* remain the immutable entry request.
    public decimal? CurrentStopLoss { get; set; }
    public decimal? CurrentTakeProfit { get; set; }
    public DateTimeOffset? ProtectionObservedAtUtc { get; set; }
    public long MagicNumber { get; set; }
    public string CorrelationMarker { get; set; } = string.Empty;
    public long? PositionTicket { get; set; }
    public long? PositionIdentifier { get; set; }
    public long? OrderTicket { get; set; }
    public long? DealTicket { get; set; }
    public long? EntryDealTicket { get; set; }
    public long? ExitDealTicket { get; set; }
    // Native DEAL_REASON from the exact exit deal only. It is evidence, not intent.
    public string? NativeExitReason { get; set; }
    // Once exact terminal evidence conflicts, the audit value remains but can never
    // become automatic strategy evidence without manual review.
    public bool NativeExitReasonConflicted { get; set; }
    public decimal? FilledVolumeLots { get; set; }
    public decimal? AverageFillPrice { get; set; }
    public decimal? ClosedVolumeLots { get; set; }
    public decimal? AverageClosePrice { get; set; }
    public DateTimeOffset? BrokerExecutedAtUtc { get; set; }
    public DateTimeOffset? BrokerClosedAtUtc { get; set; }
    public string? BrokerRetcode { get; set; }
    public string? BrokerMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PreflightAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? BrokerAcceptedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public DateTimeOffset? ReconciledAtUtc { get; set; }
    public string? ReconciliationNote { get; set; }
    public string? ReconciliationSource { get; set; }
    public List<DemoExecutionManagementAction> ManagementActions { get; set; } = [];
}

public enum DemoExecutionManagementActionKind { ModifyProtection }
public enum DemoExecutionManagementActionState { Created, Submitting, Applied, Rejected, ReconciliationRequired }

// An auditable, idempotent ledger for one protection write.  It intentionally does
// not change the lifecycle of the underlying broker execution.
public sealed class DemoExecutionManagementAction
{
    public int Id { get; set; }
    public Guid ClientManagementActionId { get; set; }
    public int DemoExecutionId { get; set; }
    public DemoExecutionManagementActionKind Kind { get; set; }
    public DemoExecutionManagementActionState State { get; set; }
    public decimal? RequestedStopLoss { get; set; }
    public decimal? RequestedTakeProfit { get; set; }
    public decimal? ObservedBeforeStopLoss { get; set; }
    public decimal? ObservedBeforeTakeProfit { get; set; }
    public decimal? AppliedStopLoss { get; set; }
    public decimal? AppliedTakeProfit { get; set; }
    public string? BrokerRetcode { get; set; }
    public string? BrokerMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? ReconciledAtUtc { get; set; }
    public string? ReconciliationNote { get; set; }
    public string? ReconciliationSource { get; set; }
    public DemoExecution? DemoExecution { get; set; }
}
