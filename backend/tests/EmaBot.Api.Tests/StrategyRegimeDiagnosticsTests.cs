using EmaBot.Api.Binance;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace EmaBot.Api.Tests;

public sealed class StrategyRegimeDiagnosticsTests
{
    [Fact]
    public void SignalDiagnostics_DoNotUseEntryOrFutureCandles()
    {
        var candles = Candles(); var trade = Trade(candles[120]);
        var original = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", candles, [trade]));
        var altered = candles.Select((candle, index) => index > 120 ? candle with { High = 999999m, Low = 1m, Close = 888888m } : candle).ToArray();
        var unchanged = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", altered, [trade]));

        Assert.Equal(original.Ema9Slope5Percent, unchanged.Ema9Slope5Percent); Assert.Equal(original.Ema15Slope5Percent, unchanged.Ema15Slope5Percent); Assert.Equal(original.Ema100Slope5Percent, unchanged.Ema100Slope5Percent); Assert.Equal(original.Ema100Slope20Percent, unchanged.Ema100Slope20Percent); Assert.Equal(original.DistanceFromEma100Percent, unchanged.DistanceFromEma100Percent); Assert.Equal(original.PriceReturn20Percent, unchanged.PriceReturn20Percent); Assert.Equal(original.Atr14Percent, unchanged.Atr14Percent); Assert.Equal(original.TrendEfficiency20, unchanged.TrendEfficiency20);
    }

    [Fact]
    public void HigherTimeframeDiagnostics_UsesLastClosedCandleForPartialSignal_AndDoesNotUseFutureData()
    {
        var ltf = Candles(140, 3); var htf = Candles(140, 15); var trade = TradeAt(htf[120].CloseTimeUtc.AddMinutes(6));
        var original = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", ltf, [trade], "15m", htf)).HigherTimeframe;
        var altered = htf.Select((candle, index) => index > 120 ? candle with { Open = 999m, High = 999m, Low = 1m, Close = 888m } : candle).ToArray();
        var alteredLtf = ltf.Select((candle, index) => index > 120 ? candle with { Open = 999m, High = 999m, Low = 1m, Close = 888m } : candle).ToArray();
        var unchanged = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", alteredLtf, [trade], "15m", altered)).HigherTimeframe;

        Assert.Equal(htf[120].CloseTimeUtc, original.CandleCloseTimeUtc); Assert.Equal(original, unchanged);
    }

    [Fact]
    public void HigherTimeframeDiagnostics_UsesCandleAtExactCloseBoundary()
    {
        var htf = Candles(140, 15); var trade = TradeAt(htf[120].CloseTimeUtc);
        var diagnostic = StrategyRegimeDiagnosticsService.DescribeHigherTimeframe(trade, "15m", htf);

        Assert.Equal(htf[120].CloseTimeUtc, diagnostic.CandleCloseTimeUtc); Assert.Equal(0m, diagnostic.AgeMinutes);
    }

    [Fact]
    public void HigherTimeframeDiagnostics_ReturnsNullFeaturesForInsufficientWarmup()
    {
        var diagnostic = StrategyRegimeDiagnosticsService.DescribeHigherTimeframe(TradeAt(DateTimeOffset.UnixEpoch.AddHours(1)), "15m", Candles(4, 15));

        Assert.Equal("15m", diagnostic.Timeframe); Assert.Null(diagnostic.Ema100); Assert.Null(diagnostic.Ema100Slope20Percent); Assert.Null(diagnostic.Atr14Percent); Assert.Null(diagnostic.TrendEfficiency20); Assert.Null(diagnostic.FullTrendAligned);
    }

    [Fact]
    public void HigherTimeframeDiagnostics_LeavesUnsupportedTimeframesNull()
    {
        var diagnostic = StrategyRegimeDiagnosticsService.DescribeHigherTimeframe(TradeAt(DateTimeOffset.UnixEpoch), StrategyRegimeDiagnosticsService.HigherTimeframe("6h"), Candles(140, 15));

        Assert.Null(diagnostic.Timeframe); Assert.Null(diagnostic.Close); Assert.Null(diagnostic.FullTrendAligned);
    }

