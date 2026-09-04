using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;

namespace EmaBot.Api.Tests;

public sealed class BacktestRequestBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SmallThreeMinuteFixedLotsRange_UsesAReasonableMinimumDeadline()
    {
        var budget = Calculate(Start, Start.AddHours(1), PaperPositionSizingMode.FixedLots);

        Assert.Equal(1, budget.EstimatedExecutionHistoryPages);
        Assert.Equal("15m", budget.PotentialHigherTimeframe);
        Assert.Equal(1, budget.EstimatedHigherTimeframeHistoryPages);
        Assert.Equal(20, budget.EstimatedExecutionCandleCount);
        Assert.Equal(1, budget.EstimatedNativeEconomicsCandidates);
        Assert.Equal(4, budget.EstimatedNativeEconomicsLogicalOperations);
        Assert.Equal(TimeSpan.FromMinutes(2), budget.ChosenRequestTimeout);
    }

    [Fact]
    public void SmallThreeMinuteMarginPercentRange_RemainsBounded()
    {
        var budget = Calculate(Start, Start.AddHours(1), PaperPositionSizingMode.MarginPercent);

        Assert.Equal(8, budget.EstimatedNativeEconomicsLogicalOperations);
        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3), budget.ChosenRequestTimeout);
        Assert.True(budget.ChosenRequestTimeout < TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void SmallThreeMinuteRiskPercentRange_GetsAdditionalButNotExcessiveEconomicsBudget()
    {
        var fixedLots = Calculate(Start, Start.AddHours(1), PaperPositionSizingMode.FixedLots);
        var risk = Calculate(Start, Start.AddHours(1), PaperPositionSizingMode.RiskPercent);

        Assert.Equal(13, risk.EstimatedNativeEconomicsLogicalOperations);
        Assert.True(risk.NativeExecutionBudget > fixedLots.NativeExecutionBudget);
        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(18), risk.ChosenRequestTimeout);
        Assert.True(risk.ChosenRequestTimeout < TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ThirtyDayThreeMinuteRange_UsesProviderExecutionPagingSemantics()
    {
        var budget = Calculate(Start, Start.AddDays(30), PaperPositionSizingMode.FixedLots);

        Assert.Equal(15, budget.EstimatedExecutionHistoryPages);
        Assert.Equal(Mt5BridgeHistoricalMarketDataProvider.HistoryPageBars, 1_000);
    }

    [Fact]
    public void ThirtyDayThreeMinuteRiskPercentRange_IncludesHistoryAndNativeEconomicsWorkload()
    {
        var budget = Calculate(Start, Start.AddDays(30), PaperPositionSizingMode.RiskPercent);

        Assert.Equal("15m", budget.PotentialHigherTimeframe);
        Assert.Equal(TimeSpan.FromHours(50), HigherTimeframeRegime.WarmupDuration("15m"));
        Assert.Equal(4, budget.EstimatedHigherTimeframeHistoryPages);
        Assert.Equal(19, budget.EstimatedTotalHistoryPages);
        Assert.Equal(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(48), budget.HistoricalDataBudget);
        Assert.Equal(14_400, budget.EstimatedExecutionCandleCount);
        Assert.Equal(30, budget.EstimatedNativeEconomicsCandidates);
        Assert.Equal(390, budget.EstimatedNativeEconomicsLogicalOperations);
        Assert.Equal(TimeSpan.FromMinutes(19) + TimeSpan.FromSeconds(45), budget.NativeExecutionBudget);
        Assert.Equal(TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(33), budget.CalculatedRequestTimeout);
        Assert.Equal(budget.CalculatedRequestTimeout, budget.ChosenRequestTimeout);
    }

    [Fact]
    public void JulyEquivalentRiskPercentWorkload_IsNotLimitedToTheFormer288SecondDeadline()
    {
        var july = Calculate(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), PaperPositionSizingMode.RiskPercent);

        Assert.Equal(14_880, july.EstimatedExecutionCandleCount);
        Assert.Equal(31, july.EstimatedNativeEconomicsCandidates);
        Assert.Equal(403, july.EstimatedNativeEconomicsLogicalOperations);
        Assert.Equal(TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(12), july.CalculatedRequestTimeout);
        Assert.True(july.ChosenRequestTimeout > TimeSpan.FromSeconds(288));
    }

    [Fact]
    public void RiskPercentBudget_ExceedsEquivalentFixedLotsBudget()
    {
        var fixedLots = Calculate(Start, Start.AddDays(30), PaperPositionSizingMode.FixedLots);
        var risk = Calculate(Start, Start.AddDays(30), PaperPositionSizingMode.RiskPercent);

        Assert.True(risk.EstimatedNativeEconomicsLogicalOperations > fixedLots.EstimatedNativeEconomicsLogicalOperations);
        Assert.True(risk.ChosenRequestTimeout > fixedLots.ChosenRequestTimeout);
    }

    [Fact]
    public void NonZeroCommission_AddsOnlyTheBoundedBreakEvenAllowance()
    {
        var zeroCommission = Calculate(Start, Start.AddDays(30), PaperPositionSizingMode.RiskPercent);
        var commission = Calculate(Start, Start.AddDays(30), PaperPositionSizingMode.RiskPercent, commissionPerLotPerSide: 2m);

        Assert.Equal(zeroCommission.EstimatedNativeEconomicsCandidates * 4, commission.EstimatedNativeEconomicsLogicalOperations - zeroCommission.EstimatedNativeEconomicsLogicalOperations);
    }

    [Fact]
    public void VeryLargeRange_IsCappedAtConfiguredFiniteMaximum()
    {
        var budget = Calculate(Start, Start.AddDays(100), PaperPositionSizingMode.RiskPercent);

        Assert.Equal(58, budget.EstimatedTotalHistoryPages);
        Assert.True(budget.CalculatedRequestTimeout > BacktestRequestTimeoutOptions.MaximumSupportedRequestTimeout);
        Assert.Equal(TimeSpan.FromMinutes(30), budget.ChosenRequestTimeout);
    }

    [Fact]
    public void MinimumClamp_AppliesWhenRawBudgetIsSmaller()
    {
        var options = Defaults();
        options.BaseProcessingBudget = TimeSpan.Zero;
        options.PerEstimatedHistoryPageBudget = TimeSpan.FromMilliseconds(1);
        options.NativeExecutionBaseBudget = TimeSpan.Zero;
        options.PerNativeEconomicsTransportAttemptBudget = TimeSpan.FromMilliseconds(1);
        var budget = BacktestRequestBudgetCalculator.Calculate("3m", Start, Start.AddHours(1), PaperPositionSizingMode.FixedLots, 0m, options);

        Assert.Equal(TimeSpan.FromMilliseconds(14), budget.CalculatedRequestTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), budget.ChosenRequestTimeout);
    }

    [Fact]
    public void UnsupportedHigherTimeframe_UsesNoPotentialHtfPages()
    {
        var budget = BacktestRequestBudgetCalculator.Calculate("6h", Start, Start.AddDays(2), PaperPositionSizingMode.FixedLots, 0m, Defaults());

        Assert.Null(budget.PotentialHigherTimeframe);
        Assert.Equal(0, budget.EstimatedHigherTimeframeHistoryPages);
    }

    [Theory]
    [InlineData(0, 0, 1, 10)]
    [InlineData(1, -1, 1, 10)]
    [InlineData(1, 0, 0, 10)]
    [InlineData(10, 0, 1, 1)]
    public void InvalidTimeoutConfiguration_IsRejected(int minimumSeconds, int baseSeconds, int perPageSeconds, int maximumSeconds)
    {
        var options = new BacktestRequestTimeoutOptions
        {
            MinimumRequestTimeout = TimeSpan.FromSeconds(minimumSeconds),
            BaseProcessingBudget = TimeSpan.FromSeconds(baseSeconds),
            PerEstimatedHistoryPageBudget = TimeSpan.FromSeconds(perPageSeconds),
            MaximumRequestTimeout = TimeSpan.FromSeconds(maximumSeconds)
        };

        Assert.NotEmpty(BacktestRequestTimeoutOptions.Validate(options));
        Assert.Throws<ArgumentException>(() => BacktestRequestBudgetCalculator.Calculate("3m", Start, Start.AddHours(1), options));
    }

    [Fact]
    public void InvalidNativeWorkloadConfigurationAndOverflowProneDurations_AreRejected()
    {
        var options = Defaults();
        options.NativeEconomicsCandidateBarWindow = 0;
        options.RiskPercentEconomicsOperationsPerCandidate = 0;
        options.PerNativeEconomicsTransportAttemptBudget = TimeSpan.MaxValue;

        Assert.NotEmpty(BacktestRequestTimeoutOptions.Validate(options));
        Assert.Throws<ArgumentException>(() => BacktestRequestBudgetCalculator.Calculate("3m", Start, Start.AddHours(1), PaperPositionSizingMode.RiskPercent, 0m, options));
    }

    [Fact]
    public void PageCountBoundary_MatchesProviderOneThousandBarWindows()
    {
        var exactWindow = Mt5BridgeHistoricalMarketDataProvider.TimeframeSpan("3m") * Mt5BridgeHistoricalMarketDataProvider.HistoryPageBars;

        Assert.Equal(1, Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount("3m", Start, Start + exactWindow));
        Assert.Equal(2, Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount("3m", Start, Start + exactWindow + TimeSpan.FromTicks(1)));
    }

    private static BacktestRequestBudget Calculate(DateTimeOffset start, DateTimeOffset end, PaperPositionSizingMode mode, decimal commissionPerLotPerSide = 0m)
        => BacktestRequestBudgetCalculator.Calculate("3m", start, end, mode, commissionPerLotPerSide, Defaults());
    private static BacktestRequestTimeoutOptions Defaults() => new();
}
