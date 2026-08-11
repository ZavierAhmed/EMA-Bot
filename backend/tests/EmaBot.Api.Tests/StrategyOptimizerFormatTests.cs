using System.IO.Compression;
using System.Text;
using EmaBot.Api.Controllers;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class StrategyOptimizerFormatTests
{
    [Fact]
    public void Workbook_IsOpenXmlAndContainsResearchSheetsAndRows()
    {
        var run = new StrategyOptimizationRun { Id = 7, Status = StrategyOptimizationStatus.Completed, RequestedStartUtc = DateTimeOffset.UnixEpoch, RequestedEndUtc = DateTimeOffset.UnixEpoch.AddDays(30), CandidateCount = 1, MarketCount = 1, SymbolsJson = "[\"BTCUSDT\"]", TimeframesJson = "[\"3m\"]", GridJson = "{}", Candidates = [new StrategyOptimizationCandidate { Id = 4, RiskReward = 1.1m, RobustCandidate = true, RobustRank = 1, Validation = new OptimizationMetrics { NetProfitFactor = 1.2m, TotalTrades = 30 }, MarketResults = [new StrategyOptimizationMarketResult { Symbol = "BTCUSDT", Timeframe = "3m", Validation = new OptimizationMetrics { NetProfitFactor = 1.2m } }] }], Trades = [new StrategyOptimizationTrade { StrategyOptimizationCandidateId = 4, Symbol = "BTCUSDT", Timeframe = "3m", Direction = SignalDirection.Long, EntryTimeUtc = DateTimeOffset.UnixEpoch, ExitTimeUtc = DateTimeOffset.UnixEpoch.AddMinutes(3), ExitReason = BacktestExitReason.TakeProfit }] };
        using var archive = new ZipArchive(new MemoryStream(StrategyOptimizerWorkbook.Create(run)), ZipArchiveMode.Read);
        var workbook = archive.GetEntry("xl/workbook.xml"); Assert.NotNull(workbook);
        using var reader = new StreamReader(workbook!.Open(), Encoding.UTF8); var xml = reader.ReadToEnd();
        foreach (var sheet in new[] { "Overview", "Ranked Candidates", "Market Results", "Best By Market", "Diagnostics", "Top Candidate Trades", "Run Configuration" }) Assert.Contains(sheet, xml);
        Assert.Equal(7, archive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
    }
}
