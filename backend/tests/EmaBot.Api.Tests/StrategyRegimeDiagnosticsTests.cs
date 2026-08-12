using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class StrategyRegimeDiagnosticsTests
{
    [Fact]
    public void SignalDiagnostics_DoNotUseEntryOrFutureCandles()
    {
        var candles = Candles(); var trade = Trade(candles[120]);
        var original = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", candles, [trade]));
        var altered = candles.Select((candle, index) => index > 120 ? candle with { High = 999999m, Low = 1m, Close = 888888m } : candle).ToArray();
        var unchanged = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", altered, [trade]));

        Assert.Equal(original.Ema9Slope5Percent, unchanged.Ema9Slope5Percent); Assert.Equal(original.Ema15Slope5Percent, unchanged.Ema15Slope5Percent); Assert.Equal(original.Ema100Slope5Percent, unchanged.Ema100Slope5Percent); Assert.Equal(original.Ema100Slope20Percent, unchanged.Ema100Slope20Percent); Assert.Equal(original.DistanceFromEma100Percent, unchanged.DistanceFromEma100Percent); Assert.Equal(original.PriceReturn20Percent, unchanged.PriceReturn20Percent); Assert.Equal(original.Atr14Percent, unchanged.Atr14Percent); Assert.Equal(original.TrendEfficiency20, unchanged.TrendEfficiency20);
    }

    private static BacktestTrade Trade(Candle signal) => new() { Direction = SignalDirection.Long, SignalTimeUtc = signal.CloseTimeUtc, SignalClose = signal.Close, EntryTimeUtc = signal.CloseTimeUtc.AddMilliseconds(1), ExitTimeUtc = signal.CloseTimeUtc.AddMinutes(3), ExitReason = BacktestExitReason.EndOfData };
    private static Candle[] Candles() => Enumerable.Range(0, 140).Select(index => { var time = DateTimeOffset.UnixEpoch.AddMinutes(index * 3); var close = 100m + index; return new Candle(time, time.AddMinutes(3).AddMilliseconds(-1), close - .5m, close + 1m, close - 1m, close, 1m, true); }).ToArray();
}
