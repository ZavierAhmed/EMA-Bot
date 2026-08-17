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

public sealed record StartPaperSessionRequest(string Interval, IReadOnlyList<string> Symbols);
public sealed record PaperSessionSummaryResponse(int Id, string Interval, PaperSessionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? StoppedAtUtc, DateTimeOffset? InterruptedAtUtc, int SymbolCount, int CompletedTrades, decimal NetPnlUsdt, decimal TotalFeesUsdt, string AccountCurrency, decimal NetPnl, decimal TotalTradingCosts, string? FailureMessage, int OpenTradeCount);
public sealed record PaperDecisionResponse(int? Id, DateTimeOffset TimeUtc, DateTimeOffset? CandleCloseTimeUtc, string Stage, SignalDirection? Direction, string Message, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, string? GapState, decimal? StopPrice, string? StopSource, DateTimeOffset? ExpectedEntryOpenUtc, decimal? Bid, decimal? Ask, decimal? EntryPrice, decimal? Lots, decimal? RequiredMargin);
public sealed record PaperPendingEntryResponse(SignalDirection Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, DateTimeOffset ExpectedEntryOpenUtc, decimal StopPrice, string StopSource, DateTimeOffset StopSourceTimeUtc, decimal? SignalOpen, decimal SignalClose, decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, string SignalGapState, bool IsReentry);
public sealed record PaperSymbolResponse(string Symbol, decimal? LatestPrice, decimal? LatestBid, decimal? LatestAsk, decimal? LatestSpread, DateTimeOffset? LastMarketEventUtc, DateTimeOffset? LastClosedCandleUtc, PaperRuntimeCandle? FormingCandle, DateTimeOffset? LastEvaluatedCandleUtc, string? Trend, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, string? GapState, SignalDirection? PendingDirection, PaperTradeResponse? OpenTrade, PaperPendingEntryResponse? PendingEntry, SignalDirection? TrendRegimeDirection, DateTimeOffset? TrendRegimeCrossoverTimeUtc, bool ReentryEligible, bool ReentryConsumed, decimal? CurrentExecutableExitPrice, PaperDecisionResponse? LastDecision, IReadOnlyList<PaperDecisionResponse> RecentDecisions, bool RuntimeDecisionHistoryAvailable);
public sealed record PaperDecisionHistoryResponse(int Total, IReadOnlyList<PaperDecisionResponse> Items);
public sealed record PaperSessionTradeHistoryResponse(int Total, IReadOnlyList<PaperTradeResponse> Items);
public sealed record PaperTradeResponse(int Id, string Symbol, PaperTradeStatus Status, SignalDirection Direction, DateTimeOffset EntryTimeUtc, DateTimeOffset? ExitTimeUtc, decimal EntryPrice, decimal? ExitPrice, decimal Quantity, decimal InitialStopLoss, decimal CurrentStopLoss, decimal CurrentTakeProfit, bool TakeProfitExtended, decimal BestFavorableProgressPercent, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal NetPnlPercent, decimal MfePrice, decimal MaePrice, PaperExitReason? ExitReason, decimal? Lots, decimal? EntryBid, decimal? EntryAsk, decimal? EntrySpread, decimal? SpreadToInitialRiskPercent, decimal? ExitBid, decimal? ExitAsk, decimal? ExitSpread, decimal? RequiredMargin, decimal? RoundTripCommission, decimal? GrossPnl, decimal? NetPnl, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, decimal? FinalStopLoss, string StopSourceType, DateTimeOffset StopSourceTimeUtc, decimal OriginalTakeProfit, decimal? FinalTakeProfit, decimal? MarginUsed, decimal? AccountEquityAtEntry, decimal? MfePercent, decimal? MaePercent, decimal? SignalOpen, decimal SignalClose, decimal? SignalEma9, decimal? SignalEma15, decimal? SignalEma100, decimal? SignalGapPercent, string SignalGapState, bool IsReentry, DateTimeOffset? TrendRegimeCrossoverTimeUtc, decimal? CurrentGrossPnl, decimal? CurrentNetPnl, decimal? CurrentPnlPercent, DateTimeOffset? CurrentPnlCalculatedAtUtc, bool CurrentPnlAvailable, bool UseAdaptiveInitialStop, decimal? SignalAtr14, decimal? ReversalPowerScore, string? ReversalPowerBand, decimal? StopAnchorPrice, decimal? StopBuffer, int? ReentryAgeBars, decimal? PnlPercentOnMargin, decimal? AccountReturnPercent, decimal? InitialRiskAmount);
public sealed record PaperSessionDetailResponse(int Id, string Interval, PaperSessionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? StoppedAtUtc, DateTimeOffset? InterruptedAtUtc, string? FailureMessage, decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, bool UseAdaptiveInitialStop, bool SameTrendReentryEnabled, int MaxReentryAgeBars, bool ExitOnOppositeCrossover, decimal FeePercentPerSide, int TotalCrossovers, int LongSignals, int ShortSignals, int RejectedByEma100, int ConfirmationFailed, int InvalidStopLoss, int SkippedWhilePositionOpen, int CompletedTrades, decimal NetPnlUsdt, decimal TotalFeesUsdt, string ConnectionState, DateTimeOffset? LastUpdateUtc, IReadOnlyList<PaperSymbolResponse> Symbols, IReadOnlyList<PaperTradeResponse> RecentTrades, MarketDataSource MarketDataSource, string AccountCurrency, string PaperPositionSizingMode, decimal PaperFixedLots, decimal PaperMarginPerTradePercent, decimal StartingBalance, decimal CurrentBalance, decimal UsedMargin, decimal NetPnl, decimal TotalTradingCosts, decimal MinEmaGapPercent, decimal MaxStopDistancePercent, int RejectedByEmaGap, int RejectedByStopDistance, int RejectedByFees, int RejectedByTradingCosts, int RejectedByInsufficientMargin, int RejectedByInvalidVolume, int RejectedByExecutableStop, bool BalanceReconciliationOk, decimal BalanceReconciliationDifference, bool MarginReconciliationOk, decimal MarginReconciliationDifference, bool WasInterrupted, int InterruptionCount, TimeSpan InterruptionDuration);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/paper-sessions")]
public sealed class PaperSessionsController(EmaBotDbContext database, TradingSettingsService settingsService, PaperTradingCoordinator coordinator, IMarketBarStreamProvider marketBarStream, IMarketProviderCapabilities capabilities, IInstrumentCatalogProvider catalog, EmaBot.Api.Mt5Bridge.IMt5AccountReader accountReader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await database.PaperSessions.AsNoTracking().OrderByDescending(session => session.CreatedAtUtc).Take(30).Select(session => new PaperSessionSummaryResponse(session.Id, session.Interval, session.Status, session.StartedAtUtc, session.StoppedAtUtc, session.InterruptedAtUtc, session.Symbols.Count, session.CompletedTrades, session.NetPnlUsdt, session.TotalFeesUsdt, session.AccountCurrency, session.NetPnl, session.TotalTradingCosts, session.FailureMessage, session.Trades.Count(trade => trade.Status == PaperTradeStatus.Open))).ToListAsync(token));

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken token)
    {
        var session = await DetailQuery().SingleOrDefaultAsync(item => item.Status == PaperSessionStatus.Running || item.Status == PaperSessionStatus.Interrupted, token);
        return session is null ? NotFound(new ApiMessage("No active paper session.")) : Ok(await ToDetailAsync(session, token));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken token) => await DetailQuery().SingleOrDefaultAsync(item => item.Id == id, token) is { } session ? Ok(await ToDetailAsync(session, token)) : NotFound(new ApiMessage("Paper session not found."));

    [HttpGet("{id:int}/export/excel")]
    public async Task<IActionResult> ExportExcel(int id, CancellationToken token)
    {
        var workbook = await PaperSessionExcelExport.CreateAsync(database, coordinator, id, token);
        return workbook is null ? NotFound(new ApiMessage("Paper session not found.")) : File(workbook, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ema-bot-paper-session-{id}.xlsx");
    }

    [HttpGet("{id:int}/trades")]
    public async Task<IActionResult> Trades(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken token = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200) return BadRequest(new ApiMessage("page must be at least 1 and pageSize must be from 1 through 200."));
        if (!await database.PaperSessions.AnyAsync(item => item.Id == id, token)) return NotFound(new ApiMessage("Paper session not found."));
        var query = database.PaperTrades.AsNoTracking().Where(trade => trade.PaperSessionId == id);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(trade => trade.EntryTimeUtc).ThenByDescending(trade => trade.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        return Ok(new PaperSessionTradeHistoryResponse(total, items.Select(trade => ToTrade(trade)).ToArray()));
    }

    [HttpGet("{id:int}/decisions")]
    public async Task<IActionResult> Decisions(int id, [FromQuery] string? symbol, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken token = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200) return BadRequest(new ApiMessage("page must be at least 1 and pageSize must be from 1 through 200."));
        if (!await database.PaperSessions.AnyAsync(item => item.Id == id, token)) return NotFound(new ApiMessage("Paper session not found."));
        var query = database.PaperDecisionEvents.AsNoTracking().Where(item => item.PaperSessionId == id);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var exactSymbol = symbol.Trim();
            query = database.Database.ProviderName == "Pomelo.EntityFrameworkCore.MySql"
                ? query.Where(item => EF.Functions.Collate(item.PaperSessionSymbol!.Symbol, "utf8mb4_bin") == exactSymbol)
                : query.Where(item => item.PaperSessionSymbol!.Symbol == exactSymbol);
        }
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(item => item.TimeUtc).ThenByDescending(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        return Ok(new PaperDecisionHistoryResponse(total, items.Select(ToDecision).ToArray()));
    }

    [HttpPost]
    public async Task<ActionResult<PaperSessionDetailResponse>> Start(StartPaperSessionRequest request, CancellationToken token)
    {
        if (!capabilities.Current.LiveBarProviderConfigured || !marketBarStream.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("The MT5 live-bar bridge must be connected before starting Paper."));
        var symbols = request.Symbols?.Select(symbol => symbol.Trim()).Where(symbol => symbol.Length > 0).ToArray() ?? [];
        if (!Mt5NativeTimeframes.IsSupported(request.Interval) || symbols.Length == 0) return BadRequest(new ApiMessage("Use a native MT5 interval and select at least one symbol."));
        if (symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length) return BadRequest(new ApiMessage("Symbols must not contain duplicates."));
        if (await database.PaperSessions.AnyAsync(session => session.Status == PaperSessionStatus.Running || session.Status == PaperSessionStatus.Interrupted, token)) return Conflict(new ApiMessage("Stop or resume the existing paper session before starting another."));
        var enabledMonitored = await database.MonitoredSymbols.Where(symbol => symbol.IsEnabled && symbol.Source == MarketDataSource.Mt5Exness).ToListAsync(token);
        var requestedSymbols = symbols.ToHashSet(StringComparer.Ordinal);
        var monitored = enabledMonitored.Where(symbol => requestedSymbols.Contains(symbol.Symbol)).ToList();
        if (monitored.Count != symbols.Length) return BadRequest(new ApiMessage("Every selected symbol must be an enabled exact MT5 instrument."));
        if (monitored.Any(symbol => symbol.PaperCommissionPerLotPerSide is null)) return BadRequest(new ApiMessage("Configure Paper commission for every selected MT5 instrument before starting a Paper session. Use 0 only for a confirmed commission-free instrument."));
        var settings = await settingsService.GetAsync(token);
        if (settings.UseHtfRegimeFilter) return BadRequest(new ApiMessage("HTF Regime Filter is currently supported for historical backtesting only."));
        EmaBot.Api.Mt5Bridge.Mt5AccountPayload account;
        var instruments = new List<InstrumentCatalogItem>();
        try
        {
            account = await accountReader.GetAsync(token);
            foreach (var symbol in monitored)
            {
                var instrument = await catalog.GetAsync(symbol.Symbol, token);
                if (instrument is null || !instrument.IsSelected || !string.Equals(instrument.Spec.BrokerSymbol, symbol.Symbol, StringComparison.Ordinal)) return BadRequest(new ApiMessage("Every selected symbol must remain available in MT5 Market Watch."));
                instruments.Add(instrument);
            }
        }
        catch (MarketDataProviderException exception) when (exception.Kind is MarketDataErrorKind.Unavailable or MarketDataErrorKind.Timeout) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("The connected MT5 account or instrument catalog is unavailable.")); }
        if (settings.PaperPositionSizingMode == PaperPositionSizingMode.FixedLots)
        {
            foreach (var instrument in instruments)
            {
                if (FixedLotsValidationMessage(instrument.Spec.BrokerSymbol, settings.PaperFixedLots, instrument.Spec.VolumeMin, instrument.Spec.VolumeMax, instrument.Spec.VolumeStep) is { } message)
                    return BadRequest(new ApiMessage(message));
            }
        }
        var now = DateTimeOffset.UtcNow;
        var session = new PaperSession { MarketDataSource = MarketDataSource.Mt5Exness, Interval = request.Interval, Status = PaperSessionStatus.Running, CreatedAtUtc = now, StartedAtUtc = now, RiskReward = settings.RiskReward, FixedOrderSizeUsdt = settings.FixedOrderSizeUsdt, MinEmaGapPercent = settings.MinEmaGapPercent, MaxStopDistancePercent = settings.MaxStopDistancePercent, PositionSizingMode = settings.PositionSizingMode, StartingBalanceUsdt = settings.SimulatedAccountBalanceUsdt, CurrentBalanceUsdt = settings.SimulatedAccountBalanceUsdt, MarginPerTradePercent = settings.MarginPerTradePercent, Leverage = settings.Leverage, WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, TrailingStopEnabled = settings.TrailingStopEnabled, UseAdaptiveInitialStop = settings.UseAdaptiveInitialStop, SameTrendReentryEnabled = settings.SameTrendReentryEnabled, MaxReentryAgeBars = settings.MaxReentryAgeBars, ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover, FeePercentPerSide = settings.FeePercentPerSide, AccountCurrency = account.Currency, PaperPositionSizingMode = settings.PaperPositionSizingMode, PaperFixedLots = settings.PaperFixedLots, PaperMarginPerTradePercent = settings.PaperMarginPerTradePercent, StartingBalance = settings.PaperStartingBalance, CurrentBalance = settings.PaperStartingBalance, Symbols = monitored.Zip(instruments).Select(pair => Snapshot(pair.First, pair.Second)).ToList() };
        database.PaperSessions.Add(session); await database.SaveChangesAsync(token);
        try { await coordinator.StartSessionAsync(session.Id, false, token); }
        catch (MarketDataProviderException) { session.Status = PaperSessionStatus.Faulted; session.FailureMessage = "Historical warmup data is unavailable."; await database.SaveChangesAsync(token); return StatusCode(502, new ApiMessage("Historical market data is currently unavailable.")); }
        return CreatedAtAction(nameof(Get), new { id = session.Id }, await ToDetailAsync(session, token));
    }

    [HttpPost("{id:int}/stop")]
    public async Task<IActionResult> Stop(int id, CancellationToken token)
    {
        try { await coordinator.StopSessionAsync(id, token); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Paper session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
    }

    [HttpPost("{id:int}/resume")]
    public async Task<IActionResult> Resume(int id, CancellationToken token)
    {
        if (!capabilities.Current.LiveBarProviderConfigured || !marketBarStream.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("The MT5 live-bar bridge must be connected before resuming Paper."));
        var existing = await database.PaperSessions.AsNoTracking().SingleOrDefaultAsync(session => session.Id == id, token);
        if (existing?.MarketDataSource == MarketDataSource.LegacyBinance) return BadRequest(new ApiMessage("Legacy Binance Paper sessions cannot be resumed through MT5."));
        try { await coordinator.StartSessionAsync(id, true, token); var session = await DetailQuery().SingleAsync(item => item.Id == id, token); return Ok(await ToDetailAsync(session, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Paper session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException) { return StatusCode(502, new ApiMessage("Historical market data is currently unavailable.")); }
    }

    private IQueryable<PaperSession> DetailQuery() => database.PaperSessions.AsNoTracking().Include(session => session.Symbols).Include(session => session.Trades);
    private async Task<PaperSessionDetailResponse> ToDetailAsync(PaperSession session, CancellationToken token)
    {
        var runtime = coordinator.GetRuntimeSnapshot();
        var snapshot = runtime?.SessionId == session.Id ? runtime : null;
        var persisted = snapshot is null ? await RecentPersistedDecisionsAsync(session, token) : new Dictionary<int, IReadOnlyList<PaperDecisionResponse>>();
        var diagnostics = PaperAccounting.Interruptions(await database.PaperDecisionEvents.AsNoTracking().Where(item => item.PaperSessionId == session.Id).ToListAsync(token));
        return ToDetail(session, runtime, persisted, diagnostics);
    }
    private async Task<IReadOnlyDictionary<int, IReadOnlyList<PaperDecisionResponse>>> RecentPersistedDecisionsAsync(PaperSession session, CancellationToken token)
    {
        var result = new Dictionary<int, IReadOnlyList<PaperDecisionResponse>>();
        foreach (var symbol in session.Symbols)
        {
            var rows = await database.PaperDecisionEvents.AsNoTracking().Where(item => item.PaperSessionSymbolId == symbol.Id).OrderByDescending(item => item.TimeUtc).ThenByDescending(item => item.Id).Take(25).ToListAsync(token);
            result[symbol.Id] = rows.Select(ToDecision).ToArray();
        }
        return result;
    }
    private static PaperSessionDetailResponse ToDetail(PaperSession session, PaperRuntimeSnapshot? runtime, IReadOnlyDictionary<int, IReadOnlyList<PaperDecisionResponse>> persistedRecent, PaperInterruptionDiagnostics? diagnostics = null)
    {
        var snapshot = runtime?.SessionId == session.Id ? runtime : null;
        var symbols = session.Symbols.Select(symbol =>
        {
            PaperSymbolRuntimeSnapshot? live = null; snapshot?.Symbols.TryGetValue(symbol.Symbol, out live);
            var bid = live?.LatestBid; var ask = live?.LatestAsk;
            var open = live?.OpenTrade ?? session.Trades.SingleOrDefault(trade => trade.PaperSessionSymbolId == symbol.Id && trade.Status == PaperTradeStatus.Open);
            var pending = live?.PendingEntry ?? PersistedPending(symbol);
            var executableExit = open is null ? null : open.Direction == SignalDirection.Long ? bid : ask;
            var recent = live is not null ? live.RecentDecisions.Select(ToDecision).ToArray() : persistedRecent.GetValueOrDefault(symbol.Id, []);
            var latestDecision = recent.FirstOrDefault();
            var ema9 = live?.Indicator?.Ema9 ?? latestDecision?.Ema9;
            var ema15 = live?.Indicator?.Ema15 ?? latestDecision?.Ema15;
            var trend = live?.Indicator?.TrendDirection.ToString() ?? (ema9, ema15) switch { ({ } fast, { } slow) when fast > slow => "Up", ({ } fast, { } slow) when fast < slow => "Down", ({ }, { }) => "Neutral", _ => null };
            return new PaperSymbolResponse(symbol.Symbol, live?.LatestPrice ?? symbol.LastKnownPrice, bid, ask, bid is not null && ask is not null ? ask - bid : null, live?.LastMarketEventUtc ?? symbol.LastMarketEventUtc, live?.LastClosedCandleUtc ?? symbol.LastProcessedClosedCandleUtc, live?.FormingCandle, live?.LastClosedCandleUtc ?? symbol.LastProcessedClosedCandleUtc, trend, ema9, ema15, live?.Indicator?.Ema100 ?? latestDecision?.Ema100, live?.Indicator?.GapPercent ?? latestDecision?.GapPercent, live?.Indicator?.GapState.ToString() ?? latestDecision?.GapState, pending?.Direction, open is null ? null : ToTrade(open, live), pending is null ? null : ToPending(pending), live?.TrendRegimeDirection ?? symbol.TrendRegimeDirection, live?.TrendRegimeCrossoverTimeUtc ?? symbol.TrendRegimeCrossoverTimeUtc, live?.ReentryEligible ?? symbol.ReentryEligible, live?.ReentryConsumed ?? symbol.ReentryConsumed, executableExit, latestDecision, recent, recent.Count > 0 || live is not null);
        }).ToArray();
        var reconciliation = PaperAccounting.Reconcile(session, session.Trades);
        diagnostics ??= new PaperInterruptionDiagnostics(session.InterruptedAtUtc is not null, session.InterruptedAtUtc is null ? 0 : 1, TimeSpan.Zero);
        return new PaperSessionDetailResponse(session.Id, session.Interval, session.Status, session.StartedAtUtc, session.StoppedAtUtc, session.InterruptedAtUtc, session.FailureMessage, session.RiskReward, session.FixedOrderSizeUsdt, session.WaitForConfirmationCandle, session.UseEma100Filter, session.TrailingStopEnabled, session.UseAdaptiveInitialStop, session.SameTrendReentryEnabled, session.MaxReentryAgeBars, session.ExitOnOppositeCrossover, session.FeePercentPerSide, session.TotalCrossovers, session.LongSignals, session.ShortSignals, session.RejectedByEma100, session.ConfirmationFailed, session.InvalidStopLoss, session.SkippedWhilePositionOpen, session.CompletedTrades, session.NetPnlUsdt, session.TotalFeesUsdt, snapshot?.ConnectionState ?? (session.Status == PaperSessionStatus.Interrupted ? "Disconnected" : "Persisted"), snapshot?.LastUpdateUtc, symbols, session.Trades.OrderByDescending(trade => trade.ExitTimeUtc ?? trade.EntryTimeUtc).Take(20).Select(trade => ToTrade(trade)).ToArray(), session.MarketDataSource, session.AccountCurrency, session.PaperPositionSizingMode.ToString(), session.PaperFixedLots, session.PaperMarginPerTradePercent, session.StartingBalance, session.CurrentBalance, session.UsedMargin, session.NetPnl, session.TotalTradingCosts, session.MinEmaGapPercent, session.MaxStopDistancePercent, session.RejectedByEmaGap, session.RejectedByStopDistance, session.RejectedByFees, session.RejectedByTradingCosts, session.RejectedByInsufficientMargin, session.RejectedByInvalidVolume, session.RejectedByExecutableStop, reconciliation.BalanceOk, reconciliation.BalanceDifference, reconciliation.MarginOk, reconciliation.MarginDifference, diagnostics.WasInterrupted, diagnostics.Count, diagnostics.TotalDuration);
    }
    private static PaperPendingEntryRuntimeSnapshot? PersistedPending(PaperSessionSymbol symbol) => symbol.PendingDirection is { } direction && symbol.PendingCrossoverTimeUtc is { } crossover && symbol.PendingSignalTimeUtc is { } signal && symbol.PendingStopPrice is { } stop && symbol.PendingStopSourceType is { } source && symbol.PendingStopSourceTimeUtc is { } stopTime ? new(direction, crossover, signal, signal.AddMilliseconds(1), stop, source, stopTime, new IndicatorSnapshot(signal, symbol.PendingSignalClose ?? 0m, symbol.PendingSignalEma9, symbol.PendingSignalEma15, symbol.PendingSignalEma100, symbol.PendingSignalGapPercent, symbol.PendingSignalGapState ?? GapState.Unchanged, TrendDirection.Neutral, symbol.PendingSignalOpen ?? 0m), symbol.PendingIsReentry) : null;
    private static PaperPendingEntryResponse ToPending(PaperPendingEntryRuntimeSnapshot pending) => new(pending.Direction, pending.CrossoverTimeUtc, pending.SignalTimeUtc, pending.ExpectedEntryOpenUtc, pending.StopPrice, pending.StopSource.ToString(), pending.StopSourceTimeUtc, pending.Snapshot.Open, pending.Snapshot.Close, pending.Snapshot.Ema9, pending.Snapshot.Ema15, pending.Snapshot.Ema100, pending.Snapshot.GapPercent, pending.Snapshot.GapState.ToString(), pending.IsReentry);
    private static PaperDecisionResponse ToDecision(PaperDecisionRuntimeEvent item) => new(null, item.TimeUtc, item.CandleCloseTimeUtc, item.Stage, item.Direction, item.Message, item.Ema9, item.Ema15, item.Ema100, item.GapPercent, item.GapState?.ToString(), item.StopPrice, item.StopSource?.ToString(), item.ExpectedEntryOpenUtc, item.Bid, item.Ask, item.EntryPrice, item.Lots, item.RequiredMargin);
    private static PaperDecisionResponse ToDecision(PaperDecisionEvent item) => new(item.Id, item.TimeUtc, item.CandleCloseTimeUtc, item.Stage, item.Direction, item.Message, item.Ema9, item.Ema15, item.Ema100, item.GapPercent, item.GapState?.ToString(), item.StopPrice, item.StopSource?.ToString(), item.ExpectedEntryOpenUtc, item.Bid, item.Ask, item.EntryPrice, item.Lots, item.RequiredMargin);
    private static PaperTradeResponse ToTrade(PaperTrade trade, PaperSymbolRuntimeSnapshot? live = null) => new(trade.Id, trade.Symbol, trade.Status, trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.Quantity, trade.InitialStopLoss, trade.CurrentStopLoss, trade.CurrentTakeProfit, trade.TakeProfitExtended, trade.BestFavorableProgressPercent, trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.MfePrice, trade.MaePrice, trade.ExitReason, trade.Lots, trade.EntryBid, trade.EntryAsk, trade.EntrySpread, SpreadToInitialRiskPercent(trade), trade.ExitBid, trade.ExitAsk, trade.ExitSpread, trade.RequiredMargin, trade.RoundTripCommission, trade.GrossPnl, trade.NetPnl, trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.FinalStopLoss, trade.StopSourceType.ToString(), trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit, trade.MarginUsed, trade.AccountEquityAtEntry, trade.MfePercent, trade.MaePercent, trade.SignalOpen, trade.SignalClose, trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState.ToString(), trade.IsReentry, trade.TrendRegimeCrossoverTimeUtc, live?.CurrentGrossPnl, live?.CurrentNetPnl, live?.CurrentPnlPercent, live?.CurrentPnlCalculatedAtUtc, live?.CurrentPnlAvailable ?? false, trade.UseAdaptiveInitialStop, trade.SignalAtr14, trade.ReversalPowerScore, trade.ReversalPowerBand?.ToString(), trade.StopAnchorPrice, trade.StopBuffer, trade.ReentryAgeBars, PaperAccounting.PnlPercentOnMargin(trade.NetPnl, trade.MarginUsed), PaperAccounting.AccountReturnPercent(trade.NetPnl, trade.AccountEquityAtEntry), trade.InitialRiskAmount);
    internal static decimal? SpreadToInitialRiskPercent(PaperTrade trade) { var risk = decimal.Abs(trade.EntryPrice - trade.InitialStopLoss); return trade.EntrySpread is { } spread && risk > 0m ? spread / risk * 100m : null; }
    internal static string? FixedLotsValidationMessage(string symbol, decimal requestedLots, decimal minimum, decimal maximum, decimal step)
    {
        if (requestedLots < minimum) return $"{symbol} cannot use {requestedLots} fixed lots. Broker minimum is {minimum}, maximum {maximum}, step {step}.";
        if (requestedLots > maximum) return $"{symbol} cannot use {requestedLots} fixed lots. Broker maximum is {maximum}, minimum {minimum}, step {step}.";
        var steps = (requestedLots - minimum) / step;
        return steps != decimal.Truncate(steps) ? $"{symbol} cannot use {requestedLots} fixed lots. Broker volume step is {step}; minimum {minimum}, maximum {maximum}." : null;
    }
    private static PaperSessionSymbol Snapshot(MonitoredSymbol symbol, InstrumentCatalogItem item) => new() { Symbol = symbol.Symbol, BrokerSymbol = item.Spec.BrokerSymbol, ContractSize = item.Spec.ContractSize, PointSize = item.Spec.PointSize, StopsLevelPoints = item.Spec.StopsLevelPoints, VolumeMin = item.Spec.VolumeMin, VolumeMax = item.Spec.VolumeMax, VolumeStep = item.Spec.VolumeStep, VolumeLimit = item.Spec.VolumeLimit, TickSize = item.Spec.TickSize, TickValueProfit = item.Spec.TickValueProfit, TickValueLoss = item.Spec.TickValueLoss, TradeMode = item.TradeMode, CommissionPerLotPerSide = symbol.PaperCommissionPerLotPerSide };
}
