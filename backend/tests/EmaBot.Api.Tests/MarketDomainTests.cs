using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.Extensions.DependencyInjection;

namespace EmaBot.Api.Tests;

public sealed class MarketDomainTests
{
    [Fact]
    public void StrategyTimeframes_HaveExactCanonicalParity()
    {
        var expected = new[] { "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" };

        Assert.Equal(expected, StrategyTimeframes.Supported);
        Assert.All(expected, timeframe => Assert.True(StrategyTimeframes.IsSupported(timeframe)));
        Assert.False(StrategyTimeframes.IsSupported("1m"));
        Assert.False(StrategyTimeframes.IsSupported("1M "));
        Assert.False(StrategyTimeframes.IsSupported("4H"));
        Assert.False(StrategyTimeframes.IsSupported(null));
    }

    [Fact]
    public async Task BinanceHistoricalAdapter_ThroughNeutralContract_PreservesBoundsClosedBarsAndOrdering()
    {
        var start = DateTimeOffset.UnixEpoch;
        var duplicate = new Candle(start.AddMinutes(3), start.AddMinutes(6).AddMilliseconds(-1), 2m, 3m, 1m, 2m, 4m, true);
        var replacement = duplicate with { Close = 2.5m };
        IHistoricalMarketDataProvider provider = new BinanceHistoricalMarketDataProvider(new HistoricalClient([
            new Candle(start.AddMinutes(-3), start.AddMilliseconds(-1), 1m, 1m, 1m, 1m, 1m, true),
            new Candle(start, start.AddMinutes(3).AddMilliseconds(-1), 1m, 2m, 0m, 1.5m, 3m, true),
            duplicate,
            replacement,
            new Candle(start.AddMinutes(6), start.AddMinutes(9).AddMilliseconds(-1), 3m, 4m, 2m, 3.5m, 5m, false)
        ]));

        var candles = await provider.GetRangeAsync("BTCUSDT", "3m", start, start.AddMinutes(6).AddMilliseconds(-1), CancellationToken.None);

        Assert.Equal(2, candles.Count);
        Assert.Equal(start, candles[0].OpenTimeUtc);
        Assert.Equal(duplicate.OpenTimeUtc, candles[1].OpenTimeUtc);
        Assert.Equal(2.5m, candles[1].Close);
    }

    [Fact]
    public async Task UnavailableLiveProvider_FailsClearly()
    {
        var provider = new UnavailableMarketBarStreamProvider();
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => provider.StreamAsync([], "3m", (_, _) => Task.CompletedTask, null, CancellationToken.None));
        Assert.Equal(UnavailableMarketBarStreamProvider.Message, error.Message);
    }

    [Fact]
    public void NeutralCandleSequence_PreservesCrossoverSignalAndNextOpenEntry()
    {
        var candles = Enumerable.Range(0, 110).Select(index =>
        {
            var open = DateTimeOffset.UnixEpoch.AddMinutes(index * 3);
            var close = index < 100 ? 100m : 100m + index - 99m;
            return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), index < 100 ? 100m : close - 1m, close + 1m, 99m, close, 1m, true);
        }).ToArray();
        var settings = new TradingSettings { WaitForConfirmationCandle = false, RiskReward = 2m, FixedOrderSizeUsdt = 100m };
        var strategy = new EmaSignalEngine();
        var evaluation = strategy.Evaluate(candles, settings);
        var calculation = new BacktestEngine(strategy).Run(candles, settings);

        Assert.Contains(evaluation.Events, item => item.Status == SignalStatus.BullishCrossover && item.Time == candles[100].CloseTimeUtc);
        Assert.Contains(evaluation.Events, item => item.Status == SignalStatus.LongSignal && item.Time == candles[100].CloseTimeUtc);
        var trade = Assert.Single(calculation.Trades, item => !item.IsReentry);
        Assert.Equal(SignalDirection.Long, trade.Direction);
        Assert.Equal(candles[100].CloseTimeUtc, trade.SignalTimeUtc);
        Assert.Equal(candles[101].OpenTimeUtc, trade.EntryTimeUtc);
    }

    [Fact]
    public void LegacyDependencyInjection_ResolvesNeutralContractsToBinanceAdapters()
    {
        using var factory = new EmaBotApiFactory();

        Assert.IsType<BinanceHistoricalMarketDataProvider>(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>());
        Assert.IsType<TestBinanceStreamClient>(factory.Services.GetRequiredService<IMarketBarStreamProvider>());
    }

    [Fact]
    public void InstrumentSpecAndMarketQuote_AreNonPersistedNeutralFoundations()
    {
        var spec = new InstrumentSpec("ExampleBroker", "SYMBOL", "Display symbol", AssetClass.Unknown, 5, 0.00001m, 1m, 0.01m, 100m, 0.01m, null, null, null);
        var quote = new MarketQuote(spec.BrokerSymbol, DateTimeOffset.UnixEpoch, 1.23450m, 1.23470m);

        Assert.Equal("ExampleBroker", spec.Broker);
        Assert.Equal(0.00020m, quote.Spread);
    }

    private sealed class HistoricalClient(IReadOnlyList<Candle> candles) : IBinanceHistoricalKlineClient
    {
        public Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc, int? limit, CancellationToken cancellationToken) => Task.FromResult(candles);
    }
}
