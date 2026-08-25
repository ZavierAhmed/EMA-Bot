using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using EmaBot.Api.Configuration;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Tests;

public sealed class BacktestLegacyCompatibilityTests
{
    [Fact]
    public async Task LegacyBacktest_WithNullNativeFields_LoadsWithoutReinterpretation()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var loaded = await database.BacktestRuns.AsNoTracking().Include(item => item.Trades).SingleAsync(item => item.Id == run.Id);
        var detail = BacktestResponseMapper.ToDetail(loaded);

        Assert.Equal("BTCUSDm", loaded.Symbol); Assert.Equal("3m", loaded.Interval); Assert.Equal(1.25m, loaded.ProfitFactor);
        AssertNullNativeRunEvidence(loaded);
        Assert.Null(detail.EconomicsMode); Assert.Null(detail.GrossProfitFactor); Assert.Null(detail.NetProfitFactor);
        Assert.Equal(1.25m, detail.ProfitFactor); // Pre-B.1 ProfitFactor remains compatibility gross PF.
    }

    [Fact]
    public async Task LegacyBacktest_ProfitFactorRemainsLegacyGrossSemantics()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var loaded = await Service(database).GetAsync(run.Id, CancellationToken.None);
        var detail = BacktestResponseMapper.ToDetail(Assert.IsType<BacktestRun>(loaded));
        var workbook = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);
        var summary = FieldRows(Read(Assert.IsType<BacktestExcelWorkbook>(workbook).Bytes)["xl/worksheets/sheet1.xml"]);

        Assert.Equal(1.25m, detail.ProfitFactor); Assert.Null(detail.NetProfitFactor); Assert.Null(detail.GrossProfitFactor);
        Assert.Equal("1.25", summary["Legacy Gross Profit Factor"]); Assert.Null(summary["Gross Profit Factor"]); Assert.Null(summary["Net Profit Factor"]);
    }

    [Fact]
    public async Task LegacyBacktestTrade_WithNullNativeEvidence_LoadsWithoutFabrication()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var loaded = await Service(database).GetAsync(run.Id, CancellationToken.None);
        var trade = Assert.Single(Assert.IsType<BacktestRun>(loaded).Trades);
        var response = Assert.Single(BacktestResponseMapper.ToDetail(Assert.IsType<BacktestRun>(loaded)).Trades);

        Assert.Equal(SignalDirection.Long, trade.Direction); Assert.Equal(100m, trade.EntryPrice); Assert.Equal(110m, trade.ExitPrice); Assert.Equal(1m, trade.Quantity);
        Assert.Equal(.05m, trade.TotalFeesUsdt); Assert.Equal(10m, trade.GrossPnlUsdt); Assert.Equal(9.95m, trade.NetPnlUsdt); Assert.True(trade.IsReentry);
        AssertNullNativeTradeEvidence(trade);
        Assert.Null(response.EntryBid); Assert.Null(response.EntryAsk); Assert.Null(response.EntrySpread); Assert.Null(response.ExitBid); Assert.Null(response.ExitAsk); Assert.Null(response.ExitSpread);
        Assert.Null(response.Lots); Assert.Null(response.RequiredMargin); Assert.Null(response.EntryCommission); Assert.Null(response.ExitCommission); Assert.Null(response.RoundTripCommission);
    }

    [Fact]
    public async Task LegacyBacktest_ListRemainsReadableWithNullNativeFields()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var listed = Assert.Single(await Service(database).ListAsync(CancellationToken.None));
        var response = BacktestResponseMapper.ToSummary(listed);

        Assert.Equal(run.Id, response.Id); Assert.Equal("BTCUSDm", response.Symbol); Assert.Equal("3m", response.Interval); Assert.Equal(MarketDataSource.Mt5Exness, response.MarketDataSource);
        Assert.Equal(1.25m, response.ProfitFactor); Assert.Null(response.EconomicsMode); Assert.Null(response.GrossProfitFactor); Assert.Null(response.NetProfitFactor);
    }

    [Fact]
    public async Task LegacyBacktest_DetailRemainsReadableWithNullNativeFields()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var loaded = await Service(database).GetAsync(run.Id, CancellationToken.None);
        var detail = BacktestResponseMapper.ToDetail(Assert.IsType<BacktestRun>(loaded));

        Assert.Equal(run.Id, detail.Id); Assert.Single(detail.Trades); Assert.Equal(2m, detail.RiskReward); Assert.Equal(.025m, detail.FeePercentPerSide);
        Assert.Equal(10m, detail.GrossPnlUsdt); Assert.Equal(9.95m, detail.NetPnlUsdt); Assert.Equal(1.25m, detail.ProfitFactor);
        Assert.Null(detail.EconomicsMode); Assert.Null(detail.AccountCurrency); Assert.Null(detail.GrossProfitFactor); Assert.Null(detail.NetProfitFactor);
    }

    [Fact]
    public async Task LegacyBacktest_DeleteDoesNotRequireNativeEconomicsFields()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        Assert.True(await Service(database).DeleteAsync(run.Id, CancellationToken.None));

        Assert.Null(await database.BacktestRuns.FindAsync(run.Id));
        Assert.Empty(await database.BacktestTrades.Where(item => item.BacktestRunId == run.Id).ToListAsync());
        Assert.Empty(await database.BacktestTradeEvents.ToListAsync());
    }

    [Fact]
    public async Task LegacyBacktestExcel_WithNullNativeFields_RemainsCompatible()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var workbook = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);
        var entries = Read(Assert.IsType<BacktestExcelWorkbook>(workbook).Bytes);
        var summary = FieldRows(entries["xl/worksheets/sheet1.xml"]); var settings = FieldRows(entries["xl/worksheets/sheet2.xml"]); var trade = Assert.Single(Rows(entries["xl/worksheets/sheet3.xml"]));

        Assert.Equal("BTCUSDm", summary["Symbol"]); Assert.Equal("0.05", summary["TotalFeesUsdt"]); Assert.Equal("1.25", summary["Legacy Gross Profit Factor"]);
        Assert.Null(summary["Gross Profit Factor"]); Assert.Null(summary["Net Profit Factor"]); Assert.Contains("saved compatibility-model assumptions", summary["Note"]!);
        Assert.Null(settings["EconomicsMode"]); Assert.Null(settings["AccountCurrency"]); Assert.Null(settings["BrokerSymbol"]); Assert.Null(settings["CommissionPerLotPerSide"]); Assert.Null(settings["StopsLevelPoints"]); Assert.Null(settings["HistoricalChartMode"]);
        Assert.Equal("100", trade["EntryPrice"]); Assert.Equal("110", trade["ExitPrice"]); Assert.Equal("0.05", trade["TotalFeesUsdt"]);
        Assert.Null(trade["EntryBid"]); Assert.Null(trade["EntryAsk"]); Assert.Null(trade["EntrySpread"]); Assert.Null(trade["ExitBid"]); Assert.Null(trade["ExitAsk"]); Assert.Null(trade["ExitSpread"]); Assert.Null(trade["Lots"]); Assert.Null(trade["RequiredMargin"]); Assert.Null(trade["EntryCommission"]); Assert.Null(trade["ExitCommission"]); Assert.Null(trade["RoundTripCommission"]);
    }

    [Fact]
    public void LegacyBacktest_FeePercentPerSideRemainsCompatibilityEconomics()
    {
        var candles = Candles(8); candles[0] = candles[0] with { Low = 90m }; candles[6] = candles[6] with { High = 120m, Low = 95m };
        var events = new[] { Event(candles, SignalStatus.BullishCrossover), Event(candles, SignalStatus.LongSignal) };
        var lowFee = LegacyEngine().RunWithEvents(candles, Settings(.05m), events).Trades.Single();
        var highFee = LegacyEngine().RunWithEvents(candles, Settings(.10m), events).Trades.Single();

        Assert.Equal(20m, lowFee.GrossPnlUsdt); Assert.Equal(.11m, lowFee.TotalFeesUsdt); Assert.Equal(19.89m, lowFee.NetPnlUsdt);
        Assert.Equal(20m, highFee.GrossPnlUsdt); Assert.Equal(.22m, highFee.TotalFeesUsdt); Assert.Equal(19.78m, highFee.NetPnlUsdt);
        Assert.True(highFee.TotalFeesUsdt > lowFee.TotalFeesUsdt); Assert.True(highFee.NetPnlUsdt < lowFee.NetPnlUsdt);
    }

    [Fact]
    public async Task LegacyBacktest_ReadAndExportDoNotBackfillNativeFields()
    {
        await using var database = Database();
        var run = CreateLegacyBacktestFixture(); database.BacktestRuns.Add(run); await database.SaveChangesAsync(); database.ChangeTracker.Clear();

        var service = Service(database);
        _ = await service.GetAsync(run.Id, CancellationToken.None); _ = await service.ListAsync(CancellationToken.None); _ = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);
        var loaded = await database.BacktestRuns.AsNoTracking().Include(item => item.Trades).SingleAsync(item => item.Id == run.Id);
        var response = BacktestResponseMapper.ToDetail(loaded);
        var json = JsonSerializer.Serialize(response, JsonOptions);

        AssertNullNativeRunEvidence(loaded); AssertNullNativeTradeEvidence(Assert.Single(loaded.Trades));
        Assert.Contains("\"economicsMode\":null", json); Assert.Contains("\"grossProfitFactor\":null", json); Assert.Contains("\"netProfitFactor\":null", json);
        Assert.Contains("\"entryBid\":null", json); Assert.Contains("\"entryAsk\":null", json); Assert.Contains("\"entrySpread\":null", json); Assert.DoesNotContain("\"entryBid\":100", json);
    }

    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static BacktestService Service(EmaBotDbContext database) => new(database, new EmptyHistorical(), new TradingSettingsService(database, Options.Create(new TradingDefaultsOptions())), LegacyEngine());
    private static BacktestEngine LegacyEngine() => new(new EmaSignalEngine());

    private static BacktestRun CreateLegacyBacktestFixture()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        return new BacktestRun
        {
            MarketDataSource = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", Interval = "3m", RequestedStartUtc = now.AddHours(-1), RequestedEndUtc = now.AddHours(1), ActualStartUtc = now, ActualEndUtc = now.AddMinutes(30), CreatedAtUtc = now, CompletedAtUtc = now.AddMinutes(30), CandleCount = 10,
            RiskReward = 2m, FixedOrderSizeUsdt = 100m, MinEmaGapPercent = .5m, MaxStopDistancePercent = 4m, PositionSizingMode = PositionSizingMode.FixedNotional, StartingBalanceUsdt = 100m, EndingBalanceUsdt = 109.95m, MarginPerTradePercent = 10m, Leverage = 1m,
            WaitForConfirmationCandle = false, UseEma100Filter = false, UseHtfRegimeFilter = false, TrailingStopEnabled = false, UseAdaptiveInitialStop = false, SameTrendReentryEnabled = true, MaxReentryAgeBars = 6, ExitOnOppositeCrossover = false, FeePercentPerSide = .025m,
            TotalTrades = 1, WinningTrades = 1, LongTrades = 1, WinRatePercent = 100m, GrossPnlUsdt = 10m, TotalFeesUsdt = .05m, NetPnlUsdt = 9.95m, ProfitFactor = 1.25m, AverageNetPnlUsdt = 9.95m, AverageRMultiple = 1m, Status = BacktestRunStatus.Completed,
            Trades = [new BacktestTrade
            {
                Direction = SignalDirection.Long, CrossoverTimeUtc = now, SignalTimeUtc = now.AddMinutes(3), EntryTimeUtc = now.AddMinutes(6), ExitTimeUtc = now.AddMinutes(9), EntryPrice = 100m, ExitPrice = 110m, Quantity = 1m, EntryNotionalUsdt = 100m, PositionSizingMode = PositionSizingMode.FixedNotional, AccountEquityAtEntryUsdt = 100m, MarginUsedUsdt = 100m, Leverage = 1m,
                InitialStopLoss = 90m, FinalStopLoss = 90m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = now, OriginalTakeProfit = 110m, FinalTakeProfit = 110m, ExitReason = BacktestExitReason.TakeProfit,
                EntryFeeUsdt = .025m, ExitFeeUsdt = .025m, TotalFeesUsdt = .05m, GrossPnlUsdt = 10m, NetPnlUsdt = 9.95m, NetPnlPercent = 9.95m, GrossRMultiple = 1m, NetRMultiple = .995m, MfePrice = 10m, MfePercent = 10m, MaePrice = 0m, MaePercent = 0m,
                SignalClose = 100m, SignalGapState = GapState.Unchanged, IsReentry = true,
                Events = [new BacktestTradeEvent { TimeUtc = now.AddMinutes(9), Type = BacktestTradeEventType.Exit, MarketPrice = 110m }]
            }]
        };
    }

    private static void AssertNullNativeRunEvidence(BacktestRun run)
    {
        Assert.Null(run.EconomicsMode); Assert.Null(run.AccountCurrency); Assert.Null(run.BrokerSymbol); Assert.Null(run.HistoricalSpreadModel); Assert.Null(run.HistoricalChartMode); Assert.Null(run.CommissionPerLotPerSide); Assert.Null(run.ContractSize); Assert.Null(run.VolumeMin); Assert.Null(run.VolumeMax); Assert.Null(run.VolumeStep); Assert.Null(run.VolumeLimit); Assert.Null(run.PointSize); Assert.Null(run.TickSize); Assert.Null(run.TickValueProfit); Assert.Null(run.TickValueLoss); Assert.Null(run.StopsLevelPoints); Assert.Null(run.TradeMode); Assert.Null(run.StartingBalance); Assert.Null(run.EndingBalance); Assert.Null(run.GrossProfitFactor); Assert.Null(run.NetProfitFactor);
        Assert.Equal(0, run.RejectedByTradingCosts); Assert.Equal(0, run.Mt5EconomicsCallCount); Assert.Equal(0, run.Mt5EconomicsElapsedMilliseconds);
    }

    private static void AssertNullNativeTradeEvidence(BacktestTrade trade)
    {
        Assert.Null(trade.Lots); Assert.Null(trade.EntryBid); Assert.Null(trade.EntryAsk); Assert.Null(trade.EntrySpread); Assert.Null(trade.ExitBid); Assert.Null(trade.ExitAsk); Assert.Null(trade.ExitSpread); Assert.Null(trade.RequiredMargin); Assert.Null(trade.MarginUsed); Assert.Null(trade.AccountEquityAtEntry); Assert.Null(trade.EntryCommission); Assert.Null(trade.ExitCommission); Assert.Null(trade.RoundTripCommission); Assert.Null(trade.GrossPnl); Assert.Null(trade.NetPnl); Assert.Null(trade.InitialRiskAmount); Assert.Null(trade.ReentryAgeBars);
    }

    private static TradingSettings Settings(decimal fee) => new() { RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = fee };
    private static StrategyEvent Event(Candle[] candles, SignalStatus status) => new(candles[5].CloseTimeUtc, SignalDirection.Long, status, new IndicatorSnapshot(candles[5].CloseTimeUtc, candles[5].Close, 1m, 1m, null, null, GapState.Unchanged, TrendDirection.Neutral));
    private static Candle[] Candles(int count) => Enumerable.Range(0, count).Select(index => { var time = DateTimeOffset.UnixEpoch.AddMinutes(index); return new Candle(time, time.AddMinutes(1).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true); }).ToArray();

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

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private sealed class EmptyHistorical : IHistoricalMarketDataProvider
    {
        public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) => Task.FromResult<IReadOnlyList<Candle>>([]);
    }
}
