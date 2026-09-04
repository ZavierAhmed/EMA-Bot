using System.IO;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Tests;

public sealed class Mt5BridgeTradeCalculatorRetryTests
{
    [Fact]
    public async Task CalculateProfit_TransientIOExceptionThenSuccess_ReturnsOriginalPayloadAfterTwoAttempts()
    {
        var payload = ProfitPayload();
        var bridge = new SequenceBridge(new IOException("pipe reset"), Response(Mt5BridgeOperation.CalculateProfit, payload));

        var result = await Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None);

        Assert.Equal(payload, result); Assert.Equal(2, bridge.Attempts);
    }

    [Fact]
    public async Task CalculateProfit_TwoTransientFailuresThenSuccess_UsesAllThreeAttempts()
    {
        var bridge = new SequenceBridge(new IOException("one"), new EndOfStreamException("two"), Response(Mt5BridgeOperation.CalculateProfit, ProfitPayload()));

        await Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None);

        Assert.Equal(3, bridge.Attempts);
    }

    [Fact]
    public async Task CalculateProfit_ThreeTransientFailures_FailsClosedWithUnavailableRootCause()
    {
        var bridge = new SequenceBridge(new IOException("one"), new IOException("two"), new IOException("three"));

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(() => Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None));

        Assert.Equal(3, bridge.Attempts); Assert.Equal(MarketDataErrorKind.Unavailable, exception.Kind); Assert.IsType<IOException>(exception.InnerException);
    }

    [Fact]
    public async Task CalculateMargin_TransientFailureThenSuccess_RetriesIdentically()
    {
        var payload = new Mt5MarginCalculationPayload("BTCUSDm", "Long", .01m, 64707.43m, 7m, "USD");
        var bridge = new SequenceBridge(new Mt5BridgeDisconnectedException("disconnected"), Response(Mt5BridgeOperation.CalculateMargin, payload));

        var result = await Calculator(bridge).CalculateMarginAsync(new Mt5CalculateMarginRequest("BTCUSDm", "Long", .01m, 64707.43m), CancellationToken.None);

        Assert.Equal(payload, result); Assert.Equal(2, bridge.Attempts);
    }

    [Theory]
    [MemberData(nameof(RetryableTransportFailures))]
    public async Task RetryableTransportFailures_AreBounded(Exception transient)
    {
        var bridge = new SequenceBridge(transient, Response(Mt5BridgeOperation.CalculateProfit, ProfitPayload()));

        await Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None);

        Assert.Equal(2, bridge.Attempts);
    }

    public static IEnumerable<object[]> RetryableTransportFailures()
    {
        yield return [new Mt5BridgeRequestTimeoutException("timed out")];
        yield return [new Mt5BridgeDisconnectedException("disconnected")];
        yield return [new Mt5BridgeUnavailableException("unavailable")];
        yield return [new Mt5BridgeRemoteException("Busy", "retry later", true)];
    }

    [Theory]
    [MemberData(nameof(NonRetryableFailures))]
    public async Task NonRetryableFailures_AreNotRetried(Exception failure)
    {
        var bridge = new SequenceBridge(failure, Response(Mt5BridgeOperation.CalculateProfit, ProfitPayload()));

        await Assert.ThrowsAnyAsync<Exception>(() => Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None));

        Assert.Equal(1, bridge.Attempts);
    }

    public static IEnumerable<object[]> NonRetryableFailures()
    {
        yield return [new Mt5BridgeRemoteException("CalculationFailed", "OrderCalcProfit failed.", false)];
        yield return [new Mt5BridgeProtocolException("invalid frame")];
        yield return [new ArgumentException("invalid request")];
    }

    [Fact]
    public async Task InvalidResponse_IsNotRetried()
    {
        var bridge = new SequenceBridge(Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, Mt5BridgeOperation.CalculateProfit, Guid.NewGuid(), null, TimeProvider.System), Response(Mt5BridgeOperation.CalculateProfit, ProfitPayload()));

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(() => Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None));

        Assert.Equal(MarketDataErrorKind.InvalidResponse, exception.Kind); Assert.Equal(1, bridge.Attempts);
    }

    [Fact]
    public async Task DeserializationFailure_IsNotRetried()
    {
        var malformed = Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, Mt5BridgeOperation.CalculateProfit, Guid.NewGuid(), "not a profit payload", TimeProvider.System);
        var bridge = new SequenceBridge(malformed, Response(Mt5BridgeOperation.CalculateProfit, ProfitPayload()));

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(() => Calculator(bridge).CalculateProfitAsync(ProfitRequest(), CancellationToken.None));

        Assert.Equal(MarketDataErrorKind.Unknown, exception.Kind); Assert.Equal(1, bridge.Attempts);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagatedWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var bridge = new SequenceBridge(new IOException("pipe reset"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Calculator(bridge).CalculateProfitAsync(ProfitRequest(), cancellation.Token));

        Assert.Equal(1, bridge.Attempts);
    }

    [Fact]
    public async Task SuccessfulFirstAttempt_UsesOneBridgeOperationAndNoDelay()
    {
        var delays = 0;
        var bridge = new SequenceBridge(Response(Mt5BridgeOperation.CalculateProfit, ProfitPayload()));
        var policy = new Mt5TradeCalculationRetryPolicy([TimeSpan.Zero, TimeSpan.Zero], (_, _) => { delays++; return Task.CompletedTask; });

        await new Mt5BridgeTradeCalculator(bridge, retryPolicy: policy).CalculateProfitAsync(ProfitRequest(), CancellationToken.None);

        Assert.Equal(1, bridge.Attempts); Assert.Equal(0, delays);
    }

    private static Mt5BridgeTradeCalculator Calculator(SequenceBridge bridge)
        => new(bridge, retryPolicy: new Mt5TradeCalculationRetryPolicy([TimeSpan.Zero, TimeSpan.Zero], (_, _) => Task.CompletedTask));
    private static Mt5CalculateProfitRequest ProfitRequest() => new("BTCUSDm", "Short", .01m, 64707.43m, 64717.977536950470226057018390m);
    private static Mt5ProfitCalculationPayload ProfitPayload() => new("BTCUSDm", "Short", .01m, 64707.43m, 64717.977536950470226057018390m, -7.49m, "USD");
    private static Mt5BridgeEnvelope Response(Mt5BridgeOperation operation, object payload) => Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, operation, Guid.NewGuid(), payload, TimeProvider.System);

    private sealed class SequenceBridge(params object[] outcomes) : IMt5BridgeRequestClient
    {
        private readonly Queue<object> _outcomes = new(outcomes);
        public int Attempts { get; private set; }
        public bool IsConnected => true;
        public Mt5BridgeStatus GetStatus() => throw new NotSupportedException();
        public Task<Mt5BridgeEnvelope> SendAsync(Mt5BridgeOperation operation, object? payload, CancellationToken cancellationToken)
        {
            Attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = _outcomes.Dequeue();
            return outcome switch { Exception exception => Task.FromException<Mt5BridgeEnvelope>(exception), Mt5BridgeEnvelope response => Task.FromResult(response), _ => throw new InvalidOperationException("Unsupported test outcome.") };
        }
    }
}
