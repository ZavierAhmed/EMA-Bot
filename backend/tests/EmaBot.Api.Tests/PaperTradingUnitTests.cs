using EmaBot.Api.Binance;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class PaperTradingUnitTests
{
    [Fact]
    public void StreamParser_HandlesCombinedClosedKlineAndNormalizesSymbol()
    {
        const string json = """{"stream":"btcusdt@kline_3m","data":{"e":"kline","E":1700000000000,"s":"btcusdt","k":{"t":1699999820000,"T":1699999999999,"i":"3m","o":"100","h":"102","l":"99","c":"101.5","v":"12.4","x":true}}}""";
        Assert.True(BinanceKlineParser.TryParse(json, out var update));
        Assert.Equal("BTCUSDT", update.Symbol); Assert.Equal("3m", update.Interval); Assert.True(update.IsClosed); Assert.Equal(101.5m, update.Close);
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{\"e\":\"kline\",\"E\":1,\"s\":\"BTCUSDT\",\"k\":{\"t\":1,\"T\":2,\"i\":\"1m\",\"o\":\"bad\",\"h\":\"2\",\"l\":\"1\",\"c\":\"1\",\"v\":\"1\",\"x\":false}}")]
    public void StreamParser_RejectsMalformedPayload(string json) => Assert.False(BinanceKlineParser.TryParse(json, out _));

    [Fact]
    public void TradeMath_UsesSharedTrailingThresholdsAndOneTimeTargetDistance()
    {
        Assert.Equal(20m, TradeMath.LockPercent(50m)); Assert.Equal(40m, TradeMath.LockPercent(70m)); Assert.Equal(70m, TradeMath.LockPercent(100m));
        var original = TradeMath.InitialTarget(100m, 90m, SignalDirection.Long, 2m);
        Assert.Equal(120m, original); Assert.Equal(108m, TradeMath.TrailingStop(100m, original, SignalDirection.Long, 40m)); Assert.Equal(122m, TradeMath.ExtendedTarget(100m, original, SignalDirection.Long));
    }

    [Fact]
    public void StreamParser_AcceptsSubscriptionAcknowledgementWithoutTreatingItAsKline()
    {
        const string acknowledgement = """{"result":null,"id":"abc"}""";
        Assert.True(BinanceKlineParser.IsSubscriptionAcknowledgement(acknowledgement));
        Assert.False(BinanceKlineParser.TryParse(acknowledgement, out _));
    }

    [Fact]
    public void MarketDataLiveness_RequiresReconnectOnlyAfterSilenceTimeout()
    {
        var start = DateTimeOffset.UnixEpoch;
        var liveness = new MarketDataLiveness(TimeSpan.FromSeconds(5), start);
        Assert.False(liveness.ReconnectRequired(start.AddSeconds(4)));
        Assert.True(liveness.ReconnectRequired(start.AddSeconds(5)));
        liveness.Observe(start.AddSeconds(5));
        Assert.False(liveness.ReconnectRequired(start.AddSeconds(9)));
        Assert.True(liveness.ReconnectRequired(start.AddSeconds(10)));
    }

    [Fact]
    public void SubscriptionAcknowledgement_DoesNotResetMarketDataLiveness()
    {
        var start = DateTimeOffset.UnixEpoch;
        var liveness = new MarketDataLiveness(TimeSpan.FromSeconds(5), start);
        Assert.True(BinanceKlineParser.IsSubscriptionAcknowledgement("""{"result":null,"id":"ack"}"""));
        Assert.True(liveness.ReconnectRequired(start.AddSeconds(5)));
    }
}
