using EmaBot.Api.Strategy;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Models;

public enum PaperSessionStatus { Running, Interrupted, Stopped, Faulted }
public enum PaperTradeStatus { Open, Closed }
public enum PaperExitReason { InitialStopLoss, TrailingStop, TakeProfit, SessionStopped }
public enum PaperTradeEventType { Entry, TrailingStopMoved, TakeProfitExtended, Exit }

public sealed class PaperSession
{
    public int Id { get; set; }
    public required string Interval { get; set; }
    public PaperSessionStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? StoppedAtUtc { get; set; }
    public DateTimeOffset? InterruptedAtUtc { get; set; }
    public string? FailureMessage { get; set; }
    public decimal RiskReward { get; set; }
    public decimal FixedOrderSizeUsdt { get; set; }
    public decimal MinEmaGapPercent { get; set; }
    public decimal MaxStopDistancePercent { get; set; }
    public PositionSizingMode PositionSizingMode { get; set; }
    public decimal StartingBalanceUsdt { get; set; }
    public decimal CurrentBalanceUsdt { get; set; }
    public decimal MarginPerTradePercent { get; set; }
    public decimal Leverage { get; set; }
    public decimal UsedMarginUsdt { get; set; }
    public bool WaitForConfirmationCandle { get; set; }
    public bool UseEma100Filter { get; set; }
    public bool TrailingStopEnabled { get; set; }
    public decimal FeePercentPerSide { get; set; }
    public int TotalCrossovers { get; set; }
    public int LongSignals { get; set; }
    public int ShortSignals { get; set; }
    public int RejectedByEma100 { get; set; }
    public int RejectedByEmaGap { get; set; }
    public int RejectedByStopDistance { get; set; }
    public int RejectedByFees { get; set; }
    public int RejectedByInsufficientMargin { get; set; }
    public int ConfirmationFailed { get; set; }
    public int InvalidStopLoss { get; set; }
    public int SkippedWhilePositionOpen { get; set; }
    public int CompletedTrades { get; set; }
    public decimal NetPnlUsdt { get; set; }
    public decimal TotalFeesUsdt { get; set; }
    public List<PaperSessionSymbol> Symbols { get; set; } = [];
    public List<PaperTrade> Trades { get; set; } = [];
}

public sealed class PaperSessionSymbol
{
    public int Id { get; set; }
    public int PaperSessionId { get; set; }
    [JsonIgnore] public PaperSession? PaperSession { get; set; }
    public required string Symbol { get; set; }
    public DateTimeOffset? LastProcessedClosedCandleUtc { get; set; }
    public DateTimeOffset? LastMarketEventUtc { get; set; }
    public decimal? LastKnownPrice { get; set; }
    public SignalDirection? PendingDirection { get; set; }
    public DateTimeOffset? PendingCrossoverTimeUtc { get; set; }
    public DateTimeOffset? PendingSignalTimeUtc { get; set; }
    public decimal? PendingStopPrice { get; set; }
    public StopSourceType? PendingStopSourceType { get; set; }
    public DateTimeOffset? PendingStopSourceTimeUtc { get; set; }
    public decimal? PendingSignalClose { get; set; }
    public decimal? PendingSignalOpen { get; set; }
    public decimal? PendingSignalEma9 { get; set; }
    public decimal? PendingSignalEma15 { get; set; }
    public decimal? PendingSignalEma100 { get; set; }
    public decimal? PendingSignalGapPercent { get; set; }
    public GapState? PendingSignalGapState { get; set; }
    public List<PaperTrade> Trades { get; set; } = [];
}

public sealed class PaperTrade
{
    public int Id { get; set; }
    public int PaperSessionId { get; set; }
    [JsonIgnore] public PaperSession? PaperSession { get; set; }
    public int PaperSessionSymbolId { get; set; }
    [JsonIgnore] public PaperSessionSymbol? PaperSessionSymbol { get; set; }
    public required string Symbol { get; set; }
    public required string Interval { get; set; }
    public PaperTradeStatus Status { get; set; }
    public SignalDirection Direction { get; set; }
    public DateTimeOffset CrossoverTimeUtc { get; set; }
    public DateTimeOffset SignalTimeUtc { get; set; }
    public DateTimeOffset EntryTimeUtc { get; set; }
    public DateTimeOffset? ExitTimeUtc { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryNotionalUsdt { get; set; }
    public PositionSizingMode PositionSizingMode { get; set; }
    public decimal? AccountEquityAtEntryUsdt { get; set; }
    public decimal? MarginUsedUsdt { get; set; }
    public decimal? Leverage { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal CurrentStopLoss { get; set; }
    public decimal? FinalStopLoss { get; set; }
    public StopSourceType StopSourceType { get; set; }
    public DateTimeOffset StopSourceTimeUtc { get; set; }
    public decimal OriginalTakeProfit { get; set; }
    public decimal CurrentTakeProfit { get; set; }
    public decimal? FinalTakeProfit { get; set; }
    public bool TakeProfitExtended { get; set; }
    public decimal BestFavorableProgressPercent { get; set; }
    public decimal EntryFeeUsdt { get; set; }
    public decimal? ExitFeeUsdt { get; set; }
    public decimal TotalFeesUsdt { get; set; }
    public decimal GrossPnlUsdt { get; set; }
    public decimal NetPnlUsdt { get; set; }
    public decimal NetPnlPercent { get; set; }
    public decimal MfePrice { get; set; }
    public decimal MfePercent { get; set; }
    public decimal MaePrice { get; set; }
    public decimal MaePercent { get; set; }
    public decimal SignalClose { get; set; }
    public decimal? SignalOpen { get; set; }
    public decimal? SignalEma9 { get; set; }
    public decimal? SignalEma15 { get; set; }
    public decimal? SignalEma100 { get; set; }
    public decimal? SignalGapPercent { get; set; }
    public GapState SignalGapState { get; set; }
    public bool IsReentry { get; set; }
    public DateTimeOffset? TrendRegimeCrossoverTimeUtc { get; set; }
    public PaperExitReason? ExitReason { get; set; }
    public List<PaperTradeEvent> Events { get; set; } = [];
}

public sealed class PaperTradeEvent
{
    public int Id { get; set; }
    public int PaperTradeId { get; set; }
    [JsonIgnore] public PaperTrade? PaperTrade { get; set; }
    public DateTimeOffset TimeUtc { get; set; }
    public PaperTradeEventType Type { get; set; }
    public decimal MarketPrice { get; set; }
    public decimal? OldStop { get; set; }
    public decimal? NewStop { get; set; }
    public decimal? OldTakeProfit { get; set; }
    public decimal? NewTakeProfit { get; set; }
    public decimal? ProgressPercent { get; set; }
}
