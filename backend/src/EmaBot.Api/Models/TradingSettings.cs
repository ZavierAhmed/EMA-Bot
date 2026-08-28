using EmaBot.Api.Strategy;

namespace EmaBot.Api.Models;

public enum PaperPositionSizingMode { FixedLots, MarginPercent, RiskPercent }

public sealed class TradingSettings
{
    public const int GlobalId = 1;
    public int Id { get; set; } = GlobalId;
    public decimal RiskReward { get; set; } = 2m;
    public decimal FixedOrderSizeUsdt { get; set; } = 100m;
    public decimal MinEmaGapPercent { get; set; } = .01m;
    public decimal MaxStopDistancePercent { get; set; }
    public PositionSizingMode PositionSizingMode { get; set; } = PositionSizingMode.FixedNotional;
    public decimal SimulatedAccountBalanceUsdt { get; set; } = 1000m;
    public decimal MarginPerTradePercent { get; set; } = 10m;
    public decimal Leverage { get; set; } = 5m;
    public bool WaitForConfirmationCandle { get; set; } = true;
    public bool UseEma100Filter { get; set; }
    public bool UseHtfRegimeFilter { get; set; }
    public bool TrailingStopEnabled { get; set; }
    public bool UseAdaptiveInitialStop { get; set; }
    public bool SameTrendReentryEnabled { get; set; }
    public int MaxReentryAgeBars { get; set; } = 6;
    public bool ExitOnOppositeCrossover { get; set; }
    public decimal FeePercentPerSide { get; set; } = 0.05m;
    public PaperPositionSizingMode PaperPositionSizingMode { get; set; } = PaperPositionSizingMode.FixedLots;
    public decimal PaperFixedLots { get; set; } = .01m;
    public decimal PaperMarginPerTradePercent { get; set; } = 10m;
    public decimal PaperRiskPerTradePercent { get; set; } = 1m;
    public decimal PaperStartingBalance { get; set; } = 1000m;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
