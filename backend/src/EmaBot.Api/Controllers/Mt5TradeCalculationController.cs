using EmaBot.Api.Auth;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

public sealed record CalculateMarginDiagnosticRequest(string BrokerSymbol, string Direction, decimal VolumeLots, decimal OpenPrice);
public sealed record CalculateProfitDiagnosticRequest(string BrokerSymbol, string Direction, decimal VolumeLots, decimal OpenPrice, decimal ClosePrice);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/mt5/trade-calculation")]
public sealed class Mt5TradeCalculationController(IMt5TradeCalculator calculator) : ControllerBase
{
    [HttpPost("margin")]
    public Task<ActionResult<Mt5MarginCalculationPayload>> Margin(CalculateMarginDiagnosticRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(() => calculator.CalculateMarginAsync(new Mt5CalculateMarginRequest(request.BrokerSymbol?.Trim() ?? string.Empty, request.Direction?.Trim() ?? string.Empty, request.VolumeLots, request.OpenPrice), cancellationToken));

    [HttpPost("profit")]
    public Task<ActionResult<Mt5ProfitCalculationPayload>> Profit(CalculateProfitDiagnosticRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(() => calculator.CalculateProfitAsync(new Mt5CalculateProfitRequest(request.BrokerSymbol?.Trim() ?? string.Empty, request.Direction?.Trim() ?? string.Empty, request.VolumeLots, request.OpenPrice, request.ClosePrice), cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException exception) { return BadRequest(new ApiMessage(exception.Message)); }
    }
}
