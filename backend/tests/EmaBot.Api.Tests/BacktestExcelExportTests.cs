using System.IO.Compression;
using System.Xml.Linq;
using EmaBot.Api.Configuration;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class BacktestExcelExportTests
{
    [Fact]
    public async Task CompletedRunExport_UsesOnlySavedForensicEvidenceWithDeterministicFiveSheets()
    {
        await using var database = Database();
        var run = await SeedCompletedRunAsync(database);
        database.TradingSettings.Add(new TradingSettings { Id = TradingSettings.GlobalId, RiskReward = 99m, FixedOrderSizeUsdt = 999m, UpdatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync();
        var historical = new CountingHistorical();
        var controller = Controller(database, historical);

        var action = await controller.ExportExcel(run.Id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(action);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal($"ema-bot-backtest-{run.Id}-BTCUSDm-3m.xlsx", file.FileDownloadName);
        using var services = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services }; http.Response.Body = new MemoryStream();
        await file.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal(0, historical.Calls);

        var entries = Read(file.FileContents);
        var workbook = entries["xl/workbook.xml"];
        foreach (var name in new[] { "SUMMARY", "SETTINGS", "TRADES", "TRADE EVENTS", "DIAGNOSTICS" }) Assert.Contains($"name=\"{name}\"", workbook);
        Assert.Equal(5, entries.Keys.Count(path => path.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
        foreach (var xml in entries.Where(item => item.Key.EndsWith(".xml", StringComparison.Ordinal)).Select(item => item.Value)) _ = XDocument.Parse(xml);

        var summary = FieldRows(entries["xl/worksheets/sheet1.xml"]);
        Assert.Equal(run.Id.ToString(), summary["BacktestRunId"]); Assert.Equal("BACKTEST / SIMULATION", summary["Classification"]); Assert.Equal("MT5 / Exness", summary["MarketDataSourceLabel"]);
        Assert.Equal("42.5", summary["GrossPnlUsdt"]); Assert.Equal("3.1", summary["TotalFeesUsdt"]); Assert.Equal("39.4", summary["NetPnlUsdt"]); Assert.Equal("1039.4", summary["EndingBalanceUsdt"]);
        Assert.Contains("saved compatibility-model assumptions", summary["Note"]!); Assert.Contains("InitialRiskPriceDistanceDefinition", summary.Keys);

        var settings = FieldRows(entries["xl/worksheets/sheet2.xml"]);
        Assert.Equal("2.5", settings["RiskReward"]); Assert.Equal("250", settings["FixedOrderSizeUsdt"]); Assert.Equal("1", settings["WaitForConfirmationCandle"]); Assert.Equal("1", settings["UseHtfRegimeFilter"]); Assert.Equal("4", settings["MaxReentryAgeBars"]);
        Assert.NotEqual("99", settings["RiskReward"]); Assert.NotEqual("999", settings["FixedOrderSizeUsdt"]);

        var trades = Rows(entries["xl/worksheets/sheet3.xml"]);
        Assert.Equal(2, trades.Count);
        Assert.Equal(run.Trades.Single(item => item.EntryTimeUtc == run.CreatedAtUtc.AddMinutes(10)).Id.ToString(), trades[0]["TradeId"]);
        Assert.Equal(run.Trades.Single(item => item.EntryTimeUtc == run.CreatedAtUtc.AddMinutes(20)).Id.ToString(), trades[1]["TradeId"]);
        Assert.Equal(2, trades.Select(item => item["TradeId"]).Distinct().Count());
        Assert.Equal("1", trades[0]["SameCandleExitConflict"]); Assert.Equal("1", trades[0]["UseAdaptiveInitialStop"]); Assert.Equal("15", trades[0]["SignalAtr14"]); Assert.Equal("Strong", trades[0]["ReversalPowerBand"]); Assert.Equal("15m", trades[0]["HtfTimeframe"]);
        Assert.Equal("15", trades[0]["MfePrice"]); Assert.Equal("6", trades[0]["MaePrice"]); Assert.Equal("1.5", trades[0]["MfeInitialR"]); Assert.Equal("0.6", trades[0]["MaeInitialR"]);
        Assert.Equal("10", trades[0]["InitialRiskPriceDistance"]); Assert.Equal("10.0", trades[0]["InitialRiskPercentOfEntry"]); Assert.Equal("20", trades[0]["InitialRiskAmountUsdt"]); Assert.Equal("20", trades[0]["HoldingMinutes"]); Assert.Equal("2", trades[0]["TargetDistancePrice"]);
        Assert.Null(trades[1]["SignalOpen"]); Assert.Null(trades[1]["HtfTimeframe"]); Assert.Null(trades[1]["SignalHtfCandleCloseTimeUtc"]);
        Assert.DoesNotContain("<t>ReentryAgeBars</t>", entries["xl/worksheets/sheet3.xml"], StringComparison.Ordinal);

        var events = Rows(entries["xl/worksheets/sheet4.xml"]);
        Assert.Equal(3, events.Count); Assert.Equal(trades[0]["TradeId"], events[0]["TradeId"]); Assert.Equal(trades[0]["TradeId"], events[1]["TradeId"]); Assert.Equal(trades[1]["TradeId"], events[2]["TradeId"]); Assert.True(string.CompareOrdinal(events[0]["TimeUtc"], events[1]["TimeUtc"]) < 0);
        Assert.Equal(3, events.Select(item => item["EventId"]).Distinct().Count()); Assert.Contains(events, item => item["Type"] == "TrailingStopMoved"); Assert.Contains(events, item => item["Type"] == "TakeProfitExtended");

        var diagnostics = FieldRows(entries["xl/worksheets/sheet5.xml"]);
        Assert.Equal("11", diagnostics["TotalCrossovers"]); Assert.Equal("7", diagnostics["LongSignals"]); Assert.Equal("5", diagnostics["ShortSignals"]); Assert.Equal("3", diagnostics["RejectedByEmaGap"]); Assert.Equal("2", diagnostics["RejectedByFees"]); Assert.Equal("12", diagnostics["SignalsTotal"]); Assert.Equal("2", diagnostics["ExecutedTradeCount"]); Assert.Equal("1", diagnostics["NormalExecutedTradeCount"]); Assert.Equal("1", diagnostics["ReentryTradeCount"]); Assert.Equal(int.Parse(diagnostics["ExecutedTradeCount"]!) , int.Parse(diagnostics["NormalExecutedTradeCount"]!) + int.Parse(diagnostics["ReentryTradeCount"]!)); Assert.Equal(1m / 12m * 100m, decimal.Parse(diagnostics["TradeExecutionRatePercent"]!, System.Globalization.CultureInfo.InvariantCulture)); Assert.Contains("do not inflate this rate", diagnostics["TradeExecutionRatePercentDefinition"]!);

        Assert.Contains("BTC&amp;USD&lt;m&gt;", entries["xl/worksheets/sheet1.xml"]);
    }

    [Fact]
    public async Task MissingRunExport_ReturnsSafeNotFoundWithoutHistoricalProviderCall()
    {
        await using var database = Database();
        var historical = new CountingHistorical();

        var action = await Controller(database, historical).ExportExcel(999999, CancellationToken.None);

        var missing = Assert.IsType<NotFoundObjectResult>(action);
        Assert.Equal("Backtest not found.", Assert.IsType<ApiMessage>(missing.Value).Message);
        Assert.Equal(0, historical.Calls);
    }

    [Fact]
    public async Task ZeroTradeRunExport_HasValidHeadersAndNoFabricatedRows()
    {
        await using var database = Database();
        var now = DateTimeOffset.UtcNow;
        var run = new BacktestRun { Symbol = "BTCUSDm", Interval = "3m", MarketDataSource = MarketDataSource.Mt5Exness, RequestedStartUtc = now.AddHours(-1), RequestedEndUtc = now, CreatedAtUtc = now, CompletedAtUtc = now, Status = BacktestRunStatus.Completed, StartingBalanceUsdt = 100m, EndingBalanceUsdt = 100m };
        database.BacktestRuns.Add(run); await database.SaveChangesAsync();

        var export = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);

        Assert.NotNull(export);
        var entries = Read(export.Bytes);
        Assert.Equal(5, entries.Keys.Count(path => path.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
        Assert.Empty(Rows(entries["xl/worksheets/sheet3.xml"])); Assert.Empty(Rows(entries["xl/worksheets/sheet4.xml"]));
        Assert.Equal(run.Id.ToString(), FieldRows(entries["xl/worksheets/sheet1.xml"])["BacktestRunId"]);
        var diagnostics = FieldRows(entries["xl/worksheets/sheet5.xml"]);
        Assert.Equal("0", diagnostics["ExecutedTradeCount"]); Assert.Equal("0", diagnostics["NormalExecutedTradeCount"]); Assert.Equal("0", diagnostics["ReentryTradeCount"]); Assert.Null(diagnostics["TradeExecutionRatePercent"]);
    }

    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static BacktestsController Controller(EmaBotDbContext database, CountingHistorical historical) => new(database, new BacktestService(database, historical, new TradingSettingsService(database, Options.Create(new TradingDefaultsOptions())), new BacktestEngine(new EmaSignalEngine())), Options.Create(new BacktestRequestTimeoutOptions()));

    private static async Task<BacktestRun> SeedCompletedRunAsync(EmaBotDbContext database)
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var later = Trade(now.AddMinutes(20), 110m, false, false);
        var earlier = Trade(now.AddMinutes(10), 100m, true, true);
        earlier.Events.AddRange([
            new BacktestTradeEvent { TimeUtc = now.AddMinutes(27), EffectiveTimeUtc = now.AddMinutes(30), Type = BacktestTradeEventType.TakeProfitExtended, MarketPrice = 112m, OldTakeProfit = 102m, NewTakeProfit = 112m, ProgressPercent = 70m },
            new BacktestTradeEvent { TimeUtc = now.AddMinutes(15), EffectiveTimeUtc = now.AddMinutes(18), Type = BacktestTradeEventType.TrailingStopMoved, MarketPrice = 108m, OldStop = 90m, NewStop = 100m, ProgressPercent = 50m }
        ]);
        later.Events.Add(new BacktestTradeEvent { TimeUtc = now.AddMinutes(25), Type = BacktestTradeEventType.Entry, MarketPrice = 110m });
        var run = new BacktestRun
        {
            MarketDataSource = MarketDataSource.Mt5Exness, Symbol = "BTC&USD<m>", Interval = "3m", RequestedStartUtc = now.AddHours(-2), RequestedEndUtc = now.AddHours(2), ActualStartUtc = now.AddHours(-1), ActualEndUtc = now.AddHours(1), CreatedAtUtc = now, CompletedAtUtc = now.AddHours(1), CandleCount = 123,
            RiskReward = 2.5m, FixedOrderSizeUsdt = 250m, MinEmaGapPercent = .75m, MaxStopDistancePercent = 4m, PositionSizingMode = PositionSizingMode.MarginPercent, StartingBalanceUsdt = 1000m, EndingBalanceUsdt = 1039.4m, MarginPerTradePercent = 10m, Leverage = 5m,
            WaitForConfirmationCandle = true, UseEma100Filter = true, UseHtfRegimeFilter = true, TrailingStopEnabled = true, UseAdaptiveInitialStop = true, SameTrendReentryEnabled = true, MaxReentryAgeBars = 4, ExitOnOppositeCrossover = true, FeePercentPerSide = .05m,
            TotalTrades = 2, WinningTrades = 1, LosingTrades = 1, BreakEvenTrades = 0, LongTrades = 1, ShortTrades = 1, WinRatePercent = 50m, GrossPnlUsdt = 42.5m, TotalFeesUsdt = 3.1m, NetPnlUsdt = 39.4m, ProfitFactor = 2m, AverageNetPnlUsdt = 19.7m, AverageRMultiple = .8m, MaxDrawdownUsdt = 12.3m,
            TotalCrossovers = 11, LongSignals = 7, ShortSignals = 5, RejectedByEma100 = 2, RejectedByEmaGap = 3, RejectedByHtfRegime = 4, RejectedByStopDistance = 5, RejectedByFees = 2, ConfirmationFailed = 6, InvalidStopLoss = 7, SkippedWhilePositionOpen = 8, NoEntryCandle = 9, Status = BacktestRunStatus.Completed,
            Trades = [later, earlier]
        };
        database.BacktestRuns.Add(run); await database.SaveChangesAsync();
        return run;
    }

    private static BacktestTrade Trade(DateTimeOffset entry, decimal entryPrice, bool adaptive, bool htf) => new()
    {
        Direction = entryPrice == 100m ? SignalDirection.Long : SignalDirection.Short, IsReentry = entryPrice != 100m, CrossoverTimeUtc = entry.AddMinutes(-6), SignalTimeUtc = entry.AddMinutes(-3), EntryTimeUtc = entry, ExitTimeUtc = entry.AddMinutes(20), EntryPrice = entryPrice, ExitPrice = entryPrice == 100m ? 112m : 105m, Quantity = 2m, EntryNotionalUsdt = 200m,
        PositionSizingMode = PositionSizingMode.MarginPercent, AccountEquityAtEntryUsdt = 1000m, MarginUsedUsdt = 100m, Leverage = 5m, InitialStopLoss = entryPrice == 100m ? 90m : 120m, FinalStopLoss = entryPrice == 100m ? 100m : 115m, StopSourceType = adaptive ? StopSourceType.AdaptiveMicroStructure : StopSourceType.Pivot, StopSourceTimeUtc = entry.AddMinutes(-3), OriginalTakeProfit = entryPrice == 100m ? 102m : 90m, FinalTakeProfit = entryPrice == 100m ? 112m : 90m, TakeProfitExtended = adaptive,
        ExitReason = BacktestExitReason.TakeProfit, SameCandleExitConflict = adaptive, EntryFeeUsdt = 1m, ExitFeeUsdt = .5m, TotalFeesUsdt = 1.5m, GrossPnlUsdt = entryPrice == 100m ? 24m : 10m, NetPnlUsdt = entryPrice == 100m ? 22.5m : 8.5m, NetPnlPercent = 11.25m, GrossRMultiple = 1.2m, NetRMultiple = 1.1m, MfePrice = adaptive ? 15m : 5m, MfePercent = adaptive ? 15m : 4.5m, MaePrice = adaptive ? 6m : 4m, MaePercent = adaptive ? 6m : 3.6m,
        SignalOpen = adaptive ? 99m : null, SignalClose = entryPrice, SignalEma9 = adaptive ? 101m : null, SignalEma15 = adaptive ? 99m : null, SignalEma100 = adaptive ? 95m : null, SignalGapPercent = adaptive ? 2m : null, SignalGapState = GapState.Expanding, UseAdaptiveInitialStop = adaptive, SignalAtr14 = adaptive ? 15m : null, ReversalPowerScore = adaptive ? 82m : null, ReversalPowerBand = adaptive ? ReversalPowerBand.Strong : null, StopAnchorPrice = adaptive ? 91m : null, StopBuffer = adaptive ? 1m : null,
        HtfTimeframe = htf ? "15m" : null, SignalHtfCandleCloseTimeUtc = htf ? entry.AddMinutes(-1) : null, SignalHtfEma100Slope20Percent = htf ? .2m : null, SignalHtfAtr14Percent = htf ? 1.1m : null, TrendRegimeCrossoverTimeUtc = adaptive ? entry.AddMinutes(-9) : null, ReentryAgeBars = 99
    };

    private static Dictionary<string, string> Read(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); });
    }

    private static Dictionary<string, string?> FieldRows(string xml) => Rows(xml).ToDictionary(row => row["Field"]!, row => row["Value"]);

    private static IReadOnlyList<Dictionary<string, string?>> Rows(string xml)
    {
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var rows = XDocument.Parse(xml).Root!.Element(ns + "sheetData")!.Elements(ns + "row").Select(row => row.Elements(ns + "c").Select(Cell).ToArray()).ToArray();
        var header = rows[0];
        return rows.Skip(1).Select(row => header.Select((name, index) => new KeyValuePair<string, string?>(name!, index < row.Length ? row[index] : null)).ToDictionary(item => item.Key, item => item.Value)).ToArray();
    }

    private static string? Cell(XElement cell)
    {
        var ns = cell.Name.Namespace;
        return cell.Attribute("t")?.Value == "inlineStr" ? cell.Element(ns + "is")?.Element(ns + "t")?.Value : cell.Element(ns + "v")?.Value;
    }

    private sealed class CountingHistorical : IHistoricalMarketDataProvider
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) { Calls++; return Task.FromResult<IReadOnlyList<Candle>>([]); }
    }
}
