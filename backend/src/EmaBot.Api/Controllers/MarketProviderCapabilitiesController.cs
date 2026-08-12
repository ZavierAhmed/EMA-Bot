using EmaBot.Api.Auth;
using EmaBot.Api.Market;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/market/provider-capabilities")]
public sealed class MarketProviderCapabilitiesController(IMarketProviderCapabilities capabilities) : ControllerBase
{
    [HttpGet]
    public ActionResult<MarketProviderCapabilities> Get() => Ok(capabilities.Current);
}
