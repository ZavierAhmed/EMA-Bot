namespace EmaBot.Api.Configuration;

public sealed class TradingDefaultsOptions
{
    public const string SectionName = "Trading";
    public decimal DefaultRiskReward { get; init; } = 2m;
    public decimal DefaultFixedOrderSizeUsdt { get; init; } = 100m;
    public bool WaitForConfirmationCandle { get; init; } = true;
    public bool UseEma100Filter { get; init; }
    public bool TrailingStopEnabled { get; init; }
    public decimal FeePercentPerSide { get; init; } = 0.05m;
}
