using System.IO.Compression;
using System.Xml.Linq;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class BacktestNativeEconomicsPersistenceTests
{
    [Fact]
    public async Task NativeBacktest_PersistsGrossAndNetProfitFactorSeparately()
    {
        await using var database = Database();
        var run = NativeRun(); database.BacktestRuns.Add(run); await database.SaveChangesAsync();
        var reloaded = await database.BacktestRuns.AsNoTracking().SingleAsync(item => item.Id == run.Id);

        Assert.Equal(BacktestEconomicsMode.Mt5HistoricalBidAsk, reloaded.EconomicsMode);
        Assert.Equal(4m / 3m, reloaded.GrossProfitFactor); Assert.Equal(.4m, reloaded.NetProfitFactor);
        Assert.NotEqual(reloaded.GrossProfitFactor, reloaded.NetProfitFactor);
    }

    [Fact]
    public async Task NativeBacktestDetailAndList_ExposeDistinctGrossAndNetProfitFactor()
    {
        await using var database = Database();
        var run = NativeRun(); database.BacktestRuns.Add(run); await database.SaveChangesAsync();
        var reloaded = await database.BacktestRuns.AsNoTracking().Include(item => item.Trades).SingleAsync(item => item.Id == run.Id);
        var detail = BacktestResponseMapper.ToDetail(reloaded); var list = BacktestResponseMapper.ToSummary(reloaded);

        Assert.Equal(BacktestEconomicsMode.Mt5HistoricalBidAsk, detail.EconomicsMode);
        Assert.Equal(4m / 3m, detail.GrossProfitFactor); Assert.Equal(.4m, detail.NetProfitFactor);
        Assert.Equal(detail.GrossProfitFactor, list.GrossProfitFactor); Assert.Equal(detail.NetProfitFactor, list.NetProfitFactor);
        Assert.Equal(3, detail.RejectedByInsufficientMargin); Assert.Equal(2, detail.RejectedByInvalidVolume); Assert.Equal(1, detail.RejectedByTradeMode);
        Assert.Equal(detail.RejectedByInsufficientMargin, list.RejectedByInsufficientMargin); Assert.Equal(detail.RejectedByInvalidVolume, list.RejectedByInvalidVolume); Assert.Equal(detail.RejectedByTradeMode, list.RejectedByTradeMode);
        Assert.Equal(PaperPositionSizingMode.FixedLots, detail.NativePositionSizingMode); Assert.Equal(.01m, detail.NativeFixedLots); Assert.Equal(2m, detail.NativeMarginPerTradePercent); Assert.Equal(100m, detail.StartingBalance);
        Assert.Equal(detail.NativePositionSizingMode, list.NativePositionSizingMode); Assert.All(detail.Trades, item => Assert.Equal(PaperPositionSizingMode.FixedLots, item.NativePositionSizingMode));
        Assert.Equal(4m / 3m, detail.ProfitFactor); // retained native compatibility field is explicitly gross.
    }

    [Fact]
    public async Task NativeBacktestExcel_ExportsDistinctGrossAndNetProfitFactor()
    {
        await using var database = Database();
        var run = NativeRun(); database.BacktestRuns.Add(run); await database.SaveChangesAsync();
        var workbook = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);
        Assert.NotNull(workbook);
        using var archive = new ZipArchive(new MemoryStream(workbook.Bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = XDocument.Parse(reader.ReadToEnd()).ToString();
        using var diagnosticsReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet5.xml")!.Open());
        var diagnostics = XDocument.Parse(diagnosticsReader.ReadToEnd()).ToString();
        Assert.Contains("Gross Profit Factor", xml); Assert.Contains("Net Profit Factor", xml);
        Assert.Contains("1.3333333333333333333333333333", xml); Assert.Contains("0.4", xml);
        Assert.Contains("Mt5HistoricalBidAsk", xml); Assert.Contains("USD", xml); Assert.Contains(Mt5HistoricalBacktestEngine.SpreadModel, xml);
        Assert.Contains("RejectedByInsufficientMargin", diagnostics); Assert.Contains("RejectedByInvalidVolume", diagnostics); Assert.Contains("RejectedByTradeMode", diagnostics);
        using var settingsReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        var settings = XDocument.Parse(settingsReader.ReadToEnd()).ToString();
        using var tradesReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet3.xml")!.Open());
        var trades = XDocument.Parse(tradesReader.ReadToEnd()).ToString();
        Assert.Contains("LegacyCompatibilityPositionSizingMode", settings); Assert.Contains("NativePositionSizingMode", settings); Assert.Contains("NativeFixedLots", settings); Assert.Contains("NativeMarginPerTradePercent", settings); Assert.Contains("NativeStartingBalance", settings); Assert.Contains("NativeSizingProvenance", settings);
        Assert.Contains("LegacyCompatibilityPositionSizingMode", trades); Assert.Contains("NativePositionSizingMode", trades); Assert.Contains("FixedLots", trades);
    }

    [Fact]
    public async Task HistoricalNativeBacktest_LeavesSizingProvenanceNullAndReportsItAsNotCaptured()
    {
        await using var database = Database();
        var run = NativeRun(); run.NativePositionSizingMode = null; run.NativeFixedLots = null; run.NativeMarginPerTradePercent = null; run.Trades.ForEach(item => item.NativePositionSizingMode = null);
        database.BacktestRuns.Add(run); await database.SaveChangesAsync();
        var reloaded = await database.BacktestRuns.AsNoTracking().Include(item => item.Trades).SingleAsync(item => item.Id == run.Id);
        var detail = BacktestResponseMapper.ToDetail(reloaded);

        Assert.Null(detail.NativePositionSizingMode); Assert.Null(detail.NativeFixedLots); Assert.Null(detail.NativeMarginPerTradePercent); Assert.All(detail.Trades, item => Assert.Null(item.NativePositionSizingMode));
        var workbook = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);
        using var archive = new ZipArchive(new MemoryStream(workbook!.Bytes), ZipArchiveMode.Read);
        using var settingsReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        Assert.Contains("Not captured historically", XDocument.Parse(settingsReader.ReadToEnd()).ToString());
    }

    [Fact]
    public void NativeBacktestResponse_MarginPercentProvenanceRemainsDistinctFromLegacyCompatibilityMode()
    {
        var run = NativeRun(); run.NativePositionSizingMode = PaperPositionSizingMode.MarginPercent; run.NativeFixedLots = .01m; run.NativeMarginPerTradePercent = 2m;

        var summary = BacktestResponseMapper.ToSummary(run);

        Assert.Equal(PositionSizingMode.FixedNotional, run.PositionSizingMode); Assert.Equal(PaperPositionSizingMode.MarginPercent, summary.NativePositionSizingMode); Assert.Equal(.01m, summary.NativeFixedLots); Assert.Equal(2m, summary.NativeMarginPerTradePercent);
    }

    [Fact]
    public void LegacyBacktestResponse_DoesNotSynthesizeNativeSizingProvenance()
    {
        var run = NativeRun(); run.EconomicsMode = BacktestEconomicsMode.LegacyCompatibility; run.NativePositionSizingMode = null; run.NativeFixedLots = null; run.NativeMarginPerTradePercent = null;

        var summary = BacktestResponseMapper.ToSummary(run);

        Assert.Null(summary.NativePositionSizingMode); Assert.Null(summary.NativeFixedLots); Assert.Null(summary.NativeMarginPerTradePercent);
    }

    [Fact]
    public async Task NativeMarginPercentBacktestExcel_ProjectsSavedNativeSizingWithoutCurrentSettings()
    {
        await using var database = Database();
        var run = NativeRun(); run.NativePositionSizingMode = PaperPositionSizingMode.MarginPercent; run.NativeMarginPerTradePercent = 2m; run.Trades.ForEach(item => item.NativePositionSizingMode = PaperPositionSizingMode.MarginPercent);
        database.BacktestRuns.Add(run); await database.SaveChangesAsync();

        var workbook = await BacktestExcelExport.CreateAsync(database, run.Id, CancellationToken.None);
        using var archive = new ZipArchive(new MemoryStream(workbook!.Bytes), ZipArchiveMode.Read);
        using var settingsReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        using var tradesReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet3.xml")!.Open());

        Assert.Contains("MarginPercent", XDocument.Parse(settingsReader.ReadToEnd()).ToString()); Assert.Contains("MarginPercent", XDocument.Parse(tradesReader.ReadToEnd()).ToString());
    }

    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static BacktestRun NativeRun()
    {
        var now = DateTimeOffset.UnixEpoch;
        return new BacktestRun
        {
            MarketDataSource = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", BrokerSymbol = "BTCUSDm", Interval = "3m", RequestedStartUtc = now, RequestedEndUtc = now.AddHours(1), CreatedAtUtc = now, CompletedAtUtc = now.AddHours(1), Status = BacktestRunStatus.Completed,
            EconomicsMode = BacktestEconomicsMode.Mt5HistoricalBidAsk, AccountCurrency = "USD", HistoricalSpreadModel = Mt5HistoricalBacktestEngine.SpreadModel, HistoricalChartMode = "Bid", CommissionPerLotPerSide = 500m, StartingBalance = 100m, NativePositionSizingMode = PaperPositionSizingMode.FixedLots, NativeFixedLots = .01m, NativeMarginPerTradePercent = 2m, EndingBalance = 985m,
            ProfitFactor = 4m / 3m, GrossProfitFactor = 4m / 3m, NetProfitFactor = .4m, PositionSizingMode = PositionSizingMode.FixedNotional, RejectedByInsufficientMargin = 3, RejectedByInvalidVolume = 2, RejectedByTradeMode = 1,
            Trades = [Trade(now.AddMinutes(3), 20m, 10m), Trade(now.AddMinutes(6), -15m, -25m)]
        };
    }
    private static BacktestTrade Trade(DateTimeOffset at, decimal gross, decimal net) => new()
    {
        Direction = SignalDirection.Long, CrossoverTimeUtc = at, SignalTimeUtc = at, EntryTimeUtc = at, ExitTimeUtc = at.AddMinutes(3), EntryPrice = 100m, ExitPrice = 99m, Quantity = .01m, InitialStopLoss = 90m, FinalStopLoss = 90m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = at, OriginalTakeProfit = 110m, FinalTakeProfit = 110m, ExitReason = BacktestExitReason.StopLoss,
        GrossPnlUsdt = gross, NetPnlUsdt = net, GrossPnl = gross, NetPnl = net, TotalFeesUsdt = gross - net, NativePositionSizingMode = PaperPositionSizingMode.FixedLots, Lots = .01m, EntryBid = 100m, EntryAsk = 100.2m, EntrySpread = .2m, ExitBid = 99m, ExitAsk = 99.2m, ExitSpread = .2m, RequiredMargin = 1m, MarginUsed = 1m, AccountEquityAtEntry = 1000m, EntryCommission = 5m, ExitCommission = 5m, RoundTripCommission = 10m
    };
}
