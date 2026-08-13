using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/symbols")]
public sealed class SymbolsController(EmaBotDbContext database, IInstrumentCatalogProvider catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MonitoredSymbolResponse>>> Get(CancellationToken cancellationToken)
        => Ok((await database.MonitoredSymbols.AsNoTracking().OrderByDescending(symbol => symbol.Source == MarketDataSource.Mt5Exness).ThenBy(symbol => symbol.Symbol).ToListAsync(cancellationToken)).Select(ToResponse).ToArray());

    [HttpPost]
    public async Task<ActionResult<MonitoredSymbolResponse>> Add(AddMonitoredSymbolRequest request, CancellationToken cancellationToken)
    {
        var requested = request.BrokerSymbol?.Trim();
        if (string.IsNullOrWhiteSpace(requested)) return BadRequest(new ApiMessage("An exact MT5 broker symbol is required."));
        try
        {
            var instrument = await catalog.GetAsync(requested, cancellationToken);
            if (instrument is null || !instrument.IsSelected || !string.Equals(instrument.Spec.BrokerSymbol, requested, StringComparison.Ordinal)) return BadRequest(new ApiMessage("Select an exact MT5 Market Watch instrument."));
            if (await database.MonitoredSymbols.AnyAsync(symbol => symbol.Source == MarketDataSource.Mt5Exness && symbol.Symbol == requested, cancellationToken)) return Conflict(new ApiMessage("This MT5 instrument is already monitored."));
            var monitored = new MonitoredSymbol
            {
                Source = MarketDataSource.Mt5Exness,
                Symbol = instrument.Spec.BrokerSymbol,
                DisplayName = instrument.Description ?? instrument.Spec.DisplaySymbol,
                BaseAsset = instrument.Spec.CurrencyBase,
                QuoteAsset = instrument.Spec.CurrencyProfit,
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            database.MonitoredSymbols.Add(monitored); await database.SaveChangesAsync(cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = monitored.Id }, ToResponse(monitored));
        }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("The MT5 instrument catalog is currently unavailable."));
        }
    }

    [HttpPatch("{id:int}/enabled")]
    public async Task<ActionResult<MonitoredSymbolResponse>> SetEnabled(int id, SetEnabledRequest request, CancellationToken cancellationToken)
    {
        var symbol = await database.MonitoredSymbols.FindAsync([id], cancellationToken);
        if (symbol is null) return NotFound(new ApiMessage("Monitored symbol not found."));
        if (symbol.Source == MarketDataSource.LegacyBinance && request.IsEnabled) return BadRequest(new ApiMessage("Legacy Binance instruments cannot be enabled for new research."));
        symbol.IsEnabled = request.IsEnabled;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(symbol));
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

    private static MonitoredSymbolResponse ToResponse(MonitoredSymbol symbol) => new(symbol.Id, symbol.Source, MarketDataSourceLabels.For(symbol.Source), symbol.Symbol, symbol.DisplayName, symbol.BaseAsset, symbol.QuoteAsset, symbol.IsEnabled);
}

public sealed record AddMonitoredSymbolRequest(string BrokerSymbol);
public sealed record SetEnabledRequest(bool IsEnabled);
public sealed record MonitoredSymbolResponse(int Id, MarketDataSource Source, string SourceLabel, string Symbol, string? DisplayName, string? BaseAsset, string? QuoteAsset, bool IsEnabled);
