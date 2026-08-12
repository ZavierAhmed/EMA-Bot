using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class HigherTimeframeRegimeTests
{
    [Fact]
    public void LongH2_PassesOnlyWithOppositeSlopeAndAtrAtBoundary()
    {
        var signal = DateTimeOffset.UnixEpoch.AddMinutes(140 * 15).AddMilliseconds(-1);
        var pass = HigherTimeframeRegime.Calculate(signal, SignalDirection.Long, "15m", HtfCandles(113.9m, -.1m, .3m));
        var wrongSlope = HigherTimeframeRegime.Calculate(signal, SignalDirection.Long, "15m", HtfCandles(86.1m, .1m, .3m));
        var lowAtr = HigherTimeframeRegime.Calculate(signal, SignalDirection.Long, "15m", HtfCandles(113.9m, -.1m, .299m));

        Assert.Equal(.60m, pass.Atr14Percent); Assert.True(HigherTimeframeRegime.PassesH2(pass, SignalDirection.Long)); Assert.False(HigherTimeframeRegime.PassesH2(wrongSlope, SignalDirection.Long)); Assert.False(HigherTimeframeRegime.PassesH2(lowAtr, SignalDirection.Long));
    }

    [Fact]
    public void ShortH2_PassesOnlyWithOppositeSlopeAndRequiredAtr()
    {
        var signal = DateTimeOffset.UnixEpoch.AddMinutes(140 * 15).AddMilliseconds(-1);
        var pass = HigherTimeframeRegime.Calculate(signal, SignalDirection.Short, "15m", HtfCandles(86.1m, .1m, .3m));
        var wrongSlope = HigherTimeframeRegime.Calculate(signal, SignalDirection.Short, "15m", HtfCandles(113.9m, -.1m, .3m));
        var lowAtr = HigherTimeframeRegime.Calculate(signal, SignalDirection.Short, "15m", HtfCandles(86.1m, .1m, .299m));

        Assert.True(HigherTimeframeRegime.PassesH2(pass, SignalDirection.Short)); Assert.False(HigherTimeframeRegime.PassesH2(wrongSlope, SignalDirection.Short)); Assert.False(HigherTimeframeRegime.PassesH2(lowAtr, SignalDirection.Short));
    }

    [Fact]
    public void H2_UsesOnlyLastClosedHigherTimeframeCandle_AndAcceptsExactBoundary()
    {
        var htf = HtfCandles(113.9m, -.1m, .3m); var exact = htf[120].CloseTimeUtc; var original = HigherTimeframeRegime.Calculate(exact, SignalDirection.Long, "15m", htf);
        var altered = htf.Select((candle, index) => index > 120 ? candle with { Open = 999m, High = 999m, Low = 1m, Close = 888m } : candle).ToArray();
        var unchanged = HigherTimeframeRegime.Calculate(exact, SignalDirection.Long, "15m", altered);
        var partial = HigherTimeframeRegime.Calculate(exact.AddMinutes(6), SignalDirection.Long, "15m", htf);

        Assert.Equal(exact, original.CandleCloseTimeUtc); Assert.Equal(original, unchanged); Assert.Equal(exact, partial.CandleCloseTimeUtc);
    }

    [Fact]
    public void H2_RejectsInsufficientOrUnsupportedContext()
    {
        var signal = DateTimeOffset.UnixEpoch.AddHours(12);
        var insufficient = HigherTimeframeRegime.Calculate(signal, SignalDirection.Long, "15m", HtfCandles(113.9m, -.1m, .3m)[..20]);
        var unsupported = HigherTimeframeRegime.Calculate(signal, SignalDirection.Long, HigherTimeframeRegime.ForExecutionTimeframe("6h"), null);

        Assert.False(HigherTimeframeRegime.PassesH2(insufficient, SignalDirection.Long)); Assert.False(HigherTimeframeRegime.PassesH2(unsupported, SignalDirection.Long));
    }

    [Fact]
    public void TrueReplay_ChangesEligibilityOnlyWhenH2IsEnabled()
    {
        var candles = LtfCandles(); var events = new[] { Event(candles, 700, SignalStatus.BullishCrossover), Event(candles, 700, SignalStatus.LongSignal) };
        var off = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, Settings(false), events);
        var on = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, Settings(true), events, new StrategyMarketContext(candles, "15m", HtfCandles(86.1m, .1m, .3m)));

        Assert.Single(off.Trades); Assert.Empty(on.Trades); Assert.Equal(0, off.Diagnostics.RejectedByHtfRegime); Assert.Equal(1, on.Diagnostics.RejectedByHtfRegime);
    }

    [Fact]
    public void TrueReplay_AllowsLongWhenH2Passes_AndOffStateIgnoresHtfData()
    {
        var candles = LtfCandles(); var events = new[] { Event(candles, 700, SignalStatus.BullishCrossover), Event(candles, 700, SignalStatus.LongSignal) };
        var pass = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, Settings(true), events, new StrategyMarketContext(candles, "15m", HtfCandles(113.9m, -.1m, .3m)));
        var baseline = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, Settings(false), events);
        var withIgnoredHtf = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, Settings(false), events, new StrategyMarketContext(candles, "15m", HtfCandles(86.1m, .1m, .3m)));

        Assert.Single(pass.Trades); Assert.Equal("15m", pass.Trades[0].HtfTimeframe); Assert.Equal(baseline.Diagnostics, withIgnoredHtf.Diagnostics); Assert.Equal(baseline.Trades.Count, withIgnoredHtf.Trades.Count); Assert.Equal(baseline.Trades[0].EntryTimeUtc, withIgnoredHtf.Trades[0].EntryTimeUtc); Assert.Equal(baseline.Trades[0].ExitTimeUtc, withIgnoredHtf.Trades[0].ExitTimeUtc); Assert.Equal(baseline.Trades[0].NetPnlUsdt, withIgnoredHtf.Trades[0].NetPnlUsdt);
    }

    private static TradingSettings Settings(bool htf) => new() { RiskReward = 1.1m, FixedOrderSizeUsdt = 100m, UseHtfRegimeFilter = htf, MinEmaGapPercent = 0m };
    private static StrategyEvent Event(IReadOnlyList<Candle> candles, int index, SignalStatus status) => new(candles[index].CloseTimeUtc, SignalDirection.Long, status, new(candles[index].CloseTimeUtc, candles[index].Close, 101m, 100m, 99m, 1m, GapState.Unchanged, TrendDirection.Up, candles[index].Open));
    private static Candle[] LtfCandles() => Enumerable.Range(0, 800).Select(index => { var open = DateTimeOffset.UnixEpoch.AddMinutes(index * 3); return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true); }).ToArray();
    private static Candle[] HtfCandles(decimal firstClose, decimal change, decimal range) => Enumerable.Range(0, 140).Select(index => { var open = DateTimeOffset.UnixEpoch.AddMinutes(index * 15); var close = firstClose + index * change; return new Candle(open, open.AddMinutes(15).AddMilliseconds(-1), close, close + range, close - range, close, 1m, true); }).ToArray();
}
