using EmaBot.Api.Binance;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

public sealed record HigherTimeframeDiagnostic(string? Timeframe, DateTimeOffset? CandleCloseTimeUtc, decimal? AgeMinutes, decimal? Close, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? EmaGapPercent, decimal? Ema9Slope5Percent, decimal? Ema15Slope5Percent, decimal? Ema100Slope5Percent, decimal? Ema100Slope20Percent, decimal? DistanceFromEma100Percent, decimal? PriceReturn20Percent, decimal? Atr14Percent, decimal? TrendEfficiency20, string? FastTrend, bool? FastTrendAligned, bool? PriceVsEma100Aligned, bool? Ema100Slope5Aligned, bool? Ema100Slope20Aligned, bool? FullTrendAligned);

public sealed record StrategyMarketContext(IReadOnlyList<Candle> ExecutionCandles, string? HigherTimeframe, IReadOnlyList<Candle>? HigherTimeframeCandles);

public static class HigherTimeframeRegime
{
    public const decimal MinimumAtrPercent = .60m;

    public static string? ForExecutionTimeframe(string timeframe) => timeframe switch { "3m" => "15m", "5m" => "30m", "15m" => "1h", "30m" => "2h", "1h" => "4h", _ => null };

    public static TimeSpan WarmupDuration(string interval) => interval switch { "3m"=>TimeSpan.FromMinutes(600),"5m"=>TimeSpan.FromMinutes(1000),"15m"=>TimeSpan.FromMinutes(3000),"30m"=>TimeSpan.FromMinutes(6000),"1h"=>TimeSpan.FromHours(200),"2h"=>TimeSpan.FromHours(400),"4h"=>TimeSpan.FromHours(800),"6h"=>TimeSpan.FromHours(1200),"8h"=>TimeSpan.FromHours(1600),"12h"=>TimeSpan.FromHours(2400),"1d"=>TimeSpan.FromDays(200),"3d"=>TimeSpan.FromDays(600),"1w"=>TimeSpan.FromDays(1400),_=>TimeSpan.FromDays(6200) };

    public static HigherTimeframeDiagnostic Calculate(DateTimeOffset signalTimeUtc, SignalDirection direction, string? timeframe, IReadOnlyList<Candle>? input)
    {
        if (timeframe is null || input is null) return Empty(null);
        var candles = input.Where(candle => candle.IsClosed).OrderBy(candle => candle.CloseTimeUtc).ToArray();
        var index = Array.FindLastIndex(candles, candle => candle.CloseTimeUtc <= signalTimeUtc);
        if (index < 0) return Empty(timeframe);
        var closes = candles.Select(candle => candle.Close).ToArray(); var ema9 = EmaCalculator.Calculate(closes, 9); var ema15 = EmaCalculator.Calculate(closes, 15); var ema100 = EmaCalculator.Calculate(closes, 100);
        var fast = ema9[index].HasValue && ema15[index].HasValue ? ema9[index] > ema15[index] ? "Bullish" : ema9[index] < ema15[index] ? "Bearish" : "Flat" : null;
        var slope5 = Slope(ema100, index, 5); var slope20 = Slope(ema100, index, 20);
        bool? fastAligned = ema9[index].HasValue && ema15[index].HasValue ? direction == SignalDirection.Long ? ema9[index] > ema15[index] : ema9[index] < ema15[index] : null;
        var priceAligned = AlignPrice(candles[index].Close, ema100[index], direction);
        bool? full = fastAligned.HasValue && priceAligned.HasValue && slope20.HasValue ? fastAligned.Value && priceAligned.Value && Align(slope20, direction) == true : null;
        return new(timeframe, candles[index].CloseTimeUtc, (decimal)(signalTimeUtc - candles[index].CloseTimeUtc).TotalMinutes, candles[index].Close, ema9[index], ema15[index], ema100[index], ema9[index].HasValue && ema15[index].HasValue && candles[index].Close != 0 ? decimal.Abs(ema9[index]!.Value - ema15[index]!.Value) / candles[index].Close * 100m : null, Slope(ema9, index, 5), Slope(ema15, index, 5), slope5, slope20, Percent(candles[index].Close, ema100[index]), Return(closes, index, 20), Atr(candles, index), Efficiency(closes, index), fast, fastAligned, priceAligned, Align(slope5, direction), Align(slope20, direction), full);
    }

    public static bool PassesH2(HigherTimeframeDiagnostic context, SignalDirection direction) => context.Ema100Slope20Percent.HasValue && context.Atr14Percent.HasValue && context.Atr14Percent.Value >= MinimumAtrPercent && (direction == SignalDirection.Long ? context.Ema100Slope20Percent.Value < 0 : context.Ema100Slope20Percent.Value > 0);

    private static HigherTimeframeDiagnostic Empty(string? timeframe) => new(timeframe, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    private static decimal? Slope(IReadOnlyList<decimal?> values, int index, int bars) => index < bars || !values.ElementAtOrDefault(index).HasValue || !values[index-bars].HasValue || values[index-bars] == 0 ? null : (values[index]!.Value - values[index-bars]!.Value) / values[index-bars]!.Value * 100m;
    private static decimal? Percent(decimal value, decimal? baseValue) => !baseValue.HasValue || baseValue == 0 ? null : (value - baseValue.Value) / baseValue.Value * 100m;
    private static decimal? Return(decimal[] values, int index, int bars) => index < bars || values[index-bars] == 0 ? null : (values[index] - values[index-bars]) / values[index-bars] * 100m;
    private static decimal? Atr(Candle[] candles, int index) { if (index < 14 || candles[index].Close == 0) return null; decimal total = 0; for (var i = index - 13; i <= index; i++) { var previous = candles[i - 1].Close; total += Math.Max(candles[i].High - candles[i].Low, Math.Max(decimal.Abs(candles[i].High - previous), decimal.Abs(candles[i].Low - previous))); } return total / 14m / candles[index].Close * 100m; }
    private static decimal? Efficiency(decimal[] values, int index) { if (index < 20) return null; decimal denominator = 0; for (var i = index - 19; i <= index; i++) denominator += decimal.Abs(values[i] - values[i - 1]); return denominator == 0 ? null : decimal.Abs(values[index] - values[index - 20]) / denominator; }
    private static bool? Align(decimal? slope, SignalDirection direction) => !slope.HasValue ? null : direction == SignalDirection.Long ? slope > 0 : slope < 0;
    private static bool? AlignPrice(decimal close, decimal? ema, SignalDirection direction) => !ema.HasValue ? null : direction == SignalDirection.Long ? close > ema : close < ema;
}
