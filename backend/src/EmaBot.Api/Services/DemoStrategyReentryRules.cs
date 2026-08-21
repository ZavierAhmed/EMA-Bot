using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

// B3B continuation math is deliberately pure.  It neither schedules nor submits.
public static class DemoStrategyReentryRules
{
    public static bool IsContinuation(IndicatorSnapshot snapshot, SignalDirection direction, TradingSettings settings)
    {
        var directional = direction == SignalDirection.Long
            ? snapshot.Ema9 > snapshot.Ema15 && snapshot.Close > snapshot.Ema9 && snapshot.Close > snapshot.Ema15 && snapshot.Close > snapshot.Open
            : direction == SignalDirection.Short && snapshot.Ema9 < snapshot.Ema15 && snapshot.Close < snapshot.Ema9 && snapshot.Close < snapshot.Ema15 && snapshot.Close < snapshot.Open;
        if (!directional) return false;
        if (settings.UseEma100Filter && (!snapshot.Ema100.HasValue || (direction == SignalDirection.Long
                ? snapshot.Ema9 <= snapshot.Ema100 || snapshot.Ema15 <= snapshot.Ema100
                : snapshot.Ema9 >= snapshot.Ema100 || snapshot.Ema15 >= snapshot.Ema100))) return false;
        return settings.MinEmaGapPercent == 0m || snapshot.GapPercent >= settings.MinEmaGapPercent;
    }

    public static int AgeBars(IReadOnlyList<Candle> candles, DateTimeOffset regimeTime, DateTimeOffset continuationTime) =>
        candles.Count(candle => candle.CloseTimeUtc > regimeTime && candle.CloseTimeUtc <= continuationTime);
}
