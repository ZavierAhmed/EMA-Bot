using EmaBot.Api.Strategy;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Models;

public enum BacktestRunStatus { Completed, Failed }
public enum StopSourceType { Pivot, FallbackLookback }
public enum BacktestExitReason { StopLoss, TakeProfit, EndOfData, TrailingStop }

public sealed class BacktestRun
{
    public int Id { get; set; }
    public required string Symbol { get; set; }
    public required string Interval { get; set; }
    public DateTimeOffset RequestedStartUtc { get; set; }
    public DateTimeOffset RequestedEndUtc { get; set; }
    public DateTimeOffset? ActualStartUtc { get; set; }
    public DateTimeOffset? ActualEndUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int CandleCount { get; set; }
    public decimal RiskReward { get; set; }
    public decimal FixedOrderSizeUsdt { get; set; }
    public bool WaitForConfirmationCandle { get; set; }
    public bool UseEma100Filter { get; set; }
    public bool TrailingStopEnabled { get; set; }
    public decimal FeePercentPerSide { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public int BreakEvenTrades { get; set; }
    public int LongTrades { get; set; }
    public int ShortTrades { get; set; }
    public decimal WinRatePercent { get; set; }
    public decimal GrossPnlUsdt { get; set; }
    public decimal NetPnlUsdt { get; set; }
    public decimal TotalFeesUsdt { get; set; }
    public decimal? ProfitFactor { get; set; }
    public decimal AverageNetPnlUsdt { get; set; }
    public decimal AverageRMultiple { get; set; }
    public decimal MaxDrawdownUsdt { get; set; }
    public int TotalCrossovers { get; set; }
    public int LongSignals { get; set; }
    public int ShortSignals { get; set; }
    public int RejectedByEma100 { get; set; }
    public int ConfirmationFailed { get; set; }
    public int InvalidStopLoss { get; set; }
    public int SkippedWhilePositionOpen { get; set; }
    public int NoEntryCandle { get; set; }
    public BacktestRunStatus Status { get; set; }
    public string? FailureMessage { get; set; }
    public List<BacktestTrade> Trades { get; set; } = [];
}

public sealed class BacktestTrade
{
    public int Id { get; set; }
    public int BacktestRunId { get; set; }
    [JsonIgnore]
    public BacktestRun? BacktestRun { get; set; }
    public SignalDirection Direction { get; set; }
    public DateTimeOffset CrossoverTimeUtc { get; set; }
    public DateTimeOffset SignalTimeUtc { get; set; }
    public DateTimeOffset EntryTimeUtc { get; set; }
    public DateTimeOffset ExitTimeUtc { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryNotionalUsdt { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal FinalStopLoss { get; set; }
    public StopSourceType StopSourceType { get; set; }
    public DateTimeOffset StopSourceTimeUtc { get; set; }
    public decimal OriginalTakeProfit { get; set; }
    public decimal FinalTakeProfit { get; set; }
    public bool TakeProfitExtended { get; set; }
    public BacktestExitReason ExitReason { get; set; }
    public bool SameCandleExitConflict { get; set; }
    public decimal EntryFeeUsdt { get; set; }
    public decimal ExitFeeUsdt { get; set; }
    public decimal TotalFeesUsdt { get; set; }
    public decimal GrossPnlUsdt { get; set; }
    public decimal NetPnlUsdt { get; set; }
    public decimal NetPnlPercent { get; set; }
    public decimal GrossRMultiple { get; set; }
    public decimal NetRMultiple { get; set; }
    public decimal MfePrice { get; set; }
    public decimal MfePercent { get; set; }
    public decimal MaePrice { get; set; }
    public decimal MaePercent { get; set; }
    public decimal SignalClose { get; set; }
    public decimal? SignalEma9 { get; set; }
    public decimal? SignalEma15 { get; set; }
    public decimal? SignalEma100 { get; set; }
    public decimal? SignalGapPercent { get; set; }
    public GapState SignalGapState { get; set; }
}
