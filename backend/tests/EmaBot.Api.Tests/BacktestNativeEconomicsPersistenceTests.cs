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
    }

    private static EmaBotDbContext Database() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static BacktestRun NativeRun()
    {
        var now = DateTimeOffset.UnixEpoch;
        return new BacktestRun
        {
            MarketDataSource = MarketDataSource.Mt5Exness, Symbol = "BTCUSDm", BrokerSymbol = "BTCUSDm", Interval = "3m", RequestedStartUtc = now, RequestedEndUtc = now.AddHours(1), CreatedAtUtc = now, CompletedAtUtc = now.AddHours(1), Status = BacktestRunStatus.Completed,
            EconomicsMode = BacktestEconomicsMode.Mt5HistoricalBidAsk, AccountCurrency = "USD", HistoricalSpreadModel = Mt5HistoricalBacktestEngine.SpreadModel, HistoricalChartMode = "Bid", CommissionPerLotPerSide = 500m, StartingBalance = 1000m, EndingBalance = 985m,
            ProfitFactor = 4m / 3m, GrossProfitFactor = 4m / 3m, NetProfitFactor = .4m, PositionSizingMode = PositionSizingMode.FixedNotional, RejectedByInsufficientMargin = 3, RejectedByInvalidVolume = 2, RejectedByTradeMode = 1,
            Trades = [Trade(now.AddMinutes(3), 20m, 10m), Trade(now.AddMinutes(6), -15m, -25m)]
        };
    }
    private static BacktestTrade Trade(DateTimeOffset at, decimal gross, decimal net) => new()
    {
        Direction = SignalDirection.Long, CrossoverTimeUtc = at, SignalTimeUtc = at, EntryTimeUtc = at, ExitTimeUtc = at.AddMinutes(3), EntryPrice = 100m, ExitPrice = 99m, Quantity = .01m, InitialStopLoss = 90m, FinalStopLoss = 90m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = at, OriginalTakeProfit = 110m, FinalTakeProfit = 110m, ExitReason = BacktestExitReason.StopLoss,
        GrossPnlUsdt = gross, NetPnlUsdt = net, GrossPnl = gross, NetPnl = net, TotalFeesUsdt = gross - net, Lots = .01m, EntryBid = 100m, EntryAsk = 100.2m, EntrySpread = .2m, ExitBid = 99m, ExitAsk = 99.2m, ExitSpread = .2m, RequiredMargin = 1m, MarginUsed = 1m, AccountEquityAtEntry = 1000m, EntryCommission = 5m, ExitCommission = 5m, RoundTripCommission = 10m
    };
}
