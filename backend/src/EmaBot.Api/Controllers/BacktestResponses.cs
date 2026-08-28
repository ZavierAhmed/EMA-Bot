using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Controllers;

// Public contracts deliberately contain scalar data only; EF entities remain internal to the API.
public sealed record BacktestRunSummaryResponse(
    int Id, string Symbol, MarketDataSource MarketDataSource, string MarketDataSourceLabel, string Interval, DateTimeOffset RequestedStartUtc, DateTimeOffset RequestedEndUtc,
    DateTimeOffset? ActualStartUtc, DateTimeOffset? ActualEndUtc, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc, int CandleCount, decimal RiskReward, decimal FixedOrderSizeUsdt,
    bool WaitForConfirmationCandle, bool UseEma100Filter, bool UseHtfRegimeFilter, bool TrailingStopEnabled, bool UseAdaptiveInitialStop, bool SameTrendReentryEnabled, int MaxReentryAgeBars, bool ExitOnOppositeCrossover, decimal FeePercentPerSide,
    int TotalTrades, int WinningTrades, int LosingTrades, int BreakEvenTrades, int LongTrades, int ShortTrades,
    decimal WinRatePercent, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal TotalFeesUsdt, decimal? ProfitFactor,
    decimal AverageNetPnlUsdt, decimal AverageRMultiple, decimal MaxDrawdownUsdt, int TotalCrossovers,
    int LongSignals, int ShortSignals, int RejectedByEma100, int RejectedByHtfRegime, int ConfirmationFailed, int InvalidStopLoss,
    int SkippedWhilePositionOpen, int NoEntryCandle, BacktestRunStatus Status, string? FailureMessage,
    BacktestEconomicsMode? EconomicsMode = null, string? AccountCurrency = null, string? BrokerSymbol = null, string? HistoricalSpreadModel = null, string? HistoricalChartMode = null, decimal? CommissionPerLotPerSide = null, decimal? StartingBalance = null, decimal? EndingBalance = null, decimal? GrossProfitFactor = null, decimal? NetProfitFactor = null, int RejectedByTradingCosts = 0, int Mt5EconomicsCallCount = 0, long Mt5EconomicsElapsedMilliseconds = 0, int RejectedByInsufficientMargin = 0, int RejectedByInvalidVolume = 0, int RejectedByTradeMode = 0, PaperPositionSizingMode? NativePositionSizingMode = null, decimal? NativeFixedLots = null, decimal? NativeMarginPerTradePercent = null, decimal? NativeRiskPerTradePercent = null, int? RejectedByRiskBelowMinimumVolume = null);

public sealed record BacktestRunDetailResponse(
    int Id, string Symbol, MarketDataSource MarketDataSource, string MarketDataSourceLabel, string Interval, DateTimeOffset RequestedStartUtc, DateTimeOffset RequestedEndUtc,
    DateTimeOffset? ActualStartUtc, DateTimeOffset? ActualEndUtc, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc, int CandleCount, decimal RiskReward, decimal FixedOrderSizeUsdt,
    bool WaitForConfirmationCandle, bool UseEma100Filter, bool UseHtfRegimeFilter, bool TrailingStopEnabled, bool UseAdaptiveInitialStop, bool SameTrendReentryEnabled, int MaxReentryAgeBars, bool ExitOnOppositeCrossover, decimal FeePercentPerSide,
    int TotalTrades, int WinningTrades, int LosingTrades, int BreakEvenTrades, int LongTrades, int ShortTrades,
    decimal WinRatePercent, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal TotalFeesUsdt, decimal? ProfitFactor,
    decimal AverageNetPnlUsdt, decimal AverageRMultiple, decimal MaxDrawdownUsdt, int TotalCrossovers,
    int LongSignals, int ShortSignals, int RejectedByEma100, int RejectedByHtfRegime, int ConfirmationFailed, int InvalidStopLoss,
    int SkippedWhilePositionOpen, int NoEntryCandle, BacktestRunStatus Status, string? FailureMessage,
    IReadOnlyList<BacktestTradeResponse> Trades,
    BacktestEconomicsMode? EconomicsMode = null, string? AccountCurrency = null, string? BrokerSymbol = null, string? HistoricalSpreadModel = null, string? HistoricalChartMode = null, decimal? CommissionPerLotPerSide = null, decimal? StartingBalance = null, decimal? EndingBalance = null, decimal? GrossProfitFactor = null, decimal? NetProfitFactor = null, int RejectedByTradingCosts = 0, int Mt5EconomicsCallCount = 0, long Mt5EconomicsElapsedMilliseconds = 0, int RejectedByInsufficientMargin = 0, int RejectedByInvalidVolume = 0, int RejectedByTradeMode = 0, PaperPositionSizingMode? NativePositionSizingMode = null, decimal? NativeFixedLots = null, decimal? NativeMarginPerTradePercent = null, decimal? NativeRiskPerTradePercent = null, int? RejectedByRiskBelowMinimumVolume = null);

