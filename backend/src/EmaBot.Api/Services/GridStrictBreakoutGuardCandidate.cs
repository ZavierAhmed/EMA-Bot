using EmaBot.Api.Models;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Services;

// Separate server-owned experiment; never added to the G4B1 catalog.
public sealed class GridStrictBreakoutGuardCandidate
{
    private GridStrictBreakoutGuardCandidate(string id, int level, decimal? adx, int? adverse, bool reference = false, Func<int, decimal?, int, bool>? sharedPredicate = null)
        => (Id, MinimumFilledLevel, MinimumAdxDelta, MinimumConsecutiveAdverseCloses, IsReference, this.sharedPredicate) = (id, level, adx, adverse, reference, sharedPredicate);
    public string Id { get; }
    public int MinimumFilledLevel { get; }
    public decimal? MinimumAdxDelta { get; }
    public int? MinimumConsecutiveAdverseCloses { get; }
    private bool IsReference { get; }
    private readonly Func<int, decimal?, int, bool>? sharedPredicate;
    public string Description => MinimumAdxDelta is null ? "Persisted actual outcomes; no guard"
        : FormattableString.Invariant($"Level >= {MinimumFilledLevel} AND AdxDeltaFromQualification >= {MinimumAdxDelta} AND ConsecutiveAdverseCloses >= {MinimumConsecutiveAdverseCloses}");
    public bool Matches(GridBacktestTelemetry row) => row.ExitReasonThisBar is null && MinimumAdxDelta is not null
        && (IsReference ? GridBreakoutGuardRules.Matches(row.MaxFilledLevelAfterBar, row.AdxDeltaFromQualification, row.ConsecutiveAdverseCloses)
            : sharedPredicate is not null ? sharedPredicate(row.MaxFilledLevelAfterBar, row.AdxDeltaFromQualification, row.ConsecutiveAdverseCloses)
            : row.MaxFilledLevelAfterBar >= MinimumFilledLevel && row.AdxDeltaFromQualification >= MinimumAdxDelta
                && row.ConsecutiveAdverseCloses >= MinimumConsecutiveAdverseCloses);
    public static IReadOnlyList<GridStrictBreakoutGuardCandidate> All { get; } = Array.AsReadOnly(new[]
    {
        new GridStrictBreakoutGuardCandidate("NO_GUARD_CONTROL", 0, null, null),
        new GridStrictBreakoutGuardCandidate("CURRENT_" + GridBreakoutGuardRules.Id, GridBreakoutGuardRules.MinimumFilledLevel,
            GridBreakoutGuardRules.MinimumAdxDelta, GridBreakoutGuardRules.MinimumConsecutiveAdverseCloses, true),
        new GridStrictBreakoutGuardCandidate(GridBreakoutGuardL4Rules.Id, GridBreakoutGuardL4Rules.MinimumFilledLevel,
            GridBreakoutGuardL4Rules.MinimumAdxDelta, GridBreakoutGuardL4Rules.MinimumConsecutiveAdverseCloses, sharedPredicate: GridBreakoutGuardL4Rules.Matches),
        new GridStrictBreakoutGuardCandidate("L3_ADX20_ADVERSE2", 3, 2.0m, 2),
        new GridStrictBreakoutGuardCandidate("L3_ADX15_ADVERSE3", 3, 1.5m, 3),
        new GridStrictBreakoutGuardCandidate("L4_ADX20_ADVERSE2", 4, 2.0m, 2)
    });
}
