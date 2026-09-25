using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Services;

// Local observer: no calculator, provider, database, or mutable strategy owner.
internal sealed class GridDeepTelemetryObserver
{
    private GridHistoricalCycleSnapshot? frozen;
    private int deepestLevel, sequence, adverseCloses, boundaryCloses;
    private decimal? previousActiveClose;
    private readonly List<GridHistoricalTelemetry> rows = [];
    public IReadOnlyList<GridHistoricalTelemetry> Rows => rows.AsReadOnly();

    public void StartCycle(GridHistoricalCycleSnapshot snapshot, int levelCount)
    {
        frozen = snapshot; deepestLevel = levelCount;
        sequence = adverseCloses = boundaryCloses = 0; previousActiveClose = null;
    }

    public GridHistoricalTelemetry Observe(Mt5HistoricalExecutionBar bar, GridRangeIndicatorSnapshot current,
        decimal pointSize, decimal? previousBidClose, GridBasketDirection direction,
        int maxBefore, int maxAfter, int newFillCount, GridExitReason? exit)
    {
        var cycle = frozen!;
        var isLong = direction == GridBasketDirection.Long;
        var boundary = isLong ? cycle.Indicators.RangeLow : cycle.Indicators.RangeHigh;
        var beyond = isLong ? bar.Close < boundary : bar.Close > boundary;
        var adverse = previousActiveClose is { } prior && (isLong ? bar.Close < prior : bar.Close > prior);
        adverseCloses = adverse ? adverseCloses + 1 : 0;
        boundaryCloses = beyond ? boundaryCloses + 1 : 0;
        previousActiveClose = bar.Close;
        var body = Math.Abs(bar.Close - bar.Open);
        decimal? trueRange = previousBidClose is { } previous
            ? Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previous), Math.Abs(bar.Low - previous))) : null;
        var row = new GridHistoricalTelemetry
        {
            CycleQualificationTimeUtc = cycle.Indicators.Time, Sequence = sequence++, TimeUtc = bar.CloseTimeUtc,
            Direction = direction.ToString(), BidOpen = bar.Open, BidHigh = bar.High, BidLow = bar.Low, BidClose = bar.Close,
            SpreadPoints = bar.SpreadPoints, SpreadPrice = bar.SpreadPoints * pointSize,
            CurrentAtr = current.Atr, CurrentAdx = current.Adx,
            CurrentRangeHigh = current.RangeHigh, CurrentRangeLow = current.RangeLow,
            DistanceFromAnchorSpacings = Math.Abs(bar.Close - cycle.Anchor) / cycle.Spacing,
            AdverseDistanceFromAnchorSpacings = (isLong ? cycle.Anchor - bar.Close : bar.Close - cycle.Anchor) / cycle.Spacing,
            AdxDeltaFromQualification = current.Adx - cycle.Indicators.Adx,
            AtrRatioToQualification = Ratio(current.Atr, cycle.Indicators.Atr),
            CandleBody = body, CandleTrueRange = trueRange,
            CandleBodyAtrRatio = Ratio(body, current.Atr), CandleTrueRangeAtrRatio = Ratio(trueRange, current.Atr),
            FrozenBoundaryPrice = boundary, CloseBeyondFrozenBoundary = beyond,
            AdverseExtremeBeyondFrozenBoundary = isLong ? bar.Low < boundary : bar.High > boundary,
            BreakoutDistanceSpacings = Math.Max(0m, isLong ? boundary - bar.Close : bar.Close - boundary) / cycle.Spacing,
            ConsecutiveAdverseCloses = adverseCloses, ConsecutiveClosesBeyondFrozenBoundary = boundaryCloses,
            MaxFilledLevelBeforeBar = maxBefore, MaxFilledLevelAfterBar = maxAfter, NewFillCount = newFillCount,
            DeepestConfiguredLevel = deepestLevel, DeepestLevelFilled = maxAfter >= deepestLevel,
            ExitReasonThisBar = exit?.ToString()
        };
        rows.Add(row);
        return row;
    }

    internal void MarkHistoricalBreakoutGuardExit(GridHistoricalTelemetry row)
    {
        if (rows.Count == 0 || !ReferenceEquals(rows[^1], row) || row.ExitReasonThisBar is not null)
            throw new InvalidOperationException("Only the current unclosed telemetry row may receive a guard exit.");
        rows[^1] = row with { ExitReasonThisBar = "BreakoutGuard" };
    }

    private static decimal? Ratio(decimal? numerator, decimal? denominator)
        => denominator is > 0m ? numerator / denominator : null;
}