public sealed record BacktestTradeResponse(
    int Id, int BacktestRunId, SignalDirection Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc,
    DateTimeOffset EntryTimeUtc, DateTimeOffset ExitTimeUtc, decimal EntryPrice, decimal ExitPrice, decimal Quantity,
    decimal EntryNotionalUsdt, decimal InitialStopLoss, decimal FinalStopLoss, StopSourceType StopSourceType,
    DateTimeOffset StopSourceTimeUtc, decimal OriginalTakeProfit, decimal FinalTakeProfit, bool TakeProfitExtended,
    BacktestExitReason ExitReason, bool SameCandleExitConflict, decimal EntryFeeUsdt, decimal ExitFeeUsdt,
    decimal TotalFeesUsdt, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal NetPnlPercent, decimal GrossRMultiple,
    decimal NetRMultiple, decimal MfePrice, decimal MfePercent, decimal MaePrice, decimal MaePercent, decimal SignalClose,
    decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, GapState SignalGapState,
    string? HtfTimeframe, DateTimeOffset? SignalHtfCandleCloseTimeUtc, decimal? SignalHtfEma100Slope20Percent, decimal? SignalHtfAtr14Percent,
    decimal? Lots = null, decimal? EntryBid = null, decimal? EntryAsk = null, decimal? EntrySpread = null, decimal? ExitBid = null, decimal? ExitAsk = null, decimal? ExitSpread = null, decimal? RequiredMargin = null, decimal? MarginUsed = null, decimal? AccountEquityAtEntry = null, decimal? EntryCommission = null, decimal? ExitCommission = null, decimal? RoundTripCommission = null, decimal? GrossPnl = null, decimal? NetPnl = null, decimal? InitialRiskAmount = null, PaperPositionSizingMode? NativePositionSizingMode = null, decimal? TargetRiskPercent = null, decimal? TargetRiskAmount = null, decimal? ActualInitialRiskPercent = null);

public static class BacktestResponseMapper
{
    public static BacktestRunSummaryResponse ToSummary(BacktestRun run) => new(
        run.Id, run.Symbol, run.MarketDataSource, MarketDataSourceLabels.For(run.MarketDataSource), run.Interval, run.RequestedStartUtc, run.RequestedEndUtc, run.ActualStartUtc, run.ActualEndUtc,
        run.CreatedAtUtc, run.CompletedAtUtc, run.CandleCount, run.RiskReward, run.FixedOrderSizeUsdt,
        run.WaitForConfirmationCandle, run.UseEma100Filter, run.UseHtfRegimeFilter, run.TrailingStopEnabled, run.UseAdaptiveInitialStop, run.SameTrendReentryEnabled, run.MaxReentryAgeBars, run.ExitOnOppositeCrossover, run.FeePercentPerSide,
        run.TotalTrades, run.WinningTrades, run.LosingTrades, run.BreakEvenTrades, run.LongTrades, run.ShortTrades,
        run.WinRatePercent, run.GrossPnlUsdt, run.NetPnlUsdt, run.TotalFeesUsdt, run.ProfitFactor,
        run.AverageNetPnlUsdt, run.AverageRMultiple, run.MaxDrawdownUsdt, run.TotalCrossovers, run.LongSignals,
        run.ShortSignals, run.RejectedByEma100, run.RejectedByHtfRegime, run.ConfirmationFailed, run.InvalidStopLoss, run.SkippedWhilePositionOpen,
        run.NoEntryCandle, run.Status, run.FailureMessage, run.EconomicsMode, run.AccountCurrency, run.BrokerSymbol, run.HistoricalSpreadModel, run.HistoricalChartMode, run.CommissionPerLotPerSide, run.StartingBalance, run.EndingBalance, run.GrossProfitFactor, run.NetProfitFactor, run.RejectedByTradingCosts, run.Mt5EconomicsCallCount, run.Mt5EconomicsElapsedMilliseconds, run.RejectedByInsufficientMargin, run.RejectedByInvalidVolume, run.RejectedByTradeMode, run.NativePositionSizingMode, run.NativeFixedLots, run.NativeMarginPerTradePercent, run.NativeRiskPerTradePercent, run.RejectedByRiskBelowMinimumVolume);

