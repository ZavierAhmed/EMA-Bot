using System.Net;
using EmaBot.Api.Binance;
using Microsoft.AspNetCore.Http;

namespace EmaBot.Api.Tests;

public sealed class BinanceTimeoutTests
{
    [Fact]
    public async Task HttpClientTimeout_BecomesGatewayTimeoutBinanceException()
    {
        using var client = new HttpClient(new DelayingHandler()) { BaseAddress = new Uri("https://example.test/"), Timeout = TimeSpan.FromMilliseconds(20) };
        var market = new BinanceFuturesMarketDataClient(client, TimeProvider.System);
        var exception = await Assert.ThrowsAsync<BinanceApiException>(() => market.GetKlinesAsync("BTCUSDT", "3m", null, null, 1, CancellationToken.None));
        Assert.Equal(StatusCodes.Status504GatewayTimeout, exception.StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTranslatedToBinanceTimeout()
    {
        using var client = new HttpClient(new DelayingHandler()) { BaseAddress = new Uri("https://example.test/"), Timeout = TimeSpan.FromSeconds(5) };
        var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var market = new BinanceFuturesMarketDataClient(client, TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => market.GetKlinesAsync("BTCUSDT", "3m", null, null, 1, cancellation.Token));
    }

    private sealed class DelayingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
