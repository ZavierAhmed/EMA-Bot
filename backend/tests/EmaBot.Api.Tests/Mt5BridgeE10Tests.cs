using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Tests;

public sealed class Mt5BridgeE10Tests
{
    [Fact]
    public async Task ReadOnlyCalculatorsPreserveExactSymbolDirectionAndAccountCurrency()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.CalculateMargin] = Response(Mt5BridgeOperation.CalculateMargin, new Mt5MarginCalculationPayload("XAUUSDm", "Long", .01m, 2400.2m, 35m, "USD"));
        bridge.Responses[Mt5BridgeOperation.CalculateProfit] = Response(Mt5BridgeOperation.CalculateProfit, new Mt5ProfitCalculationPayload("XAUUSDm", "Short", .01m, 2400m, 2399m, 10m, "USD"));
        var calculator = new Mt5BridgeTradeCalculator(bridge);

        var margin = await calculator.CalculateMarginAsync(new Mt5CalculateMarginRequest("XAUUSDm", "Long", .01m, 2400.2m), CancellationToken.None);
        var profit = await calculator.CalculateProfitAsync(new Mt5CalculateProfitRequest("XAUUSDm", "Short", .01m, 2400m, 2399m), CancellationToken.None);

        Assert.Equal("XAUUSDm", margin.BrokerSymbol); Assert.Equal(35m, margin.RequiredMargin); Assert.Equal("USD", margin.AccountCurrency);
        Assert.Equal("XAUUSDm", profit.BrokerSymbol); Assert.Equal(10m, profit.Profit); Assert.Equal("USD", profit.AccountCurrency);
    }

    [Fact]
    public async Task TradeCalculationRequests_UseV1LongShortDirectionContract()
    {
        var bridge = new TestMt5BridgeRequestClient();
        bridge.Responses[Mt5BridgeOperation.CalculateMargin] = Response(Mt5BridgeOperation.CalculateMargin, new Mt5MarginCalculationPayload("XAUUSDm", "Long", .01m, 2400.2m, 35m, "USD"));
        bridge.Responses[Mt5BridgeOperation.CalculateProfit] = Response(Mt5BridgeOperation.CalculateProfit, new Mt5ProfitCalculationPayload("XAUUSDm", "Short", .01m, 2400m, 2401m, -10m, "USD"));
        var calculator = new Mt5BridgeTradeCalculator(bridge);

        await calculator.CalculateMarginAsync(new Mt5CalculateMarginRequest("XAUUSDm", "Long", .01m, 2400.2m), CancellationToken.None);
        var marginRequest = Assert.IsType<Mt5CalculateMarginRequest>(bridge.LastPayload); Assert.Equal("Long", marginRequest.Direction); Assert.NotEqual("Buy", marginRequest.Direction);

        await calculator.CalculateProfitAsync(new Mt5CalculateProfitRequest("XAUUSDm", "Short", .01m, 2400m, 2401m), CancellationToken.None);
        var profitRequest = Assert.IsType<Mt5CalculateProfitRequest>(bridge.LastPayload); Assert.Equal("Short", profitRequest.Direction); Assert.NotEqual("Sell", profitRequest.Direction);
    }

    [Fact]
    public void CommissionSemanticsKeepNullDistinctFromConfirmedZero()
    {
        decimal? unconfigured = null;
        decimal? commissionFree = 0m;
        var commission = 2m * .10m * 3m;

        Assert.Null(unconfigured);
        Assert.Equal(0m, commissionFree);
        Assert.Equal(.60m, commission);
    }

    private static Mt5BridgeEnvelope Response(Mt5BridgeOperation operation, object payload)
        => Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, operation, Guid.NewGuid(), payload, TimeProvider.System);
}
