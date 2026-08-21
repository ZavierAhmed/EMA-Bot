using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategyReentryRulesTests
{
    [Fact]
    public void Continuation_RequiresSameTrendCandleStructureAndConfiguredFilters()
    {
        var settings = new TradingSettings { MinEmaGapPercent = .2m, UseEma100Filter = true };
        var accepted = new IndicatorSnapshot(DateTimeOffset.UnixEpoch, 106m, 105m, 104m, 100m, .3m, GapState.Expanding, TrendDirection.Up, 103m);

        Assert.True(DemoStrategyReentryRules.IsContinuation(accepted, SignalDirection.Long, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Open = 107m }, SignalDirection.Long, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { GapPercent = .1m }, SignalDirection.Long, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Ema100 = 105m }, SignalDirection.Long, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Ema9 = 103m, Ema15 = 104m }, SignalDirection.Long, settings));
    }

    [Fact]
    public void ShortContinuation_RequiresBearishFastEmaStructureAndConfiguredFilters()
    {
        var settings = new TradingSettings { MinEmaGapPercent = .2m, UseEma100Filter = true };
        var accepted = new IndicatorSnapshot(DateTimeOffset.UnixEpoch, 94m, 95m, 96m, 100m, .3m, GapState.Expanding, TrendDirection.Down, 97m);

        Assert.True(DemoStrategyReentryRules.IsContinuation(accepted, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Ema9 = 96m }, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Close = 95m }, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Close = 96m }, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Open = 93m }, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Ema100 = null }, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { Ema100 = 95m }, SignalDirection.Short, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(accepted with { GapPercent = .1m }, SignalDirection.Short, settings));
        Assert.True(DemoStrategyReentryRules.IsContinuation(accepted with { GapPercent = null }, SignalDirection.Short, new TradingSettings { MinEmaGapPercent = 0m, UseEma100Filter = true }));
    }

    [Fact]
    public void AgeBars_CountsOnlyClosedBarsAfterRegimeThroughContinuation()
    {
        var start = DateTimeOffset.UnixEpoch;
        var candles = Enumerable.Range(0, 5).Select(index => new Candle(start.AddMinutes(index * 3), start.AddMinutes((index + 1) * 3).AddMilliseconds(-1), 1m, 1m, 1m, 1m, 1m, true)).ToArray();

        Assert.Equal(2, DemoStrategyReentryRules.AgeBars(candles, candles[1].CloseTimeUtc, candles[3].CloseTimeUtc));
        Assert.Equal(0, DemoStrategyReentryRules.AgeBars(candles, candles[3].CloseTimeUtc, candles[2].CloseTimeUtc));
    }
}
