using EmaBot.Api.Strategy;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Models;

// This is deliberately separate from both PaperSession and DemoExecution.  It records
// the strategy decision which may (once only) be linked to the broker-intent ledger.
public enum DemoStrategySessionStatus { Created, Running, Interrupted, Stopped, Faulted }
public enum DemoStrategyIntentStatus { Created, WaitingForEntryWindow, Submitting, ExecutionLinked, Rejected, Expired, Blocked, ReconciliationRequired }

public sealed class DemoStrategySession
{
    public int Id { get; set; }
    public required string Interval { get; set; }
    public DemoStrategySessionStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? StoppedAtUtc { get; set; }
    public DateTimeOffset? InterruptedAtUtc { get; set; }
    public string? FailureMessage { get; set; }
    public bool AutomationEnabledAtCreation { get; set; }
    public decimal FixedLots { get; set; }
    public decimal RiskReward { get; set; }
    public decimal MinEmaGapPercent { get; set; }
    public decimal MaxStopDistancePercent { get; set; }
    public bool WaitForConfirmationCandle { get; set; }
    public bool UseEma100Filter { get; set; }
    public bool UseAdaptiveInitialStop { get; set; }
    public List<DemoStrategySessionSymbol> Symbols { get; set; } = [];
    public List<DemoStrategyIntent> Intents { get; set; } = [];
}

public sealed class DemoStrategySessionSymbol
{
    public int Id { get; set; }
    public int DemoStrategySessionId { get; set; }
    [JsonIgnore] public DemoStrategySession? DemoStrategySession { get; set; }
    public required string Symbol { get; set; }
    public required string BrokerSymbol { get; set; }
    public DateTimeOffset? LastProcessedClosedCandleUtc { get; set; }
    public DateTimeOffset? LastMarketEventUtc { get; set; }
    public List<DemoStrategyIntent> Intents { get; set; } = [];
}

public sealed class DemoStrategyIntent
{
    public int Id { get; set; }
    public int DemoStrategySessionId { get; set; }
    [JsonIgnore] public DemoStrategySession? DemoStrategySession { get; set; }
    public int DemoStrategySessionSymbolId { get; set; }
    [JsonIgnore] public DemoStrategySessionSymbol? DemoStrategySessionSymbol { get; set; }
    public SignalDirection Direction { get; set; }
    public DateTimeOffset CrossoverTimeUtc { get; set; }
    public DateTimeOffset SignalTimeUtc { get; set; }
    public DateTimeOffset ExpectedEntryOpenUtc { get; set; }
    public decimal SignalOpen { get; set; }
    public decimal SignalClose { get; set; }
    public decimal? SignalEma9 { get; set; }
    public decimal? SignalEma15 { get; set; }
    public decimal? SignalEma100 { get; set; }
    public decimal? SignalGapPercent { get; set; }
    public GapState SignalGapState { get; set; }
    public decimal StructuralStopLoss { get; set; }
    public StopSourceType StopSourceType { get; set; }
    public DateTimeOffset StopSourceTimeUtc { get; set; }
    public decimal? IntendedTakeProfit { get; set; }
    public decimal IntendedVolumeLots { get; set; }
    public Guid ClientExecutionId { get; set; }
    public DemoStrategyIntentStatus Status { get; set; }
    public int? DemoExecutionId { get; set; }
    [JsonIgnore] public DemoExecution? DemoExecution { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
}
