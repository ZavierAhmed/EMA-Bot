namespace EmaBot.Api.Models;

// Historical research evidence only; never consumed by strategy decisions.
public sealed class GridBacktestTelemetry
{
    public int Id { get; set; }
    public int GridBacktestCycleId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset TimeUtc { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal BidOpen { get; set; }
    public decimal BidHigh { get; set; }
    public decimal BidLow { get; set; }
    public decimal BidClose { get; set; }
    public int SpreadPoints { get; set; }
    public decimal SpreadPrice { get; set; }
    public decimal? CurrentAtr { get; set; }
    public decimal? CurrentAdx { get; set; }
    public decimal CurrentRangeHigh { get; set; }
    public decimal CurrentRangeLow { get; set; }
    public decimal DistanceFromAnchorSpacings { get; set; }
    public decimal AdverseDistanceFromAnchorSpacings { get; set; }
    public decimal? AdxDeltaFromQualification { get; set; }
    public decimal? AtrRatioToQualification { get; set; }
    public decimal CandleBody { get; set; }
    public decimal? CandleTrueRange { get; set; }
    public decimal? CandleBodyAtrRatio { get; set; }
    public decimal? CandleTrueRangeAtrRatio { get; set; }
    public decimal FrozenBoundaryPrice { get; set; }
    public bool CloseBeyondFrozenBoundary { get; set; }
    public bool AdverseExtremeBeyondFrozenBoundary { get; set; }
    public decimal BreakoutDistanceSpacings { get; set; }
    public int ConsecutiveAdverseCloses { get; set; }
    public int ConsecutiveClosesBeyondFrozenBoundary { get; set; }
    public int MaxFilledLevelBeforeBar { get; set; }
    public int MaxFilledLevelAfterBar { get; set; }
    public int NewFillCount { get; set; }
    public int DeepestConfiguredLevel { get; set; }
    public bool DeepestLevelFilled { get; set; }
    public string? ExitReasonThisBar { get; set; }
}
