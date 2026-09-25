namespace EmaBot.Api.Strategy.Grid;

// Immutable historical-only behavior and export metadata. No customizable rules.
public sealed class GridHistoricalBreakoutGuard
{
    private readonly Func<int, decimal?, int, bool> predicate;
    private GridHistoricalBreakoutGuard(string id, int level, decimal adx, int adverse, Func<int, decimal?, int, bool> predicate)
        => (Id, MinimumFilledLevel, MinimumAdxDelta, MinimumConsecutiveAdverseCloses, this.predicate) = (id, level, adx, adverse, predicate);
    public string Id { get; }
    public int MinimumFilledLevel { get; }
    public decimal MinimumAdxDelta { get; }
    public int MinimumConsecutiveAdverseCloses { get; }
    public bool Matches(int level, decimal? adxDelta, int adverseCloses) => predicate(level, adxDelta, adverseCloses);
    private static GridHistoricalBreakoutGuard L3 { get; } = new(GridBreakoutGuardRules.Id, GridBreakoutGuardRules.MinimumFilledLevel,
        GridBreakoutGuardRules.MinimumAdxDelta, GridBreakoutGuardRules.MinimumConsecutiveAdverseCloses, GridBreakoutGuardRules.Matches);
    private static GridHistoricalBreakoutGuard L4 { get; } = new(GridBreakoutGuardL4Rules.Id, GridBreakoutGuardL4Rules.MinimumFilledLevel,
        GridBreakoutGuardL4Rules.MinimumAdxDelta, GridBreakoutGuardL4Rules.MinimumConsecutiveAdverseCloses, GridBreakoutGuardL4Rules.Matches);
    public static GridHistoricalBreakoutGuard? Resolve(string? id) => id switch
    {
        null => null,
        GridBreakoutGuardRules.Id => L3,
        GridBreakoutGuardL4Rules.Id => L4,
        _ => throw new ArgumentException("Unsupported frozen historical Grid guard.", nameof(id))
    };
}
