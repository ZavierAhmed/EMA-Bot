using EmaBot.Api.Market;

namespace EmaBot.Api.Mt5Bridge;

public interface IMt5TradeCalculator
{
    Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken);
    Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken);
}

public sealed class Mt5BridgeTradeCalculator(IMt5BridgeRequestClient bridge) : IMt5TradeCalculator
{
    public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken)
        => SendAsync<Mt5MarginCalculationPayload>(Mt5BridgeOperation.CalculateMargin, request, cancellationToken);

    public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken)
        => SendAsync<Mt5ProfitCalculationPayload>(Mt5BridgeOperation.CalculateProfit, request, cancellationToken);

    private async Task<T> SendAsync<T>(Mt5BridgeOperation operation, object request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(operation, request, cancellationToken);
            return response.DeserializePayload<T>() ?? throw new MarketDataProviderException("MT5 trade calculation", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid trade calculation.");
        }
        catch (Exception exception) when (exception is not MarketDataProviderException)
        {
            throw Mt5BridgeProviderErrors.TradeCalculation(exception);
        }
    }
}
