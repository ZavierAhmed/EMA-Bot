namespace EmaBot.Api.Models;

// G4B3-only threshold evidence; existing G4B1 DTO and workbook columns stay frozen.
public sealed record GridStrictBreakoutGuardCandidateResult(GridBreakoutGuardCandidateResult Result,
    decimal? MinimumAdxDelta, int? MinimumConsecutiveAdverseCloses);
