using EmaBot.Api.Controllers;
using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class TradeExplorerUnitTests
{
    [Fact]
    public void MonthlyChartArithmetic_UsesCalendarMonths()
    {
        var date = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero), BinanceIntervalMath.Shift(date, "1M", 1));
    }

    [Fact]
    public void BacktestTrailingEvent_UsesEarningCloseAndFollowingOpen()
    {
        var candles = Enumerable.Range(0, 8).Select(index => new Candle(DateTimeOffset.UnixEpoch.AddMinutes(index), DateTimeOffset.UnixEpoch.AddMinutes(index + 1).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true)).ToArray(); candles[0] = candles[0] with { Low = 90m }; candles[6] = candles[6] with { High = 110m, Low = 99m };
        var eventTime = candles[5].CloseTimeUtc; var snapshot = new IndicatorSnapshot(eventTime, 100m, 1m, 1m, null, null, GapState.Unchanged, TrendDirection.Neutral); var events = new[] { new StrategyEvent(eventTime, SignalDirection.Long, SignalStatus.BullishCrossover, snapshot), new StrategyEvent(eventTime, SignalDirection.Long, SignalStatus.LongSignal, snapshot) };
        var trade = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, new TradingSettings { RiskReward = 2m, FixedOrderSizeUsdt = 100m, TrailingStopEnabled = true }, events).Trades.Single(); var trailing = Assert.Single(trade.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved);
        Assert.Equal(candles[6].CloseTimeUtc, trailing.TimeUtc); Assert.Equal(candles[7].OpenTimeUtc, trailing.EffectiveTimeUtc);
    }
}
