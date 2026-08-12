using System.IO.Compression;
using System.Security;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/strategy-optimizer")]
public sealed class StrategyOptimizerController(EmaBotDbContext database, StrategyOptimizationService service, StrategyRegimeDiagnosticsService regimeDiagnostics, ILogger<StrategyOptimizerController> logger) : ControllerBase
{
    [HttpGet("options")] public async Task<IActionResult> Options(CancellationToken token) => Ok(await service.GetOptionsAsync(token));
    [HttpPost("runs")] public async Task<IActionResult> Start(StrategyOptimizerStartRequest request, CancellationToken token) { try { var run = await service.StartAsync(request, token); return Accepted($"api/strategy-optimizer/runs/{run.Id}", Summary(run)); } catch (ArgumentException exception) { return BadRequest(new ApiMessage(exception.Message)); } catch (InvalidOperationException exception) { return Conflict(new ApiMessage(exception.Message)); } }
    [HttpGet("runs")] public async Task<IActionResult> Runs(CancellationToken token) => Ok((await database.StrategyOptimizationRuns.AsNoTracking().Include(run => run.Candidates).OrderByDescending(run => run.CreatedAtUtc).Take(30).ToListAsync(token)).Select(Summary));
    [HttpGet("runs/{id:int}")] public async Task<IActionResult> Run(int id, CancellationToken token) { var run = await database.StrategyOptimizationRuns.AsNoTracking().Include(value => value.Candidates).SingleOrDefaultAsync(value => value.Id == id, token); return run is null ? NotFound(new ApiMessage("Optimization run not found.")) : Ok(Summary(run)); }
    [HttpGet("runs/{id:int}/candidates")] public async Task<IActionResult> Candidates(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken token = default) { page=Math.Max(page,1); pageSize=Math.Clamp(pageSize,1,100); var all=(await database.StrategyOptimizationCandidates.AsNoTracking().Where(candidate=>candidate.StrategyOptimizationRunId==id).ToListAsync(token)).OrderBy(candidate=>candidate.RobustRank??int.MaxValue).ThenByDescending(candidate=>candidate.Validation.NetProfitFactor).ThenBy(candidate=>candidate.Id).ToArray(); var baseline=all.FirstOrDefault(candidate=>candidate.IsBaseline); return Ok(new { total=all.Length, items=all.Skip((page-1)*pageSize).Take(pageSize).Select(candidate=>Candidate(candidate,baseline)) }); }
    [HttpGet("runs/{id:int}/candidates/{candidateId:int}")] public async Task<IActionResult> CandidateDetail(int id, int candidateId, CancellationToken token) { var candidate=await database.StrategyOptimizationCandidates.AsNoTracking().Include(value=>value.MarketResults).SingleOrDefaultAsync(value=>value.Id==candidateId&&value.StrategyOptimizationRunId==id,token); var baseline=await database.StrategyOptimizationCandidates.AsNoTracking().SingleOrDefaultAsync(value=>value.StrategyOptimizationRunId==id&&value.IsBaseline,token); return candidate is null ? NotFound(new ApiMessage("Candidate not found.")) : Ok(new { candidate=Candidate(candidate,baseline), markets=candidate.MarketResults.Select(Market) }); }
    [HttpPost("runs/{id:int}/cancel")] public async Task<IActionResult> Cancel(int id, CancellationToken token)
    {
        if (await service.CancelAsync(id, token)) return Accepted();
        return await database.StrategyOptimizationRuns.AsNoTracking().AnyAsync(run => run.Id == id, token)
            ? Conflict(new ApiMessage("The optimization run is already terminal."))
            : NotFound(new ApiMessage("Optimization run not found."));
    }
    [HttpGet("runs/{id:int}/excel")] public async Task<IActionResult> Excel(int id, CancellationToken token)
    {
        var total = Stopwatch.StartNew();
        try
        {
            var load = Stopwatch.StartNew();
            var run = await database.StrategyOptimizationRuns.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, token);
            if (run is null) return NotFound(new ApiMessage("Optimization run not found."));
            if (run.Status != StrategyOptimizationStatus.Completed) return Conflict(new ApiMessage("The optimizer Excel export is available after the research run completes."));
            var candidates = await database.StrategyOptimizationCandidates.AsNoTracking().AsSplitQuery().Include(candidate => candidate.MarketResults).Where(candidate => candidate.StrategyOptimizationRunId == id).ToListAsync(token);
            var trades = await database.StrategyOptimizationTrades.AsNoTracking().Where(trade => trade.StrategyOptimizationRunId == id).ToListAsync(token);
            load.Stop();
            // All entities are detached; assembling navigations is export-only and never writes data back.
            run.Candidates = candidates; run.Trades = trades;
            var workbook = Stopwatch.StartNew(); var bytes = StrategyOptimizerWorkbook.Create(run); workbook.Stop();
            logger.LogInformation("Optimizer export {RunId}: {Candidates} candidates, {Markets} markets, {Trades} trades, DB load {Load}, workbook {Workbook}, {Bytes} bytes, total {Total}.", id, candidates.Count, candidates.Sum(candidate => candidate.MarketResults.Count), trades.Count, load.Elapsed, workbook.Elapsed, bytes.Length, total.Elapsed);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ema-bot-optimizer-{id}.xlsx");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Optimizer Excel export {RunId} failed after {Elapsed}.", id, total.Elapsed);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiMessage("The optimizer Excel export could not be generated. Check the API log for details."));
        }
    }
    [HttpGet("runs/{runId:int}/candidates/{candidateId:int}/regime-excel")]
    public async Task<IActionResult> RegimeExcel(int runId, int candidateId, CancellationToken token)
    {
        var total = Stopwatch.StartNew();
        try
        {
            var data = await regimeDiagnostics.CreateAsync(runId, candidateId, token);
            if (data is null) return NotFound(new ApiMessage("Completed optimizer run or candidate not found."));
            var workbook = Stopwatch.StartNew(); var bytes = StrategyRegimeWorkbook.Create(data); workbook.Stop();
            logger.LogInformation("Regime diagnostics export run {RunId}, candidate {CandidateId}: {Trades} trades, workbook {WorkbookDuration}, {WorkbookBytes} bytes, total {TotalDuration}.", runId, candidateId, data.Trades.Count, workbook.Elapsed, bytes.Length, total.Elapsed);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ema-bot-regime-{runId}-{candidateId}.xlsx");
        }
        catch (Exception exception) { logger.LogError(exception, "Regime diagnostic export for run {RunId}, candidate {CandidateId} failed.", runId, candidateId); return StatusCode(StatusCodes.Status500InternalServerError, new ApiMessage("The regime diagnostic export could not be generated. Check the API log for details.")); }
    }

    private object Summary(StrategyOptimizationRun run) { var runtime = service.RuntimeStatusFor(run.Id, run.Status); return new { run.Id, status=run.Status.ToString(), phase=runtime.Phase, runtime.FinalizationTotalMarkets, runtime.FinalizationCompletedMarkets, run.CreatedAtUtc, run.StartedAtUtc, run.CompletedAtUtc, run.FailureMessage, run.RequestedStartUtc, run.RequestedEndUtc, run.CandidateCount, run.MarketCount, run.TotalWork, run.CompletedWork, progress=run.TotalWork==0?0m:(decimal)run.CompletedWork/run.TotalWork*100m, run.RecommendedCandidateId, assumptions=new { run.SimulatedAccountBalanceUsdt, run.FixedOrderSizeUsdt, run.MarginPerTradePercent, run.Leverage, run.FeePercentPerSide, positionSizingMode=run.PositionSizingMode.ToString() }, robustCandidateCount=run.Candidates.Count(candidate=>candidate.RobustCandidate) }; }
    private static object Candidate(StrategyOptimizationCandidate value, StrategyOptimizationCandidate? baseline = null) => new { value.Id,value.RiskReward,value.MinEmaGapPercent,value.MaxStopDistancePercent,value.WaitForConfirmationCandle,value.UseEma100Filter,value.TrailingStopEnabled,value.IsBaseline,value.RobustCandidate,value.RobustRank,value.ProfitableMarketRatio,full=value.Full,development=value.Development,validation=value.Validation, baselineDeltas=baseline is null ? null : new { deltaNetPnlUsdt=value.Validation.NetPnlUsdt-baseline.Validation.NetPnlUsdt,deltaNetReturnPercent=value.Validation.NetReturnPercent-baseline.Validation.NetReturnPercent,deltaNetProfitFactor=(value.Validation.NetProfitFactor ?? 0m)-(baseline.Validation.NetProfitFactor ?? 0m),deltaMaxDrawdownPercent=value.Validation.MaxDrawdownPercent-baseline.Validation.MaxDrawdownPercent,deltaTradeCount=value.Validation.TotalTrades-baseline.Validation.TotalTrades,deltaWinRatePercent=value.Validation.WinRatePercent-baseline.Validation.WinRatePercent } };
    private static object Market(StrategyOptimizationMarketResult value) => new { value.Id,value.Symbol,value.Timeframe,full=value.Full,development=value.Development,validation=value.Validation };
}

