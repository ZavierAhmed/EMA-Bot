namespace EmaBot.Api.Strategy.Grid;

// Frozen L4 rule shared by G4B3 screening and G4B4 real historical execution.
public static class GridBreakoutGuardL4Rules
{
    public const string Id = "L4_ADX15_ADVERSE2";
    public const int MinimumFilledLevel = 4;
    public const decimal MinimumAdxDelta = 1.5m;
    public const int MinimumConsecutiveAdverseCloses = 2;
    public static bool Matches(int maxFilledLevel, decimal? adxDelta, int adverseCloses)
        => maxFilledLevel >= MinimumFilledLevel && adxDelta >= MinimumAdxDelta
            && adverseCloses >= MinimumConsecutiveAdverseCloses;
}
