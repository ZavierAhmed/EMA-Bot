using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/backtests/grid")]
public sealed class GridBacktestsController(GridBacktestService service, EmaBotDbContext database) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken token) => Ok((await service.ListAsync(token)).Select(GridBacktestResponses.ToResponse));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken token)
        => await service.GetAsync(id, token) is { } run ? Ok(GridBacktestResponses.ToDetail(run)) : NotFound(new ApiMessage("Grid backtest not found."));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken token)
        => await service.DeleteAsync(id, token) ? NoContent() : NotFound(new ApiMessage("Grid backtest not found."));
    [HttpGet("{id:int}/export/excel")]
    public async Task<IActionResult> ExportExcel(int id, CancellationToken token)
    {
        var workbook = await GridBacktestExcelExport.CreateAsync(database, id, token);
        return workbook is null ? NotFound(new ApiMessage("Grid backtest not found."))
            : File(workbook.Bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"grid-backtest-{id}.xlsx");
    }
}
