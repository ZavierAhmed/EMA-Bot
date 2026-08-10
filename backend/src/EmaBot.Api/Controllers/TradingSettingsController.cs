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
        if (request.RiskReward <= 0 || request.RiskReward > 1000 || request.FixedOrderSizeUsdt <= 0 || request.FixedOrderSizeUsdt > 10_000_000) return BadRequest(new ApiMessage("Risk / reward and fixed order size must be positive and within supported limits."));
        var settings = await settingsService.GetAsync(cancellationToken);
        settings.RiskReward = request.RiskReward;
        settings.FixedOrderSizeUsdt = request.FixedOrderSizeUsdt;
        settings.WaitForConfirmationCandle = request.WaitForConfirmationCandle;
        settings.UseEma100Filter = request.UseEma100Filter;
        settings.TrailingStopEnabled = request.TrailingStopEnabled;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(settings));
    }

    internal static TradingSettingsResponse ToResponse(TradingSettings settings) => new(settings.RiskReward, settings.FixedOrderSizeUsdt, settings.WaitForConfirmationCandle, settings.UseEma100Filter, settings.TrailingStopEnabled, settings.UpdatedAtUtc);
}

public sealed record UpdateTradingSettingsRequest(decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled);
public sealed record TradingSettingsResponse(decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, DateTimeOffset UpdatedAtUtc);
