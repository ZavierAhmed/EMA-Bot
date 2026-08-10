using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/strategy")]
public sealed class StrategyController(IBinanceFuturesMarketDataClient binance, TradingSettingsService settingsService, EmaSignalEngine engine) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult<StrategyPreviewResponse>> Preview([FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!BinanceIntervals.IsSupported(interval)) return BadRequest(new ApiMessage("Unsupported Binance interval."));
        if (limit is < 100 or > 1500) return BadRequest(new ApiMessage("Limit must be between 100 and 1500 for an EMA 100 preview."));
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return BadRequest(new ApiMessage("A Binance symbol is required."));
        try
        {
            if (!(await binance.GetTradableUsdtPerpetualSymbolsAsync(cancellationToken)).Any(contract => contract.Symbol == normalized)) return BadRequest(new ApiMessage("The symbol is not an active USDT perpetual Binance Futures contract."));
            var candles = await binance.GetKlinesAsync(normalized, interval, null, null, limit ?? 300, cancellationToken);
            var evaluation = engine.Evaluate(candles, await settingsService.GetAsync(cancellationToken));
            var latest = evaluation.Snapshots.LastOrDefault(snapshot => snapshot.Ema100.HasValue);
            if (latest is null) return BadRequest(new ApiMessage("Not enough closed candles were returned for EMA 100."));
            return Ok(new StrategyPreviewResponse(normalized, interval, latest.Time, latest.Close, latest.Ema9, latest.Ema15, latest.Ema100, latest.TrendDirection, latest.GapPercent, latest.GapState, evaluation.Events.TakeLast(20).ToArray()));
        }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (BinanceApiException exception) { return StatusCode(exception.IsRateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status502BadGateway, new ApiMessage(exception.Message)); }
    }
}

public sealed record StrategyPreviewResponse(string Symbol, string Interval, DateTimeOffset LatestClosedCandleTime, decimal LatestClose, decimal? Ema9, decimal? Ema15, decimal? Ema100, TrendDirection TrendDirection, decimal? GapPercent, GapState GapState, IReadOnlyList<StrategyEvent> Events);
