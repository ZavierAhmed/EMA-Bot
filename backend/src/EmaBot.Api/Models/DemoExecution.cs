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
    public long MagicNumber { get; set; }
    public string CorrelationMarker { get; set; } = string.Empty;
    public long? PositionTicket { get; set; }
    public long? PositionIdentifier { get; set; }
    public long? OrderTicket { get; set; }
    public long? DealTicket { get; set; }
    public long? EntryDealTicket { get; set; }
    public long? ExitDealTicket { get; set; }
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
}
