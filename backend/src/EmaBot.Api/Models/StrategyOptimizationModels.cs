using EmaBot.Api.Strategy;

namespace EmaBot.Api.Models;

public enum StrategyOptimizationStatus { Queued, Running, Completed, Cancelled, Failed, Interrupted }

public sealed class StrategyOptimizationRun
{
    public int Id { get; set; }
    public StrategyOptimizationStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset RequestedStartUtc { get; set; }
    public DateTimeOffset RequestedEndUtc { get; set; }
    public string SymbolsJson { get; set; } = "[]";
    public string TimeframesJson { get; set; } = "[]";
    public string GridJson { get; set; } = "{}";
    public string BaselineSettingsJson { get; set; } = "{}";
    public int CandidateCount { get; set; }
    public int MarketCount { get; set; }
    public int TotalWork { get; set; }
    public int CompletedWork { get; set; }
    public decimal SimulatedAccountBalanceUsdt { get; set; }
    public decimal FixedOrderSizeUsdt { get; set; }
    public decimal MarginPerTradePercent { get; set; }
    public decimal Leverage { get; set; }
    public decimal FeePercentPerSide { get; set; }
    public PositionSizingMode PositionSizingMode { get; set; }
    public int? RecommendedCandidateId { get; set; }
    public List<StrategyOptimizationCandidate> Candidates { get; set; } = [];
    public List<StrategyOptimizationTrade> Trades { get; set; } = [];
}

public sealed class StrategyOptimizationCandidate
{
    public int Id { get; set; }
    public int StrategyOptimizationRunId { get; set; }
    public StrategyOptimizationRun? StrategyOptimizationRun { get; set; }
    public decimal RiskReward { get; set; }
    public decimal MinEmaGapPercent { get; set; }
    public decimal MaxStopDistancePercent { get; set; }
    public bool WaitForConfirmationCandle { get; set; }
    public bool UseEma100Filter { get; set; }
    public bool TrailingStopEnabled { get; set; }
    public bool IsBaseline { get; set; }
    public bool RobustCandidate { get; set; }
    public int? RobustRank { get; set; }
    public decimal ProfitableMarketRatio { get; set; }
    public OptimizationMetrics Full { get; set; } = new();
    public OptimizationMetrics Development { get; set; } = new();
    public OptimizationMetrics Validation { get; set; } = new();
    public List<StrategyOptimizationMarketResult> MarketResults { get; set; } = [];
}

public sealed class StrategyOptimizationMarketResult
{
    public int Id { get; set; }
    public int StrategyOptimizationCandidateId { get; set; }
    public StrategyOptimizationCandidate? StrategyOptimizationCandidate { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public OptimizationMetrics Full { get; set; } = new();
    public OptimizationMetrics Development { get; set; } = new();
    public OptimizationMetrics Validation { get; set; } = new();
}

public sealed class StrategyOptimizationTrade
{
    public int Id { get; set; }
    public int StrategyOptimizationRunId { get; set; }
    public int StrategyOptimizationCandidateId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public SignalDirection Direction { get; set; }
    public bool IsReentry { get; set; }
    public DateTimeOffset EntryTimeUtc { get; set; }
    public DateTimeOffset ExitTimeUtc { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal FinalStopLoss { get; set; }
    public decimal OriginalTakeProfit { get; set; }
    public decimal FinalTakeProfit { get; set; }
    public decimal GrossPnlUsdt { get; set; }
    public decimal TotalFeesUsdt { get; set; }
    public decimal NetPnlUsdt { get; set; }
    public decimal NetRMultiple { get; set; }
    public BacktestExitReason ExitReason { get; set; }
    public decimal? SignalEma9 { get; set; }
    public decimal? SignalEma15 { get; set; }
    public decimal? SignalEma100 { get; set; }
    public decimal? SignalGapPercent { get; set; }
    public decimal ExpectedNetTargetR { get; set; }
}

public sealed class OptimizationMetrics
{
    public decimal GrossPnlUsdt { get; set; } public decimal TotalFeesUsdt { get; set; } public decimal NetPnlUsdt { get; set; }
    public decimal NetReturnPercent { get; set; } public decimal? GrossProfitFactor { get; set; } public decimal? NetProfitFactor { get; set; }
    public decimal WinRatePercent { get; set; } public int TotalTrades { get; set; } public int WinningTrades { get; set; } public int LosingTrades { get; set; } public int BreakEvenTrades { get; set; } public int LongTrades { get; set; } public int ShortTrades { get; set; }
    public decimal MaxDrawdownUsdt { get; set; } public decimal MaxDrawdownPercent { get; set; } public decimal AverageNetPnl { get; set; } public decimal AverageNetR { get; set; } public decimal MedianHoldingMinutes { get; set; } public decimal MaximumHoldingMinutes { get; set; }
    public decimal LongNetPnl { get; set; } public decimal ShortNetPnl { get; set; } public int ReentryTrades { get; set; } public decimal ReentryNetPnl { get; set; }
    public decimal MedianExpectedNetTargetR { get; set; } public decimal MinimumExpectedNetTargetR { get; set; } public decimal AverageExpectedNetTargetR { get; set; }
    public int TotalCrossovers { get; set; } public int LongSignals { get; set; } public int ShortSignals { get; set; } public int ConfirmationFailed { get; set; } public int RejectedByEma100 { get; set; } public int RejectedByEmaGap { get; set; } public int RejectedByStopDistance { get; set; } public int RejectedByFees { get; set; } public int InvalidStopLoss { get; set; } public int SkippedWhilePositionOpen { get; set; } public int NoEntryCandle { get; set; }
}
