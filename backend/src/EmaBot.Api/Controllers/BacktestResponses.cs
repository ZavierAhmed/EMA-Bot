using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Controllers;

// Public contracts deliberately contain scalar data only; EF entities remain internal to the API.
public sealed record BacktestRunSummaryResponse(
    int Id, string Symbol, string Interval, DateTimeOffset RequestedStartUtc, DateTimeOffset RequestedEndUtc,
    DateTimeOffset? ActualStartUtc, DateTimeOffset? ActualEndUtc, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc, int CandleCount, decimal RiskReward, decimal FixedOrderSizeUsdt,
    bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide,
    int TotalTrades, int WinningTrades, int LosingTrades, int BreakEvenTrades, int LongTrades, int ShortTrades,
    decimal WinRatePercent, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal TotalFeesUsdt, decimal? ProfitFactor,
    decimal AverageNetPnlUsdt, decimal AverageRMultiple, decimal MaxDrawdownUsdt, int TotalCrossovers,
    int LongSignals, int ShortSignals, int RejectedByEma100, int ConfirmationFailed, int InvalidStopLoss,
    int SkippedWhilePositionOpen, int NoEntryCandle, BacktestRunStatus Status, string? FailureMessage);

public sealed record BacktestRunDetailResponse(
    int Id, string Symbol, string Interval, DateTimeOffset RequestedStartUtc, DateTimeOffset RequestedEndUtc,
    DateTimeOffset? ActualStartUtc, DateTimeOffset? ActualEndUtc, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc, int CandleCount, decimal RiskReward, decimal FixedOrderSizeUsdt,
    bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide,
    int TotalTrades, int WinningTrades, int LosingTrades, int BreakEvenTrades, int LongTrades, int ShortTrades,
    decimal WinRatePercent, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal TotalFeesUsdt, decimal? ProfitFactor,
    decimal AverageNetPnlUsdt, decimal AverageRMultiple, decimal MaxDrawdownUsdt, int TotalCrossovers,
    int LongSignals, int ShortSignals, int RejectedByEma100, int ConfirmationFailed, int InvalidStopLoss,
    int SkippedWhilePositionOpen, int NoEntryCandle, BacktestRunStatus Status, string? FailureMessage,
    IReadOnlyList<BacktestTradeResponse> Trades);

public sealed record BacktestTradeResponse(
    int Id, int BacktestRunId, SignalDirection Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc,
    DateTimeOffset EntryTimeUtc, DateTimeOffset ExitTimeUtc, decimal EntryPrice, decimal ExitPrice, decimal Quantity,
    decimal EntryNotionalUsdt, decimal InitialStopLoss, decimal FinalStopLoss, StopSourceType StopSourceType,
    DateTimeOffset StopSourceTimeUtc, decimal OriginalTakeProfit, decimal FinalTakeProfit, bool TakeProfitExtended,
    BacktestExitReason ExitReason, bool SameCandleExitConflict, decimal EntryFeeUsdt, decimal ExitFeeUsdt,
    decimal TotalFeesUsdt, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal NetPnlPercent, decimal GrossRMultiple,
    decimal NetRMultiple, decimal MfePrice, decimal MfePercent, decimal MaePrice, decimal MaePercent, decimal SignalClose,
    decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, GapState SignalGapState);

public static class BacktestResponseMapper
{
    public static BacktestRunSummaryResponse ToSummary(BacktestRun run) => new(
        run.Id, run.Symbol, run.Interval, run.RequestedStartUtc, run.RequestedEndUtc, run.ActualStartUtc, run.ActualEndUtc,
        run.CreatedAtUtc, run.CompletedAtUtc, run.CandleCount, run.RiskReward, run.FixedOrderSizeUsdt,
        run.WaitForConfirmationCandle, run.UseEma100Filter, run.TrailingStopEnabled, run.FeePercentPerSide,
        run.TotalTrades, run.WinningTrades, run.LosingTrades, run.BreakEvenTrades, run.LongTrades, run.ShortTrades,
        run.WinRatePercent, run.GrossPnlUsdt, run.NetPnlUsdt, run.TotalFeesUsdt, run.ProfitFactor,
        run.AverageNetPnlUsdt, run.AverageRMultiple, run.MaxDrawdownUsdt, run.TotalCrossovers, run.LongSignals,
        run.ShortSignals, run.RejectedByEma100, run.ConfirmationFailed, run.InvalidStopLoss, run.SkippedWhilePositionOpen,
        run.NoEntryCandle, run.Status, run.FailureMessage);

    public static BacktestRunDetailResponse ToDetail(BacktestRun run)
    {
        var summary = ToSummary(run);
        return new(summary.Id, summary.Symbol, summary.Interval, summary.RequestedStartUtc, summary.RequestedEndUtc,
            summary.ActualStartUtc, summary.ActualEndUtc, summary.CreatedAtUtc, summary.CompletedAtUtc, summary.CandleCount,
            summary.RiskReward, summary.FixedOrderSizeUsdt, summary.WaitForConfirmationCandle, summary.UseEma100Filter,
            summary.TrailingStopEnabled, summary.FeePercentPerSide, summary.TotalTrades, summary.WinningTrades,
            summary.LosingTrades, summary.BreakEvenTrades, summary.LongTrades, summary.ShortTrades, summary.WinRatePercent,
            summary.GrossPnlUsdt, summary.NetPnlUsdt, summary.TotalFeesUsdt, summary.ProfitFactor, summary.AverageNetPnlUsdt,
            summary.AverageRMultiple, summary.MaxDrawdownUsdt, summary.TotalCrossovers, summary.LongSignals,
            summary.ShortSignals, summary.RejectedByEma100, summary.ConfirmationFailed, summary.InvalidStopLoss,
            summary.SkippedWhilePositionOpen, summary.NoEntryCandle, summary.Status, summary.FailureMessage,
            run.Trades.OrderBy(trade => trade.EntryTimeUtc).Select(ToTrade).ToArray());
    }

    private static BacktestTradeResponse ToTrade(BacktestTrade trade) => new(
        trade.Id, trade.BacktestRunId, trade.Direction, trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.EntryTimeUtc,
        trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.Quantity, trade.EntryNotionalUsdt, trade.InitialStopLoss,
        trade.FinalStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit,
        trade.TakeProfitExtended, trade.ExitReason, trade.SameCandleExitConflict, trade.EntryFeeUsdt, trade.ExitFeeUsdt,
        trade.TotalFeesUsdt, trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.GrossRMultiple,
        trade.NetRMultiple, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent, trade.SignalClose,
        trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState);
}
