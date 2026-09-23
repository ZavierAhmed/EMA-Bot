using System.Diagnostics;
using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Services;

// Per-run instrumentation/evidence only. Exactly one delegate call per request;
// the shared MT5 calculator remains the sole retry authority.
internal sealed class GridHistoricalEconomics(IMt5TradeCalculator authority, string currency) : IMt5TradeCalculator
{
    // V1 Number(double) serializes with DoubleToString(value, 10). Allow at
    // most one unit of that representation, independent of price/volume scale.
    // Only validate the echo: retain the original request and all Grid prices.
    private static bool Mt5BridgeNumberMatches(decimal requested, decimal echoed)
        => decimal.Abs(requested - echoed) <= 0.0000000001m;

    private readonly Stopwatch elapsed = new();
    public int Calls { get; private set; }
    public long ElapsedMilliseconds => elapsed.ElapsedMilliseconds;
    public Dictionary<Mt5CalculateProfitRequest, decimal> Profits { get; } = [];
    public Dictionary<Mt5CalculateMarginRequest, decimal> Margins { get; } = [];
    public void ClearEvidence() { Profits.Clear(); Margins.Clear(); }

    public async Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++; elapsed.Start();
        try
        {
            var result = await authority.CalculateProfitAsync(request, token);
            token.ThrowIfCancellationRequested();
            if (result.BrokerSymbol != request.BrokerSymbol || result.Direction != request.Direction || !Mt5BridgeNumberMatches(request.VolumeLots, result.VolumeLots)
                || !Mt5BridgeNumberMatches(request.OpenPrice, result.OpenPrice) || !Mt5BridgeNumberMatches(request.ClosePrice, result.ClosePrice) || result.AccountCurrency != currency)
                throw new InvalidOperationException("MT5 profit response does not match requested economics/account currency.");
            Profits[request] = result.Profit;
            return result;
        }
        finally { elapsed.Stop(); }
    }
    public async Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++; elapsed.Start();
        try
        {
            var result = await authority.CalculateMarginAsync(request, token);
            token.ThrowIfCancellationRequested();
            if (result.BrokerSymbol != request.BrokerSymbol || result.Direction != request.Direction || !Mt5BridgeNumberMatches(request.VolumeLots, result.VolumeLots)
                || !Mt5BridgeNumberMatches(request.OpenPrice, result.OpenPrice) || result.AccountCurrency != currency || result.RequiredMargin <= 0m)
                throw new InvalidOperationException("MT5 margin response is invalid or does not match requested economics/account currency.");
            Margins[request] = result.RequiredMargin;
            return result;
        }
        finally { elapsed.Stop(); }
    }
}
