using System.ComponentModel.DataAnnotations;
using EmaBot.Api.Auth;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/settings/trading")]
public sealed class TradingSettingsController(TradingSettingsService settingsService, EmaBot.Api.Data.EmaBotDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TradingSettingsResponse>> Get(CancellationToken cancellationToken) => Ok(ToResponse(await settingsService.GetAsync(cancellationToken)));

    [HttpPut]
    public async Task<ActionResult<TradingSettingsResponse>> Put(UpdateTradingSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request.RiskReward <= 0 || request.RiskReward > 1000 || request.FixedOrderSizeUsdt <= 0 || request.FixedOrderSizeUsdt > 10_000_000 || request.FeePercentPerSide is < 0 or > 5) return BadRequest(new ApiMessage("Trading settings are outside supported limits."));
        var settings = await settingsService.GetAsync(cancellationToken);
        settings.RiskReward = request.RiskReward;
        settings.FixedOrderSizeUsdt = request.FixedOrderSizeUsdt;
        settings.WaitForConfirmationCandle = request.WaitForConfirmationCandle;
        settings.UseEma100Filter = request.UseEma100Filter;
        settings.TrailingStopEnabled = request.TrailingStopEnabled;
        settings.FeePercentPerSide = request.FeePercentPerSide;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(settings));
    }

    internal static TradingSettingsResponse ToResponse(TradingSettings settings) => new(settings.RiskReward, settings.FixedOrderSizeUsdt, settings.WaitForConfirmationCandle, settings.UseEma100Filter, settings.TrailingStopEnabled, settings.FeePercentPerSide, settings.UpdatedAtUtc);
}

public sealed record UpdateTradingSettingsRequest(decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide = 0.05m);
public sealed record TradingSettingsResponse(decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide, DateTimeOffset UpdatedAtUtc);
