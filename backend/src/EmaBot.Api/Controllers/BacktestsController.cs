using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/backtests")]
public sealed class BacktestsController(EmaBotDbContext database, BacktestService service, IOptions<BacktestRequestTimeoutOptions> timeoutOptions, ILogger<BacktestsController>? logger = null, GridBacktestService? gridService = null) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BacktestRunDetailResponse>> Run(BacktestRequest request, CancellationToken token)
    {
        if (request.StrategyId is HistoricalStrategyIds.Grid or HistoricalStrategyIds.GridResearch or HistoricalStrategyIds.GridBreakoutGuard) return await RunGrid(request, token);
        if (request.StrategyId is not null && request.StrategyId != HistoricalStrategyIds.Ema) return BadRequest(new ApiMessage("Unsupported historical StrategyId."));
        if (!Mt5NativeTimeframes.IsSupported(request.Interval) || request.StartUtc >= request.EndUtc) return BadRequest(new ApiMessage("Use an MT5-native interval and a valid UTC date range. The 3d timeframe is not available for MT5 research."));
        var symbol = request.Symbol.Trim();
        var monitored = string.IsNullOrWhiteSpace(symbol) ? null : await database.MonitoredSymbols.AsNoTracking().SingleOrDefaultAsync(x => x.Source == MarketDataSource.Mt5Exness && x.Symbol == symbol && x.IsEnabled, token);
        if (monitored is null) return BadRequest(new ApiMessage("The exact MT5 instrument must be monitored and enabled."));
        var nativeSizingMode = await service.GetNativePositionSizingModeAsync(token);
        var budget = BacktestRequestBudgetCalculator.Calculate(request.Interval, request.StartUtc, request.EndUtc, nativeSizingMode, monitored.PaperCommissionPerLotPerSide ?? 0m, timeoutOptions.Value);
        logger?.LogInformation("Backtest workload budget for {BrokerSymbol} {Timeframe} from {StartUtc} to {EndUtc}: executionPages={ExecutionPages}, htf={PotentialHigherTimeframe}, htfPages={HigherTimeframePages}, historicalBudgetMs={HistoricalBudgetMilliseconds}, nativeSizingMode={NativeSizingMode}, executionCandles={ExecutionCandles}, nativeCandidates={NativeCandidates}, nativeLogicalOperations={NativeLogicalOperations}, nativeBudgetMs={NativeBudgetMilliseconds}, timeoutMs={TimeoutMilliseconds}.", symbol, request.Interval, request.StartUtc, request.EndUtc, budget.EstimatedExecutionHistoryPages, budget.PotentialHigherTimeframe, budget.EstimatedHigherTimeframeHistoryPages, budget.HistoricalDataBudget.TotalMilliseconds, budget.NativePositionSizingMode, budget.EstimatedExecutionCandleCount, budget.EstimatedNativeEconomicsCandidates, budget.EstimatedNativeEconomicsLogicalOperations, budget.NativeExecutionBudget.TotalMilliseconds, budget.ChosenRequestTimeout.TotalMilliseconds);
        var requestAborted = ControllerContext.HttpContext?.RequestAborted ?? token;
        using var deadline = new CancellationTokenSource(budget.ChosenRequestTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, requestAborted, deadline.Token);
        try { var run = await service.RunAsync(symbol, request.Interval, request.StartUtc, request.EndUtc, operation.Token); return CreatedAtAction(nameof(Get), new { id = run.Id }, BacktestResponseMapper.ToDetail(run)); }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (Mt5RiskPercentConfigurationException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (Mt5NativeEconomicsUnavailableException exception)
        {
            var operationName = exception.FailureReason == Mt5NativeRiskSizingFailure.MarginCalculationUnavailable ? "margin" : "stop-risk";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage($"MT5 RiskPercent {operationName} calculation became unavailable. No backtest was saved. Verify the MT5 bridge is connected and retry."));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || requestAborted.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return StatusCode(StatusCodes.Status504GatewayTimeout, new ApiMessage("Backtest exceeded its workload-aware processing deadline. Verify MT5 availability and retry.")); }
        catch (MarketDataProviderException exception) { return StatusCode(exception.Kind == MarketDataErrorKind.RateLimited ? 429 : exception.Kind == MarketDataErrorKind.Timeout ? 504 : 503, new ApiMessage(exception.Message.Contains("history", StringComparison.OrdinalIgnoreCase) ? "MT5 history is still loading. Retry shortly." : "MT5 historical market data is currently unavailable.")); }
    }
    private async Task<ActionResult<BacktestRunDetailResponse>> RunGrid(BacktestRequest request, CancellationToken token)
    {
        if (!Mt5NativeTimeframes.IsSupported(request.Interval) || request.StartUtc >= request.EndUtc || request.StartingBalance is not > 0m)
            return BadRequest(new ApiMessage("Grid requires a valid timeframe/date range and positive StartingBalance."));
        if (gridService is null) return StatusCode(503, new ApiMessage("Grid service is unavailable."));
        var aborted = ControllerContext.HttpContext?.RequestAborted ?? token;
        using var deadline = new CancellationTokenSource(GridBacktestBudget.Calculate(request.Interval, request.StartUtc, request.EndUtc, timeoutOptions.Value));
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, aborted, deadline.Token);
        try
        {
            var run = await gridService.RunAsync(request.Symbol, request.Interval, request.StartUtc, request.EndUtc, request.StartingBalance.Value, operation.Token, request.StrategyId!);
            return Created($"/api/backtests/grid/{run.Id}", GridBacktestResponses.ToDetail(run));
        }
        catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested || aborted.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return StatusCode(504, new ApiMessage("Grid backtest exceeded its workload deadline. No run was saved.")); }
        catch (MarketDataProviderException exception) { return StatusCode(exception.Kind == MarketDataErrorKind.RateLimited ? 429 : exception.Kind == MarketDataErrorKind.Timeout ? 504 : 503, new ApiMessage("MT5 history or economics are unavailable. No Grid run was saved.")); }
        catch (GridHistoricalExecutionException exception)
        {
            return StatusCode(exception.Diagnostic.Code is "InvalidRequest" or "InvalidSettings" or "InvalidNativeInstrument" or "InvalidNativeBar" ? 400 : 503,
                new ApiMessage($"Grid execution failed ({exception.Diagnostic.Code}). No run was saved."));
        }
        catch (GridNativeEconomicsUnavailableException) { return StatusCode(503, new ApiMessage("Grid native economics are unavailable. No run was saved.")); }
    }
    [HttpGet("economics-preview")]
    public async Task<IActionResult> EconomicsPreview([FromQuery] string symbol, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest(new ApiMessage("A broker symbol is required."));
        try { return Ok(await service.GetMt5EconomicsPreviewAsync(symbol.Trim(), token)); }
        catch (MarketDataProviderException exception) { return Ok(new Mt5HistoricalBacktestEconomicsPreview(false, exception.Message, symbol.Trim())); }
    }
    [HttpGet] public async Task<IActionResult> List(CancellationToken token) => Ok((await service.ListAsync(token)).Select(BacktestResponseMapper.ToSummary));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken token) => (await service.GetAsync(id, token)) is { } run ? Ok(BacktestResponseMapper.ToDetail(run)) : NotFound(new ApiMessage("Backtest not found."));
    [HttpGet("{id:int}/export/excel")]
    public async Task<IActionResult> ExportExcel(int id, CancellationToken token)
    {
        var workbook = await BacktestExcelExport.CreateAsync(database, id, token);
        if (workbook is null) return NotFound(new ApiMessage("Backtest not found."));
        return File(workbook.Bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ema-bot-backtest-{id}-{FilenamePart(workbook.Symbol)}-{FilenamePart(workbook.Interval)}.xlsx");
    }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken token) => await service.DeleteAsync(id, token) ? NoContent() : NotFound(new ApiMessage("Backtest not found."));

    private static string FilenamePart(string value)
    {
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrEmpty(safe) ? "unknown" : safe;
    }
}
public static class HistoricalStrategyIds
{
    public const string Ema = "EMA_TREND_V1";
    public const string Grid = Strategy.Grid.GridRangeSettings.StrategyId;
    public const string GridBreakoutGuard = Strategy.Grid.GridHistoricalStrategyProfile.BreakoutGuardStrategyId;
    public const string GridResearch = Strategy.Grid.GridHistoricalStrategyProfile.ResearchStrategyId;
}
public sealed record BacktestRequest(string Symbol, string Interval, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    string? StrategyId = null, decimal? StartingBalance = null);
