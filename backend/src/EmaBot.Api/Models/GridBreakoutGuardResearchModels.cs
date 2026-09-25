namespace EmaBot.Api.Models;

public sealed record GridBreakoutGuardBasketResult
{
    public int RunId { get; init; }
    public int CycleId { get; init; }
    public int BasketId { get; init; }
    public string CandidateId { get; init; } = string.Empty;
    public string StrategyId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string ActualExitReason { get; init; } = string.Empty;
    public DateTimeOffset ActualExitTimeUtc { get; init; }
    public decimal ActualExitPrice { get; init; }
    public decimal ActualNetPnl { get; init; }
    public bool Triggered { get; init; }
    public DateTimeOffset? TriggerTimeUtc { get; init; }
    public int? TriggerLevel { get; init; }
    public int? MaxFilledLevelAtTrigger { get; init; }
    public decimal? TriggerBidClose { get; init; }
    public decimal? TriggerSpreadPrice { get; init; }
    public decimal? ShadowExitPrice { get; init; }
    public decimal? TriggerAdx { get; init; }
    public decimal? TriggerAdxDelta { get; init; }
    public decimal? TriggerAtr { get; init; }
    public decimal? TriggerAtrRatio { get; init; }
    public decimal? TriggerTrueRangeAtrRatio { get; init; }
    public decimal? TriggerBodyAtrRatio { get; init; }
    public bool? TriggerCloseBeyondBoundary { get; init; }
    public bool? TriggerExtremeBeyondBoundary { get; init; }
    public int? TriggerConsecutiveAdverseCloses { get; init; }
    public int? TriggerConsecutiveBoundaryCloses { get; init; }
    public int? ShadowOpenLegCount { get; init; }
    public decimal ShadowGrossPnl { get; init; }
    public decimal ShadowEntryCommission { get; init; }
    public decimal ShadowExitCommission { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public decimal DeltaVsActualNetPnl { get; init; }
    public string Classification { get; init; } = string.Empty;
}

public sealed record GridBreakoutGuardCandidateResult
{
    public string CandidateId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int MinimumFilledLevel { get; init; }
    public string ResultType { get; init; } = string.Empty;
    public int BasketCount { get; init; }
    public int TriggeredBasketCount { get; init; }
    public int NoTriggerBasketCount { get; init; }
    public int EmergencyStopInterceptedCount { get; init; }
    public int WinnerCutEarlyCount { get; init; }
    public int EndOfDataInterceptedCount { get; init; }
    public decimal ActualNetPnl { get; init; }
    public decimal ActualGrossProfit { get; init; }
    public decimal ActualGrossLoss { get; init; }
    public decimal? ActualGrossProfitFactor { get; init; }
    public decimal? ActualNetProfitFactor { get; init; }
    public decimal FixedPathShadowNetPnl { get; init; }
    public decimal FixedPathDeltaVsActual { get; init; }
    public decimal SavedLossAmount { get; init; }
    public decimal LostWinnerProfitAmount { get; init; }
    public int TriggeredActualLossCount { get; init; }
    public int TriggeredActualWinnerCount { get; init; }
    public decimal? AverageDeltaPerTriggeredBasket { get; init; }
    public int PositiveDeltaBasketCount { get; init; }
    public int NegativeDeltaBasketCount { get; init; }
    public int ZeroDeltaBasketCount { get; init; }
    public decimal FixedPathShadowGrossProfit { get; init; }
    public decimal FixedPathShadowGrossLoss { get; init; }
    public decimal? FixedPathShadowProfitFactor { get; init; }
    public int NativeProfitCallCount { get; init; }
    public int UniqueNativeProfitRequestCount { get; init; }
}

public sealed record GridBreakoutGuardShadowResult(GridBacktestRun Source,
    IReadOnlyList<GridBreakoutGuardCandidateResult> Candidates, IReadOnlyList<GridBreakoutGuardBasketResult> Baskets,
    int NativeProfitCallCount, int UniqueNativeProfitRequestCount)
{
    public const string ResultType = "FIXED-PATH SHADOW RESULT / SCREENING ONLY";
    public const string Warning = "These results are screening estimates, not a strategy backtest. Early exits were evaluated against the original saved basket path. Future sizing, cooldown, qualification and later baskets were not rerun.";
}
