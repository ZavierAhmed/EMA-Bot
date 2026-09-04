using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Models;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed record BacktestRequestBudget(
    int EstimatedExecutionHistoryPages,
    string? PotentialHigherTimeframe,
    int EstimatedHigherTimeframeHistoryPages,
    int EstimatedTotalHistoryPages,
    TimeSpan HistoricalDataBudget,
    PaperPositionSizingMode NativePositionSizingMode,
    long EstimatedExecutionCandleCount,
    long EstimatedNativeEconomicsCandidates,
    long EstimatedNativeEconomicsLogicalOperations,
    TimeSpan NativeExecutionBudget,
    TimeSpan CalculatedRequestTimeout,
    TimeSpan ChosenRequestTimeout);

public static class BacktestRequestBudgetCalculator
{
    public static BacktestRequestBudget Calculate(string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, BacktestRequestTimeoutOptions options)
        => Calculate(interval, startUtc, endUtc, PaperPositionSizingMode.FixedLots, 0m, options);

    public static BacktestRequestBudget Calculate(string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, PaperPositionSizingMode nativePositionSizingMode, decimal commissionPerLotPerSide, BacktestRequestTimeoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = BacktestRequestTimeoutOptions.Validate(options);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        if (!Enum.IsDefined(nativePositionSizingMode)) throw new ArgumentOutOfRangeException(nameof(nativePositionSizingMode));
        if (commissionPerLotPerSide < 0m) throw new ArgumentOutOfRangeException(nameof(commissionPerLotPerSide));

        var executionPages = Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount(interval, startUtc, endUtc);
        var higherTimeframe = HigherTimeframeRegime.ForExecutionTimeframe(interval);
        var higherTimeframePages = 0;
        if (higherTimeframe is not null)
        {
            var higherTimeframeStart = startUtc - HigherTimeframeRegime.WarmupDuration(higherTimeframe);
            higherTimeframePages = Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount(higherTimeframe, higherTimeframeStart, endUtc);
        }

        var totalPages = checked(executionPages + higherTimeframePages);
        var historicalBudget = CalculateHistoricalBudget(options, totalPages);
        var executionCandles = EstimateExecutionCandles(interval, startUtc, endUtc);
        var candidates = DivideRoundUp(executionCandles, options.NativeEconomicsCandidateBarWindow);
        var operationsPerCandidate = OperationsPerCandidate(options, nativePositionSizingMode, commissionPerLotPerSide > 0m);
        var logicalOperations = checked(candidates * operationsPerCandidate);
        var nativeBudget = CalculateNativeExecutionBudget(options, logicalOperations);
        var calculated = AddBounded(historicalBudget, nativeBudget);
        var chosen = calculated < options.MinimumRequestTimeout
            ? options.MinimumRequestTimeout
            : calculated > options.MaximumRequestTimeout
                ? options.MaximumRequestTimeout
                : calculated;
        return new BacktestRequestBudget(executionPages, higherTimeframe, higherTimeframePages, totalPages, historicalBudget, nativePositionSizingMode, executionCandles, candidates, logicalOperations, nativeBudget, calculated, chosen);
    }

    private static TimeSpan CalculateHistoricalBudget(BacktestRequestTimeoutOptions options, int totalPages)
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

    private static TimeSpan CalculateNativeExecutionBudget(BacktestRequestTimeoutOptions options, long logicalOperations)
    {
        try
        {
            var transportAttempts = checked(logicalOperations * Mt5TradeCalculationRetryPolicy.Default.MaxAttempts);
            return TimeSpan.FromTicks(checked(options.NativeExecutionBaseBudget.Ticks + checked(transportAttempts * options.PerNativeEconomicsTransportAttemptBudget.Ticks)));
        }
        catch (OverflowException)
        {
            return TimeSpan.MaxValue;
        }
    }

    private static long EstimateExecutionCandles(string interval, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var timeframe = Mt5BridgeHistoricalMarketDataProvider.TimeframeSpan(interval);
        var duration = endUtc - startUtc;
        if (duration <= TimeSpan.Zero) throw new ArgumentException("Backtest end must be after start.");
        return DivideRoundUp(duration.Ticks, timeframe.Ticks);
    }

