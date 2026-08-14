using EmaBot.Api.Models;
using EmaBot.Api.Services;

namespace EmaBot.Api.Tests;

public sealed class StrategyOptimizerCandidateTests
{
    [Fact]
    public void BaselineAlreadyInGrid_WithDifferentDecimalScale_IsIncludedOnce()
    {
        var settings = Settings(1.100000000000000000m);
        var candidates = StrategyOptimizationService.CandidateSettings(Grid([1.1m]), settings);

        Assert.Single(candidates);
        Assert.Single(candidates, candidate => Same(candidate, settings));
    }

    [Fact]
    public void BaselineOutsideGrid_IsAddedOnce()
    {
        var settings = Settings(1.1m);
        var candidates = StrategyOptimizationService.CandidateSettings(Grid([.9m]), settings);

        Assert.Equal(2, candidates.Count);
        Assert.Single(candidates, candidate => Same(candidate, settings));
    }

    [Fact]
    public void NumericValuesWithDifferentDecimalScales_Deduplicate()
    {
        var candidates = StrategyOptimizationService.CandidateSettings(Grid([1.1m, 1.100000000000000000m]), Settings(1.1m));

        Assert.Single(candidates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdaptiveInitialStop_IsFixedAcrossCandidateGeneration(bool enabled)
    {
        var settings = Settings(1.1m); settings.UseAdaptiveInitialStop = enabled;
        var candidates = StrategyOptimizationService.CandidateSettings(Grid([.9m, 1.1m]), settings);
        Assert.All(candidates, candidate => Assert.Equal(enabled, candidate.UseAdaptiveInitialStop));
    }

    private static StrategyOptimizerGrid Grid(IReadOnlyList<decimal> riskRewards) => new(riskRewards, [0m], [0m], [false], [true], [false]);
    private static TradingSettings Settings(decimal riskReward) => new() { RiskReward = riskReward, MinEmaGapPercent = 0m, MaxStopDistancePercent = 0m, WaitForConfirmationCandle = false, UseEma100Filter = true, TrailingStopEnabled = false };
    private static bool Same(TradingSettings left, TradingSettings right) => left.RiskReward == right.RiskReward && left.MinEmaGapPercent == right.MinEmaGapPercent && left.MaxStopDistancePercent == right.MaxStopDistancePercent && left.WaitForConfirmationCandle == right.WaitForConfirmationCandle && left.UseEma100Filter == right.UseEma100Filter && left.TrailingStopEnabled == right.TrailingStopEnabled;
}
