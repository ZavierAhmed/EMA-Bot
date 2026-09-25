using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy.Grid;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EmaBot.Api.Tests;

public sealed class GridStrictBreakoutGuardResearchTests
{
    private static readonly string[] Ids = ["NO_GUARD_CONTROL", "CURRENT_L3_ADX15_ADVERSE2", "L4_ADX15_ADVERSE2", "L3_ADX20_ADVERSE2", "L3_ADX15_ADVERSE3", "L4_ADX20_ADVERSE2"];
    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<GridBacktestRun> Source(bool isLong = true, string exit = "TakeProfit", int levels = 5)
    {
        var run = GridBacktestPersistence.Map(await TelemetryFixture.Run(levels, isLong, exit), DateTimeOffset.UtcNow);
        foreach (var t in run.Cycles.SelectMany(c => c.Telemetry)) { t.AdxDeltaFromQualification = 2m; t.ConsecutiveAdverseCloses = 3; }
        return run;
    }
    private static async Task Save(EmaBotDbContext db, GridBacktestRun run)
    { db.GridBacktestRuns.Add(run); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
    private static GridStrictBreakoutGuardCandidate Candidate(string id) => GridStrictBreakoutGuardCandidate.All.Single(c => c.Id == id);
    private static string[] ZipContents(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        return zip.Entries.OrderBy(e => e.FullName).Select(e => { using var r = new StreamReader(e.Open()); return e.FullName + r.ReadToEnd(); }).ToArray();
    }
    private static XElement[] SheetRows(ZipArchive zip, int n)
    { using var s = zip.GetEntry($"xl/worksheets/sheet{n}.xml")!.Open(); return XDocument.Load(s).Descendants().Where(e => e.Name.LocalName == "row").ToArray(); }

    [Fact]
    public void ExactImmutableCatalogAndReferenceConstants()
    {
        Assert.Equal(Ids, GridStrictBreakoutGuardCandidate.All.Select(c => c.Id));
        Assert.Empty(typeof(GridStrictBreakoutGuardCandidate).GetConstructors());
        Assert.All(typeof(GridStrictBreakoutGuardCandidate).GetProperties(), p => Assert.Null(p.SetMethod));
        var reference = Candidate("CURRENT_" + GridBreakoutGuardRules.Id);
        Assert.Equal(GridBreakoutGuardRules.MinimumFilledLevel, reference.MinimumFilledLevel);
        Assert.Equal(GridBreakoutGuardRules.MinimumAdxDelta, reference.MinimumAdxDelta);
        Assert.Equal(GridBreakoutGuardRules.MinimumConsecutiveAdverseCloses, reference.MinimumConsecutiveAdverseCloses);
        Assert.False(Candidate(Ids[0]).Matches(new() { MaxFilledLevelAfterBar = 5, AdxDeltaFromQualification = 100m, ConsecutiveAdverseCloses = 100 }));
    }

    [Theory]
    [InlineData("L4_ADX15_ADVERSE2", 3, 1.5, 2, false)] [InlineData("L4_ADX15_ADVERSE2", 4, 1.5, 2, true)]
    [InlineData("L3_ADX20_ADVERSE2", 3, 1.99, 2, false)] [InlineData("L3_ADX20_ADVERSE2", 3, 2.0, 2, true)]
    [InlineData("L3_ADX15_ADVERSE3", 3, 1.5, 2, false)] [InlineData("L3_ADX15_ADVERSE3", 3, 1.5, 3, true)]
    [InlineData("L4_ADX20_ADVERSE2", 3, 2.0, 2, false)] [InlineData("L4_ADX20_ADVERSE2", 4, 1.99, 2, false)]
    [InlineData("L4_ADX20_ADVERSE2", 4, 2.0, 2, true)]
    public void StrictInclusiveBoundaries(string id, int level, double adx, int adverse, bool expected)
    {
        var row = new GridBacktestTelemetry { MaxFilledLevelAfterBar = level, AdxDeltaFromQualification = (decimal)adx, ConsecutiveAdverseCloses = adverse };
        Assert.Equal(expected, Candidate(id).Matches(row));
        row.AdxDeltaFromQualification = null; Assert.False(Candidate(id).Matches(row));
        row.AdxDeltaFromQualification = (decimal)adx;
        foreach (var exit in new[] { "TakeProfit", "EmergencyStop" }) { row.ExitReasonThisBar = exit; Assert.False(Candidate(id).Matches(row)); }
    }

    [Fact]
    public void ReferenceMatchesSharedRealAndG4B1RuleAcrossEvidence()
    {
        foreach (var level in new[] { 2, 3, 4, 5 })
        foreach (var adx in new decimal?[] { null, 1.49m, 1.5m, 2m })
        foreach (var adverse in new[] { 0, 1, 2, 3 })
        {
            var row = new GridBacktestTelemetry { MaxFilledLevelAfterBar = level, AdxDeltaFromQualification = adx, ConsecutiveAdverseCloses = adverse };
            var expected = GridBreakoutGuardRules.Matches(level, adx, adverse);
            Assert.Equal(expected, Candidate(Ids[1]).Matches(row));
            Assert.Equal(expected, GridBreakoutGuardCandidate.Guards.Single(c => c.Id == GridBreakoutGuardRules.Id).Matches(row));
        }
        Assert.All(GridStrictBreakoutGuardCandidate.All, c => Assert.False(c.Matches(new() { MaxFilledLevelAfterBar = 5, AdxDeltaFromQualification = null, ConsecutiveAdverseCloses = 99 })));
    }

    [Theory]
    [InlineData(true, "TakeProfit")] [InlineData(false, "TakeProfit")]
    [InlineData(true, "EmergencyStop")] [InlineData(false, "EmergencyStop")]
    [InlineData(true, "EndOfData")] [InlineData(false, "EndOfData")]
    public async Task EarliestNativeExitIncludesSameBarLegsExcludesFutureAndCachesLocally(bool isLong, string exit)
    {
        await using var db = Database(); var run = await Source(isLong, exit); await Save(db, run);
        var native = new Native(); var simulator = new GridBreakoutGuardShadowSimulator(db, native);
        var result = await simulator.SimulateStrictAsync(run.Id, default);
        Assert.Equal(Ids, result.Candidates.Select(c => c.CandidateId));
        var trigger = run.Cycles[0].Telemetry[3]; var basket = run.Cycles[0].Baskets[0];
        foreach (var row in result.Baskets.Where(b => b.Triggered))
        {
            Assert.Equal(trigger.TimeUtc, row.TriggerTimeUtc); Assert.Equal(4, row.ShadowOpenLegCount);
            Assert.Equal(trigger.BidClose + (isLong ? 0m : trigger.SpreadPrice), row.ShadowExitPrice);
            var legs = basket.Legs.Where(l => l.FillTimeUtc <= trigger.TimeUtc).ToArray();
            Assert.Contains(legs, l => l.FillTimeUtc == trigger.TimeUtc);
            Assert.Equal(4 * Native.Profit, row.ShadowGrossPnl);
            Assert.Equal(legs.Sum(l => l.EntryCommission), row.ShadowEntryCommission);
            Assert.Equal(legs.Sum(l => l.Lots * run.CommissionPerLotPerSide), row.ShadowExitCommission);
            Assert.Equal(row.ShadowGrossPnl - row.ShadowEntryCommission - row.ShadowExitCommission, row.ShadowNetPnl);
            Assert.Equal(row.ShadowNetPnl - row.ActualNetPnl, row.DeltaVsActualNetPnl);
            Assert.NotEqual(basket.Legs.Sum(l => l.ExitCommission), row.ShadowExitCommission);
            Assert.All(legs, l => Assert.Contains(new Mt5CalculateProfitRequest(run.BrokerSymbol, basket.Direction, l.Lots, l.FillPrice, row.ShadowExitPrice!.Value), native.Requests));
        }
        Assert.Equal(5, result.Baskets.Count(b => b.Triggered));
        Assert.DoesNotContain(native.Requests, r => r.OpenPrice == basket.Legs[4].FillPrice);
        Assert.Equal(4, native.Requests.Count); Assert.Equal(4, result.NativeProfitCallCount); Assert.Equal(4, result.UniqueNativeProfitRequestCount);
        await simulator.SimulateStrictAsync(run.Id, default); Assert.Equal(8, native.Requests.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task StricterCandidatesWaitForTheirOwnFirstEligibleCandle()
    {
        await using var db = Database(); var run = await Source();
        run.Cycles[0].Telemetry[3].AdxDeltaFromQualification = 1.5m;
        run.Cycles[0].Telemetry[3].ConsecutiveAdverseCloses = 2;
        await Save(db, run);
        var result = await new GridBreakoutGuardShadowSimulator(db, new Native()).SimulateStrictAsync(run.Id, default);
        foreach (var b in result.Baskets.Where(b => b.Triggered))
            Assert.Equal(run.Cycles[0].Telemetry[b.CandidateId == Ids[1] || b.CandidateId == Ids[2] ? 3 : 4].TimeUtc, b.TriggerTimeUtc);
    }

    [Theory]
    [InlineData("TakeProfit")] [InlineData("EmergencyStop")] [InlineData("EndOfData")]
    public async Task ActualFinalExitAlwaysWinsAndNoTriggerRetainsActual(string exit)
    {
        await using var db = Database(); var run = await Source(exit: exit);
        foreach (var t in run.Cycles[0].Telemetry.SkipLast(1)) t.AdxDeltaFromQualification = null;
        await Save(db, run); var native = new Native();
        var result = await new GridBreakoutGuardShadowSimulator(db, native).SimulateStrictAsync(run.Id, default);
        Assert.Empty(native.Requests);
        Assert.All(result.Baskets, b => { Assert.False(b.Triggered); Assert.Equal(b.ActualNetPnl, b.ShadowNetPnl); Assert.Equal(0m, b.DeltaVsActualNetPnl); Assert.Null(b.TriggerTimeUtc); Assert.Equal("NoTrigger", b.Classification); });
    }

    [Theory]
    [InlineData("GRID_RANGE_4L_RESEARCH_V1")] [InlineData("GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1")]
    [InlineData("EMA_TREND_V1")] [InlineData("old")]
    public async Task StrictSourceRestrictionsFailBeforeNative(string source)
    {
        await using var db = Database(); var run = await Source(levels: source == "GRID_RANGE_4L_RESEARCH_V1" ? 4 : 5);
        if (source == "old") run.Cycles[0].Telemetry.Clear(); else run.StrategyId = source;
        await Save(db, run); var native = new Native();
        var error = await Assert.ThrowsAsync<GridGuardResearchException>(() => new GridBreakoutGuardShadowSimulator(db, native).SimulateStrictAsync(run.Id, default));
        Assert.Equal("Strict Grid guard research requires a telemetry-enabled GRID_RANGE_V1 backtest.", error.Message);
        Assert.Equal(400, error.StatusCode); Assert.Empty(native.Requests);
    }

    [Fact]
    public async Task AggregatesWorkbookReadOnlyAndG4B1Preservation()
    {
        await using var db = Database(); var run = await Source();
        foreach (var exit in new[] { "EmergencyStop", "EndOfData" }) { var other = await Source(exit: exit); other.Cycles[0].Sequence = run.Cycles.Count; run.Cycles.Add(other.Cycles[0]); }
        var baskets = run.Cycles.SelectMany(c => c.Baskets).ToArray();
        run.BasketCount = baskets.Length; run.NetPnl = baskets.Sum(b => b.NetPnl); run.GrossPnl = baskets.Sum(b => b.GrossPnl);
        run.TotalCommission = baskets.Sum(b => b.Commission); run.EndingBalance = run.StartingBalance + run.NetPnl;
        run.NetProfitFactor = GridGuardResearchSourceValidation.ProfitFactor(baskets.Select(b => b.NetPnl));
        run.GrossProfitFactor = GridGuardResearchSourceValidation.ProfitFactor(baskets.Select(b => b.GrossPnl));
        await Save(db, run);
        async Task<string> Saved() => JsonSerializer.Serialize(await GridBacktestService.Graph(db).Include(r => r.Cycles).ThenInclude(c => c.Telemetry).AsNoTracking().SingleAsync());
        var before = await Saved(); var simulator = new GridBreakoutGuardShadowSimulator(db, new Native());
        var g4b1 = await simulator.SimulateAsync(run.Id, default);
        var oldWorkbook = GridBreakoutGuardResearchExcelExport.Create(g4b1);
        var normal = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!.Bytes;
        var result = await simulator.SimulateStrictAsync(run.Id, default);
        var control = result.Candidates[0]; Assert.Equal(run.NetPnl, control.FixedPathShadowNetPnl); Assert.Equal(run.BasketCount, control.BasketCount); Assert.Equal(run.NetProfitFactor, control.FixedPathShadowProfitFactor);
        foreach (var c in result.Candidates)
        {
            var rows = result.Baskets.Where(b => b.CandidateId == c.CandidateId).ToArray();
            Assert.Equal(rows.Sum(b => b.ShadowNetPnl), c.FixedPathShadowNetPnl);
            Assert.Equal(rows.Sum(b => b.DeltaVsActualNetPnl), c.FixedPathDeltaVsActual);
            Assert.Equal(c.BasketCount, c.TriggeredBasketCount + c.NoTriggerBasketCount);
            Assert.Contains("SCREENING ONLY", c.ResultType);
        }
        Assert.Equal(g4b1.Baskets.Where(b => b.CandidateId == GridBreakoutGuardRules.Id).Select(b => b with { CandidateId = Ids[1] }), result.Baskets.Where(b => b.CandidateId == Ids[1]));
        Assert.Equal(before, await Saved()); Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(ZipContents(normal), ZipContents((await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!.Bytes));
        Assert.Equal(ZipContents(oldWorkbook), ZipContents(GridBreakoutGuardResearchExcelExport.Create(await simulator.SimulateAsync(run.Id, default))));
        using var legacy = new ZipArchive(new MemoryStream(oldWorkbook));
        Assert.Equal(9, SheetRows(legacy, 2).Length);
        Assert.Equal(typeof(GridBreakoutGuardCandidateResult).GetProperties().Select(p => p.Name), SheetRows(legacy, 2)[0].Elements().Select(e => e.Value));
        using var strict = new ZipArchive(new MemoryStream(GridStrictBreakoutGuardResearchExcelExport.Create(result)));
        using var workbook = strict.GetEntry("xl/workbook.xml")!.Open();
        Assert.Equal(new[] { "SUMMARY", "CANDIDATES", "BASKET_RESULTS", "TRIGGERS" }, XDocument.Load(workbook).Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => e.Attribute("name")!.Value));
        Assert.Contains(GridStrictBreakoutGuardResearchExcelExport.Warning, string.Join("", SheetRows(strict, 1).Select(r => r.Value)));
        var candidates = SheetRows(strict, 2); Assert.Equal(7, candidates.Length);
        Assert.Equal(Ids, candidates.Skip(1).Select(r => r.Elements().First().Value));
        Assert.Contains("MinimumFilledLevel", candidates[0].Value); Assert.Contains("MinimumAdxDelta", candidates[0].Value); Assert.Contains("MinimumConsecutiveAdverseCloses", candidates[0].Value);
        Assert.Equal(19, SheetRows(strict, 3).Length); Assert.Equal(result.Baskets.Count(b => b.Triggered) + 1, SheetRows(strict, 4).Length);
        Assert.Equal(new[] { typeof(EmaBotDbContext), typeof(IMt5TradeCalculator) }, typeof(GridBreakoutGuardShadowSimulator).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public async Task AdminHttpEndpointRejectsQueriesAndExportsSixCandidates()
    {
        using var baseFactory = new EmaBotApiFactory(); var native = new Native();
        await using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<IMt5TradeCalculator>(); s.AddSingleton<IMt5TradeCalculator>(native); }));
        using var client = factory.CreateClient(); const string route = "/api/backtests/grid/1/research/strict-breakout-guards/export/excel";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(AppRoles.Admin, typeof(GridBreakoutGuardResearchController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Equal(new[] { typeof(int), typeof(CancellationToken) }, typeof(GridBreakoutGuardResearchController).GetMethod("ExportStrictExcel")!.GetParameters().Select(p => p.ParameterType));
        var csrf = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) };
        login.Headers.Add("X-CSRF-TOKEN", csrf!.Token); Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(route + "?adx=0&candidate=custom")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode); Assert.Empty(native.Requests);
        int id;
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var run = await Source(); await Save(db, run); id = run.Id; }
        var response = await client.GetAsync($"/api/backtests/grid/{id}/research/strict-breakout-guards/export/excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"grid-strict-guard-research-{id}.xlsx", response.Content.Headers.ContentDisposition!.ToString());
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync())); Assert.Equal(7, SheetRows(zip, 2).Length);
    }

    private sealed class Native : IMt5TradeCalculator
    {
        public const decimal Profit = 7.123456789m;
        public List<Mt5CalculateProfitRequest> Requests { get; } = [];
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        { Requests.Add(r); return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice, Profit, "USD")); }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token) => throw new Exception("No margin or trading operation expected.");
    }
}
