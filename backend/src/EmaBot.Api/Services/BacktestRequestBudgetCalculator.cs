using EmaBot.Api.Mt5Bridge;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed record BacktestRequestBudget(
    int EstimatedExecutionHistoryPages,
    string? PotentialHigherTimeframe,
    int EstimatedHigherTimeframeHistoryPages,
    int EstimatedTotalHistoryPages,
    TimeSpan CalculatedRequestTimeout,
    TimeSpan ChosenRequestTimeout);

public static class BacktestRequestBudgetCalculator
{
    public static BacktestRequestBudget Calculate(string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, BacktestRequestTimeoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = BacktestRequestTimeoutOptions.Validate(options);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));

        var executionPages = Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount(interval, startUtc, endUtc);
        var higherTimeframe = HigherTimeframeRegime.ForExecutionTimeframe(interval);
        var higherTimeframePages = 0;
        if (higherTimeframe is not null)
        {
            var higherTimeframeStart = startUtc - HigherTimeframeRegime.WarmupDuration(higherTimeframe);
            higherTimeframePages = Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount(higherTimeframe, higherTimeframeStart, endUtc);
        }

        var totalPages = checked(executionPages + higherTimeframePages);
        var calculated = CalculateTimeout(options, totalPages);
        var chosen = calculated < options.MinimumRequestTimeout
            ? options.MinimumRequestTimeout
            : calculated > options.MaximumRequestTimeout
                ? options.MaximumRequestTimeout
                : calculated;
        return new BacktestRequestBudget(executionPages, higherTimeframe, higherTimeframePages, totalPages, calculated, chosen);
    }

    private static TimeSpan CalculateTimeout(BacktestRequestTimeoutOptions options, int totalPages)
    {
        try
        {
            return TimeSpan.FromTicks(checked(options.BaseProcessingBudget.Ticks + checked((long)totalPages * options.PerEstimatedHistoryPageBudget.Ticks)));
        }
        catch (OverflowException)
        {
            return TimeSpan.MaxValue;
        }
    }
}

public sealed class BacktestRequestTimeoutOptions
{
    public const string SectionName = "Backtest";
    public TimeSpan MinimumRequestTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan BaseProcessingBudget { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan PerEstimatedHistoryPageBudget { get; set; } = TimeSpan.FromSeconds(12);
    public TimeSpan MaximumRequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<string> Validate(BacktestRequestTimeoutOptions options)
    {
        var errors = new List<string>();
        if (options.MinimumRequestTimeout <= TimeSpan.Zero) errors.Add("Backtest:MinimumRequestTimeout must be greater than zero.");
        if (options.BaseProcessingBudget < TimeSpan.Zero) errors.Add("Backtest:BaseProcessingBudget must be zero or greater.");
        if (options.PerEstimatedHistoryPageBudget <= TimeSpan.Zero) errors.Add("Backtest:PerEstimatedHistoryPageBudget must be greater than zero.");
        if (options.MaximumRequestTimeout < options.MinimumRequestTimeout) errors.Add("Backtest:MaximumRequestTimeout must be greater than or equal to MinimumRequestTimeout.");
        return errors;
    }
}

public sealed class BacktestRequestTimeoutOptionsValidator : IValidateOptions<BacktestRequestTimeoutOptions>
{
    public ValidateOptionsResult Validate(string? name, BacktestRequestTimeoutOptions options)
    {
        var errors = BacktestRequestTimeoutOptions.Validate(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
