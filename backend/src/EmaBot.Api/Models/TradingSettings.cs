namespace EmaBot.Api.Models;

public sealed class TradingSettings
{
    public const int GlobalId = 1;
    public int Id { get; set; } = GlobalId;
    public decimal RiskReward { get; set; } = 2m;
    public decimal FixedOrderSizeUsdt { get; set; } = 100m;
    public bool WaitForConfirmationCandle { get; set; } = true;
    public bool UseEma100Filter { get; set; }
    public bool TrailingStopEnabled { get; set; }
    public decimal FeePercentPerSide { get; set; } = 0.05m;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
