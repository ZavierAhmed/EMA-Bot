using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmaBot.Api.Tests;

public sealed class GridDeepTelemetryPersistenceTests
{
    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static GridBacktestService Service(EmaBotDbContext db) => new(db, null!, null!, null!, null!, NullLogger<GridBacktestService>.Instance);
    private static XDocument Sheet(ZipArchive zip, int n)
    { using var stream = zip.GetEntry($"xl/worksheets/sheet{n}.xml")!.Open(); return XDocument.Load(stream); }
    private static XElement[] Rows(XDocument sheet) => sheet.Descendants().Where(e => e.Name.LocalName == "row").ToArray();

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task AtomicGraphRoundTripPreservesEveryFieldAndKeepsNormalDetailSmall(int levels)
    {
        await using var db = Database();
        var result = await TelemetryFixture.Run(levels, true, "EmergencyStop");
        var run = GridBacktestPersistence.Map(result, DateTimeOffset.UtcNow);
        db.GridBacktestRuns.Add(run); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = await db.Set<GridBacktestTelemetry>().OrderBy(t => t.Sequence).ToListAsync();
        Assert.Equal(result.Telemetry.Count, loaded.Count);
        var cycleId = (await db.Set<GridBacktestCycle>().SingleAsync()).Id;
        for (var i = 0; i < loaded.Count; i++)
        {
            Assert.Equal(cycleId, loaded[i].GridBacktestCycleId);
            foreach (var property in typeof(GridHistoricalTelemetry).GetProperties().Where(p => p.Name != "CycleQualificationTimeUtc"))
                Assert.Equal(property.GetValue(result.Telemetry[i]), typeof(GridBacktestTelemetry).GetProperty(property.Name)!.GetValue(loaded[i]));
        }
        db.ChangeTracker.Clear();
        var detailRun = (await Service(db).GetAsync(run.Id, default))!;
        Assert.All(detailRun.Cycles, c => Assert.Empty(c.Telemetry)); // Not queried for normal detail.
        var json = JsonSerializer.Serialize(GridBacktestResponses.ToDetail(detailRun));
        Assert.DoesNotContain("Telemetry", json); Assert.DoesNotContain("CurrentAdx", json);
        Assert.True(await Service(db).DeleteAsync(run.Id, default));
        Assert.Empty(db.Set<GridBacktestTelemetry>()); Assert.Empty(db.Set<GridBacktestCycle>());
    }

