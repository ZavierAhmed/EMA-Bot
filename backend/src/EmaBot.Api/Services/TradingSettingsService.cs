using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed class TradingSettingsService(EmaBotDbContext database, IOptions<TradingDefaultsOptions> defaults)
{
    public async Task<TradingSettings> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await database.TradingSettings.SingleOrDefaultAsync(settings => settings.Id == TradingSettings.GlobalId, cancellationToken);
        if (settings is not null) return settings;
        settings = new TradingSettings
        {
            RiskReward = defaults.Value.DefaultRiskReward,
            FixedOrderSizeUsdt = defaults.Value.DefaultFixedOrderSizeUsdt,
            WaitForConfirmationCandle = defaults.Value.WaitForConfirmationCandle,
            UseEma100Filter = defaults.Value.UseEma100Filter,
            TrailingStopEnabled = defaults.Value.TrailingStopEnabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        database.TradingSettings.Add(settings);
        await database.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
