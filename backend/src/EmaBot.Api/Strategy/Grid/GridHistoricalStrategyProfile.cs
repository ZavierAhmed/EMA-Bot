namespace EmaBot.Api.Strategy.Grid;

// Historical research only. No client-supplied settings or execution registration.
public sealed class GridHistoricalStrategyProfile
{
    public const string ResearchStrategyId = "GRID_RANGE_4L_RESEARCH_V1";
    public static GridHistoricalStrategyProfile Baseline5Level { get; } = new(GridRangeSettings.StrategyId, new());
    public static GridHistoricalStrategyProfile Research4Level { get; } = new(ResearchStrategyId, new(LevelCount: 4));

    private GridHistoricalStrategyProfile(string strategyId, GridRangeSettings settings)
        => (StrategyId, Settings) = (strategyId, settings);
    public string StrategyId { get; }
    public GridRangeSettings Settings { get; }
    public static GridHistoricalStrategyProfile Resolve(string strategyId) => strategyId switch
    {
        GridRangeSettings.StrategyId => Baseline5Level,
        ResearchStrategyId => Research4Level,
        _ => throw new ArgumentException("Unsupported historical Grid StrategyId.", nameof(strategyId))
    };
}
