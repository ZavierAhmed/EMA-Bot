using EmaBot.Api.Auth;
using EmaBot.Api.Market;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/instruments")]
public sealed class InstrumentsController(IInstrumentCatalogProvider catalog, IMarketQuoteProvider quotes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InstrumentCatalogItem>>> GetAvailable(CancellationToken cancellationToken)
    {
        try { return Ok(await catalog.GetAvailableAsync(cancellationToken)); }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage(exception.Message)); }
    }

    [HttpGet("{brokerSymbol}")]
    public async Task<ActionResult<InstrumentCatalogItem>> Get(string brokerSymbol, CancellationToken cancellationToken)
    {
        if (!IsValidBrokerSymbol(brokerSymbol)) return BadRequest(new ApiMessage("A valid broker symbol is required."));
        try { return await catalog.GetAsync(brokerSymbol, cancellationToken) is { } item ? Ok(item) : NotFound(new ApiMessage("Instrument not found.")); }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage(exception.Message)); }
    }

    [HttpGet("{brokerSymbol}/quote")]
    public async Task<ActionResult<MarketQuote>> GetQuote(string brokerSymbol, CancellationToken cancellationToken)
    {
        if (!IsValidBrokerSymbol(brokerSymbol)) return BadRequest(new ApiMessage("A valid broker symbol is required."));
        try { return Ok(await quotes.GetQuoteAsync(brokerSymbol, cancellationToken)); }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage(exception.Message)); }
    }

    private static bool IsValidBrokerSymbol(string? brokerSymbol) => !string.IsNullOrWhiteSpace(brokerSymbol) && brokerSymbol.Length <= 128 && !brokerSymbol.Any(char.IsControl);
}