    [Fact]
    public void Describe_PreservesCandidateTradeExecution()
    {
        var signal = Candles()[120]; var trade = new BacktestTrade { Direction = SignalDirection.Long, SignalTimeUtc = signal.CloseTimeUtc, SignalClose = signal.Close, EntryTimeUtc = signal.CloseTimeUtc.AddMilliseconds(1), ExitTimeUtc = signal.CloseTimeUtc.AddMinutes(3), EntryPrice = 101m, ExitPrice = 105m, GrossPnlUsdt = 4m, TotalFeesUsdt = .2m, NetPnlUsdt = 3.8m, NetRMultiple = 1.9m, ExitReason = BacktestExitReason.TakeProfit };
        var actual = Assert.Single(StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", Candles(), [trade], "15m", Candles(140, 15))).Trade;

        Assert.Equal(trade.Direction, actual.Direction); Assert.Equal(trade.SignalTimeUtc, actual.SignalTimeUtc); Assert.Equal(trade.EntryTimeUtc, actual.EntryTimeUtc); Assert.Equal(trade.ExitTimeUtc, actual.ExitTimeUtc); Assert.Equal(trade.EntryPrice, actual.EntryPrice); Assert.Equal(trade.ExitPrice, actual.ExitPrice); Assert.Equal(trade.GrossPnlUsdt, actual.GrossPnlUsdt); Assert.Equal(trade.TotalFeesUsdt, actual.TotalFeesUsdt); Assert.Equal(trade.NetPnlUsdt, actual.NetPnlUsdt); Assert.Equal(trade.ExitReason, actual.ExitReason);
    }

    [Theory]
    [InlineData("3m", "15m")]
    [InlineData("5m", "30m")]
    [InlineData("15m", "1h")]
    [InlineData("30m", "2h")]
    [InlineData("1h", "4h")]
    public void HigherTimeframeMapping_IsFixed(string execution, string htf) => Assert.Equal(htf, StrategyRegimeDiagnosticsService.HigherTimeframe(execution));

    [Fact]
    public async Task CreateAsync_CachesExecutionAndHigherTimeframeSeries()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<EmaBot.Api.Data.EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new EmaBot.Api.Data.EmaBotDbContext(options); await database.Database.EnsureCreatedAsync();
        var run = new StrategyOptimizationRun { Status = StrategyOptimizationStatus.Completed, RequestedStartUtc = DateTimeOffset.UnixEpoch, RequestedEndUtc = DateTimeOffset.UnixEpoch.AddDays(30), SymbolsJson = "[\"BTCUSDT\",\"ETHUSDT\"]", TimeframesJson = "[\"3m\",\"5m\"]", SimulatedAccountBalanceUsdt = 1000m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = .05m }; var candidate = new StrategyOptimizationCandidate { StrategyOptimizationRun = run, RiskReward = 1.1m };
        database.StrategyOptimizationRuns.Add(run); database.StrategyOptimizationCandidates.Add(candidate); await database.SaveChangesAsync(); var historical = new CountingHistorical(Candles(140, 3));
        var data = await new StrategyRegimeDiagnosticsService(database, new TestResolver(historical), new BacktestEngine(new EmaSignalEngine())).CreateAsync(run.Id, candidate.Id, CancellationToken.None);

        Assert.NotNull(data); foreach (var symbol in new[] { "BTCUSDT", "ETHUSDT" }) foreach (var timeframe in new[] { "3m", "5m", "15m", "30m" }) Assert.Equal(1, historical.Count(symbol, timeframe));
    }

    [Fact]
    public void Workbook_ContainsHigherTimeframeSheets()
    {
        var run = new StrategyOptimizationRun { RequestedStartUtc = DateTimeOffset.UnixEpoch, RequestedEndUtc = DateTimeOffset.UnixEpoch.AddDays(30), SymbolsJson = "[]", TimeframesJson = "[]" };
        var candidate = new StrategyOptimizationCandidate(); var trade = Trade(Candles()[120]);
        var diagnostics = StrategyRegimeDiagnosticsService.Describe("BTCUSDT", "3m", Candles(), [trade], "15m", Candles(140, 15));
        using var archive = new ZipArchive(new MemoryStream(StrategyRegimeWorkbook.Create(new RegimeExportData(run, candidate, diagnostics))), ZipArchiveMode.Read);

        var workbook = archive.GetEntry("xl/workbook.xml"); Assert.NotNull(workbook); using var reader = new StreamReader(workbook!.Open()); var xml = reader.ReadToEnd();
        Assert.Contains("HTF Alignment Summary", xml); Assert.Contains("HTF Summary", xml);
    }

    private static BacktestTrade Trade(Candle signal) => new() { Direction = SignalDirection.Long, SignalTimeUtc = signal.CloseTimeUtc, SignalClose = signal.Close, EntryTimeUtc = signal.CloseTimeUtc.AddMilliseconds(1), ExitTimeUtc = signal.CloseTimeUtc.AddMinutes(3), ExitReason = BacktestExitReason.EndOfData };
    private static BacktestTrade TradeAt(DateTimeOffset signalTime) => new() { Direction = SignalDirection.Long, SignalTimeUtc = signalTime, SignalClose = 100m, EntryTimeUtc = signalTime.AddMilliseconds(1), ExitTimeUtc = signalTime.AddMinutes(3), ExitReason = BacktestExitReason.EndOfData };
    private static Candle[] Candles() => Candles(140, 3);
    private static Candle[] Candles(int count, int minutes) => Enumerable.Range(0, count).Select(index => { var time = DateTimeOffset.UnixEpoch.AddMinutes(index * minutes); var close = 100m + index; return new Candle(time, time.AddMinutes(minutes).AddMilliseconds(-1), close - .5m, close + 1m, close - 1m, close, 1m, true); }).ToArray();
    private sealed class CountingHistorical(IReadOnlyList<Candle> candles) : IHistoricalMarketDataProvider { private readonly Dictionary<(string Symbol,string Frame),int> requests = []; public int Count(string symbol,string frame) => requests.GetValueOrDefault((symbol,frame)); public Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol,string interval,DateTimeOffset startUtc,DateTimeOffset endUtc,CancellationToken token){requests[(symbol,interval)]=Count(symbol,interval)+1;return Task.FromResult(candles);} }
    private sealed class TestResolver(IHistoricalMarketDataProvider historical) : IHistoricalMarketDataProviderResolver { public IHistoricalMarketDataProvider Resolve(MarketDataSource source) => historical; }
}
