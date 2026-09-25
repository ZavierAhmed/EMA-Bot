namespace EmaBot.Api.Strategy.Grid;

// Frozen historical research rule shared by shadow screening and real reruns.
public static class GridBreakoutGuardRules
{
    public const string Id = "L3_ADX15_ADVERSE2";
    public const int MinimumFilledLevel = 3;
    public const decimal MinimumAdxDelta = 1.5m;
    public const int MinimumConsecutiveAdverseCloses = 2;
    public static bool Matches(int maxFilledLevel, decimal? adxDelta, int adverseCloses)
        => maxFilledLevel >= MinimumFilledLevel && adxDelta >= MinimumAdxDelta
            && adverseCloses >= MinimumConsecutiveAdverseCloses;
}
