using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Services;

public static class GridBacktestBudget
{
    public static TimeSpan Calculate(string interval, DateTimeOffset start, DateTimeOffset end, BacktestRequestTimeoutOptions options)
    {
        if (BacktestRequestTimeoutOptions.Validate(options).Count > 0 || start >= end) throw new ArgumentException("Invalid Grid workload budget.");
        var span = Mt5BridgeHistoricalMarketDataProvider.TimeframeSpan(interval);
        var candles = decimal.Ceiling((decimal)(end - start).Ticks / span.Ticks);
        var pages = Mt5BridgeHistoricalMarketDataProvider.EstimateRangePageCount(interval, GridBacktestService.WarmupStart(interval, start), end);
        // Worst case per reporting bar: 30 preflight calls + 5 fill-margin + 5 exit
        // profit. Rejected cycles may retry next candle. No EMA candidate estimate.
        var ticks = options.BaseProcessingBudget.Ticks + (decimal)pages * options.PerEstimatedHistoryPageBudget.Ticks
            + options.NativeExecutionBaseBudget.Ticks + candles * 40m * Mt5TradeCalculationRetryPolicy.Default.MaxAttempts * options.PerNativeEconomicsTransportAttemptBudget.Ticks;
        return TimeSpan.FromTicks((long)Math.Clamp(ticks, options.MinimumRequestTimeout.Ticks, options.MaximumRequestTimeout.Ticks));
    }
}
