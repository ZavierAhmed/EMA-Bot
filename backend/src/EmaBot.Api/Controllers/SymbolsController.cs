using System.ComponentModel.DataAnnotations;
using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/symbols")]
public sealed class SymbolsController(EmaBotDbContext database, IBinanceFuturesMarketDataClient binance) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MonitoredSymbolResponse>>> Get(CancellationToken cancellationToken) => Ok(await database.MonitoredSymbols.OrderBy(symbol => symbol.Symbol).Select(symbol => new MonitoredSymbolResponse(symbol.Id, symbol.Symbol, symbol.BaseAsset, symbol.QuoteAsset, symbol.IsEnabled)).ToListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<MonitoredSymbolResponse>> Add(AddSymbolRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Symbol.Trim().ToUpperInvariant();
        if (await database.MonitoredSymbols.AnyAsync(symbol => symbol.Symbol == normalized, cancellationToken)) return Conflict(new ApiMessage("That symbol is already monitored."));
        try
        {
            var contract = (await binance.GetTradableUsdtPerpetualSymbolsAsync(cancellationToken)).SingleOrDefault(symbol => symbol.Symbol == normalized);
            if (contract is null) return BadRequest(new ApiMessage("The symbol is not an active USDT perpetual Binance Futures contract."));
            var monitored = new MonitoredSymbol { Symbol = contract.Symbol, BaseAsset = contract.BaseAsset, QuoteAsset = contract.QuoteAsset, CreatedAtUtc = DateTimeOffset.UtcNow };
            database.MonitoredSymbols.Add(monitored);
            await database.SaveChangesAsync(cancellationToken);
            return CreatedAtAction(nameof(Get), new MonitoredSymbolResponse(monitored.Id, monitored.Symbol, monitored.BaseAsset, monitored.QuoteAsset, monitored.IsEnabled));
        }
        catch (BinanceApiException exception) { return Upstream(exception); }
    }

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

    private ObjectResult Upstream(BinanceApiException exception) => StatusCode(exception.IsRateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status502BadGateway, new ApiMessage(exception.Message));
}

public sealed record AddSymbolRequest([param: Required, StringLength(32)] string Symbol);
public sealed record SetEnabledRequest(bool IsEnabled);
public sealed record MonitoredSymbolResponse(int Id, string Symbol, string BaseAsset, string QuoteAsset, bool IsEnabled);
