using EmaBot.Api.Auth;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/mt5/account")]
public sealed class Mt5AccountController(IMt5AccountReader accounts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Mt5AccountResponse>> Get(CancellationToken cancellationToken)
    {
        try { return Ok(Mt5AccountResponse.From(await accounts.GetAsync(cancellationToken))); }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage(exception.Message)); }
    }
}