    public static BacktestRunDetailResponse ToDetail(BacktestRun run)
    {
        var summary = ToSummary(run);
        return new(summary.Id, summary.Symbol, summary.MarketDataSource, summary.MarketDataSourceLabel, summary.Interval, summary.RequestedStartUtc, summary.RequestedEndUtc,
            summary.ActualStartUtc, summary.ActualEndUtc, summary.CreatedAtUtc, summary.CompletedAtUtc, summary.CandleCount,
            summary.RiskReward, summary.FixedOrderSizeUsdt, summary.WaitForConfirmationCandle, summary.UseEma100Filter, summary.UseHtfRegimeFilter,
            summary.TrailingStopEnabled, summary.UseAdaptiveInitialStop, summary.SameTrendReentryEnabled, summary.MaxReentryAgeBars, summary.ExitOnOppositeCrossover, summary.FeePercentPerSide, summary.TotalTrades, summary.WinningTrades,
            summary.LosingTrades, summary.BreakEvenTrades, summary.LongTrades, summary.ShortTrades, summary.WinRatePercent,
            summary.GrossPnlUsdt, summary.NetPnlUsdt, summary.TotalFeesUsdt, summary.ProfitFactor, summary.AverageNetPnlUsdt,
            summary.AverageRMultiple, summary.MaxDrawdownUsdt, summary.TotalCrossovers, summary.LongSignals,
            summary.ShortSignals, summary.RejectedByEma100, summary.RejectedByHtfRegime, summary.ConfirmationFailed, summary.InvalidStopLoss,
            summary.SkippedWhilePositionOpen, summary.NoEntryCandle, summary.Status, summary.FailureMessage,
            run.Trades.OrderBy(trade => trade.EntryTimeUtc).Select(ToTrade).ToArray(), summary.EconomicsMode, summary.AccountCurrency, summary.BrokerSymbol, summary.HistoricalSpreadModel, summary.HistoricalChartMode, summary.CommissionPerLotPerSide, summary.StartingBalance, summary.EndingBalance, summary.GrossProfitFactor, summary.NetProfitFactor, summary.RejectedByTradingCosts, summary.Mt5EconomicsCallCount, summary.Mt5EconomicsElapsedMilliseconds, summary.RejectedByInsufficientMargin, summary.RejectedByInvalidVolume, summary.RejectedByTradeMode, summary.NativePositionSizingMode, summary.NativeFixedLots, summary.NativeMarginPerTradePercent, summary.NativeRiskPerTradePercent, summary.RejectedByRiskBelowMinimumVolume);
    }

    private static BacktestTradeResponse ToTrade(BacktestTrade trade) => new(
        trade.Id, trade.BacktestRunId, trade.Direction, trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.EntryTimeUtc,
        trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.Quantity, trade.EntryNotionalUsdt, trade.InitialStopLoss,
        trade.FinalStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit,
        trade.TakeProfitExtended, trade.ExitReason, trade.SameCandleExitConflict, trade.EntryFeeUsdt, trade.ExitFeeUsdt,
        trade.TotalFeesUsdt, trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.GrossRMultiple,
        trade.NetRMultiple, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent, trade.SignalClose,
        trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState,
        trade.HtfTimeframe, trade.SignalHtfCandleCloseTimeUtc, trade.SignalHtfEma100Slope20Percent, trade.SignalHtfAtr14Percent, trade.Lots, trade.EntryBid, trade.EntryAsk, trade.EntrySpread, trade.ExitBid, trade.ExitAsk, trade.ExitSpread, trade.RequiredMargin, trade.MarginUsed, trade.AccountEquityAtEntry, trade.EntryCommission, trade.ExitCommission, trade.RoundTripCommission, trade.GrossPnl, trade.NetPnl, trade.InitialRiskAmount, trade.NativePositionSizingMode, trade.TargetRiskPercent, trade.TargetRiskAmount, trade.ActualInitialRiskPercent);
}
