using System.ComponentModel.DataAnnotations;
using EmaBot.Api.Auth;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
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
        if (request.RiskReward <= 0 || request.RiskReward > 1000 || request.FixedOrderSizeUsdt <= 0 || request.FixedOrderSizeUsdt > 10_000_000 || request.FeePercentPerSide is < 0 or > 5 || request.MinEmaGapPercent is < 0 or > 10 || request.MaxStopDistancePercent is < 0 or > 25 || request.SimulatedAccountBalanceUsdt <= 0 || request.MarginPerTradePercent is <= 0 or > 100 || request.Leverage is < 1 or > 125 || request.PaperFixedLots <= 0 || request.PaperMarginPerTradePercent is <= 0 or > 100 || request.PaperStartingBalance <= 0) return BadRequest(new ApiMessage("Trading settings are outside supported limits."));
        var settings = await settingsService.GetAsync(cancellationToken);
        settings.RiskReward = request.RiskReward;
        settings.FixedOrderSizeUsdt = request.FixedOrderSizeUsdt;
        settings.MinEmaGapPercent = request.MinEmaGapPercent;
        settings.MaxStopDistancePercent = request.MaxStopDistancePercent;
        settings.PositionSizingMode = request.PositionSizingMode;
        settings.SimulatedAccountBalanceUsdt = request.SimulatedAccountBalanceUsdt;
        settings.MarginPerTradePercent = request.MarginPerTradePercent;
        settings.Leverage = request.Leverage;
        settings.WaitForConfirmationCandle = request.WaitForConfirmationCandle;
        settings.UseEma100Filter = request.UseEma100Filter;
        settings.UseHtfRegimeFilter = request.UseHtfRegimeFilter;
        settings.TrailingStopEnabled = request.TrailingStopEnabled;
        settings.FeePercentPerSide = request.FeePercentPerSide;
        settings.PaperPositionSizingMode = request.PaperPositionSizingMode;
        settings.PaperFixedLots = request.PaperFixedLots;
        settings.PaperMarginPerTradePercent = request.PaperMarginPerTradePercent;
        settings.PaperStartingBalance = request.PaperStartingBalance;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(settings));
    }

    internal static TradingSettingsResponse ToResponse(TradingSettings settings) => new(settings.RiskReward, settings.FixedOrderSizeUsdt, settings.MinEmaGapPercent, settings.MaxStopDistancePercent, settings.PositionSizingMode.ToString(), settings.SimulatedAccountBalanceUsdt, settings.MarginPerTradePercent, settings.Leverage, settings.WaitForConfirmationCandle, settings.UseEma100Filter, settings.UseHtfRegimeFilter, settings.TrailingStopEnabled, settings.FeePercentPerSide, settings.PaperPositionSizingMode.ToString(), settings.PaperFixedLots, settings.PaperMarginPerTradePercent, settings.PaperStartingBalance, settings.UpdatedAtUtc);
}

public sealed record UpdateTradingSettingsRequest(decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool UseHtfRegimeFilter, bool TrailingStopEnabled, decimal FeePercentPerSide = 0.05m, decimal MinEmaGapPercent = 0.01m, decimal MaxStopDistancePercent = 0m, PositionSizingMode PositionSizingMode = PositionSizingMode.FixedNotional, decimal SimulatedAccountBalanceUsdt = 1000m, decimal MarginPerTradePercent = 10m, decimal Leverage = 5m, PaperPositionSizingMode PaperPositionSizingMode = PaperPositionSizingMode.FixedLots, decimal PaperFixedLots = .01m, decimal PaperMarginPerTradePercent = 10m, decimal PaperStartingBalance = 1000m);
public sealed record TradingSettingsResponse(decimal RiskReward, decimal FixedOrderSizeUsdt, decimal MinEmaGapPercent, decimal MaxStopDistancePercent, string PositionSizingMode, decimal SimulatedAccountBalanceUsdt, decimal MarginPerTradePercent, decimal Leverage, bool WaitForConfirmationCandle, bool UseEma100Filter, bool UseHtfRegimeFilter, bool TrailingStopEnabled, decimal FeePercentPerSide, string PaperPositionSizingMode, decimal PaperFixedLots, decimal PaperMarginPerTradePercent, decimal PaperStartingBalance, DateTimeOffset UpdatedAtUtc);