    [Theory]
    [InlineData(4, false)] [InlineData(5, false)] [InlineData(4, true)] [InlineData(5, true)]
    public async Task WorkbookHasFortyTelemetryColumnsForNewAndOldRunsUsingOnlyStoredEvidence(int levels, bool oldRun)
    {
        await using var db = Database(); var native = new TelemetryFixture.Native();
        var result = await TelemetryFixture.Run(levels, false, "TakeProfit", native);
        var run = GridBacktestPersistence.Map(result, DateTimeOffset.UtcNow);
        if (oldRun) foreach (var cycle in run.Cycles) cycle.Telemetry.Clear();
        else
        {
            // Sentinel stored values prove export never recomputes indicators/ratios.
            run.Cycles[0].Telemetry[0].CurrentAdx = 777m;
            run.Cycles[0].Telemetry[0].CandleTrueRange = null;
            run.Cycles[0].Telemetry[0].CandleBody = 0m;
        }
        db.GridBacktestRuns.Add(run); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var calls = native.Profits.Count + native.Margins.Count;
        var workbook = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!;
        Assert.Equal(calls, native.Profits.Count + native.Margins.Count);
        using var zip = new ZipArchive(new MemoryStream(workbook.Bytes));
        using var xml = zip.GetEntry("xl/workbook.xml")!.Open();
        Assert.Equal(new[] { "SUMMARY", "CYCLES", "BASKETS", "LEGS", "EVENTS", "DIAGNOSTICS", "TELEMETRY" },
            XDocument.Load(xml).Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => e.Attribute("name")!.Value));
        var rows = Rows(Sheet(zip, 7)); var headers = rows[0].Elements().Select(e => e.Value).ToArray();
        Assert.Equal(new[] { "CycleId", "Sequence", "TimeUtc", "Direction", "BidOpen", "BidHigh", "BidLow", "BidClose", "SpreadPoints", "SpreadPrice",
            "CurrentAtr", "CurrentAdx", "QualificationAtr", "QualificationAdx", "AdxDeltaFromQualification", "AtrRatioToQualification",
            "FrozenRangeHigh", "FrozenRangeLow", "CurrentRangeHigh", "CurrentRangeLow", "Anchor", "Spacing", "FrozenBoundaryPrice",
            "DistanceFromAnchorSpacings", "AdverseDistanceFromAnchorSpacings", "BreakoutDistanceSpacings", "CandleBody", "CandleTrueRange", "CandleBodyAtrRatio", "CandleTrueRangeAtrRatio",
            "CloseBeyondFrozenBoundary", "AdverseExtremeBeyondFrozenBoundary", "ConsecutiveAdverseCloses", "ConsecutiveClosesBeyondFrozenBoundary",
            "MaxFilledLevelBeforeBar", "MaxFilledLevelAfterBar", "NewFillCount", "DeepestConfiguredLevel", "DeepestLevelFilled", "ExitReasonThisBar" }, headers);
        Assert.Equal(oldRun ? 1 : result.Telemetry.Count + 1, rows.Length);
        if (!oldRun)
        {
            var values = rows[1].Elements().ToArray();
            Assert.Equal("777", values[Array.IndexOf(headers, "CurrentAdx")].Value);
            Assert.Equal("", values[Array.IndexOf(headers, "CandleTrueRange")].Value);
            Assert.Equal("0", values[Array.IndexOf(headers, "CandleBody")].Value);
            Assert.Equal(run.Cycles[0].Id.ToString(), values[0].Value);
            Assert.Equal("4", values[Array.IndexOf(headers, "QualificationAtr")].Value);
        }
        var summary = Rows(Sheet(zip, 1)).ToDictionary(row => row.Elements().First().Value, row => row.Elements().Last().Value);
        Assert.Equal(oldRun ? "0" : result.Telemetry.Count.ToString(), summary["TelemetryRows"]);
        Assert.Equal(oldRun ? "0" : "1", summary["ActiveCyclesWithTelemetry"]);
    }

    [Fact]
    public void OneAdditiveMigrationOnlyCreatesTelemetryWithCascadeAndUniqueOrdering()
    {
        using var db = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseMySql("Server=localhost;Database=unused;", new MySqlServerVersion(new Version(8, 4, 0))).Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = Assert.Single(assembly.Migrations, p => string.CompareOrdinal(p.Key, "20260916103317_AddGridHistoricalBacktests") > 0);
        Assert.EndsWith("_AddGridDeepTelemetry", migration.Key);
        var operations = assembly.CreateMigration(migration.Value, db.Database.ProviderName!).UpOperations;
        Assert.Equal(3, operations.Count);
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("GridBacktestTelemetry", table.Name);
        var fk = Assert.Single(table.ForeignKeys); Assert.Equal("GridBacktestCycles", fk.PrincipalTable); Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
        Assert.Equal(2, operations.OfType<CreateIndexOperation>().Count());
        Assert.All(operations.OfType<CreateIndexOperation>(), i => { Assert.True(i.IsUnique); Assert.Equal(table.Name, i.Table); });
        Assert.All(table.Columns.Where(c => c.ClrType == typeof(decimal)), c => { Assert.Equal(28, c.Precision); Assert.Equal(12, c.Scale); });
        Assert.True(table.Columns.Single(c => c.Name == "CandleTrueRange").IsNullable);
        var sql = db.GetService<IMigrator>().GenerateScript("20260916103317_AddGridHistoricalBacktests", migration.Key);
        Assert.Contains("CREATE TABLE `GridBacktestTelemetry`", sql); Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql);
    }
}
