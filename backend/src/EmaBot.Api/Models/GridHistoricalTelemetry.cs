namespace EmaBot.Api.Models;

// Historical research evidence only; never consumed by strategy decisions.
public sealed record GridHistoricalTelemetry
{
    public DateTimeOffset CycleQualificationTimeUtc { get; init; }
    public int Sequence { get; init; }
    public DateTimeOffset TimeUtc { get; init; }
    public string Direction { get; init; } = string.Empty;
    public decimal BidOpen { get; init; }
    public decimal BidHigh { get; init; }
    public decimal BidLow { get; init; }
    public decimal BidClose { get; init; }
    public int SpreadPoints { get; init; }
    public decimal SpreadPrice { get; init; }
    public decimal? CurrentAtr { get; init; }
    public decimal? CurrentAdx { get; init; }
    public decimal CurrentRangeHigh { get; init; }
    public decimal CurrentRangeLow { get; init; }
    public decimal DistanceFromAnchorSpacings { get; init; }
    public decimal AdverseDistanceFromAnchorSpacings { get; init; }
    public decimal? AdxDeltaFromQualification { get; init; }
    public decimal? AtrRatioToQualification { get; init; }
    public decimal CandleBody { get; init; }
    public decimal? CandleTrueRange { get; init; }
    public decimal? CandleBodyAtrRatio { get; init; }
    public decimal? CandleTrueRangeAtrRatio { get; init; }
    public decimal FrozenBoundaryPrice { get; init; }
    public bool CloseBeyondFrozenBoundary { get; init; }
    public bool AdverseExtremeBeyondFrozenBoundary { get; init; }
    public decimal BreakoutDistanceSpacings { get; init; }
    public int ConsecutiveAdverseCloses { get; init; }
    public int ConsecutiveClosesBeyondFrozenBoundary { get; init; }
    public int MaxFilledLevelBeforeBar { get; init; }
    public int MaxFilledLevelAfterBar { get; init; }
    public int NewFillCount { get; init; }
    public int DeepestConfiguredLevel { get; init; }
    public bool DeepestLevelFilled { get; init; }
    public string? ExitReasonThisBar { get; init; }
}
