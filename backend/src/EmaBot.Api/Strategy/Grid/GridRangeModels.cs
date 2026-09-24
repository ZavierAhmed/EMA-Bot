namespace EmaBot.Api.Strategy.Grid;

public sealed record GridRangeSettings(int LevelCount = 5, decimal GridBasketRiskPercent = 1m, int CooldownBars = 3)
{
    public const string StrategyId = "GRID_RANGE_V1";
    public int EmergencyStopDistanceLevels => LevelCount + 1;
    public bool IsValid => LevelCount is 4 or 5 && GridBasketRiskPercent is > 0m and <= 100m && CooldownBars >= 0;
}

public enum GridBasketDirection { Long, Short }
[Flags]
public enum GridAllowedDirections { None = 0, Long = 1, Short = 2, Both = Long | Short }
public enum GridLevelStatus { Candidate, Filled, Canceled, Closed }
public enum GridExitReason { TakeProfit, EmergencyStop }
public enum GridCycleDiagnostics
{
    InsufficientWarmup, InvalidCandles, InvalidSettings, AtrUnavailable, AdxUnavailable,
    InvalidAtr, AdxAboveThreshold, InvalidAnchorOrSpacing, CloseOutsideAnchor,
    RiskBelowMinimumVolume, RiskCannotBeSafelySized, RiskCalculationUnavailable,
    MarginCalculationUnavailable, InsufficientMargin, ActiveCycle, Cooldown, AmbiguousFirstSide
}

public sealed record GridRangeIndicatorSnapshot(DateTimeOffset Time, decimal RangeHigh, decimal RangeLow,
    decimal Close, decimal? Atr, decimal? Adx, int CompletedBars);
public sealed record GridRangeQualification(GridRangeIndicatorSnapshot Snapshot, GridCycleDiagnostics? Failure)
{
    public bool IsQualified => Failure is null;
    public decimal Anchor => (Snapshot.RangeHigh + Snapshot.RangeLow) / 2m;
    public decimal Spacing => (Snapshot.Atr ?? 0m) * .50m;
}
public sealed record GridRangeLevel(int Number, GridBasketDirection Direction, decimal Price, decimal Lots,
    GridLevelStatus Status = GridLevelStatus.Candidate, DateTimeOffset? FillTime = null);
public sealed record GridRiskAccount(decimal Equity, decimal FreeMargin, decimal VolumeMin, decimal VolumeMax,
    decimal VolumeStep, decimal? VolumeLimit = null, decimal ExistingLongVolume = 0m, decimal ExistingShortVolume = 0m,
    GridAllowedDirections AllowedDirections = GridAllowedDirections.Both);
public sealed record GridRiskDiagnostic(string Operation, string Symbol, GridBasketDirection Direction,
    decimal Lots, decimal Entry, decimal Stop, string Detail);
public sealed record GridRangeSizing(decimal Lots, decimal TargetRiskAmount, decimal? LongRisk, decimal? ShortRisk,
    decimal? LongMargin, decimal? ShortMargin, GridCycleDiagnostics? Failure = null, GridRiskDiagnostic? Diagnostic = null,
    GridAllowedDirections AllowedDirections = GridAllowedDirections.Both)
{
    public bool IsSuccess => Failure is null && Lots > 0m;
    public bool Allows(GridBasketDirection direction) => (AllowedDirections &
        (direction == GridBasketDirection.Long ? GridAllowedDirections.Long : GridAllowedDirections.Short)) != 0;
}

// Construction and transitions are owned by the engine; callers cannot mutate frozen prices or legs.
public sealed class GridRangeCycle
{
    internal GridRangeCycle(string symbol, GridRangeSettings settings, GridRangeQualification qualification, GridRangeSizing sizing)
    {
        Symbol = symbol; Settings = settings; Snapshot = qualification.Snapshot; Sizing = sizing;
        Anchor = qualification.Anchor; Spacing = qualification.Spacing;
        LongStop = Anchor - settings.EmergencyStopDistanceLevels * Spacing; ShortStop = Anchor + settings.EmergencyStopDistanceLevels * Spacing;
        Levels = Array.AsReadOnly(Enum.GetValues<GridBasketDirection>().SelectMany(direction =>
            Enumerable.Range(1, settings.LevelCount).Select(n => new GridRangeLevel(n, direction,
                Anchor + (direction == GridBasketDirection.Long ? -n : n) * Spacing, sizing.Lots,
                sizing.Allows(direction) ? GridLevelStatus.Candidate : GridLevelStatus.Canceled))).ToArray());
    }
    public string Symbol { get; }
    public GridRangeSettings Settings { get; }
    public GridRangeIndicatorSnapshot Snapshot { get; }
    public GridRangeSizing Sizing { get; }
    public decimal Anchor { get; }
    public decimal Spacing { get; }
    public decimal LongStop { get; }
    public decimal ShortStop { get; }
    public decimal TakeProfit => Anchor;
    public IReadOnlyList<GridRangeLevel> Levels { get; internal set; }
    public GridBasketDirection? Direction { get; internal set; }
    public GridExitReason? ExitReason { get; internal set; }
    public decimal? ExitPrice { get; internal set; }
    public DateTimeOffset? ExitTime { get; internal set; }
}
public sealed record GridCycleCreation(GridRangeCycle? Cycle, GridCycleDiagnostics? Failure, GridRangeSizing? Sizing = null);
public sealed record GridBarResult(IReadOnlyList<GridRangeLevel> NewFills, GridExitReason? ExitReason = null,
    GridCycleDiagnostics? Diagnostic = null);
