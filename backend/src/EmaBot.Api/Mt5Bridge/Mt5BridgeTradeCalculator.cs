using EmaBot.Api.Market;
using Microsoft.Extensions.Logging;

namespace EmaBot.Api.Mt5Bridge;

public interface IMt5TradeCalculator
{
    Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken);
    Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken);
}

public sealed class Mt5BridgeTradeCalculator(
    IMt5BridgeRequestClient bridge,
    ILogger<Mt5BridgeTradeCalculator>? logger = null,
    Mt5TradeCalculationRetryPolicy? retryPolicy = null) : IMt5TradeCalculator
{
    private readonly Mt5TradeCalculationRetryPolicy _retryPolicy = retryPolicy ?? Mt5TradeCalculationRetryPolicy.Default;
    public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken)
        => SendAsync<Mt5MarginCalculationPayload>(Mt5BridgeOperation.CalculateMargin, request, cancellationToken);

    public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken)
        => SendAsync<Mt5ProfitCalculationPayload>(Mt5BridgeOperation.CalculateProfit, request, cancellationToken);

    private async Task<T> SendAsync<T>(Mt5BridgeOperation operation, object request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await bridge.SendAsync(operation, request, cancellationToken);
                return response.DeserializePayload<T>() ?? throw new MarketDataProviderException("MT5 trade calculation", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid trade calculation.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < _retryPolicy.MaxAttempts && Mt5TradeCalculationTransientFailures.IsRetryable(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger?.LogWarning(exception,
                    "Retrying MT5 trade calculation. Operation={Operation} Attempt={Attempt} MaximumAttempts={MaximumAttempts} BrokerSymbol={BrokerSymbol} Direction={Direction} Lots={Lots} ExceptionType={ExceptionType} ProviderErrorKind={ProviderErrorKind} RemoteRetryable={RemoteRetryable}",
                    operation, attempt + 1, _retryPolicy.MaxAttempts, BrokerSymbol(request), Direction(request), Lots(request), exception.GetType().Name, Mt5BridgeProviderErrors.KindFor(exception)?.ToString(), (exception as Mt5BridgeRemoteException)?.Retryable);
                await _retryPolicy.DelayAsync(_retryPolicy.DelayBeforeAttempt(attempt + 1), cancellationToken);
            }
            catch (Exception exception) when (exception is not MarketDataProviderException)
            {
                throw Mt5BridgeProviderErrors.TradeCalculation(exception);
            }
        }
    }

    private static string BrokerSymbol(object request) => request switch { Mt5CalculateMarginRequest margin => margin.BrokerSymbol, Mt5CalculateProfitRequest profit => profit.BrokerSymbol, _ => string.Empty };
    private static string Direction(object request) => request switch { Mt5CalculateMarginRequest margin => margin.Direction, Mt5CalculateProfitRequest profit => profit.Direction, _ => string.Empty };
    private static decimal Lots(object request) => request switch { Mt5CalculateMarginRequest margin => margin.VolumeLots, Mt5CalculateProfitRequest profit => profit.VolumeLots, _ => 0m };
}

public sealed class Mt5TradeCalculationRetryPolicy
{
    public static readonly Mt5TradeCalculationRetryPolicy Default = new([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1100)]);

    public Mt5TradeCalculationRetryPolicy(IReadOnlyList<TimeSpan> retryDelays, Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        if (retryDelays.Count != 2 || retryDelays.Any(delay => delay < TimeSpan.Zero)) throw new ArgumentException("The MT5 trade-calculation retry policy requires two non-negative retry delays.", nameof(retryDelays));
        RetryDelays = retryDelays;
        DelayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
    }

    public IReadOnlyList<TimeSpan> RetryDelays { get; }
    public int MaxAttempts => RetryDelays.Count + 1;
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; }
    public TimeSpan DelayBeforeAttempt(int attempt) => RetryDelays[attempt - 2];
}

internal static class Mt5TradeCalculationTransientFailures
{
    public static bool IsRetryable(Exception exception) => exception switch
    {
        Mt5BridgeRequestTimeoutException => true,
        Mt5BridgeDisconnectedException => true,
        Mt5BridgeUnavailableException => true,
        Mt5BridgeRemoteException { Retryable: true } => true,
        EndOfStreamException => true,
        IOException => true,
        _ => false
    };
}
