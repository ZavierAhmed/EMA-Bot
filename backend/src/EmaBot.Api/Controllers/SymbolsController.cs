using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/symbols")]
public sealed class SymbolsController(EmaBotDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MonitoredSymbolResponse>>> Get(CancellationToken cancellationToken) => Ok(await database.MonitoredSymbols.OrderBy(symbol => symbol.Symbol).Select(symbol => new MonitoredSymbolResponse(symbol.Id, symbol.Symbol, symbol.BaseAsset, symbol.QuoteAsset, symbol.IsEnabled)).ToListAsync(cancellationToken));

    [HttpPatch("{id:int}/enabled")]
    public async Task<ActionResult<MonitoredSymbolResponse>> SetEnabled(int id, SetEnabledRequest request, CancellationToken cancellationToken)
    {
        var symbol = await database.MonitoredSymbols.FindAsync([id], cancellationToken);
        if (symbol is null) return NotFound(new ApiMessage("Monitored symbol not found."));
        symbol.IsEnabled = request.IsEnabled;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(new MonitoredSymbolResponse(symbol.Id, symbol.Symbol, symbol.BaseAsset, symbol.QuoteAsset, symbol.IsEnabled));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var symbol = await database.MonitoredSymbols.FindAsync([id], cancellationToken);
        if (symbol is null) return NotFound(new ApiMessage("Monitored symbol not found."));
        database.MonitoredSymbols.Remove(symbol);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record SetEnabledRequest(bool IsEnabled);
public sealed record MonitoredSymbolResponse(int Id, string Symbol, string BaseAsset, string QuoteAsset, bool IsEnabled);
