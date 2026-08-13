using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/strategy")]
public sealed class StrategyController(EmaBotDbContext database, IHistoricalMarketDataProvider historical, TradingSettingsService settingsService, EmaSignalEngine engine) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult<StrategyPreviewResponse>> Preview([FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!Mt5NativeTimeframes.IsSupported(interval)) return BadRequest(new ApiMessage("Unsupported MT5 timeframe. The 3d timeframe is not available for MT5 research."));
        if (limit is < 100 or > 1500) return BadRequest(new ApiMessage("Limit must be between 100 and 1500 for an EMA 100 preview."));
        var normalized = (symbol ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return BadRequest(new ApiMessage("A monitored instrument is required."));
        try
        {
            if (!await database.MonitoredSymbols.AnyAsync(contract => contract.Source == MarketDataSource.Mt5Exness && contract.Symbol == normalized && contract.IsEnabled, cancellationToken)) return BadRequest(new ApiMessage("The exact MT5 instrument must be monitored and enabled."));
            var candles = await historical.GetLatestAsync(normalized, interval, limit ?? 300, cancellationToken);
            var evaluation = engine.Evaluate(candles, await settingsService.GetAsync(cancellationToken));
            var latest = evaluation.Snapshots.LastOrDefault(snapshot => snapshot.Ema100.HasValue);
            if (latest is null) return BadRequest(new ApiMessage("Not enough closed candles were returned for EMA 100."));
            return Ok(new StrategyPreviewResponse(normalized, interval, latest.Time, latest.Close, latest.Ema9, latest.Ema15, latest.Ema100, latest.TrendDirection, latest.GapPercent, latest.GapState, evaluation.Events.TakeLast(20).ToArray()));
        }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException exception) { return StatusCode(exception.Kind == MarketDataErrorKind.RateLimited ? StatusCodes.Status429TooManyRequests : exception.Kind == MarketDataErrorKind.Timeout ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status503ServiceUnavailable, new ApiMessage(exception.Message.Contains("history", StringComparison.OrdinalIgnoreCase) ? "MT5 history is still loading. Retry shortly." : "MT5 historical market data is currently unavailable.")); }
    }
}

public sealed record StrategyPreviewResponse(string Symbol, string Interval, DateTimeOffset LatestClosedCandleTime, decimal LatestClose, decimal? Ema9, decimal? Ema15, decimal? Ema100, TrendDirection TrendDirection, decimal? GapPercent, GapState GapState, IReadOnlyList<StrategyEvent> Events);
