using EmaBot.Api.Models;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Services;

public sealed class GridBreakoutGuardCandidate
{
    private readonly Func<GridBacktestTelemetry, bool> predicate;
    private GridBreakoutGuardCandidate(string id, string description, int minimumFilledLevel, Func<GridBacktestTelemetry, bool> predicate)
        => (Id, Description, MinimumFilledLevel, this.predicate) = (id, description, minimumFilledLevel, predicate);
    public string Id { get; }
    public string Description { get; }
    public int MinimumFilledLevel { get; }
    public bool Matches(GridBacktestTelemetry row) => row.ExitReasonThisBar is null
        && row.MaxFilledLevelAfterBar >= MinimumFilledLevel && predicate(row);
    public static GridBreakoutGuardCandidate Control { get; } = new("NO_GUARD_CONTROL", "Persisted actual outcomes; no guard", 0, _ => false);
    public static IReadOnlyList<GridBreakoutGuardCandidate> Guards { get; } = Array.AsReadOnly(new[]
    {
        new GridBreakoutGuardCandidate("L3_BOUNDARY_CLOSE", "CloseBeyondFrozenBoundary", 3, t => t.CloseBeyondFrozenBoundary),
        new GridBreakoutGuardCandidate("L3_BOUNDARY_EXTREME_ADX10", "AdverseExtremeBeyondFrozenBoundary AND AdxDeltaFromQualification >= 1.0", 3,
            t => t.AdverseExtremeBeyondFrozenBoundary && t.AdxDeltaFromQualification >= 1m),
        new GridBreakoutGuardCandidate("L3_ADX10_TR15", "AdxDeltaFromQualification >= 1.0 AND CandleTrueRangeAtrRatio >= 1.5", 3,
            t => t.AdxDeltaFromQualification >= 1m && t.CandleTrueRangeAtrRatio >= 1.5m),
        new GridBreakoutGuardCandidate("L3_ADX15_TR15", "AdxDeltaFromQualification >= 1.5 AND CandleTrueRangeAtrRatio >= 1.5", 3,
            t => t.AdxDeltaFromQualification >= 1.5m && t.CandleTrueRangeAtrRatio >= 1.5m),
        new GridBreakoutGuardCandidate("L3_ADX15_ADVERSE2", "AdxDeltaFromQualification >= 1.5 AND ConsecutiveAdverseCloses >= 2", 3,
            t => GridBreakoutGuardRules.Matches(t.MaxFilledLevelAfterBar, t.AdxDeltaFromQualification, t.ConsecutiveAdverseCloses)),
        new GridBreakoutGuardCandidate("L3_COMPOSITE", "CloseBeyondFrozenBoundary OR (AdxDeltaFromQualification >= 1.5 AND CandleTrueRangeAtrRatio >= 1.5)", 3, Composite),
        new GridBreakoutGuardCandidate("L2_COMPOSITE", "CloseBeyondFrozenBoundary OR (AdxDeltaFromQualification >= 1.5 AND CandleTrueRangeAtrRatio >= 1.5)", 2, Composite)
    });
    private static bool Composite(GridBacktestTelemetry t) => t.CloseBeyondFrozenBoundary
        || t.AdxDeltaFromQualification >= 1.5m && t.CandleTrueRangeAtrRatio >= 1.5m;
}
