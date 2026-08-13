using EmaBot.Api.Binance;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class Mt5BridgeBarProviderTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;
    public Mt5BridgeBarProviderTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public void NativeMt5TimeframesExcludeCanonicalThreeDay()
    {
        Assert.True(StrategyTimeframes.IsSupported("3d")); Assert.False(Mt5NativeTimeframes.IsSupported("3d"));
        Assert.Throws<ArgumentException>(() => Mt5BridgeHistoricalMarketDataProvider.ValidateTimeframe("3d"));
    }

    [Fact]
    public async Task HistoricalMapping_DerivesActualSuccessorCloseTimesAndExcludesCurrent()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.GetLatestBars] = Response(Mt5BridgeOperation.GetLatestBars, Bars());
        var provider = new Mt5BridgeHistoricalMarketDataProvider(bridge);
        var candles = await provider.GetLatestAsync("XAUUSDm", "3m", 2, CancellationToken.None);

        Assert.Equal(2, candles.Count); Assert.Equal(Time(12, 2, 59, 999), candles[0].CloseTimeUtc); Assert.Equal(Time(12, 5, 59, 999), candles[1].CloseTimeUtc);
        Assert.All(candles, candle => Assert.True(candle.IsClosed)); Assert.Equal(30m, candles[0].Volume); Assert.Equal(20m, candles[1].Volume);
    }

    [Fact]
    public async Task LatestCount_IsClosedBarCountAndAllowsMaximum1500()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.GetLatestBars] = Response(Mt5BridgeOperation.GetLatestBars, Bars());
        var provider = new Mt5BridgeHistoricalMarketDataProvider(bridge);

        await provider.GetLatestAsync("XAUUSDm", "3m", 1_500, CancellationToken.None);

        Assert.Equal(new Mt5GetLatestBarsRequest("XAUUSDm", "3m", 1_500), bridge.LastPayload);
    }

    [Fact]
    public async Task RangeMapping_DeduplicatesAscendingAndFiltersClosedBoundaries()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.GetBarsRange] = Response(Mt5BridgeOperation.GetBarsRange, new[] { Bar(12, 0, false), Bar(12, 3, false), Bar(12, 3, false), Bar(12, 6, true) });
        var provider = new Mt5BridgeHistoricalMarketDataProvider(bridge);
        var candles = await provider.GetRangeAsync("XAUUSDm", "3m", Time(12, 0), Time(12, 6), CancellationToken.None);

        Assert.Equal(2, candles.Count); Assert.Equal(Time(12, 0), candles[0].OpenTimeUtc); Assert.Equal(Time(12, 3), candles[1].OpenTimeUtc); Assert.All(candles, candle => Assert.True(candle.CloseTimeUtc <= Time(12, 6)));
    }

    [Fact]
    public async Task FractionalRangeEnd_CeilsWireStopButKeepsExactFinalFilter()
    {
        var bridge = new TestMt5BridgeRequestClient();
        var start = new DateTimeOffset(2026, 8, 13, 23, 54, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 8, 13, 23, 59, 59, 999, TimeSpan.Zero);
        bridge.Responses[Mt5BridgeOperation.GetBarsRange] = Response(Mt5BridgeOperation.GetBarsRange, new[] { BarAt(start, false), BarAt(start.AddMinutes(3), false), BarAt(new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero), true) });
        var provider = new Mt5BridgeHistoricalMarketDataProvider(bridge);

        var candles = await provider.GetRangeAsync("XAUUSDm", "3m", start, end, CancellationToken.None);
        var request = Assert.IsType<Mt5GetBarsRangeRequest>(bridge.LastPayload);

        Assert.Equal(new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), request.EndUnixSeconds);
        var final = Assert.Single(candles, candle => candle.OpenTimeUtc == start.AddMinutes(3));
        Assert.Equal(end, final.CloseTimeUtc); Assert.DoesNotContain(candles, candle => candle.OpenTimeUtc.Date > start.Date);
        Assert.Equal(end.ToUnixTimeSeconds(), Mt5BridgeHistoricalMarketDataProvider.ToInclusiveStopUnixSeconds(new DateTimeOffset(2026, 8, 13, 23, 59, 59, TimeSpan.Zero)));
    }

    [Fact]
    public async Task StreamBaselineRolloverAndDuplicateSuppressionPreserveCurrentPrice()
    {
        var state = new Mt5BridgeMarketBarStreamProvider.StreamState(); var updates = new List<MarketBarUpdate>();
        var a = Bar(12, 0, false); var b = Bar(12, 3, true); var c = Bar(12, 6, true, close: 103m);
        await Mt5BridgeMarketBarStreamProvider.EmitAsync(new("XAUUSDm", "3m", Time(12, 3, 1), a, b), state, Add, CancellationToken.None);
        await Mt5BridgeMarketBarStreamProvider.EmitAsync(new("XAUUSDm", "3m", Time(12, 3, 2), a, b with { Close = 102m }), state, Add, CancellationToken.None);
        await Mt5BridgeMarketBarStreamProvider.EmitAsync(new("XAUUSDm", "3m", Time(12, 6, 1), b with { IsCurrent = false }, c), state, Add, CancellationToken.None);
        await Mt5BridgeMarketBarStreamProvider.EmitAsync(new("XAUUSDm", "3m", Time(12, 6, 1), b with { IsCurrent = false }, c), state, Add, CancellationToken.None);

        Assert.Equal(4, updates.Count); Assert.False(updates[0].IsClosed); Assert.Equal(101m, updates[0].Close); Assert.Equal(102m, updates[1].Close); Assert.True(updates[2].IsClosed); Assert.False(updates[3].IsClosed); Assert.Equal(updates[2].CloseTimeUtc.AddMilliseconds(1), updates[3].OpenTimeUtc); Assert.Equal(103m, updates[3].Close);
        Task Add(MarketBarUpdate update, CancellationToken _) { updates.Add(update); return Task.CompletedTask; }
    }

    [Fact]
    public void TestHostUsesDeterministicHistoricalOverrideWhileConcreteMt5ProvidersResolve()
    {
        using var scope = _factory.Services.CreateScope(); var services = scope.ServiceProvider;
        Assert.IsType<BinanceHistoricalMarketDataProvider>(services.GetRequiredService<IHistoricalMarketDataProvider>());
        Assert.IsType<TestBinanceStreamClient>(services.GetRequiredService<IMarketBarStreamProvider>());
        Assert.IsType<Mt5BridgeHistoricalMarketDataProvider>(services.GetRequiredService<Mt5BridgeHistoricalMarketDataProvider>());
        Assert.IsType<Mt5BridgeMarketBarStreamProvider>(services.GetRequiredService<Mt5BridgeMarketBarStreamProvider>());
    }

    [Fact]
    public async Task BarProviderTranslatesBridgeErrorsWithoutLeakingTransportExceptions()
    {
        var bridge = new TestMt5BridgeRequestClient { Exception = new Mt5BridgeRemoteException("HistoryNotReady", "not ready", true) };
        var provider = new Mt5BridgeHistoricalMarketDataProvider(bridge);
        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(() => provider.GetLatestAsync("XAUUSDm", "3m", 1, CancellationToken.None));
        Assert.Equal(MarketDataErrorKind.Unavailable, exception.Kind);
    }

    private static Mt5BridgeEnvelope Response(Mt5BridgeOperation operation, object payload) => Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, operation, Guid.NewGuid(), payload, TimeProvider.System);
    private static Mt5BarPayload[] Bars() => [Bar(12, 0, false, realVolume: 30), Bar(12, 3, false, realVolume: 0), Bar(12, 6, true, realVolume: 40)];
    private static Mt5BarPayload Bar(int hour, int minute, bool current, decimal close = 101m, long realVolume = 0) => new("XAUUSDm", "3m", Time(hour, minute), 100m, 105m, 99m, close, 20, realVolume, 4, current);
    private static Mt5BarPayload BarAt(DateTimeOffset open, bool current) => new("XAUUSDm", "3m", open, 100m, 105m, 99m, 101m, 20, 0, 4, current);
    private static DateTimeOffset Time(int hour, int minute, int second = 0, int millisecond = 0) => new(2026, 8, 13, hour, minute, second, millisecond, TimeSpan.Zero);
}
