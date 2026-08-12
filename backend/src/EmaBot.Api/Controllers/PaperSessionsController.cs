using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

public sealed record StartPaperSessionRequest(string Interval, IReadOnlyList<string> Symbols);
public sealed record PaperSessionSummaryResponse(int Id, string Interval, PaperSessionStatus Status, DateTimeOffset StartedAtUtc, int SymbolCount, int CompletedTrades, decimal NetPnlUsdt, decimal TotalFeesUsdt);
public sealed record PaperSymbolResponse(string Symbol, decimal? LatestPrice, DateTimeOffset? LastMarketEventUtc, DateTimeOffset? LastClosedCandleUtc, string? Trend, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, string? GapState, SignalDirection? PendingDirection, PaperTradeResponse? OpenTrade);
public sealed record PaperTradeResponse(int Id, string Symbol, PaperTradeStatus Status, SignalDirection Direction, DateTimeOffset EntryTimeUtc, DateTimeOffset? ExitTimeUtc, decimal EntryPrice, decimal? ExitPrice, decimal Quantity, decimal InitialStopLoss, decimal CurrentStopLoss, decimal CurrentTakeProfit, bool TakeProfitExtended, decimal BestFavorableProgressPercent, decimal GrossPnlUsdt, decimal NetPnlUsdt, decimal NetPnlPercent, decimal MfePrice, decimal MaePrice, PaperExitReason? ExitReason);
public sealed record PaperSessionDetailResponse(int Id, string Interval, PaperSessionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? StoppedAtUtc, DateTimeOffset? InterruptedAtUtc, string? FailureMessage, decimal RiskReward, decimal FixedOrderSizeUsdt, bool WaitForConfirmationCandle, bool UseEma100Filter, bool TrailingStopEnabled, decimal FeePercentPerSide, int TotalCrossovers, int LongSignals, int ShortSignals, int RejectedByEma100, int ConfirmationFailed, int InvalidStopLoss, int SkippedWhilePositionOpen, int CompletedTrades, decimal NetPnlUsdt, decimal TotalFeesUsdt, string ConnectionState, DateTimeOffset? LastUpdateUtc, IReadOnlyList<PaperSymbolResponse> Symbols, IReadOnlyList<PaperTradeResponse> RecentTrades);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/paper-sessions")]
public sealed class PaperSessionsController(EmaBotDbContext database, TradingSettingsService settingsService, PaperTradingCoordinator coordinator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok((await database.PaperSessions.AsNoTracking().Include(session => session.Symbols).OrderByDescending(session => session.CreatedAtUtc).Take(30).ToListAsync(token)).Select(session => new PaperSessionSummaryResponse(session.Id, session.Interval, session.Status, session.StartedAtUtc, session.Symbols.Count, session.CompletedTrades, session.NetPnlUsdt, session.TotalFeesUsdt)));

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken token)
    {
        var session = await DetailQuery().SingleOrDefaultAsync(item => item.Status == PaperSessionStatus.Running || item.Status == PaperSessionStatus.Interrupted, token);
        return session is null ? NotFound(new ApiMessage("No active paper session.")) : Ok(ToDetail(session, coordinator.GetRuntimeSnapshot()));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken token) => await DetailQuery().SingleOrDefaultAsync(item => item.Id == id, token) is { } session ? Ok(ToDetail(session, coordinator.GetRuntimeSnapshot())) : NotFound(new ApiMessage("Paper session not found."));

    [HttpPost]
    public async Task<ActionResult<PaperSessionDetailResponse>> Start(StartPaperSessionRequest request, CancellationToken token)
    {
        var symbols = request.Symbols?.Select(symbol => symbol.Trim().ToUpperInvariant()).Where(symbol => symbol.Length > 0).ToArray() ?? [];
        if (!BinanceIntervals.IsSupported(request.Interval) || symbols.Length == 0) return BadRequest(new ApiMessage("Use a supported interval and select at least one symbol."));
        if (symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length) return BadRequest(new ApiMessage("Symbols must not contain duplicates."));
        if (await database.PaperSessions.AnyAsync(session => session.Status == PaperSessionStatus.Running || session.Status == PaperSessionStatus.Interrupted, token)) return Conflict(new ApiMessage("Stop or resume the existing paper session before starting another."));
        var enabledMonitored = await database.MonitoredSymbols.Where(symbol => symbol.IsEnabled).ToListAsync(token);
        var requestedSymbols = symbols.ToHashSet(StringComparer.Ordinal);
        var monitored = enabledMonitored.Where(symbol => requestedSymbols.Contains(symbol.Symbol)).ToList();
        if (monitored.Count != symbols.Length) return BadRequest(new ApiMessage("Every selected symbol must be monitored and enabled."));
        var settings = await settingsService.GetAsync(token);
        if (settings.UseHtfRegimeFilter) return BadRequest(new ApiMessage("HTF Regime Filter is currently supported for historical backtesting only."));
        var now = DateTimeOffset.UtcNow;
        var session = new PaperSession { Interval = request.Interval, Status = PaperSessionStatus.Running, CreatedAtUtc = now, StartedAtUtc = now, RiskReward = settings.RiskReward, FixedOrderSizeUsdt = settings.FixedOrderSizeUsdt, MinEmaGapPercent = settings.MinEmaGapPercent, MaxStopDistancePercent = settings.MaxStopDistancePercent, PositionSizingMode = settings.PositionSizingMode, StartingBalanceUsdt = settings.SimulatedAccountBalanceUsdt, CurrentBalanceUsdt = settings.SimulatedAccountBalanceUsdt, MarginPerTradePercent = settings.MarginPerTradePercent, Leverage = settings.Leverage, WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, TrailingStopEnabled = settings.TrailingStopEnabled, FeePercentPerSide = settings.FeePercentPerSide, Symbols = monitored.Select(symbol => new PaperSessionSymbol { Symbol = symbol.Symbol }).ToList() };
        database.PaperSessions.Add(session); await database.SaveChangesAsync(token);
        try { await coordinator.StartSessionAsync(session.Id, false, token); }
        catch (BinanceApiException) { session.Status = PaperSessionStatus.Faulted; session.FailureMessage = "Public Binance warmup data is unavailable."; await database.SaveChangesAsync(token); return StatusCode(502, new ApiMessage("Binance market data is currently unavailable.")); }
        return CreatedAtAction(nameof(Get), new { id = session.Id }, ToDetail(session, coordinator.GetRuntimeSnapshot()));
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
        try { await coordinator.StartSessionAsync(id, true, token); var session = await DetailQuery().SingleAsync(item => item.Id == id, token); return Ok(ToDetail(session, coordinator.GetRuntimeSnapshot())); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Paper session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
        catch (BinanceApiException) { return StatusCode(502, new ApiMessage("Binance market data is currently unavailable.")); }
    }

    private IQueryable<PaperSession> DetailQuery() => database.PaperSessions.AsNoTracking().Include(session => session.Symbols).Include(session => session.Trades).ThenInclude(trade => trade.Events);
    private static PaperSessionDetailResponse ToDetail(PaperSession session, PaperRuntimeSnapshot? runtime)
    {
        var snapshot = runtime?.SessionId == session.Id ? runtime : null;
        var symbols = session.Symbols.Select(symbol => { PaperSymbolRuntimeSnapshot? live = null; snapshot?.Symbols.TryGetValue(symbol.Symbol, out live); return new PaperSymbolResponse(symbol.Symbol, live?.LatestPrice ?? symbol.LastKnownPrice, live?.LastMarketEventUtc ?? symbol.LastMarketEventUtc, live?.LastClosedCandleUtc ?? symbol.LastProcessedClosedCandleUtc, live?.Indicator?.TrendDirection.ToString(), live?.Indicator?.Ema9, live?.Indicator?.Ema15, live?.Indicator?.Ema100, live?.Indicator?.GapPercent, live?.Indicator?.GapState.ToString(), live?.PendingDirection ?? symbol.PendingDirection, live?.OpenTrade is { } open ? ToTrade(open) : null); }).ToArray();
        return new PaperSessionDetailResponse(session.Id, session.Interval, session.Status, session.StartedAtUtc, session.StoppedAtUtc, session.InterruptedAtUtc, session.FailureMessage, session.RiskReward, session.FixedOrderSizeUsdt, session.WaitForConfirmationCandle, session.UseEma100Filter, session.TrailingStopEnabled, session.FeePercentPerSide, session.TotalCrossovers, session.LongSignals, session.ShortSignals, session.RejectedByEma100, session.ConfirmationFailed, session.InvalidStopLoss, session.SkippedWhilePositionOpen, session.CompletedTrades, session.NetPnlUsdt, session.TotalFeesUsdt, snapshot?.ConnectionState ?? (session.Status == PaperSessionStatus.Interrupted ? "Disconnected" : "Persisted"), snapshot?.LastUpdateUtc, symbols, session.Trades.OrderByDescending(trade => trade.ExitTimeUtc ?? trade.EntryTimeUtc).Take(20).Select(ToTrade).ToArray());
    }
    private static PaperTradeResponse ToTrade(PaperTrade trade) => new(trade.Id, trade.Symbol, trade.Status, trade.Direction, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.Quantity, trade.InitialStopLoss, trade.CurrentStopLoss, trade.CurrentTakeProfit, trade.TakeProfitExtended, trade.BestFavorableProgressPercent, trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.MfePrice, trade.MaePrice, trade.ExitReason);
}
