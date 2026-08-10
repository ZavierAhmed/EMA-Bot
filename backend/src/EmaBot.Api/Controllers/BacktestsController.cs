using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/backtests")]
public sealed class BacktestsController(EmaBotDbContext database, BacktestService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BacktestRunDetailResponse>> Run(BacktestRequest request, CancellationToken token)
    {
        if (!BinanceIntervals.IsSupported(request.Interval) || request.StartUtc >= request.EndUtc) return BadRequest(new ApiMessage("Use a supported interval and a valid UTC date range."));
        var symbol = request.Symbol.Trim().ToUpperInvariant(); if (!await database.MonitoredSymbols.AnyAsync(x => x.Symbol == symbol && x.IsEnabled, token)) return BadRequest(new ApiMessage("The symbol must be monitored and enabled."));
        try { var run = await service.RunAsync(symbol, request.Interval, request.StartUtc, request.EndUtc, token); return CreatedAtAction(nameof(Get), new { id = run.Id }, BacktestResponseMapper.ToDetail(run)); }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (BinanceApiException exception) { return StatusCode(exception.IsRateLimited ? 429 : 502, new ApiMessage(exception.Message)); }
    }
    [HttpGet] public async Task<IActionResult> List(CancellationToken token) => Ok((await service.ListAsync(token)).Select(BacktestResponseMapper.ToSummary));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken token) => (await service.GetAsync(id, token)) is { } run ? Ok(BacktestResponseMapper.ToDetail(run)) : NotFound(new ApiMessage("Backtest not found."));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken token) => await service.DeleteAsync(id, token) ? NoContent() : NotFound(new ApiMessage("Backtest not found."));
}
public sealed record BacktestRequest(string Symbol, string Interval, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
