using System.Net;
using System.Net.Http.Json;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class GridBreakoutGuardResearchTests
{
    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static GridBreakoutGuardCandidate Candidate(string id) => GridBreakoutGuardCandidate.Guards.Single(c => c.Id == id);
    private static void SetSignal(GridBacktestTelemetry t, bool value)
    {
        t.CloseBeyondFrozenBoundary = t.AdverseExtremeBeyondFrozenBoundary = value;
        t.AdxDeltaFromQualification = value ? 2m : null; t.CandleTrueRangeAtrRatio = value ? 2m : null;
        t.ConsecutiveAdverseCloses = value ? 3 : 0;
    }
    private static async Task<GridBacktestRun> Source(bool isLong = true, string exit = "TakeProfit", int levels = 5)
    {
        var result = await TelemetryFixture.Run(levels, isLong, exit);
        var run = GridBacktestPersistence.Map(result, DateTimeOffset.UtcNow);
        foreach (var t in run.Cycles.SelectMany(c => c.Telemetry)) SetSignal(t, true);
        return run;
    }
    private static async Task Save(EmaBotDbContext db, GridBacktestRun run)
    { db.GridBacktestRuns.Add(run); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }

    [Fact]
    public void CatalogIsExactlySevenFrozenCandidatesPlusSeparateControl()
    {
        Assert.Equal(new[] { "L3_BOUNDARY_CLOSE", "L3_BOUNDARY_EXTREME_ADX10", "L3_ADX10_TR15", "L3_ADX15_TR15", "L3_ADX15_ADVERSE2", "L3_COMPOSITE", "L2_COMPOSITE" }, GridBreakoutGuardCandidate.Guards.Select(c => c.Id));
        Assert.Equal(new[] { 3, 3, 3, 3, 3, 3, 2 }, GridBreakoutGuardCandidate.Guards.Select(c => c.MinimumFilledLevel));
        Assert.Empty(typeof(GridBreakoutGuardCandidate).GetConstructors());
        Assert.False(GridBreakoutGuardCandidate.Control.Matches(new() { MaxFilledLevelAfterBar = 5, CloseBeyondFrozenBoundary = true }));
    }

    [Theory]
    [InlineData("L3_BOUNDARY_CLOSE", true, false, null, null, 0, true)]
    [InlineData("L3_BOUNDARY_CLOSE", false, true, 2.0, 2.0, 3, false)]
    [InlineData("L3_BOUNDARY_EXTREME_ADX10", false, true, 1.0, null, 0, true)]
    [InlineData("L3_BOUNDARY_EXTREME_ADX10", true, false, 2.0, 2.0, 0, false)]
    [InlineData("L3_BOUNDARY_EXTREME_ADX10", false, true, null, 2.0, 0, false)]
    [InlineData("L3_ADX10_TR15", false, false, 1.0, 1.5, 0, true)]
    [InlineData("L3_ADX10_TR15", false, false, .99, 1.5, 0, false)]
    [InlineData("L3_ADX10_TR15", false, false, 1.0, 1.49, 0, false)]
    [InlineData("L3_ADX10_TR15", false, false, null, 2.0, 0, false)]
    [InlineData("L3_ADX10_TR15", false, false, 2.0, null, 0, false)]
    [InlineData("L3_ADX15_TR15", false, false, 1.5, 1.5, 0, true)]
    [InlineData("L3_ADX15_TR15", false, false, 1.49, 2.0, 0, false)]
    [InlineData("L3_ADX15_TR15", false, false, 2.0, null, 0, false)]
    [InlineData("L3_ADX15_ADVERSE2", false, false, 1.5, null, 2, true)]
    [InlineData("L3_ADX15_ADVERSE2", false, false, 1.49, null, 2, false)]
    [InlineData("L3_ADX15_ADVERSE2", false, false, 2.0, 2.0, 1, false)]
    [InlineData("L3_ADX15_ADVERSE2", false, false, null, 2.0, 2, false)]
    [InlineData("L3_COMPOSITE", true, false, null, null, 0, true)]
    [InlineData("L3_COMPOSITE", false, false, 1.5, 1.5, 0, true)]
    [InlineData("L3_COMPOSITE", false, true, null, 2.0, 3, false)]
    [InlineData("L2_COMPOSITE", true, false, null, null, 0, true)]
    [InlineData("L2_COMPOSITE", false, false, 1.5, 1.5, 0, true)]
    [InlineData("L2_COMPOSITE", false, true, 2.0, null, 3, false)]
    public void ExactPredicatesAndNullHandling(string id, bool close, bool extreme, double? adx, double? tr, int adverse, bool expected)
    {
        var row = new GridBacktestTelemetry { MaxFilledLevelAfterBar = 3, CloseBeyondFrozenBoundary = close,
            AdverseExtremeBeyondFrozenBoundary = extreme, AdxDeltaFromQualification = (decimal?)adx,
            CandleTrueRangeAtrRatio = (decimal?)tr, ConsecutiveAdverseCloses = adverse };
        Assert.Equal(expected, Candidate(id).Matches(row));
        row.MaxFilledLevelAfterBar = 1; Assert.False(Candidate(id).Matches(row));
        row.MaxFilledLevelAfterBar = 2; Assert.Equal(id == "L2_COMPOSITE" && expected, Candidate(id).Matches(row));
        row.MaxFilledLevelAfterBar = 5; row.ExitReasonThisBar = "EmergencyStop"; Assert.False(Candidate(id).Matches(row));
        row.ExitReasonThisBar = "TakeProfit"; Assert.False(Candidate(id).Matches(row));
    }

    [Theory]
    [InlineData(true, "TakeProfit", "WinnerCutEarly")] [InlineData(false, "TakeProfit", "WinnerCutEarly")]
    [InlineData(true, "EmergencyStop", "EmergencyStopIntercepted")] [InlineData(false, "EmergencyStop", "EmergencyStopIntercepted")]
    [InlineData(true, "EndOfData", "EndOfDataIntercepted")] [InlineData(false, "EndOfData", "EndOfDataIntercepted")]
    public async Task EarliestCompletedTriggerUsesNativeOpenLegsAndExactCommission(bool isLong, string exit, string classification)
    {
        await using var db = Database(); var run = await Source(isLong, exit); await Save(db, run);
        var native = new Native(); var simulator = new GridBreakoutGuardShadowSimulator(db, native);
        var result = await simulator.SimulateAsync(run.Id, default);
        var row = result.Baskets.Single(b => b.CandidateId == "L3_BOUNDARY_CLOSE");
        var sourceBasket = run.Cycles.Single().Baskets.Single(); var trigger = run.Cycles.Single().Telemetry[3];
        Assert.True(row.Triggered); Assert.Equal(trigger.TimeUtc, row.TriggerTimeUtc); Assert.Equal(3, row.TriggerLevel);
        Assert.Equal(4, row.MaxFilledLevelAtTrigger); Assert.Equal(4, row.ShadowOpenLegCount);
        Assert.Equal(trigger.BidClose + (isLong ? 0m : trigger.SpreadPrice), row.ShadowExitPrice);
        Assert.Equal(classification, row.Classification);
        var included = sourceBasket.Legs.Where(l => l.FillTimeUtc <= trigger.TimeUtc).ToArray();
        Assert.Equal(4, included.Length); Assert.Contains(included, l => l.FillTimeUtc == trigger.TimeUtc);
        Assert.DoesNotContain(native.Requests, r => r.OpenPrice == sourceBasket.Legs.Single(l => l.LevelNumber == 5).FillPrice);
        Assert.Equal(4 * Native.Profit, row.ShadowGrossPnl); // Deliberately unrelated to price formula.
        Assert.Equal(included.Sum(l => l.EntryCommission), row.ShadowEntryCommission);
        Assert.Equal(included.Sum(l => l.Lots * run.CommissionPerLotPerSide), row.ShadowExitCommission);
        Assert.NotEqual(sourceBasket.Legs.Sum(l => l.ExitCommission), row.ShadowExitCommission);
        Assert.Equal(row.ShadowGrossPnl - row.ShadowEntryCommission - row.ShadowExitCommission, row.ShadowNetPnl);
        Assert.Equal(row.ShadowNetPnl - row.ActualNetPnl, row.DeltaVsActualNetPnl);
        foreach (var leg in included)
            Assert.Contains(new Mt5CalculateProfitRequest(run.BrokerSymbol, isLong ? "Long" : "Short", leg.Lots, leg.FillPrice, row.ShadowExitPrice!.Value), native.Requests);
        var earlier = result.Baskets.Single(b => b.CandidateId == "L2_COMPOSITE");
        Assert.Equal(run.Cycles[0].Telemetry[2].TimeUtc, earlier.TriggerTimeUtc); Assert.Equal(2, earlier.ShadowOpenLegCount);
        Assert.Equal(6, result.NativeProfitCallCount); Assert.Equal(6, result.UniqueNativeProfitRequestCount);
        Assert.Equal(6, native.Requests.Count); // Six L3 rules share four requests, L2 adds two.
        await simulator.SimulateAsync(run.Id, default); Assert.Equal(12, native.Requests.Count); // No cross-invocation cache.
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("TakeProfit")] [InlineData("EmergencyStop")] [InlineData("EndOfData")]
    public async Task FinalActualExitCandleCannotBeReplacedAndNoTriggerUsesActualOutcome(string exit)
    {
        await using var db = Database(); var run = await Source(exit: exit);
        var telemetry = run.Cycles[0].Telemetry;
        foreach (var t in telemetry) SetSignal(t, false);
        SetSignal(telemetry[^1], true);
        await Save(db, run); var native = new Native();
        var result = await new GridBreakoutGuardShadowSimulator(db, native).SimulateAsync(run.Id, default);
        Assert.Empty(native.Requests);
        Assert.All(result.Baskets, b =>
        {
            Assert.False(b.Triggered); Assert.Equal("NoTrigger", b.Classification); Assert.Equal(b.ActualNetPnl, b.ShadowNetPnl);
            Assert.Equal(0m, b.DeltaVsActualNetPnl); Assert.Null(b.ShadowOpenLegCount); Assert.Null(b.ShadowExitPrice);
            foreach (var property in typeof(GridBreakoutGuardBasketResult).GetProperties().Where(p => p.Name.StartsWith("Trigger") && p.Name != "Triggered"))
                Assert.Null(property.GetValue(b));
        });
    }

    [Theory]
    [InlineData("old")] [InlineData("status")] [InlineData("strategy")] [InlineData("currency")] [InlineData("broker")]
    [InlineData("direction")] [InlineData("sequence")] [InlineData("duplicateTime")] [InlineData("level")] [InlineData("newFill")]
    [InlineData("missingLeg")] [InlineData("missingBasket")] [InlineData("exit")] [InlineData("price")] [InlineData("spread")]
    [InlineData("net")] [InlineData("pf")] [InlineData("lot")] [InlineData("missingBar")]
    public async Task InvalidSavedEvidenceFailsBeforeNativeCalls(string invalid)
    {
        await using var db = Database(); var run = await Source(); var cycle = run.Cycles[0]; var b = cycle.Baskets[0]; var t = cycle.Telemetry[0];
        switch (invalid)
        {
            case "old": cycle.Telemetry.Clear(); break;
            case "status": run.Status = "Running"; break;
            case "strategy": run.StrategyId = "EMA_TREND_V1"; break;
            case "currency": run.AccountCurrency = ""; break;
            case "broker": run.BrokerSymbol = ""; break;
            case "direction": t.Direction = "Short"; break;
            case "sequence": t.Sequence = 50; break;
            case "duplicateTime": cycle.Telemetry[1].TimeUtc = t.TimeUtc; break;
            case "level": t.MaxFilledLevelAfterBar = 5; break;
            case "newFill": t.NewFillCount = 0; break;
            case "missingLeg": b.Legs.RemoveAt(0); break;
            case "missingBasket": cycle.Baskets.Clear(); break;
            case "exit": t.ExitReasonThisBar = "EmergencyStop"; break;
            case "price": t.BidClose = -1m; break;
            case "spread": t.SpreadPrice = -1m; break;
            case "net": run.NetPnl += 1m; break;
            case "pf": run.NetProfitFactor = 123m; break;
            case "lot": b.Legs[0].Lots = 0m; break;
            case "missingBar": cycle.Telemetry.RemoveAt(0); break;
        }
        await Save(db, run); var native = new Native();
        var ex = await Assert.ThrowsAsync<GridGuardResearchException>(() => new GridBreakoutGuardShadowSimulator(db, native).SimulateAsync(run.Id, default));
        Assert.Equal(400, ex.StatusCode); Assert.Empty(native.Requests);
        if (invalid == "old") Assert.Equal("Grid breakout research requires a telemetry-enabled Grid backtest.", ex.Message);
    }

    [Fact]
    public async Task MissingRunIs404AndCancellationPropagates()
    {
        await using var db = Database(); var service = new GridBreakoutGuardShadowSimulator(db, new Native());
        Assert.Equal(404, (await Assert.ThrowsAsync<GridGuardResearchException>(() => service.SimulateAsync(99, default))).StatusCode);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SimulateAsync(99, cts.Token));
    }

    [Theory]
    [InlineData("round", false)] [InlineData("currency", true)] [InlineData("symbol", true)] [InlineData("direction", true)]
    [InlineData("price", true)] [InlineData("throw", true)]
    public async Task G21NativeEchoValidationAndSafeErrorsArePreserved(string mode, bool rejects)
    {
        await using var db = Database(); var run = await Source(); run.Cycles[0].Baskets[0].Legs[0].FillPrice += .000000000023m;
        await Save(db, run); var service = new GridBreakoutGuardShadowSimulator(db, new Native { Mode = mode });
        if (rejects)
        {
            var ex = await Assert.ThrowsAsync<GridGuardResearchException>(() => service.SimulateAsync(run.Id, default));
            Assert.Equal(503, ex.StatusCode); Assert.DoesNotContain("secret", ex.Message);
        }
        else Assert.Equal(8, (await service.SimulateAsync(run.Id, default)).Candidates.Count);
    }

    [Fact]
    public void SimulatorDependsOnlyOnSavedDatabaseAndNativeCalculator()
    {
        Assert.Equal(new[] { typeof(EmaBotDbContext), typeof(IMt5TradeCalculator) },
            typeof(GridBreakoutGuardShadowSimulator).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task ControlAggregatesWorkbookAndReadOnlySourceReconcile(int levels)
    {
        await using var db = Database(); var run = await Source(levels: levels);
        foreach (var reason in new[] { "EmergencyStop", "EndOfData" })
        {
            var another = await Source(exit: reason, levels: levels); var cycle = another.Cycles.Single();
            cycle.Sequence = run.Cycles.Count; run.Cycles.Add(cycle);
        }
        var actual = run.Cycles.SelectMany(c => c.Baskets).ToArray();
        run.BasketCount = actual.Length; run.NetPnl = actual.Sum(b => b.NetPnl); run.GrossPnl = actual.Sum(b => b.GrossPnl);
        run.TotalCommission = actual.Sum(b => b.Commission); run.EndingBalance = run.StartingBalance + run.NetPnl;
        run.NetProfitFactor = GridGuardResearchSourceValidation.ProfitFactor(actual.Select(b => b.NetPnl));
        run.GrossProfitFactor = GridGuardResearchSourceValidation.ProfitFactor(actual.Select(b => b.GrossPnl));
        await Save(db, run);
        async Task<string> Saved() => JsonSerializer.Serialize(await GridBacktestService.Graph(db)
            .Include(r => r.Cycles).ThenInclude(c => c.Telemetry).AsNoTracking().SingleAsync());
        var before = await Saved(); var normalBefore = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!;
        var native = new Native { Value = -2m };
        var result = await new GridBreakoutGuardShadowSimulator(db, native).SimulateAsync(run.Id, default);
        var control = result.Candidates[0]; Assert.Equal("NO_GUARD_CONTROL", control.CandidateId);
        Assert.Equal(run.BasketCount, control.BasketCount); Assert.Equal(run.NetPnl, control.ActualNetPnl);
        Assert.Equal(run.NetPnl, control.FixedPathShadowNetPnl); Assert.Equal(run.NetProfitFactor, control.FixedPathShadowProfitFactor);
        Assert.Equal(run.GrossProfitFactor, control.ActualGrossProfitFactor);
        Assert.Equal(actual.Where(b => b.GrossPnl > 0).Sum(b => b.GrossPnl), control.ActualGrossProfit);
        Assert.Equal(-actual.Where(b => b.GrossPnl < 0).Sum(b => b.GrossPnl), control.ActualGrossLoss);
        Assert.Equal(0m, control.FixedPathDeltaVsActual); Assert.Equal(0, control.NativeProfitCallCount);
        Assert.Null(control.AverageDeltaPerTriggeredBasket);
        foreach (var c in result.Candidates)
        {
            var rows = result.Baskets.Where(b => b.CandidateId == c.CandidateId).ToArray();
            Assert.Equal(rows.Sum(b => b.ShadowNetPnl), c.FixedPathShadowNetPnl);
            Assert.Equal(rows.Sum(b => b.DeltaVsActualNetPnl), c.FixedPathDeltaVsActual);
            Assert.Equal(c.TriggeredBasketCount, c.EmergencyStopInterceptedCount + c.WinnerCutEarlyCount + c.EndOfDataInterceptedCount);
            Assert.Equal(c.BasketCount, c.PositiveDeltaBasketCount + c.NegativeDeltaBasketCount + c.ZeroDeltaBasketCount);
            Assert.Equal(rows.Where(b => b.ActualExitReason == "EmergencyStop" && b.DeltaVsActualNetPnl > 0).Sum(b => b.DeltaVsActualNetPnl), c.SavedLossAmount);
            Assert.Equal(-rows.Where(b => b.ActualExitReason == "TakeProfit" && b.DeltaVsActualNetPnl < 0).Sum(b => b.DeltaVsActualNetPnl), c.LostWinnerProfitAmount);
            Assert.Contains("SCREENING ONLY", c.ResultType);
        }
        Assert.Contains(result.Baskets, b => b.Classification == "WinnerCutEarly" && b.DeltaVsActualNetPnl < 0m);
        Assert.Equal(before, await Saved()); Assert.Empty(db.ChangeTracker.Entries());
        var normalAfter = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!;
        Assert.Equal(ZipContents(normalBefore.Bytes), ZipContents(normalAfter.Bytes));
        var workbook = GridBreakoutGuardResearchExcelExport.Create(result);
        using var zip = new ZipArchive(new MemoryStream(workbook));
        XDocument Xml(string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
        Assert.Equal(new[] { "SUMMARY", "CANDIDATES", "BASKET_RESULTS", "TRIGGERS" },
            Xml("xl/workbook.xml").Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => e.Attribute("name")!.Value));
        Assert.Contains(GridBreakoutGuardShadowResult.Warning, Xml("xl/worksheets/sheet1.xml").Root!.Value);
        Assert.Contains("SCREENING ONLY", Xml("xl/worksheets/sheet1.xml").Root!.Value);
        Assert.Equal(9, Xml("xl/worksheets/sheet2.xml").Descendants().Count(e => e.Name.LocalName == "row"));
        Assert.Equal(25, Xml("xl/worksheets/sheet3.xml").Descendants().Count(e => e.Name.LocalName == "row"));
        Assert.Equal(result.Baskets.Count(b => b.Triggered) + 1, Xml("xl/worksheets/sheet4.xml").Descendants().Count(e => e.Name.LocalName == "row"));
        Assert.Equal(new[] { "NO_GUARD_CONTROL" }.Concat(GridBreakoutGuardCandidate.Guards.Select(c => c.Id)), result.Candidates.Select(c => c.CandidateId));
    }

    private static string[] ZipContents(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        return zip.Entries.OrderBy(e => e.FullName).Select(e => { using var reader = new StreamReader(e.Open()); return e.FullName + reader.ReadToEnd(); }).ToArray();
    }

    [Fact]
    public async Task WorsenedLosingBasketIsReportedWithoutClamping()
    {
        await using var db = Database(); var run = await Source(exit: "EmergencyStop"); await Save(db, run);
        var result = await new GridBreakoutGuardShadowSimulator(db, new Native { Value = -100m }).SimulateAsync(run.Id, default);
        var c = result.Candidates.Single(c => c.CandidateId == "L3_BOUNDARY_CLOSE");
        Assert.True(c.FixedPathDeltaVsActual < 0m); Assert.Equal(1, c.NegativeDeltaBasketCount); Assert.Equal(0m, c.SavedLossAmount);
        Assert.Equal(1, c.TriggeredActualLossCount); Assert.Equal(0, c.TriggeredActualWinnerCount);
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task AdminOnlyHttpExportUsesFrozenCatalogAndReturnsSafeOldRunError(int levels)
    {
        using var baseFactory = new EmaBotApiFactory(); var native = new Native();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMt5TradeCalculator>(); services.AddSingleton<IMt5TradeCalculator>(native);
        }));
        using var client = factory.CreateClient();
        const string route = "/api/backtests/grid/1/research/breakout-guards/export/excel";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode); Assert.Empty(native.Requests);
        Assert.Equal(AppRoles.Admin, typeof(GridBreakoutGuardResearchController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Equal(new[] { typeof(int), typeof(CancellationToken) }, typeof(GridBreakoutGuardResearchController).GetMethod("ExportExcel")!.GetParameters().Select(p => p.ParameterType));
        var csrf = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) };
        login.Headers.Add("X-CSRF-TOKEN", csrf!.Token); Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(route + "?adx=0&candidate=custom")).StatusCode); Assert.Empty(native.Requests);
        int id, oldId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var run = await Source(levels: levels); await Save(db, run); id = run.Id;
            var old = await Source(levels: levels); old.Cycles[0].Telemetry.Clear(); await Save(db, old); oldId = old.Id;
        }
        var response = await client.GetAsync($"/api/backtests/grid/{id}/research/breakout-guards/export/excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"grid-breakout-guard-research-{id}.xlsx", response.Content.Headers.ContentDisposition!.ToString());
        var error = await client.GetAsync($"/api/backtests/grid/{oldId}/research/breakout-guards/export/excel");
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("Grid breakout research requires a telemetry-enabled Grid backtest.", await error.Content.ReadAsStringAsync());
    }

    private sealed class Native : IMt5TradeCalculator
    {
        public const decimal Profit = 7.123456789m;
        public decimal Value { get; init; } = Profit;
        public string? Mode { get; init; }
        public List<Mt5CalculateProfitRequest> Requests { get; } = [];
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Requests.Add(r); if (Mode == "throw") throw new InvalidOperationException("secret");
            return Task.FromResult(new Mt5ProfitCalculationPayload(Mode == "symbol" ? "wrong" : r.BrokerSymbol,
                Mode == "direction" ? "wrong" : r.Direction, decimal.Round(r.VolumeLots, 10),
                Mode == "price" ? r.OpenPrice + .001m : decimal.Round(r.OpenPrice, 10), decimal.Round(r.ClosePrice, 10), Value, Mode == "currency" ? "EUR" : "USD"));
        }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token) => throw new Exception("Margin must never be requested.");
    }
}
