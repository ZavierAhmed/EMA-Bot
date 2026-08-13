using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

public enum TradeSource { Backtest, Paper }
public sealed record TradeSummaryResponse(TradeSource Source, int Id, int ParentId, string Symbol, string Interval, string Status, SignalDirection Direction, DateTimeOffset EntryTimeUtc, DateTimeOffset? ExitTimeUtc, decimal EntryPrice, decimal? ExitPrice, string? ExitReason, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal NetPnlPercent, decimal TotalFeesUsdt, decimal? GrossRMultiple, decimal? NetRMultiple);
public sealed record TradeEventResponse(DateTimeOffset TimeUtc, DateTimeOffset? EffectiveTimeUtc, string Type, decimal MarketPrice, decimal? OldStop, decimal? NewStop, decimal? OldTakeProfit, decimal? NewTakeProfit, decimal? ProgressPercent);
public sealed record TradeDetailResponse(TradeSummaryResponse Summary, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, decimal Quantity, decimal EntryNotionalUsdt, decimal InitialStopLoss, decimal FinalStopLoss, StopSourceType StopSourceType, DateTimeOffset StopSourceTimeUtc, decimal OriginalTakeProfit, decimal FinalTakeProfit, bool TakeProfitExtended, decimal EntryFeeUsdt, decimal? ExitFeeUsdt, decimal MfePrice, decimal MfePercent, decimal MaePrice, decimal MaePercent, decimal SignalClose, decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, GapState SignalGapState, decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide, IReadOnlyList<TradeEventResponse> Events, bool HasDetailedManagementHistory, decimal? SignalOpen, string PositionSizingMode, decimal? AccountEquityAtEntryUsdt, decimal? MarginUsedUsdt, decimal? Leverage, bool IsReentry, DateTimeOffset? TrendRegimeCrossoverTimeUtc, decimal MinEmaGapPercent);
public sealed record TradeChartCandleResponse(DateTimeOffset OpenTimeUtc, DateTimeOffset CloseTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
public sealed record TradeChartPointResponse(DateTimeOffset TimeUtc, decimal? Value);
public sealed record TradeChartResponse(string Symbol, string Interval, IReadOnlyList<TradeChartCandleResponse> Candles, IReadOnlyList<TradeChartPointResponse> Ema9, IReadOnlyList<TradeChartPointResponse> Ema15, IReadOnlyList<TradeChartPointResponse> Ema100);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/trades")]
public sealed class TradesController(EmaBotDbContext database, IHistoricalMarketDataProviderResolver historicalProviders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TradeSummaryResponse>>> List([FromQuery] string? source, [FromQuery] string? symbol, [FromQuery] string? interval, [FromQuery] string? direction, [FromQuery] string? status, [FromQuery] string? outcome, [FromQuery] int? limit, CancellationToken token)
    {
        if (!TrySource(source, out var requestedSource)) return BadRequest(new ApiMessage("Source must be All, Backtest, or Paper."));
        if (!TryDirection(direction, out var requestedDirection)) return BadRequest(new ApiMessage("Direction must be Long or Short."));
        if (!IsOneOf(status, "All", "Open", "Closed")) return BadRequest(new ApiMessage("Status must be All, Open, or Closed."));
        if (!IsOneOf(outcome, "All", "Win", "Loss", "BreakEven", "Open")) return BadRequest(new ApiMessage("Outcome must be All, Win, Loss, BreakEven, or Open."));
        var take = Math.Clamp(limit ?? 100, 1, 250); var normalized = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();
        var results = new List<TradeSummaryResponse>();
        if (requestedSource is null or TradeSource.Backtest)
        {
            var query = database.BacktestTrades.AsNoTracking().Include(trade => trade.BacktestRun).AsQueryable();
            if (normalized is not null) query = query.Where(trade => trade.BacktestRun!.Symbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(trade => trade.BacktestRun!.Interval == interval);
            if (requestedDirection is not null) query = query.Where(trade => trade.Direction == requestedDirection);
            if (!string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)) results.AddRange((await query.ToListAsync(token)).Select(BacktestSummary));
        }
        if (requestedSource is null or TradeSource.Paper)
        {
            var query = database.PaperTrades.AsNoTracking().Include(trade => trade.PaperSession).AsQueryable();
            if (normalized is not null) query = query.Where(trade => trade.Symbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(trade => trade.Interval == interval);
            if (requestedDirection is not null) query = query.Where(trade => trade.Direction == requestedDirection);
            if (string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)) query = query.Where(trade => trade.Status == PaperTradeStatus.Open);
            if (string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase)) query = query.Where(trade => trade.Status == PaperTradeStatus.Closed);
            results.AddRange((await query.ToListAsync(token)).Select(PaperSummary));
        }
        if (!string.IsNullOrWhiteSpace(outcome) && !string.Equals(outcome, "All", StringComparison.OrdinalIgnoreCase)) results = results.Where(item => MatchesOutcome(item, outcome)).ToList();
        return Ok(results.OrderByDescending(item => item.EntryTimeUtc).Take(take).ToArray());
    }

    [HttpGet("{source}/{id:int}")]
    public async Task<ActionResult<TradeDetailResponse>> Detail(string source, int id, CancellationToken token)
    {
        if (!TrySource(source, out var requested) || requested is null) return BadRequest(new ApiMessage("Source must be backtest or paper."));
        if (requested == TradeSource.Backtest)
        {
            var trade = await database.BacktestTrades.AsNoTracking().Include(item => item.BacktestRun).Include(item => item.Events).SingleOrDefaultAsync(item => item.Id == id, token);
            return trade is null ? NotFound(new ApiMessage("Trade not found.")) : Ok(BacktestDetail(trade));
        }
        var paper = await database.PaperTrades.AsNoTracking().Include(item => item.PaperSession).Include(item => item.Events).SingleOrDefaultAsync(item => item.Id == id, token);
        return paper is null ? NotFound(new ApiMessage("Trade not found.")) : Ok(PaperDetail(paper));
    }

    [HttpGet("{source}/{id:int}/chart")]
    public async Task<ActionResult<TradeChartResponse>> Chart(string source, int id, CancellationToken token)
    {
        if (!TrySource(source, out var requested) || requested is null) return BadRequest(new ApiMessage("Source must be backtest or paper."));
        var identity = requested == TradeSource.Backtest
            ? await database.BacktestTrades.AsNoTracking().Include(item => item.BacktestRun).Where(item => item.Id == id).Select(item => new ChartIdentity(item.BacktestRun!.Symbol, item.BacktestRun.Interval, item.CrossoverTimeUtc, item.ExitTimeUtc, item.BacktestRun.MarketDataSource)).SingleOrDefaultAsync(token)
            : await database.PaperTrades.AsNoTracking().Include(item => item.PaperSession).Where(item => item.Id == id).Select(item => new ChartIdentity(item.Symbol, item.Interval, item.CrossoverTimeUtc, item.ExitTimeUtc, item.PaperSession!.MarketDataSource)).SingleOrDefaultAsync(token);
        if (identity is null) return NotFound(new ApiMessage("Trade not found."));
        var visibleStart = StrategyTimeframes.Shift(identity.CrossoverTimeUtc, identity.Interval, -120);
        var warmupStart = StrategyTimeframes.Shift(visibleStart, identity.Interval, -120);
        var visibleEnd = identity.ExitTimeUtc is { } exit ? StrategyTimeframes.Shift(exit, identity.Interval, 30) : DateTimeOffset.UtcNow;
        if (visibleEnd <= warmupStart) return BadRequest(new ApiMessage("Trade chart window is invalid."));
        try
        {
            var historical = historicalProviders.Resolve(identity.MarketDataSource);
            var candles = (await historical.GetRangeAsync(identity.Symbol, identity.Interval, warmupStart, visibleEnd, token)).Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
            var visible = candles.Where(candle => candle.OpenTimeUtc >= visibleStart).ToArray();
            if (visible.Length > 5000) return BadRequest(new ApiMessage("The requested trade chart exceeds 5,000 candles."));
            var closes = candles.Select(candle => candle.Close).ToArray();
            var ema9 = EmaCalculator.Calculate(closes, 9); var ema15 = EmaCalculator.Calculate(closes, 15); var ema100 = EmaCalculator.Calculate(closes, 100);
            var indices = candles.Select((candle, index) => (candle, index)).Where(item => item.candle.OpenTimeUtc >= visibleStart).ToArray();
            return Ok(new TradeChartResponse(identity.Symbol, identity.Interval, visible.Select(candle => new TradeChartCandleResponse(candle.OpenTimeUtc, candle.CloseTimeUtc, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume)).ToArray(), indices.Select(item => new TradeChartPointResponse(item.candle.OpenTimeUtc, ema9[item.index])).ToArray(), indices.Select(item => new TradeChartPointResponse(item.candle.OpenTimeUtc, ema15[item.index])).ToArray(), indices.Select(item => new TradeChartPointResponse(item.candle.OpenTimeUtc, ema100[item.index])).ToArray()));
        }
        catch (MarketDataProviderException exception) when (exception.Kind == MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status504GatewayTimeout, new ApiMessage("Chart data request timed out. Retry the chart.")); }
        catch (MarketDataProviderException) { return StatusCode(502, new ApiMessage("Chart data is currently unavailable.")); }
    }

    private sealed record ChartIdentity(string Symbol, string Interval, DateTimeOffset CrossoverTimeUtc, DateTimeOffset? ExitTimeUtc, MarketDataSource MarketDataSource);
    private static bool TrySource(string? value, out TradeSource? source) { source = null; if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)) return true; if (string.Equals(value, "Backtest", StringComparison.OrdinalIgnoreCase)) { source = TradeSource.Backtest; return true; } if (string.Equals(value, "Paper", StringComparison.OrdinalIgnoreCase)) { source = TradeSource.Paper; return true; } return false; }
    private static bool TryDirection(string? value, out SignalDirection? direction) { direction = null; if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)) return true; if (string.Equals(value, "Long", StringComparison.OrdinalIgnoreCase)) { direction = SignalDirection.Long; return true; } if (string.Equals(value, "Short", StringComparison.OrdinalIgnoreCase)) { direction = SignalDirection.Short; return true; } return false; }
    private static bool IsOneOf(string? value, params string[] allowed) => string.IsNullOrWhiteSpace(value) || allowed.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
    private static bool MatchesOutcome(TradeSummaryResponse item, string outcome) => outcome.ToLowerInvariant() switch { "open" => item.Status == "Open", "win" => item.NetPnlUsdt > 0, "loss" => item.NetPnlUsdt < 0, "breakeven" or "break-even" => item.Status == "Closed" && item.NetPnlUsdt == 0, _ => true };
    private static TradeSummaryResponse BacktestSummary(BacktestTrade trade) => new(TradeSource.Backtest, trade.Id, trade.BacktestRunId, trade.BacktestRun!.Symbol, trade.BacktestRun.Interval, "Closed", trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.ExitReason.ToString(), trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.TotalFeesUsdt, trade.GrossRMultiple, trade.NetRMultiple);
    private static TradeSummaryResponse PaperSummary(PaperTrade trade) { var risk = Math.Abs(trade.EntryPrice - trade.InitialStopLoss) * trade.Quantity; return new(TradeSource.Paper, trade.Id, trade.PaperSessionId, trade.Symbol, trade.Interval, trade.Status.ToString(), trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.ExitReason?.ToString(), trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.TotalFeesUsdt, risk == 0 ? null : trade.GrossPnlUsdt / risk, risk == 0 ? null : trade.NetPnlUsdt / risk); }
    private static TradeDetailResponse BacktestDetail(BacktestTrade trade) { var run = trade.BacktestRun!; var events = trade.Events.OrderBy(item => item.TimeUtc).Select(item => new TradeEventResponse(item.TimeUtc, item.EffectiveTimeUtc, item.Type.ToString(), item.MarketPrice, item.OldStop, item.NewStop, item.OldTakeProfit, item.NewTakeProfit, item.ProgressPercent)).ToArray(); return new(BacktestSummary(trade), trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.Quantity, trade.EntryNotionalUsdt, trade.InitialStopLoss, trade.FinalStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit, trade.TakeProfitExtended, trade.EntryFeeUsdt, trade.ExitFeeUsdt, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent, trade.SignalClose, trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState, run.RiskReward, run.FixedOrderSizeUsdt, run.WaitForConfirmationCandle, run.UseEma100Filter, run.TrailingStopEnabled, run.FeePercentPerSide, events, events.Length > 0, trade.SignalOpen, trade.PositionSizingMode.ToString(), trade.AccountEquityAtEntryUsdt, trade.MarginUsedUsdt, trade.Leverage, trade.IsReentry, trade.TrendRegimeCrossoverTimeUtc, run.MinEmaGapPercent); }
    private static TradeDetailResponse PaperDetail(PaperTrade trade) { var session = trade.PaperSession!; var events = trade.Events.OrderBy(item => item.TimeUtc).Select(item => new TradeEventResponse(item.TimeUtc, item.TimeUtc, item.Type.ToString(), item.MarketPrice, item.OldStop, item.NewStop, item.OldTakeProfit, item.NewTakeProfit, item.ProgressPercent)).ToArray(); return new(PaperSummary(trade), trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.Quantity, trade.EntryNotionalUsdt, trade.InitialStopLoss, trade.FinalStopLoss ?? trade.CurrentStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit ?? trade.CurrentTakeProfit, trade.TakeProfitExtended, trade.EntryFeeUsdt, trade.ExitFeeUsdt, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent, trade.SignalClose, trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState, session.RiskReward, session.FixedOrderSizeUsdt, session.WaitForConfirmationCandle, session.UseEma100Filter, session.TrailingStopEnabled, session.FeePercentPerSide, events, true, trade.SignalOpen, trade.PositionSizingMode.ToString(), trade.AccountEquityAtEntryUsdt, trade.MarginUsedUsdt, trade.Leverage, trade.IsReentry, trade.TrendRegimeCrossoverTimeUtc, session.MinEmaGapPercent); }
}
