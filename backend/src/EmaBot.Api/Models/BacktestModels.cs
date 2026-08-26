using EmaBot.Api.Strategy;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Models;

public enum BacktestRunStatus { Completed, Failed }
public enum BacktestEconomicsMode { LegacyCompatibility, Mt5HistoricalBidAsk }
public enum StopSourceType { Pivot, FallbackLookback, AdaptiveSignalCandle, AdaptiveMicroStructure, AdaptiveLegacyFallback }
public enum ReversalPowerBand { Weak, Normal, Strong }
public enum BacktestExitReason { StopLoss, TakeProfit, EndOfData, TrailingStop, OppositeCrossover }
public enum BacktestTradeEventType { Entry, TrailingStopMoved, TakeProfitExtended, Exit }

public sealed class BacktestRun
{
    public int Id { get; set; }
    public MarketDataSource MarketDataSource { get; set; }
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
    public decimal MinEmaGapPercent { get; set; }
    public decimal MaxStopDistancePercent { get; set; }
    public PositionSizingMode PositionSizingMode { get; set; }
    public decimal StartingBalanceUsdt { get; set; }
    public decimal EndingBalanceUsdt { get; set; }
    public decimal MarginPerTradePercent { get; set; }
    public decimal Leverage { get; set; }
    public bool WaitForConfirmationCandle { get; set; }
    public bool UseEma100Filter { get; set; }
    public bool UseHtfRegimeFilter { get; set; }
    public bool TrailingStopEnabled { get; set; }
    public bool UseAdaptiveInitialStop { get; set; }
    public bool SameTrendReentryEnabled { get; set; }
    public int MaxReentryAgeBars { get; set; } = 6;
    public bool ExitOnOppositeCrossover { get; set; }
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
    public int RejectedByEmaGap { get; set; }
    public int RejectedByHtfRegime { get; set; }
    public int RejectedByStopDistance { get; set; }
    public int RejectedByFees { get; set; }
    public int ConfirmationFailed { get; set; }
    public int InvalidStopLoss { get; set; }
    public int SkippedWhilePositionOpen { get; set; }
    public int NoEntryCandle { get; set; }
    public BacktestRunStatus Status { get; set; }
    public string? FailureMessage { get; set; }
    // Nullable additive evidence keeps historical compatibility rows intact.
    public BacktestEconomicsMode? EconomicsMode { get; set; }
    public string? AccountCurrency { get; set; }
    public string? BrokerSymbol { get; set; }
    public string? HistoricalSpreadModel { get; set; }
    public string? HistoricalChartMode { get; set; }
    public decimal? CommissionPerLotPerSide { get; set; }
    public decimal? ContractSize { get; set; }
    public decimal? VolumeMin { get; set; }
    public decimal? VolumeMax { get; set; }
    public decimal? VolumeStep { get; set; }
    public decimal? VolumeLimit { get; set; }
    public decimal? PointSize { get; set; }
    public decimal? TickSize { get; set; }
    public decimal? TickValueProfit { get; set; }
    public decimal? TickValueLoss { get; set; }
    public int? StopsLevelPoints { get; set; }
    public string? TradeMode { get; set; }
    public decimal? StartingBalance { get; set; }
    public decimal? EndingBalance { get; set; }
    public decimal? GrossProfitFactor { get; set; }
    public decimal? NetProfitFactor { get; set; }
    public int RejectedByTradingCosts { get; set; }
    // Additive native-execution diagnostics. Existing rows retain their persisted zero values;
    // they are not retroactively interpreted from the historical combined skip counter.
    public int RejectedByInsufficientMargin { get; set; }
    public int RejectedByInvalidVolume { get; set; }
    public int RejectedByTradeMode { get; set; }
    public int Mt5EconomicsCallCount { get; set; }
    public long Mt5EconomicsElapsedMilliseconds { get; set; }
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
    public PositionSizingMode PositionSizingMode { get; set; }
    public decimal? AccountEquityAtEntryUsdt { get; set; }
    public decimal? MarginUsedUsdt { get; set; }
    public decimal? Leverage { get; set; }
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
    public decimal? SignalOpen { get; set; }
    public decimal? SignalEma9 { get; set; }
    public decimal? SignalEma15 { get; set; }
    public decimal? SignalEma100 { get; set; }
    public decimal? SignalGapPercent { get; set; }
    public string? HtfTimeframe { get; set; }
    public DateTimeOffset? SignalHtfCandleCloseTimeUtc { get; set; }
    public decimal? SignalHtfEma100Slope20Percent { get; set; }
    public decimal? SignalHtfAtr14Percent { get; set; }
    public GapState SignalGapState { get; set; }
    public bool IsReentry { get; set; }
    public DateTimeOffset? TrendRegimeCrossoverTimeUtc { get; set; }
    public int? ReentryAgeBars { get; set; }
    public bool UseAdaptiveInitialStop { get; set; }
    public decimal? SignalAtr14 { get; set; }
    public decimal? ReversalPowerScore { get; set; }
    public ReversalPowerBand? ReversalPowerBand { get; set; }
    public decimal? StopAnchorPrice { get; set; }
    public decimal? StopBuffer { get; set; }
    public decimal? Lots { get; set; }
    public decimal? EntryBid { get; set; }
    public decimal? EntryAsk { get; set; }
    public decimal? EntrySpread { get; set; }
    public decimal? ExitBid { get; set; }
    public decimal? ExitAsk { get; set; }
    public decimal? ExitSpread { get; set; }
    public decimal? RequiredMargin { get; set; }
    public decimal? MarginUsed { get; set; }
    public decimal? AccountEquityAtEntry { get; set; }
    public decimal? EntryCommission { get; set; }
    public decimal? ExitCommission { get; set; }
    public decimal? RoundTripCommission { get; set; }
    public decimal? GrossPnl { get; set; }
    public decimal? NetPnl { get; set; }
    public decimal? InitialRiskAmount { get; set; }
    public List<BacktestTradeEvent> Events { get; set; } = [];
}

public sealed class BacktestTradeEvent
{
    public int Id { get; set; }
    public int BacktestTradeId { get; set; }
    [JsonIgnore] public BacktestTrade? BacktestTrade { get; set; }
    public DateTimeOffset TimeUtc { get; set; }
    public DateTimeOffset? EffectiveTimeUtc { get; set; }
    public BacktestTradeEventType Type { get; set; }
    public decimal MarketPrice { get; set; }
    public decimal? OldStop { get; set; }
    public decimal? NewStop { get; set; }
    public decimal? OldTakeProfit { get; set; }
    public decimal? NewTakeProfit { get; set; }
    public decimal? ProgressPercent { get; set; }
}
