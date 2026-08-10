using System.Net;
using System.Text;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EmaBot.Api.Tests;

public sealed class MarketDataAndStrategyTests
{
    [Fact]
    public void TradingSettingsId_IsExplicitlyApplicationGenerated()
    {
        using var database = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var settingsId = database.Model.FindEntityType(typeof(TradingSettings))!.FindProperty(nameof(TradingSettings.Id))!;
        var monitoredSymbolId = database.Model.FindEntityType(typeof(MonitoredSymbol))!.FindProperty(nameof(MonitoredSymbol.Id))!;
        Assert.Equal(ValueGenerated.Never, settingsId.ValueGenerated);
        Assert.Equal(ValueGenerated.OnAdd, monitoredSymbolId.ValueGenerated);
    }

    [Fact]
    public void Ema_UsesSmaSeedAndDoesNotEmitBeforeWarmup()
    {
        var result = EmaCalculator.Calculate(Enumerable.Range(1, 10).Select(value => (decimal)value).ToArray(), 9);
        Assert.All(result.Take(8), value => Assert.Null(value));
        Assert.Equal(5m, result[8]);
        Assert.Equal(6m, result[9]);
        Assert.All(EmaCalculator.Calculate(Enumerable.Range(1, 99).Select(value => (decimal)value).ToArray(), 100), value => Assert.Null(value));
    }

    [Fact]
    public async Task Client_FiltersEligibleContractsAndParsesClosedKlines()
    {
        const string exchange = """{"symbols":[{"symbol":"BTCUSDT","baseAsset":"BTC","quoteAsset":"USDT","status":"TRADING","contractType":"PERPETUAL"},{"symbol":"OLDUSDT","baseAsset":"OLD","quoteAsset":"USDT","status":"BREAK","contractType":"PERPETUAL"},{"symbol":"BTCUSD","baseAsset":"BTC","quoteAsset":"USD","status":"TRADING","contractType":"PERPETUAL"}]}""";
        const string klines = """[[1700000000000,"1","2","0.5","1.5","100",1700000179999,"0",1,"0","0","0"]]""";
        var client = new BinanceFuturesMarketDataClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_.RequestUri!.AbsolutePath.Contains("exchangeInfo") ? exchange : klines, Encoding.UTF8, "application/json") })) { BaseAddress = new Uri("https://fapi.binance.com/") }, TimeProvider.System);
        var symbols = await client.GetTradableUsdtPerpetualSymbolsAsync(CancellationToken.None);
        var candles = await client.GetKlinesAsync("btcusdt", "3m", null, null, 10, CancellationToken.None);
        Assert.Single(symbols); Assert.Equal("BTCUSDT", symbols[0].Symbol); Assert.Single(candles); Assert.True(candles[0].IsClosed); Assert.Equal(1.5m, candles[0].Close);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetKlinesAsync("BTCUSDT", "1m", null, null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Client_MapsRateLimitToControlledException()
    {
        var client = new BinanceFuturesMarketDataClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"msg\":\"Too many requests\"}") })) { BaseAddress = new Uri("https://fapi.binance.com/") }, TimeProvider.System);
        var exception = await Assert.ThrowsAsync<BinanceApiException>(() => client.GetExchangeInfoAsync(CancellationToken.None));
        Assert.True(exception.IsRateLimited);
    }

    [Fact]
    public void Engine_DetectsCrossoverOnce_ConfirmationAndEma100Filter()
    {
        var engine = new EmaSignalEngine();
        var settings = new TradingSettings { WaitForConfirmationCandle = true, UseEma100Filter = true };
        var snapshots = new[] { Snapshot(1, 1, 2, 1, 2), Snapshot(2, 3, 2, 1, 4), Snapshot(3, 4, 3, 1, 5), Snapshot(4, 5, 3, 1, 6) };
        var events = engine.EvaluateSnapshots(snapshots, settings);
        Assert.Single(events, item => item.Status == SignalStatus.BullishCrossover);
        Assert.Single(events, item => item.Status == SignalStatus.AwaitingConfirmation);
        Assert.Single(events, item => item.Status == SignalStatus.LongSignal);
    }

    [Fact]
    public void Engine_FailedConfirmationExpiresAndDoesNotUseLaterCandle()
    {
        var engine = new EmaSignalEngine();
        var settings = new TradingSettings { WaitForConfirmationCandle = true };
        var events = engine.EvaluateSnapshots(new[] { Snapshot(1, 1, 2, 1, 2), Snapshot(2, 3, 2, 1, 4), Snapshot(3, 2.5m, 2, 1, 1), Snapshot(4, 4, 2, 1, 6) }, settings);
        Assert.Single(events, item => item.Status == SignalStatus.ConfirmationFailed);
        Assert.DoesNotContain(events, item => item.Status == SignalStatus.LongSignal);
    }

    [Fact]
    public void Engine_RecrossCannotConfirmOldDirectionButStartsOppositePendingSetup()
    {
        var engine = new EmaSignalEngine();
        var settings = new TradingSettings { WaitForConfirmationCandle = true };
        var events = engine.EvaluateSnapshots(new[] { Snapshot(1, 1, 2, 1, 2), Snapshot(2, 3, 2, 1, 4), Snapshot(3, 1, 2, 1, 3) }, settings);
        Assert.Contains(events, item => item.Direction == SignalDirection.Long && item.Status == SignalStatus.ConfirmationFailed);
        Assert.Contains(events, item => item.Direction == SignalDirection.Short && item.Status == SignalStatus.BearishCrossover);
        Assert.Contains(events, item => item.Direction == SignalDirection.Short && item.Status == SignalStatus.AwaitingConfirmation);
        Assert.DoesNotContain(events, item => item.Direction == SignalDirection.Long && item.Status == SignalStatus.LongSignal);
    }

    [Fact]
    public void Engine_ExcludesOpenCandlesAndCalculatesGapState()
    {
        var engine = new EmaSignalEngine();
        var openOnly = new[] { new Candle(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, 1, 1, 1, false) };
        Assert.Empty(engine.Evaluate(openOnly, new TradingSettings()).Snapshots);
        var events = engine.EvaluateSnapshots(new[] { Snapshot(1, 2, 1, 1, 10), Snapshot(2, 3, 1, 1, 10) }, new TradingSettings());
        Assert.Empty(events);
        var evaluation = engine.Evaluate(Enumerable.Range(1, 101).Select(index => new Candle(DateTimeOffset.UnixEpoch.AddMinutes(index), DateTimeOffset.UnixEpoch.AddMinutes(index).AddSeconds(1), index, index, index, index, 1, true)).ToArray(), new TradingSettings());
        var last = evaluation.Snapshots.Last();
        Assert.Equal(decimal.Abs(last.Ema9!.Value - last.Ema15!.Value) / last.Close * 100m, last.GapPercent);
    }

    private static IndicatorSnapshot Snapshot(int minute, decimal ema9, decimal ema15, decimal ema100, decimal close) => new(DateTimeOffset.UnixEpoch.AddMinutes(minute), close, ema9, ema15, ema100, decimal.Abs(ema9 - ema15) / close * 100m, GapState.Unchanged, ema9 > ema15 ? TrendDirection.Up : ema9 < ema15 ? TrendDirection.Down : TrendDirection.Neutral);
}

public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
}
