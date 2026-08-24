using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;

namespace EmaBot.Api.Tests;

public sealed class BacktestRequestBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SmallThreeMinuteRange_UsesMinimumTimeout()
    {
        var budget = Calculate(Start, Start.AddHours(1));

        Assert.Equal(1, budget.EstimatedExecutionHistoryPages);
        Assert.Equal("15m", budget.PotentialHigherTimeframe);
        Assert.Equal(1, budget.EstimatedHigherTimeframeHistoryPages);
        Assert.Equal(TimeSpan.FromMinutes(2), budget.ChosenRequestTimeout);
    }

    [Fact]
    public void ThirtyDayThreeMinuteRange_UsesProviderExecutionPagingSemantics()
    {
        var budget = Calculate(Start, Start.AddDays(30));

        Assert.Equal(15, budget.EstimatedExecutionHistoryPages);
        Assert.Equal(Mt5BridgeHistoricalMarketDataProvider.HistoryPageBars, 1_000);
    }

    [Fact]
    public void ThirtyDayThreeMinuteRange_IncludesPotentialHtfWarmupAndUsesExpectedFormula()
    {
        var budget = Calculate(Start, Start.AddDays(30));

        Assert.Equal("15m", budget.PotentialHigherTimeframe);
        Assert.Equal(TimeSpan.FromHours(50), HigherTimeframeRegime.WarmupDuration("15m"));
        Assert.Equal(4, budget.EstimatedHigherTimeframeHistoryPages);
        Assert.Equal(19, budget.EstimatedTotalHistoryPages);
        Assert.Equal(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(48), budget.CalculatedRequestTimeout);
        Assert.Equal(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(48), budget.ChosenRequestTimeout);
    }

    [Fact]
    public void UnsupportedHigherTimeframe_UsesNoPotentialHtfPages()
    {
        var budget = BacktestRequestBudgetCalculator.Calculate("6h", Start, Start.AddDays(2), Defaults());

        Assert.Null(budget.PotentialHigherTimeframe);
        Assert.Equal(0, budget.EstimatedHigherTimeframeHistoryPages);
    }

    [Fact]
    public void VeryLargeRange_IsCappedAtConfiguredMaximum()
    {
        var budget = Calculate(Start, Start.AddDays(100));

        Assert.Equal(58, budget.EstimatedTotalHistoryPages);
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(36), budget.CalculatedRequestTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), budget.ChosenRequestTimeout);
    }

    [Fact]
    public void MinimumClamp_AppliesWhenRawBudgetIsSmaller()
    {
        var options = Defaults();
        options.BaseProcessingBudget = TimeSpan.Zero;
        options.PerEstimatedHistoryPageBudget = TimeSpan.FromSeconds(1);
        var budget = BacktestRequestBudgetCalculator.Calculate("3m", Start, Start.AddHours(1), options);

        Assert.Equal(TimeSpan.FromSeconds(2), budget.CalculatedRequestTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), budget.ChosenRequestTimeout);
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
    public void PageCountBoundary_MatchesProviderOneThousandBarWindows()
    {
        var exactWindow = Mt5BridgeHistoricalMarketDataProvider.TimeframeSpan("3m") * Mt5BridgeHistoricalMarketDataProvider.HistoryPageBars;

        Assert.Equal(1, Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount("3m", Start, Start + exactWindow));
        Assert.Equal(2, Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount("3m", Start, Start + exactWindow + TimeSpan.FromTicks(1)));
    }

    private static BacktestRequestBudget Calculate(DateTimeOffset start, DateTimeOffset end) => BacktestRequestBudgetCalculator.Calculate("3m", start, end, Defaults());
    private static BacktestRequestTimeoutOptions Defaults() => new();
}