    private static long OperationsPerCandidate(BacktestRequestTimeoutOptions options, PaperPositionSizingMode mode, bool hasCommission)
    {
        var sizingOperations = mode switch
        {
            PaperPositionSizingMode.FixedLots => options.FixedLotsEconomicsOperationsPerCandidate,
            PaperPositionSizingMode.MarginPercent => options.MarginPercentEconomicsOperationsPerCandidate,
            PaperPositionSizingMode.RiskPercent => options.RiskPercentEconomicsOperationsPerCandidate,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return checked((long)sizingOperations + (hasCommission ? options.NonZeroCommissionBreakEvenOperationsPerCandidate : 0));
    }

    private static long DivideRoundUp(long value, long divisor) => checked((value + divisor - 1) / divisor);
    private static TimeSpan AddBounded(TimeSpan first, TimeSpan second)
    {
        try { return TimeSpan.FromTicks(checked(first.Ticks + second.Ticks)); }
        catch (OverflowException) { return TimeSpan.MaxValue; }
    }
}

public sealed class BacktestRequestTimeoutOptions
{
    public const string SectionName = "Backtest";
    public static readonly TimeSpan MaximumSupportedRequestTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaximumNativeEconomicsTransportAttemptBudget = TimeSpan.FromSeconds(10);
    public TimeSpan MinimumRequestTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan BaseProcessingBudget { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan PerEstimatedHistoryPageBudget { get; set; } = TimeSpan.FromSeconds(12);
    public TimeSpan NativeExecutionBaseBudget { get; set; } = TimeSpan.FromSeconds(15);
    public int NativeEconomicsCandidateBarWindow { get; set; } = 480;
    public int FixedLotsEconomicsOperationsPerCandidate { get; set; } = 4;
    public int MarginPercentEconomicsOperationsPerCandidate { get; set; } = 8;
    public int RiskPercentEconomicsOperationsPerCandidate { get; set; } = 13;
    public int NonZeroCommissionBreakEvenOperationsPerCandidate { get; set; } = 4;
    public TimeSpan PerNativeEconomicsTransportAttemptBudget { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRequestTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<string> Validate(BacktestRequestTimeoutOptions options)
    {
        var errors = new List<string>();
        if (options.MinimumRequestTimeout <= TimeSpan.Zero) errors.Add("Backtest:MinimumRequestTimeout must be greater than zero.");
        if (options.BaseProcessingBudget < TimeSpan.Zero) errors.Add("Backtest:BaseProcessingBudget must be zero or greater.");
        if (options.PerEstimatedHistoryPageBudget <= TimeSpan.Zero) errors.Add("Backtest:PerEstimatedHistoryPageBudget must be greater than zero.");
        if (options.NativeExecutionBaseBudget < TimeSpan.Zero) errors.Add("Backtest:NativeExecutionBaseBudget must be zero or greater.");
        if (options.NativeEconomicsCandidateBarWindow <= 0) errors.Add("Backtest:NativeEconomicsCandidateBarWindow must be greater than zero.");
        if (options.FixedLotsEconomicsOperationsPerCandidate <= 0) errors.Add("Backtest:FixedLotsEconomicsOperationsPerCandidate must be greater than zero.");
        if (options.MarginPercentEconomicsOperationsPerCandidate <= 0) errors.Add("Backtest:MarginPercentEconomicsOperationsPerCandidate must be greater than zero.");
        if (options.RiskPercentEconomicsOperationsPerCandidate <= 0) errors.Add("Backtest:RiskPercentEconomicsOperationsPerCandidate must be greater than zero.");
        if (options.NonZeroCommissionBreakEvenOperationsPerCandidate <= 0) errors.Add("Backtest:NonZeroCommissionBreakEvenOperationsPerCandidate must be greater than zero.");
        if (options.PerNativeEconomicsTransportAttemptBudget <= TimeSpan.Zero) errors.Add("Backtest:PerNativeEconomicsTransportAttemptBudget must be greater than zero.");
        if (options.MaximumRequestTimeout < options.MinimumRequestTimeout) errors.Add("Backtest:MaximumRequestTimeout must be greater than or equal to MinimumRequestTimeout.");
        if (options.MaximumRequestTimeout > MaximumSupportedRequestTimeout) errors.Add("Backtest:MaximumRequestTimeout exceeds the supported bounded native-economics deadline.");
        if (options.PerEstimatedHistoryPageBudget > MaximumSupportedRequestTimeout || options.NativeExecutionBaseBudget > MaximumSupportedRequestTimeout || options.PerNativeEconomicsTransportAttemptBudget > MaximumNativeEconomicsTransportAttemptBudget)
            errors.Add("Backtest workload durations exceed supported bounded values.");
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