public static class StrategyOptimizerWorkbook
{
    public static byte[] Create(StrategyOptimizationRun run)
    {
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture; return CreateInvariant(run); }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
    private static byte[] CreateInvariant(StrategyOptimizationRun run)
    {
        var baseline = run.Candidates.SingleOrDefault(candidate => candidate.IsBaseline);
        var sheets = new List<(string Name, IEnumerable<IEnumerable<string?>> Rows)>
        {
            ("Overview", [["Run ID",run.Id.ToString()],["Status",run.Status.ToString()],["Period",$"{run.RequestedStartUtc:O} to {run.RequestedEndUtc:O}"],["Candidates",run.CandidateCount.ToString()],["Markets",run.MarketCount.ToString()],["Recommended candidate",run.RecommendedCandidateId?.ToString() ?? "No robust candidate found"]]),
            ("Ranked Candidates", new[] { new[] { "Candidate ID","Robust Rank","Baseline","Robust Candidate","R:R","Min EMA Gap %","Max Stop Distance %","Confirmation","EMA100","Trailing","Full Trades","Full Gross","Full Fees","Full Net","Full Gross PF","Full Net PF","Development Trades","Development Gross","Development Fees","Development Net","Development Gross PF","Development Net PF","Validation Trades","Validation Win %","Validation Gross","Validation Fees","Validation Net","Validation Net %","Validation Gross PF","Validation Net PF","Validation Max DD %","Profitable Market Ratio","Median Expected Net Target R","Minimum Expected Net Target R","Average Expected Net Target R","Delta Net PnL","Delta Net %","Delta Net PF","Delta Max DD %","Delta Trade Count","Delta Win %" } }.Concat(run.Candidates.OrderBy(candidate=>candidate.RobustRank??int.MaxValue).Select(candidate => new[] { candidate.Id.ToString(),candidate.RobustRank?.ToString(),candidate.IsBaseline.ToString(),candidate.RobustCandidate.ToString(),candidate.RiskReward.ToString(),candidate.MinEmaGapPercent.ToString(),candidate.MaxStopDistancePercent.ToString(),candidate.WaitForConfirmationCandle.ToString(),candidate.UseEma100Filter.ToString(),candidate.TrailingStopEnabled.ToString(),candidate.Full.TotalTrades.ToString(),candidate.Full.GrossPnlUsdt.ToString(),candidate.Full.TotalFeesUsdt.ToString(),candidate.Full.NetPnlUsdt.ToString(),candidate.Full.GrossProfitFactor?.ToString(),candidate.Full.NetProfitFactor?.ToString(),candidate.Development.TotalTrades.ToString(),candidate.Development.GrossPnlUsdt.ToString(),candidate.Development.TotalFeesUsdt.ToString(),candidate.Development.NetPnlUsdt.ToString(),candidate.Development.GrossProfitFactor?.ToString(),candidate.Development.NetProfitFactor?.ToString(),candidate.Validation.TotalTrades.ToString(),candidate.Validation.WinRatePercent.ToString(),candidate.Validation.GrossPnlUsdt.ToString(),candidate.Validation.TotalFeesUsdt.ToString(),candidate.Validation.NetPnlUsdt.ToString(),candidate.Validation.NetReturnPercent.ToString(),candidate.Validation.GrossProfitFactor?.ToString(),candidate.Validation.NetProfitFactor?.ToString(),candidate.Validation.MaxDrawdownPercent.ToString(),candidate.ProfitableMarketRatio.ToString(),candidate.Validation.MedianExpectedNetTargetR.ToString(),candidate.Validation.MinimumExpectedNetTargetR.ToString(),candidate.Validation.AverageExpectedNetTargetR.ToString(),(candidate.Validation.NetPnlUsdt-(baseline?.Validation.NetPnlUsdt ?? 0m)).ToString(),(candidate.Validation.NetReturnPercent-(baseline?.Validation.NetReturnPercent ?? 0m)).ToString(),((candidate.Validation.NetProfitFactor ?? 0m)-(baseline?.Validation.NetProfitFactor ?? 0m)).ToString(),(candidate.Validation.MaxDrawdownPercent-(baseline?.Validation.MaxDrawdownPercent ?? 0m)).ToString(),(candidate.Validation.TotalTrades-(baseline?.Validation.TotalTrades ?? 0)).ToString(),(candidate.Validation.WinRatePercent-(baseline?.Validation.WinRatePercent ?? 0m)).ToString() }))),
            ("Market Results", new[] { new[] { "Candidate","Symbol","Timeframe","Full Net","Development Net","Development Net PF","Validation Net","Validation Net PF","Validation Fees","Validation Trades" } }.Concat(run.Candidates.SelectMany(candidate=>candidate.MarketResults.Select(market=>new[] { candidate.Id.ToString(),market.Symbol,market.Timeframe,market.Full.NetPnlUsdt.ToString(),market.Development.NetPnlUsdt.ToString(),market.Development.NetProfitFactor?.ToString(),market.Validation.NetPnlUsdt.ToString(),market.Validation.NetProfitFactor?.ToString(),market.Validation.TotalFeesUsdt.ToString(),market.Validation.TotalTrades.ToString() })))),
            ("Best By Market", BestRows(run)),
            ("Diagnostics", new[] { new[] { "Candidate","Segment","TotalCrossovers","LongSignals","ShortSignals","ConfirmationFailed","RejectedByEma100","RejectedByEmaGap","RejectedByStopDistance","RejectedByFees","InvalidStopLoss","SkippedWhilePositionOpen","NoEntryCandle" } }.Concat(run.Candidates.SelectMany(candidate => new[] { ("Full",candidate.Full), ("Development",candidate.Development), ("Validation",candidate.Validation) }.Select(value => new[] { candidate.Id.ToString(),value.Item1,value.Item2.TotalCrossovers.ToString(),value.Item2.LongSignals.ToString(),value.Item2.ShortSignals.ToString(),value.Item2.ConfirmationFailed.ToString(),value.Item2.RejectedByEma100.ToString(),value.Item2.RejectedByEmaGap.ToString(),value.Item2.RejectedByStopDistance.ToString(),value.Item2.RejectedByFees.ToString(),value.Item2.InvalidStopLoss.ToString(),value.Item2.SkippedWhilePositionOpen.ToString(),value.Item2.NoEntryCandle.ToString() })))),
            ("Top Candidate Trades", new[] { new[] { "Candidate ID","Symbol","Timeframe","Direction","IsReentry","EntryTimeUtc","ExitTimeUtc","HoldingMinutes","EntryPrice","ExitPrice","InitialStopLoss","FinalStopLoss","OriginalTakeProfit","FinalTakeProfit","GrossPnlUsdt","TotalFeesUsdt","NetPnlUsdt","NetRMultiple","ExitReason","Signal EMA9","Signal EMA15","Signal EMA100","Signal Gap","ExpectedNetTargetR" } }.Concat(run.Trades.Select(trade=>new[] { trade.StrategyOptimizationCandidateId.ToString(),trade.Symbol,trade.Timeframe,trade.Direction.ToString(),trade.IsReentry.ToString(),trade.EntryTimeUtc.ToString("O"),trade.ExitTimeUtc.ToString("O"),(trade.ExitTimeUtc-trade.EntryTimeUtc).TotalMinutes.ToString(),trade.EntryPrice.ToString(),trade.ExitPrice.ToString(),trade.InitialStopLoss.ToString(),trade.FinalStopLoss.ToString(),trade.OriginalTakeProfit.ToString(),trade.FinalTakeProfit.ToString(),trade.GrossPnlUsdt.ToString(),trade.TotalFeesUsdt.ToString(),trade.NetPnlUsdt.ToString(),trade.NetRMultiple.ToString(),trade.ExitReason.ToString(),trade.SignalEma9?.ToString(),trade.SignalEma15?.ToString(),trade.SignalEma100?.ToString(),trade.SignalGapPercent?.ToString(),trade.ExpectedNetTargetR.ToString() }))),
            ("Run Configuration", [["Symbols",run.SymbolsJson],["Timeframes",run.TimeframesJson],["Parameter grid",run.GridJson],["Position sizing mode",run.PositionSizingMode.ToString()],["Balance",run.SimulatedAccountBalanceUsdt.ToString()],["Fixed notional",run.FixedOrderSizeUsdt.ToString()],["Margin per trade",run.MarginPerTradePercent.ToString()],["Leverage",run.Leverage.ToString()],["Fee per side",run.FeePercentPerSide.ToString()]])
        };
        using var stream=new MemoryStream(); using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){ Write(zip,"[Content_Types].xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"+string.Concat(Enumerable.Range(1,sheets.Count).Select(i=>$"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))+"</Types>"); Write(zip,"_rels/.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"); Write(zip,"xl/workbook.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"+string.Concat(sheets.Select((sheet,index)=>$"<sheet name=\"{sheet.Name}\" sheetId=\"{index+1}\" r:id=\"rId{index+1}\"/>"))+"</sheets></workbook>"); Write(zip,"xl/_rels/workbook.xml.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"+string.Concat(Enumerable.Range(1,sheets.Count).Select(i=>$"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>"))+"</Relationships>"); foreach(var (sheet,index) in sheets.Select((sheet,index)=>(sheet,index+1))) WriteSheet(zip,$"xl/worksheets/sheet{index}.xml",sheet.Rows); } return stream.ToArray();
    }
    private static IEnumerable<IEnumerable<string?>> BestRows(StrategyOptimizationRun run)
    {
        var rows = new List<IEnumerable<string?>> { new[] { "Scope", "Market", "Candidate ID", "Validation Net PF", "Validation Trades", "Sample Qualified" } };
        var results = run.Candidates.SelectMany(candidate => candidate.MarketResults.Select(market => new { Candidate = candidate, Market = market }));
        Add("Global", "All selected markets", run.Candidates.Select(candidate => (candidate, candidate.Validation)));
        foreach (var group in results.GroupBy(value => value.Market.Symbol)) Add("Symbol", group.Key, group.Select(value => (value.Candidate, value.Market.Validation)));
        foreach (var group in results.GroupBy(value => value.Market.Timeframe)) Add("Timeframe", group.Key, group.Select(value => (value.Candidate, value.Market.Validation)));
        foreach (var group in results.GroupBy(value => $"{value.Market.Symbol} {value.Market.Timeframe}")) Add("Symbol + timeframe", group.Key, group.Select(value => (value.Candidate, value.Market.Validation)));
        return rows;

        void Add(string scope, string market, IEnumerable<(StrategyOptimizationCandidate Candidate, OptimizationMetrics Metrics)> candidates)
        {
            var best = candidates.OrderByDescending(value => value.Metrics.NetProfitFactor).ThenByDescending(value => value.Metrics.NetReturnPercent).ThenBy(value => value.Candidate.Id).FirstOrDefault();
            rows.Add(new[] { scope, market, best.Candidate?.Id.ToString(), best.Metrics?.NetProfitFactor?.ToString(), best.Metrics?.TotalTrades.ToString(), (best.Metrics?.TotalTrades >= 10).ToString() });
        }
    }
    private static void Write(ZipArchive archive,string name,string text){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),Encoding.UTF8);writer.Write(text);}
    private static void WriteSheet(ZipArchive archive, string name, IEnumerable<IEnumerable<string?>> rows)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach (var row in rows)
        {
            writer.Write("<row>");
            foreach (var value in row) { writer.Write("<c t=\"inlineStr\"><is><t>"); writer.Write(SecurityElement.Escape(value ?? string.Empty)); writer.Write("</t></is></c>"); }
            writer.Write("</row>");
        }
        writer.Write("</sheetData></worksheet>");
    }
}
