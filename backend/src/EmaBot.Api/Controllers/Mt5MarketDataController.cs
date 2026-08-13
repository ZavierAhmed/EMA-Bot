using EmaBot.Api.Auth;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/mt5/market-data")]
public sealed class Mt5MarketDataController(Mt5BridgeHistoricalMarketDataProvider historical, IMt5BridgeRequestClient bridge) : ControllerBase
{
    [HttpGet("latest")]
    public Task<ActionResult<IReadOnlyList<Candle>>> Latest(string symbol, string timeframe, int count, CancellationToken cancellationToken) => Bars(() => historical.GetLatestAsync(symbol, timeframe, count, cancellationToken));
    [HttpGet("range")]
    public Task<ActionResult<IReadOnlyList<Candle>>> Range(string symbol, string timeframe, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken) => Bars(() => historical.GetRangeAsync(symbol, timeframe, startUtc, endUtc, cancellationToken));
    [HttpGet("snapshot")]
    public async Task<ActionResult<Mt5BarSnapshotPayload>> Snapshot(string symbol, string timeframe, CancellationToken cancellationToken)
    {
        try { Mt5BridgeHistoricalMarketDataProvider.ValidateTimeframe(timeframe); var response = await bridge.SendAsync(Mt5BridgeOperation.GetBarSnapshot, new Mt5GetBarSnapshotRequest(symbol, timeframe), cancellationToken); return Ok(response.DeserializePayload<Mt5BarSnapshotPayload>() ?? throw new MarketDataProviderException("MT5 live bars", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid bar snapshot.")); }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (Exception exception) { return StatusCode(503, new ApiMessage(Mt5BridgeHistoricalMarketDataProvider.Translate(exception).Message)); }
    }
    private async Task<ActionResult<IReadOnlyList<Candle>>> Bars(Func<Task<IReadOnlyList<Candle>>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException exception) { return StatusCode(503, new ApiMessage(exception.Message)); }
    }
}
