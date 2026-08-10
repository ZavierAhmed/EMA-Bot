using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/binance")]
public sealed class BinanceController(IBinanceFuturesMarketDataClient binance) : ControllerBase
{
    [HttpGet("symbols")]
    public async Task<ActionResult<IReadOnlyList<BinanceSymbol>>> GetSymbols(CancellationToken cancellationToken)
    {
        try { return Ok(await binance.GetTradableUsdtPerpetualSymbolsAsync(cancellationToken)); }
        catch (BinanceApiException exception) { return UpstreamError(exception); }
    }

    internal ObjectResult UpstreamError(BinanceApiException exception) => StatusCode(exception.IsRateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status502BadGateway, new ApiMessage(exception.Message));
}
