using EmaBot.Api.Auth;
using EmaBot.Api.Mt5Bridge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/mt5/bridge")]
public sealed class Mt5BridgeController(IMt5BridgeRequestClient bridge) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<Mt5BridgeStatusResponse> Status() => Ok(Mt5BridgeStatusResponse.From(bridge.GetStatus()));

    [HttpPost("ping")]
    public async Task<ActionResult<Mt5BridgeStatusResponse>> Ping(CancellationToken cancellationToken)
    {
        if (!bridge.IsConnected) return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("The MT5 bridge is not connected."));
        try { await bridge.SendAsync(Mt5BridgeOperation.Ping, null, cancellationToken); return Ok(Mt5BridgeStatusResponse.From(bridge.GetStatus())); }
        catch (Mt5BridgeUnavailableException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("The MT5 bridge is not connected.")); }
    }
}
