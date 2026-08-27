using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

public enum TradeSource { Backtest, Paper, ExnessDemo }
public sealed record TradeSummaryResponse(TradeSource Source, int Id, int ParentId, string Symbol, string Interval, string Status, SignalDirection Direction, DateTimeOffset EntryTimeUtc, DateTimeOffset? ExitTimeUtc, decimal? EntryPrice, decimal? ExitPrice, string? ExitReason, decimal? GrossPnlUsdt, decimal? NetPnlUsdt, decimal? NetPnlPercent, decimal? TotalFeesUsdt, decimal? GrossRMultiple, decimal? NetRMultiple)
{
    public MarketDataSource MarketDataSource { get; init; } = MarketDataSource.LegacyBinance;
    public string AccountCurrency { get; init; } = "USDT";
    public decimal? GrossPnl { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? TradingCosts { get; init; }
    public string PnlPercentBasis { get; init; } = "EntryNotional";
    public decimal? PnlPercentOnMargin { get; init; }
    public decimal? AccountReturnPercent { get; init; }
    public int? SessionId { get; init; }
    public int? IntentId { get; init; }
    public bool? IsReentry { get; init; }
    public DateTimeOffset? CrossoverTimeUtc { get; init; }
    public DateTimeOffset? SignalTimeUtc { get; init; }
    public DateTimeOffset? ExpectedEntryOpenUtc { get; init; }
    public DateTimeOffset? BrokerExecutedAtUtc { get; init; }
    public decimal? AverageFillPrice { get; init; }
    public decimal? FilledVolumeLots { get; init; }
    public DateTimeOffset? BrokerClosedAtUtc { get; init; }
    public decimal? AverageClosePrice { get; init; }
    public string? NativeExitReason { get; init; }
    public bool PnlEvidenceAvailable { get; init; }
    public string? PnlEvidenceType { get; init; }
    public string? PnlEvidenceReason { get; init; }
    public string? ReconciliationSource { get; init; }
    public string? ReconciliationNote { get; init; }
}
public sealed record TradeEventResponse(DateTimeOffset TimeUtc, DateTimeOffset? EffectiveTimeUtc, string Type, decimal MarketPrice, decimal? OldStop, decimal? NewStop, decimal? OldTakeProfit, decimal? NewTakeProfit, decimal? ProgressPercent);
public sealed record TradeDetailResponse(TradeSummaryResponse Summary, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, decimal Quantity, decimal EntryNotionalUsdt, decimal InitialStopLoss, decimal FinalStopLoss, StopSourceType StopSourceType, DateTimeOffset StopSourceTimeUtc, decimal OriginalTakeProfit, decimal FinalTakeProfit, bool TakeProfitExtended, decimal EntryFeeUsdt, decimal? ExitFeeUsdt, decimal MfePrice, decimal MfePercent, decimal MaePrice, decimal MaePercent, decimal SignalClose, decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, GapState SignalGapState, decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide, IReadOnlyList<TradeEventResponse> Events, bool HasDetailedManagementHistory, decimal? SignalOpen, string PositionSizingMode, decimal? AccountEquityAtEntryUsdt, decimal? MarginUsedUsdt, decimal? Leverage, bool IsReentry, DateTimeOffset? TrendRegimeCrossoverTimeUtc, decimal MinEmaGapPercent)
{
    public decimal? Lots { get; init; }
    public decimal? RequiredMargin { get; init; }
    public decimal? MarginUsed { get; init; }
    public decimal? AccountEquityAtEntry { get; init; }
    public decimal? RoundTripCommission { get; init; }
    public decimal? EntryBid { get; init; }
    public decimal? EntryAsk { get; init; }
    public decimal? EntrySpread { get; init; }
    public decimal? ExitBid { get; init; }
    public decimal? ExitAsk { get; init; }
    public decimal? ExitSpread { get; init; }
    public string? PaperPositionSizingMode { get; init; }
    public bool UseAdaptiveInitialStop { get; init; }
    public decimal? SignalAtr14 { get; init; }
    public decimal? ReversalPowerScore { get; init; }
    public string? ReversalPowerBand { get; init; }
    public decimal? StopAnchorPrice { get; init; }
    public decimal? StopBuffer { get; init; }
    public decimal? InitialRiskAmount { get; init; }
    public BacktestEconomicsMode? EconomicsMode { get; init; }
    public string? NativePositionSizingMode { get; init; }
}
public sealed record TradeChartCandleResponse(DateTimeOffset OpenTimeUtc, DateTimeOffset CloseTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
public sealed record TradeChartPointResponse(DateTimeOffset TimeUtc, decimal? Value);
public sealed record TradeChartResponse(string Symbol, string Interval, IReadOnlyList<TradeChartCandleResponse> Candles, IReadOnlyList<TradeChartPointResponse> Ema9, IReadOnlyList<TradeChartPointResponse> Ema15, IReadOnlyList<TradeChartPointResponse> Ema100);
public sealed record ExnessDemoTradeSummaryResponse(TradeSource Source, int Id, int ParentId, int SessionId, int IntentId, string Symbol, string Interval, string Direction, bool IsReentry, string Status, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, DateTimeOffset ExpectedEntryOpenUtc, DateTimeOffset? BrokerExecutedAtUtc, decimal? AverageFillPrice, decimal? FilledVolumeLots, DateTimeOffset? BrokerClosedAtUtc, decimal? AverageClosePrice, string? NativeExitReason, string? AccountCurrency, decimal? EvaluatedBrokerPnl, string? PnlEvidenceType, bool PnlEvidenceAvailable, string? PnlEvidenceReason, string? ReconciliationSource, string? ReconciliationNote);
public sealed record ExnessDemoTradeDetailResponse(ExnessDemoTradeSummaryResponse Summary, ExnessDemoTradeIntentResponse Intent, ExnessDemoTradeSessionSnapshotResponse Session, ExnessDemoTradeExecutionResponse Execution, ExnessDemoTradePnlResponse BrokerPnl, IReadOnlyList<DemoStrategyManagementResponse> Management, IReadOnlyList<DemoExecutionManagementActionResponse> ManagementActions);
public sealed record ExnessDemoTradeIntentResponse(int SessionId, int IntentId, int ExecutionId, Guid ClientExecutionId, string EntryType, string Direction, string Status, string? Reason, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, DateTimeOffset ExpectedEntryOpenUtc, decimal SignalOpen, decimal SignalClose, decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, string SignalGapState, decimal StructuralStopLoss, string StopSourceType, DateTimeOffset StopSourceTimeUtc, decimal? IntendedTakeProfit, decimal IntendedVolumeLots, bool IsReentry, int? ReentrySourceDemoExecutionId, DateTimeOffset? TrendRegimeCrossoverTimeUtc, int? ReentryAgeBars);
public sealed record ExnessDemoTradeSessionSnapshotResponse(decimal InitialAllocation, bool AutomationEnabledAtCreation, decimal FixedLots, decimal RiskReward, bool WaitForConfirmationCandle, bool UseEma100Filter, decimal MinEmaGapPercent, decimal MaxStopDistancePercent, bool UseAdaptiveInitialStop, bool TrailingStopEnabled, bool ExitOnOppositeCrossover, bool SameTrendReentryEnabled, int MaxReentryAgeBars);
public sealed record ExnessDemoTradeExecutionResponse(string State, string Provider, string BrokerSymbol, string Side, decimal VolumeLots, decimal? FilledVolumeLots, decimal? AverageFillPrice, decimal? ClosedVolumeLots, decimal? AverageClosePrice, decimal? RequestedStopLoss, decimal? RequestedTakeProfit, decimal? CurrentStopLoss, decimal? CurrentTakeProfit, DateTimeOffset? ProtectionObservedAtUtc, long MagicNumber, long? PositionTicket, long? PositionIdentifier, long? OrderTicket, long? EntryDealTicket, long? ExitDealTicket, string? NativeExitReason, bool NativeExitReasonConflicted, string? BrokerRetcode, string? BrokerMessage, DateTimeOffset CreatedAtUtc, DateTimeOffset? PreflightAtUtc, DateTimeOffset? SubmittedAtUtc, DateTimeOffset? BrokerAcceptedAtUtc, DateTimeOffset? BrokerExecutedAtUtc, DateTimeOffset? BrokerClosedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset? ReconciledAtUtc, string? ReconciliationSource, string? ReconciliationNote);
public sealed record ExnessDemoTradePnlResponse(string? BrokerAccountCurrency, decimal? BrokerEntryProfit, decimal? BrokerEntryCommission, decimal? BrokerEntrySwap, decimal? BrokerEntryFee, DateTimeOffset? BrokerEntryPnlObservedAtUtc, decimal? BrokerCurrentProfit, decimal? BrokerCurrentSwap, DateTimeOffset? BrokerCurrentPnlObservedAtUtc, decimal? BrokerHistoryProfit, decimal? BrokerHistoryCommission, decimal? BrokerHistorySwap, decimal? BrokerHistoryFee, DateTimeOffset? BrokerHistoryPnlObservedAtUtc, bool EvaluatedAvailable, decimal? EvaluatedAmount, string? EvaluatedEvidenceType, string EvaluatedReason);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/trades")]
public sealed class TradesController(EmaBotDbContext database, IHistoricalMarketDataProviderResolver historicalProviders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TradeSummaryResponse>>> List([FromQuery] string? source, [FromQuery] string? symbol, [FromQuery] string? interval, [FromQuery] string? direction, [FromQuery] string? status, [FromQuery] string? outcome, [FromQuery] int? limit, CancellationToken token, [FromQuery] int? sessionId = null)
    {
        if (!TrySource(source, out var requestedSource)) return BadRequest(new ApiMessage("Source must be All, Backtest, Paper, or ExnessDemo."));
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
        var demoResults = new List<TradeSummaryResponse>();
        if (requestedSource is null or TradeSource.ExnessDemo)
        {
            var query = database.DemoStrategyIntents.AsNoTracking().Include(item => item.DemoStrategySession).Include(item => item.DemoStrategySessionSymbol).Include(item => item.DemoExecution).Where(item => item.DemoExecutionId != null).AsQueryable();
            if (sessionId is not null) query = query.Where(item => item.DemoStrategySessionId == sessionId);
            if (normalized is not null) query = query.Where(item => item.DemoStrategySessionSymbol!.BrokerSymbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(item => item.DemoStrategySession!.Interval == interval);
            if (requestedDirection is not null) query = query.Where(item => item.Direction == requestedDirection);
            var items = await query.ToListAsync(token);
            demoResults.AddRange(items.Select(DemoTradeSummary));
            if (string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase)) demoResults = demoResults.Where(item => item.Status == DemoExecutionState.Closed.ToString()).ToList();
            if (string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)) demoResults = demoResults.Where(item => item.Status is "Open" or "PartiallyFilled" or "BrokerAccepted" or "CloseRequested").ToList();
        }
        if (!string.IsNullOrWhiteSpace(outcome) && !string.Equals(outcome, "All", StringComparison.OrdinalIgnoreCase)) results = results.Where(item => MatchesOutcome(item, outcome)).ToList();
        results.AddRange(demoResults.Where(item => string.IsNullOrWhiteSpace(outcome) || string.Equals(outcome, "All", StringComparison.OrdinalIgnoreCase) || MatchesOutcome(item, outcome)));
        return Ok(results.OrderByDescending(item => item.EntryTimeUtc).Take(take).ToArray());
    }

    [HttpGet("{source}/{id:int}")]
    public async Task<ActionResult<TradeDetailResponse>> Detail(string source, int id, CancellationToken token)
    {
        if (!TrySource(source, out var requested) || requested is null || requested == TradeSource.ExnessDemo) return BadRequest(new ApiMessage("Source must be backtest or paper."));
        if (requested == TradeSource.Backtest)
        {
            var trade = await database.BacktestTrades.AsNoTracking().Include(item => item.BacktestRun).Include(item => item.Events).SingleOrDefaultAsync(item => item.Id == id, token);
            return trade is null ? NotFound(new ApiMessage("Trade not found.")) : Ok(BacktestDetail(trade));
        }
        var paper = await database.PaperTrades.AsNoTracking().Include(item => item.PaperSession).Include(item => item.Events).SingleOrDefaultAsync(item => item.Id == id, token);
        return paper is null ? NotFound(new ApiMessage("Trade not found.")) : Ok(PaperDetail(paper));
    }
    [HttpGet("exnessdemo/{id:int}")]
    public async Task<ActionResult<ExnessDemoTradeDetailResponse>> ExnessDemoDetail(int id, CancellationToken token)
    {
        var intent = await database.DemoStrategyIntents.AsNoTracking().Include(item => item.DemoStrategySession).Include(item => item.DemoStrategySessionSymbol).Include(item => item.DemoExecution).ThenInclude(item => item!.ManagementActions).SingleOrDefaultAsync(item => item.DemoExecutionId == id, token);
        if (intent?.DemoExecution is null || intent.DemoStrategySession is null || intent.DemoStrategySessionSymbol is null) return NotFound(new ApiMessage("Trade not found."));
        var management = await database.DemoStrategyPositionManagement.AsNoTracking().Where(item => item.DemoStrategySessionId == intent.DemoStrategySessionId && item.DemoExecutionId == id).OrderBy(item => item.Id).ToListAsync(token);
        return Ok(DemoDetail(intent, management));
    }

    [HttpGet("{source}/{id:int}/chart")]
    public async Task<ActionResult<TradeChartResponse>> Chart(string source, int id, CancellationToken token)
    {
        if (!TrySource(source, out var requested) || requested is null) return BadRequest(new ApiMessage("Source must be backtest, paper, or exnessdemo."));
        var identity = requested == TradeSource.Backtest
            ? await database.BacktestTrades.AsNoTracking().Include(item => item.BacktestRun).Where(item => item.Id == id).Select(item => new ChartIdentity(item.BacktestRun!.Symbol, item.BacktestRun.Interval, item.CrossoverTimeUtc, item.ExitTimeUtc, item.BacktestRun.MarketDataSource)).SingleOrDefaultAsync(token)
            : requested == TradeSource.Paper
                ? await database.PaperTrades.AsNoTracking().Include(item => item.PaperSession).Where(item => item.Id == id).Select(item => new ChartIdentity(item.Symbol, item.Interval, item.CrossoverTimeUtc, item.ExitTimeUtc, item.PaperSession!.MarketDataSource)).SingleOrDefaultAsync(token)
                : await database.DemoStrategyIntents.AsNoTracking().Include(item => item.DemoStrategySession).Include(item => item.DemoStrategySessionSymbol).Include(item => item.DemoExecution).Where(item => item.DemoExecutionId == id).Select(item => new ChartIdentity(item.DemoStrategySessionSymbol!.BrokerSymbol, item.DemoStrategySession!.Interval, item.CrossoverTimeUtc, item.DemoExecution!.BrokerClosedAtUtc ?? item.DemoExecution.ClosedAtUtc, MarketDataSource.Mt5Exness)).SingleOrDefaultAsync(token);
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
    private static bool TrySource(string? value, out TradeSource? source) { source = null; if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)) return true; if (string.Equals(value, "Backtest", StringComparison.OrdinalIgnoreCase)) { source = TradeSource.Backtest; return true; } if (string.Equals(value, "Paper", StringComparison.OrdinalIgnoreCase)) { source = TradeSource.Paper; return true; } if (string.Equals(value, "ExnessDemo", StringComparison.OrdinalIgnoreCase)) { source = TradeSource.ExnessDemo; return true; } return false; }
    private static bool TryDirection(string? value, out SignalDirection? direction) { direction = null; if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)) return true; if (string.Equals(value, "Long", StringComparison.OrdinalIgnoreCase)) { direction = SignalDirection.Long; return true; } if (string.Equals(value, "Short", StringComparison.OrdinalIgnoreCase)) { direction = SignalDirection.Short; return true; } return false; }
    private static bool IsOneOf(string? value, params string[] allowed) => string.IsNullOrWhiteSpace(value) || allowed.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
    private static bool MatchesOutcome(TradeSummaryResponse item, string outcome) => outcome.ToLowerInvariant() switch { "open" => item.Status is "Open" or "PartiallyFilled" or "BrokerAccepted" or "CloseRequested", "win" => item.Status == "Closed" && item.NetPnl is > 0m, "loss" => item.Status == "Closed" && item.NetPnl is < 0m, "breakeven" or "break-even" => item.Status == "Closed" && item.NetPnl == 0m, _ => true };
    private static TradeSummaryResponse BacktestSummary(BacktestTrade trade) => new(TradeSource.Backtest, trade.Id, trade.BacktestRunId, trade.BacktestRun!.Symbol, trade.BacktestRun.Interval, "Closed", trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.ExitReason.ToString(), trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.TotalFeesUsdt, trade.GrossRMultiple, trade.NetRMultiple) { MarketDataSource = trade.BacktestRun.MarketDataSource, GrossPnl = trade.GrossPnlUsdt, NetPnl = trade.NetPnlUsdt, TradingCosts = trade.TotalFeesUsdt };
    private static TradeSummaryResponse PaperSummary(PaperTrade trade)
    {
        var session = trade.PaperSession!;
        if (session.MarketDataSource == MarketDataSource.Mt5Exness)
        {
            var marginPercent = PaperAccounting.PnlPercentOnMargin(trade.NetPnl, trade.MarginUsed);
            return new(TradeSource.Paper, trade.Id, trade.PaperSessionId, trade.Symbol, trade.Interval, trade.Status.ToString(), trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.ExitReason?.ToString(), trade.GrossPnlUsdt, trade.NetPnlUsdt, marginPercent ?? 0m, trade.TotalFeesUsdt, null, null) { MarketDataSource = session.MarketDataSource, AccountCurrency = session.AccountCurrency, GrossPnl = trade.GrossPnl ?? 0m, NetPnl = trade.NetPnl ?? 0m, TradingCosts = trade.RoundTripCommission ?? 0m, PnlPercentBasis = "MarginUsed", PnlPercentOnMargin = marginPercent, AccountReturnPercent = PaperAccounting.AccountReturnPercent(trade.NetPnl, trade.AccountEquityAtEntry) };
        }
        var risk = Math.Abs(trade.EntryPrice - trade.InitialStopLoss) * trade.Quantity;
        return new(TradeSource.Paper, trade.Id, trade.PaperSessionId, trade.Symbol, trade.Interval, trade.Status.ToString(), trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.ExitReason?.ToString(), trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.TotalFeesUsdt, risk == 0 ? null : trade.GrossPnlUsdt / risk, risk == 0 ? null : trade.NetPnlUsdt / risk) { MarketDataSource = session.MarketDataSource, GrossPnl = trade.GrossPnlUsdt, NetPnl = trade.NetPnlUsdt, TradingCosts = trade.TotalFeesUsdt };
    }
    private static ExnessDemoTradeSummaryResponse DemoSummary(DemoStrategyIntent intent)
    {
        var execution = intent.DemoExecution!;
        var session = intent.DemoStrategySession!;
        var symbol = intent.DemoStrategySessionSymbol!;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);
        return new(TradeSource.ExnessDemo, execution.Id, session.Id, session.Id, intent.Id, symbol.BrokerSymbol, session.Interval, intent.Direction.ToString(), intent.IsReentry, execution.State.ToString(), intent.CrossoverTimeUtc, intent.SignalTimeUtc, intent.ExpectedEntryOpenUtc, execution.BrokerExecutedAtUtc, execution.AverageFillPrice, execution.FilledVolumeLots, execution.BrokerClosedAtUtc ?? execution.ClosedAtUtc, execution.AverageClosePrice, execution.NativeExitReason, evidence.Available ? evidence.AccountCurrency : execution.BrokerAccountCurrency, evidence.Available ? evidence.Amount : null, evidence.Available ? (execution.State == DemoExecutionState.Closed ? "Closed broker-history evidence" : "Open broker evidence") : null, evidence.Available, evidence.Reason, execution.ReconciliationSource, execution.ReconciliationNote);
    }
    private static TradeSummaryResponse DemoTradeSummary(DemoStrategyIntent intent)
    {
        var summary = DemoSummary(intent);
        return new(summary.Source, summary.Id, summary.ParentId, summary.Symbol, summary.Interval, summary.Status, Enum.Parse<SignalDirection>(summary.Direction), summary.ExpectedEntryOpenUtc, summary.BrokerClosedAtUtc, summary.AverageFillPrice, summary.AverageClosePrice, summary.NativeExitReason, summary.EvaluatedBrokerPnl, summary.EvaluatedBrokerPnl, null, null, null, null)
        {
            MarketDataSource = MarketDataSource.Mt5Exness,
            AccountCurrency = summary.AccountCurrency ?? string.Empty,
            GrossPnl = summary.EvaluatedBrokerPnl,
            NetPnl = summary.EvaluatedBrokerPnl,
            TradingCosts = null,
            PnlPercentBasis = "BrokerEvidence",
            SessionId = summary.SessionId,
            IntentId = summary.IntentId,
            IsReentry = summary.IsReentry,
            CrossoverTimeUtc = summary.CrossoverTimeUtc,
            SignalTimeUtc = summary.SignalTimeUtc,
            ExpectedEntryOpenUtc = summary.ExpectedEntryOpenUtc,
            BrokerExecutedAtUtc = summary.BrokerExecutedAtUtc,
            AverageFillPrice = summary.AverageFillPrice,
            FilledVolumeLots = summary.FilledVolumeLots,
            BrokerClosedAtUtc = summary.BrokerClosedAtUtc,
            AverageClosePrice = summary.AverageClosePrice,
            NativeExitReason = summary.NativeExitReason,
            PnlEvidenceAvailable = summary.PnlEvidenceAvailable,
            PnlEvidenceType = summary.PnlEvidenceType,
            PnlEvidenceReason = summary.PnlEvidenceReason,
            ReconciliationSource = summary.ReconciliationSource,
            ReconciliationNote = summary.ReconciliationNote
        };
    }
    private static ExnessDemoTradeDetailResponse DemoDetail(DemoStrategyIntent intent, IReadOnlyList<DemoStrategyPositionManagement> management)
    {
        var execution = intent.DemoExecution!;
        var session = intent.DemoStrategySession!;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);
        var actions = execution.ManagementActions.OrderBy(item => item.Id).Select(Action).ToArray();
        return new(DemoSummary(intent), new(session.Id, intent.Id, execution.Id, intent.ClientExecutionId, intent.IsReentry ? "Re-entry" : "Normal Entry", intent.Direction.ToString(), intent.Status.ToString(), intent.Reason, intent.CrossoverTimeUtc, intent.SignalTimeUtc, intent.ExpectedEntryOpenUtc, intent.SignalOpen, intent.SignalClose, intent.SignalEma9, intent.SignalEma15, intent.SignalEma100, intent.SignalGapPercent, intent.SignalGapState.ToString(), intent.StructuralStopLoss, intent.StopSourceType.ToString(), intent.StopSourceTimeUtc, intent.IntendedTakeProfit, intent.IntendedVolumeLots, intent.IsReentry, intent.ReentrySourceDemoExecutionId, intent.TrendRegimeCrossoverTimeUtc, intent.ReentryAgeBars), new(session.InitialAllocation, session.AutomationEnabledAtCreation, session.FixedLots, session.RiskReward, session.WaitForConfirmationCandle, session.UseEma100Filter, session.MinEmaGapPercent, session.MaxStopDistancePercent, session.UseAdaptiveInitialStop, session.TrailingStopEnabled, session.ExitOnOppositeCrossover, session.SameTrendReentryEnabled, session.MaxReentryAgeBars), new(execution.State.ToString(), execution.Provider, execution.BrokerSymbol, execution.Side, execution.VolumeLots, execution.FilledVolumeLots, execution.AverageFillPrice, execution.ClosedVolumeLots, execution.AverageClosePrice, execution.RequestedStopLoss, execution.RequestedTakeProfit, execution.CurrentStopLoss, execution.CurrentTakeProfit, execution.ProtectionObservedAtUtc, execution.MagicNumber, execution.PositionTicket, execution.PositionIdentifier, execution.OrderTicket, execution.EntryDealTicket, execution.ExitDealTicket, execution.NativeExitReason, execution.NativeExitReasonConflicted, execution.BrokerRetcode, execution.BrokerMessage, execution.CreatedAtUtc, execution.PreflightAtUtc, execution.SubmittedAtUtc, execution.BrokerAcceptedAtUtc, execution.BrokerExecutedAtUtc, execution.BrokerClosedAtUtc, execution.ClosedAtUtc, execution.ReconciledAtUtc, execution.ReconciliationSource, execution.ReconciliationNote), new(execution.BrokerAccountCurrency, execution.BrokerEntryProfit, execution.BrokerEntryCommission, execution.BrokerEntrySwap, execution.BrokerEntryFee, execution.BrokerEntryPnlObservedAtUtc, execution.BrokerCurrentProfit, execution.BrokerCurrentSwap, execution.BrokerCurrentPnlObservedAtUtc, execution.BrokerHistoryProfit, execution.BrokerHistoryCommission, execution.BrokerHistorySwap, execution.BrokerHistoryFee, execution.BrokerHistoryPnlObservedAtUtc, evidence.Available, evidence.Available ? evidence.Amount : null, evidence.Available ? (execution.State == DemoExecutionState.Closed ? "Closed broker-history evidence" : "Open broker evidence") : null, evidence.Reason), management.Select(Management).ToArray(), actions);
    }
    private static DemoStrategyManagementResponse Management(DemoStrategyPositionManagement item) => new(item.Id, item.DemoStrategyIntentId, item.DemoExecutionId, item.State.ToString(), item.OriginalEntryPrice, item.OriginalStopLoss, item.OriginalTakeProfit, item.BestFavorablePrice, item.BestFavorableProgressPercent, item.TakeProfitExtensionState.ToString(), item.TargetExtensionAppliedAtUtc, item.HighestAttemptedLockPercent, item.HighestAppliedLockPercent, item.PendingProtectionActionId, item.PendingProtectionLockPercent, item.PendingProtectionExtendsTarget, item.PendingDesiredStopLoss, item.PendingDesiredTakeProfit, item.OppositeSignalTimeUtc, item.OppositeSignalDirection, item.OppositeCloseState.ToString(), item.OppositeCloseRequestedAtUtc, item.LastManagedAtUtc, item.LastReason, item.CreatedAtUtc, item.UpdatedAtUtc);
    private static DemoExecutionManagementActionResponse Action(DemoExecutionManagementAction item) => new(item.Id, item.ClientManagementActionId, item.Kind.ToString(), item.State.ToString(), item.RequestedStopLoss, item.RequestedTakeProfit, item.ObservedBeforeStopLoss, item.ObservedBeforeTakeProfit, item.AppliedStopLoss, item.AppliedTakeProfit, item.BrokerRetcode, item.BrokerMessage, item.CreatedAtUtc, item.SubmittedAtUtc, item.CompletedAtUtc, item.ReconciledAtUtc, item.ReconciliationNote, item.ReconciliationSource);
    private static TradeDetailResponse BacktestDetail(BacktestTrade trade) { var run = trade.BacktestRun!; var events = trade.Events.OrderBy(item => item.TimeUtc).Select(item => new TradeEventResponse(item.TimeUtc, item.EffectiveTimeUtc, item.Type.ToString(), item.MarketPrice, item.OldStop, item.NewStop, item.OldTakeProfit, item.NewTakeProfit, item.ProgressPercent)).ToArray(); return new(BacktestSummary(trade), trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.Quantity, trade.EntryNotionalUsdt, trade.InitialStopLoss, trade.FinalStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit, trade.TakeProfitExtended, trade.EntryFeeUsdt, trade.ExitFeeUsdt, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent, trade.SignalClose, trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState, run.RiskReward, run.FixedOrderSizeUsdt, run.WaitForConfirmationCandle, run.UseEma100Filter, run.TrailingStopEnabled, run.FeePercentPerSide, events, events.Length > 0, trade.SignalOpen, trade.PositionSizingMode.ToString(), trade.AccountEquityAtEntryUsdt, trade.MarginUsedUsdt, trade.Leverage, trade.IsReentry, trade.TrendRegimeCrossoverTimeUtc, run.MinEmaGapPercent) { EconomicsMode = run.EconomicsMode, NativePositionSizingMode = trade.NativePositionSizingMode?.ToString(), UseAdaptiveInitialStop = trade.UseAdaptiveInitialStop, SignalAtr14 = trade.SignalAtr14, ReversalPowerScore = trade.ReversalPowerScore, ReversalPowerBand = trade.ReversalPowerBand?.ToString(), StopAnchorPrice = trade.StopAnchorPrice, StopBuffer = trade.StopBuffer }; }
    private static TradeDetailResponse PaperDetail(PaperTrade trade) { var session = trade.PaperSession!; var events = trade.Events.OrderBy(item => item.TimeUtc).Select(item => new TradeEventResponse(item.TimeUtc, item.TimeUtc, item.Type.ToString(), item.MarketPrice, item.OldStop, item.NewStop, item.OldTakeProfit, item.NewTakeProfit, item.ProgressPercent)).ToArray(); return new(PaperSummary(trade), trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.Quantity, trade.EntryNotionalUsdt, trade.InitialStopLoss, trade.FinalStopLoss ?? trade.CurrentStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit ?? trade.CurrentTakeProfit, trade.TakeProfitExtended, trade.EntryFeeUsdt, trade.ExitFeeUsdt, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent, trade.SignalClose, trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState, session.RiskReward, session.FixedOrderSizeUsdt, session.WaitForConfirmationCandle, session.UseEma100Filter, session.TrailingStopEnabled, session.FeePercentPerSide, events, true, trade.SignalOpen, trade.PositionSizingMode.ToString(), trade.AccountEquityAtEntryUsdt, trade.MarginUsedUsdt, trade.Leverage, trade.IsReentry, trade.TrendRegimeCrossoverTimeUtc, session.MinEmaGapPercent) { Lots = trade.Lots, RequiredMargin = trade.RequiredMargin, MarginUsed = trade.MarginUsed, AccountEquityAtEntry = trade.AccountEquityAtEntry, RoundTripCommission = trade.RoundTripCommission, EntryBid = trade.EntryBid, EntryAsk = trade.EntryAsk, EntrySpread = trade.EntrySpread, ExitBid = trade.ExitBid, ExitAsk = trade.ExitAsk, ExitSpread = trade.ExitSpread, PaperPositionSizingMode = session.MarketDataSource == MarketDataSource.Mt5Exness ? session.PaperPositionSizingMode.ToString() : null, UseAdaptiveInitialStop = trade.UseAdaptiveInitialStop, SignalAtr14 = trade.SignalAtr14, ReversalPowerScore = trade.ReversalPowerScore, ReversalPowerBand = trade.ReversalPowerBand?.ToString(), StopAnchorPrice = trade.StopAnchorPrice, StopBuffer = trade.StopBuffer, InitialRiskAmount = trade.InitialRiskAmount }; }
}
