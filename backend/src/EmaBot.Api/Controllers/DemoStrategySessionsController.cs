using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Controllers;

public sealed record CreateDemoStrategySessionRequest(string Interval, IReadOnlyList<string> Symbols);
public sealed record DemoStrategyIntentResponse(int Id, string Direction, DateTimeOffset CrossoverTimeUtc, DateTimeOffset SignalTimeUtc, DateTimeOffset ExpectedEntryOpenUtc, decimal StructuralStopLoss, decimal? IntendedTakeProfit, decimal IntendedVolumeLots, Guid ClientExecutionId, string Status, int? DemoExecutionId, string? Reason);
public sealed record DemoStrategySymbolResponse(string Symbol, string BrokerSymbol, DateTimeOffset? LastProcessedClosedCandleUtc, DateTimeOffset? LastMarketEventUtc, IReadOnlyList<DemoStrategyIntentResponse> RecentIntents);
public sealed record DemoStrategySessionResponse(int Id, string Interval, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? StoppedAtUtc, DateTimeOffset? InterruptedAtUtc, string? FailureMessage, bool AutomationEnabled, bool ManagementEnabled, bool TrailingStopEnabled, bool ExitOnOppositeCrossover, decimal FixedLots, decimal RiskReward, IReadOnlyList<DemoStrategySymbolResponse> Symbols, DemoStrategyRuntimeSnapshot? Runtime);

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/demo-strategy-sessions")]
public sealed class DemoStrategySessionsController(EmaBotDbContext database, TradingSettingsService settingsService, DemoStrategyCoordinator coordinator, IOptions<DemoStrategyAutomationOptions> automation) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<DemoStrategySessionResponse>> Create(CreateDemoStrategySessionRequest request, CancellationToken token)
    {
        var symbols = request.Symbols?.Select(item => item.Trim()).Where(item => item.Length > 0).ToArray() ?? [];
        if (!Mt5NativeTimeframes.IsSupported(request.Interval) || symbols.Length == 0) return BadRequest(new ApiMessage("Use a native MT5 interval and select at least one enabled MT5 symbol."));
        if (symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length) return BadRequest(new ApiMessage("Symbols must not contain duplicates."));
        if (await database.DemoStrategySessions.AnyAsync(item => item.Status == DemoStrategySessionStatus.Running || item.Status == DemoStrategySessionStatus.Interrupted, token)) return Conflict(new ApiMessage("Stop or resume the existing Demo strategy session before creating another."));
        var monitored = await database.MonitoredSymbols.Where(item => item.IsEnabled && item.Source == MarketDataSource.Mt5Exness && symbols.Contains(item.Symbol)).ToListAsync(token);
        if (monitored.Count != symbols.Length) return BadRequest(new ApiMessage("Every selected symbol must be an enabled exact MT5 instrument."));
        var settings = await settingsService.GetAsync(token);
        if (settings.UseHtfRegimeFilter) return BadRequest(new ApiMessage("HTF Regime Filter is currently supported for historical backtesting only."));
        var now = DateTimeOffset.UtcNow;
        var session = new DemoStrategySession
        {
            Interval = request.Interval, Status = DemoStrategySessionStatus.Created, CreatedAtUtc = now,
            AutomationEnabledAtCreation = automation.Value.Enabled, FixedLots = automation.Value.FixedLots,
            RiskReward = settings.RiskReward, MinEmaGapPercent = settings.MinEmaGapPercent, MaxStopDistancePercent = settings.MaxStopDistancePercent,
            WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, UseAdaptiveInitialStop = settings.UseAdaptiveInitialStop,
            TrailingStopEnabled = settings.TrailingStopEnabled, ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover,
            Symbols = monitored.Select(item => new DemoStrategySessionSymbol { Symbol = item.Symbol, BrokerSymbol = item.Symbol }).ToList()
        };
        database.DemoStrategySessions.Add(session);
        await database.SaveChangesAsync(token);
        return CreatedAtAction(nameof(Get), new { id = session.Id }, ToResponse(session));
    }

    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken token)
    {
        try { await coordinator.StartSessionAsync(id, false, token); return Ok(await GetResponseAsync(id, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("MT5 historical market data is unavailable; observation was not started.")); }
    }

    [HttpPost("{id:int}/resume")]
    public async Task<IActionResult> Resume(int id, CancellationToken token)
    {
        try { await coordinator.StartSessionAsync(id, true, token); return Ok(await GetResponseAsync(id, token)); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
        catch (MarketDataProviderException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage("MT5 historical market data is unavailable; observation was not started.")); }
    }

    [HttpPost("{id:int}/stop")]
    public async Task<IActionResult> Stop(int id, CancellationToken token)
    {
        try { await coordinator.StopSessionAsync(id, token); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(new ApiMessage("Demo strategy session not found.")); }
        catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken token) => await GetResponseAsync(id, token) is { } response ? Ok(response) : NotFound(new ApiMessage("Demo strategy session not found."));
    [HttpGet("{id:int}/runtime")]
    public async Task<IActionResult> Runtime(int id, CancellationToken token)
    {
        if (!await database.DemoStrategySessions.AnyAsync(item => item.Id == id, token)) return NotFound(new ApiMessage("Demo strategy session not found."));
        var runtime = coordinator.GetRuntimeSnapshot();
        return Ok(runtime?.SessionId == id ? runtime : null);
    }

    private async Task<DemoStrategySessionResponse?> GetResponseAsync(int id, CancellationToken token)
    {
        var session = await database.DemoStrategySessions.AsNoTracking().Include(item => item.Symbols).ThenInclude(item => item.Intents).SingleOrDefaultAsync(item => item.Id == id, token);
        return session is null ? null : ToResponse(session);
    }
    private DemoStrategySessionResponse ToResponse(DemoStrategySession session)
    {
        var runtime = coordinator.GetRuntimeSnapshot(); if (runtime?.SessionId != session.Id) runtime = null;
        return new(session.Id, session.Interval, session.Status.ToString(), session.CreatedAtUtc, session.StartedAtUtc, session.StoppedAtUtc, session.InterruptedAtUtc, session.FailureMessage, automation.Value.Enabled, automation.Value.ManagementEnabled, session.TrailingStopEnabled, session.ExitOnOppositeCrossover, session.FixedLots, session.RiskReward,
            session.Symbols.Select(symbol => new DemoStrategySymbolResponse(symbol.Symbol, symbol.BrokerSymbol, symbol.LastProcessedClosedCandleUtc, symbol.LastMarketEventUtc, symbol.Intents.OrderByDescending(item => item.CreatedAtUtc).Take(25).Select(intent => new DemoStrategyIntentResponse(intent.Id, intent.Direction.ToString(), intent.CrossoverTimeUtc, intent.SignalTimeUtc, intent.ExpectedEntryOpenUtc, intent.StructuralStopLoss, intent.IntendedTakeProfit, intent.IntendedVolumeLots, intent.ClientExecutionId, intent.Status.ToString(), intent.DemoExecutionId, intent.Reason)).ToArray())).ToArray(), runtime);
    }
}
