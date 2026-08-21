using EmaBot.Api.Auth;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

public sealed record DemoMarketOrderRequest(Guid ClientExecutionId, string BrokerSymbol, string Side, decimal VolumeLots, decimal? StopLoss, decimal? TakeProfit);
[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/demo-executions")]
public sealed class DemoExecutionsController(IDemoExecutionService service) : ControllerBase
{
    [HttpGet("readiness")] public async Task<IActionResult> Readiness(CancellationToken token) => Ok(await service.ReadinessAsync(token));
    [HttpPost] public async Task<IActionResult> Submit(DemoMarketOrderRequest request, CancellationToken token) { try { return Ok(await service.SubmitAsync(new(request.ClientExecutionId, request.BrokerSymbol, request.Side, request.VolumeLots, request.StopLoss, request.TakeProfit), token)); } catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); } }
    [HttpGet("{clientExecutionId:guid}")] public async Task<IActionResult> Get(Guid clientExecutionId, CancellationToken token) => await service.GetAsync(clientExecutionId, token) is { } execution ? Ok(execution) : NotFound(new ApiMessage("Demo execution not found."));
    [HttpPost("{clientExecutionId:guid}/reconcile")] public async Task<IActionResult> Reconcile(Guid clientExecutionId, CancellationToken token) => await service.ReconcileAsync(clientExecutionId, token) is { } execution ? Ok(execution) : NotFound(new ApiMessage("Demo execution not found."));
    [HttpPost("{clientExecutionId:guid}/close")] public async Task<IActionResult> Close(Guid clientExecutionId, CancellationToken token) => await service.CloseAsync(clientExecutionId, token) is { } execution ? Ok(execution) : NotFound(new ApiMessage("Demo execution not found."));
}
