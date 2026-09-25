using EmaBot.Api.Auth;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/backtests/grid")]
public sealed class GridBreakoutGuardResearchController(GridBreakoutGuardShadowSimulator simulator) : ControllerBase
{
    [HttpGet("{id:int}/research/breakout-guards/export/excel")]
    public async Task<IActionResult> ExportExcel(int id, CancellationToken token)
    {
        if (Request.Query.Count != 0) return BadRequest(new ApiMessage("Grid breakout research uses a frozen server-owned catalog; query parameters are not accepted."));
        try
        {
            var result = await simulator.SimulateAsync(id, token);
            return File(GridBreakoutGuardResearchExcelExport.Create(result), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"grid-breakout-guard-research-{id}.xlsx");
        }
        catch (GridGuardResearchException exception) { return StatusCode(exception.StatusCode, new ApiMessage(exception.Message)); }
    }
}
