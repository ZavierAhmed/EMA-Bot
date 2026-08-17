using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EmaBot.Api.Tests;

public sealed class PaperSessionExcelExportTests(EmaBotApiFactory factory) : IClassFixture<EmaBotApiFactory>
{
    [Fact]
    public async Task Export_ContainsCompleteIsolatedPaperSessionEvidence()
    {
        var (id, decisionMessages) = await SeedAsync();
        using var client = await AdminClientAsync();
        using var response = await client.GetAsync($"/api/paper-sessions/{id}/export/excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"ema-bot-paper-session-{id}.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var entries = Read(await response.Content.ReadAsByteArrayAsync());
        var workbook = entries["xl/workbook.xml"];
        foreach (var name in new[] { "Session", "Settings", "Symbols", "Trades", "Trade Events", "Decisions" }) Assert.Contains($"name=\"{name}\"", workbook);
        Assert.Equal(6, entries.Keys.Count(key => key.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
        var all = string.Concat(entries.Values);
        Assert.Contains("EURUSDm", all); Assert.Contains("XAUUSDm", all); Assert.DoesNotContain("SESSION-B-ONLY", all);
        Assert.Contains("USD", all); Assert.Contains("0.01", all); Assert.Contains("-9.97", all); Assert.Contains("Entry Bid", all);
        Assert.Contains("AdaptiveMicroStructure", all); Assert.Contains("1.25", all); Assert.Contains("Strong", all); Assert.Contains("99.5", all); Assert.Contains("0.25", all);
        Assert.Contains("Open", all); Assert.Contains("TrailingStopMoved", all); Assert.Contains("TakeProfitExtended", all);
        foreach (var message in decisionMessages) Assert.Contains(message, all);
        var closed = TradeRow(entries["xl/worksheets/sheet4.xml"], "Closed");
        Assert.Equal("-9.97", closed["Gross P/L"]); Assert.Equal("-9.97", closed["Net P/L"]); Assert.Equal("USD", closed["Account Currency"]);
        var open = TradeRow(entries["xl/worksheets/sheet4.xml"], "Open");
        foreach (var field in new[] { "Gross P/L", "Net P/L", "P/L % On Margin", "Account Return %", "Exit UTC", "Exit Price", "Exit Bid", "Exit Ask", "Exit Spread", "Exit Reason", "Final SL", "Final TP", "Current Executable Exit Price", "Current Gross P/L", "Current Net P/L", "Current P/L % On Margin", "Current P/L Valuation UTC", "Current P/L Available", "Legacy Gross P/L USDT", "Legacy Net P/L USDT", "Legacy Fees USDT" }) Assert.Null(open[field]);
        using var scope = factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperSessions.SingleAsync(item => item.Id == id);
        Assert.Equal(PaperSessionStatus.Running, persisted.Status);
    }

    [Fact]
    public async Task Export_ReturnsNotFoundForMissingSession()
    {
        using var client = await AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/paper-sessions/999999/export/excel")).StatusCode);
    }

    private async Task<(int Id, IReadOnlyList<string> Messages)> SeedAsync()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10); var messages = Enumerable.Range(1, 60).Select(index => $"decision-ledger-{index:00}").ToArray();
        using var scope = factory.Services.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var first = new PaperSessionSymbol { Symbol = "EURUSDm", BrokerSymbol = "EURUSDm", ContractSize = 100000m, VolumeMin = .01m, VolumeStep = .01m, TickSize = .00001m, TickValueProfit = 1m, TickValueLoss = 1m, PointSize = .00001m, StopsLevelPoints = 10, CommissionPerLotPerSide = 0m, PendingDirection = SignalDirection.Long, PendingCrossoverTimeUtc = now, PendingSignalTimeUtc = now.AddMinutes(3), PendingStopPrice = 1.08m, PendingStopSourceType = StopSourceType.Pivot, PendingStopSourceTimeUtc = now, PendingSignalClose = 1.1m, PendingSignalEma9 = 1.11m, PendingSignalEma15 = 1.09m, PendingSignalEma100 = 1.05m, PendingSignalAtr14 = .001m, PendingReversalPowerScore = 80m, PendingReversalPowerBand = ReversalPowerBand.Strong, PendingStopAnchorPrice = 1.081m, PendingStopBuffer = .0002m };
        var second = new PaperSessionSymbol { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm", ContractSize = 100m, VolumeMin = .01m, VolumeStep = .01m, TickSize = .01m, TickValueProfit = 1m, TickValueLoss = 1m, PointSize = .01m, StopsLevelPoints = 10, CommissionPerLotPerSide = 0m };
        var session = new PaperSession { MarketDataSource = MarketDataSource.Mt5Exness, Interval = "3m", Status = PaperSessionStatus.Running, CreatedAtUtc = now, StartedAtUtc = now, RiskReward = 2m, WaitForConfirmationCandle = true, UseEma100Filter = true, UseAdaptiveInitialStop = true, TrailingStopEnabled = true, AccountCurrency = "USD", PaperPositionSizingMode = PaperPositionSizingMode.FixedLots, PaperFixedLots = .01m, StartingBalance = 1000m, CurrentBalance = 990.03m, UsedMargin = 35m, NetPnl = -9.97m, TotalTradingCosts = 0m, Symbols = [first, second] };
        var closed = Trade(first, now, PaperTradeStatus.Closed, -9.97m); closed.Events.AddRange([new PaperTradeEvent { TimeUtc = now.AddMinutes(3), Type = PaperTradeEventType.Entry, MarketPrice = 100m }, new PaperTradeEvent { TimeUtc = now.AddMinutes(6), Type = PaperTradeEventType.TrailingStopMoved, MarketPrice = 101m, OldStop = 99m, NewStop = 100m, ProgressPercent = 50m }, new PaperTradeEvent { TimeUtc = now.AddMinutes(9), Type = PaperTradeEventType.TakeProfitExtended, MarketPrice = 102m, OldTakeProfit = 102m, NewTakeProfit = 103m, ProgressPercent = 70m }, new PaperTradeEvent { TimeUtc = now.AddMinutes(12), Type = PaperTradeEventType.Exit, MarketPrice = 90m }]);
        var open = Trade(second, now, PaperTradeStatus.Open, null); session.Trades.AddRange([closed, open]);
        foreach (var message in messages) session.DecisionEvents.Add(new PaperDecisionEvent { PaperSessionSymbol = first, TimeUtc = now, CandleCloseTimeUtc = now, Stage = "CandleEvaluated", Direction = SignalDirection.Long, Message = message, Ema9 = 101m, Ema15 = 100m, Ema100 = 99m, GapPercent = 1m, GapState = GapState.Expanding, StopPrice = 99m, StopSource = StopSourceType.AdaptiveMicroStructure, ExpectedEntryOpenUtc = now.AddMinutes(3), Bid = 100m, Ask = 100.01m, EntryPrice = 100.01m, Lots = .01m, RequiredMargin = 35m });
        var otherSymbol = new PaperSessionSymbol { Symbol = "SESSION-B-ONLY" }; var other = new PaperSession { MarketDataSource = MarketDataSource.Mt5Exness, Interval = "3m", Status = PaperSessionStatus.Stopped, CreatedAtUtc = now, StartedAtUtc = now, AccountCurrency = "USD", Symbols = [otherSymbol] }; var otherTrade = Trade(otherSymbol, now, PaperTradeStatus.Closed, 1m); otherTrade.Events.Add(new PaperTradeEvent { TimeUtc = now, Type = PaperTradeEventType.Exit, MarketPrice = 1m }); other.Trades.Add(otherTrade); other.DecisionEvents.Add(new PaperDecisionEvent { PaperSessionSymbol = otherSymbol, TimeUtc = now, Stage = "Other", Message = "SESSION-B-ONLY" });
        database.AddRange(session, other); await database.SaveChangesAsync(); return (session.Id, messages);
    }

    private static PaperTrade Trade(PaperSessionSymbol symbol, DateTimeOffset at, PaperTradeStatus status, decimal? net) => new() { PaperSessionSymbol = symbol, Symbol = symbol.Symbol, Interval = "3m", Status = status, Direction = SignalDirection.Long, CrossoverTimeUtc = at, SignalTimeUtc = at, EntryTimeUtc = at.AddMinutes(3), ExitTimeUtc = status == PaperTradeStatus.Closed ? at.AddMinutes(12) : null, EntryPrice = 100m, ExitPrice = status == PaperTradeStatus.Closed ? 90m : null, Quantity = 1m, InitialStopLoss = 99m, CurrentStopLoss = 99.5m, FinalStopLoss = status == PaperTradeStatus.Closed ? 99.5m : null, StopSourceType = StopSourceType.AdaptiveMicroStructure, StopSourceTimeUtc = at, OriginalTakeProfit = 102m, CurrentTakeProfit = 103m, FinalTakeProfit = status == PaperTradeStatus.Closed ? 103m : null, TakeProfitExtended = true, Lots = .01m, RequiredMargin = 35m, MarginUsed = 35m, AccountEquityAtEntry = 1000m, EntryBid = 100m, EntryAsk = 100.01m, EntrySpread = .01m, ExitBid = 90m, ExitAsk = 90.01m, ExitSpread = .01m, GrossPnl = net, RoundTripCommission = 0m, NetPnl = net, NetPnlPercent = net is null ? 0m : net.Value / 1000m * 100m, MfePrice = 102m, MfePercent = 2m, MaePrice = 99m, MaePercent = 1m, BestFavorableProgressPercent = 70m, SignalOpen = 100m, SignalClose = 101m, SignalEma9 = 101m, SignalEma15 = 100m, SignalEma100 = 99m, SignalGapPercent = 1m, SignalGapState = GapState.Expanding, UseAdaptiveInitialStop = true, SignalAtr14 = 1.25m, ReversalPowerScore = 80m, ReversalPowerBand = ReversalPowerBand.Strong, StopAnchorPrice = 99.5m, StopBuffer = .25m };
    private static Dictionary<string, string> Read(byte[] bytes) { using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); return archive.Entries.ToDictionary(entry => entry.FullName, entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }); }
    private static IReadOnlyDictionary<string, string?> TradeRow(string xml, string status)
    {
        var ns = (XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = XDocument.Parse(xml).Descendants(ns + "row").Select(row => row.Elements(ns + "c").Select(cell => cell.Element(ns + "v")?.Value ?? cell.Descendants(ns + "t").FirstOrDefault()?.Value).ToArray()).ToArray();
        var headers = rows[0]; var row = rows.Single(item => item.Length > 5 && item[5] == status);
        return headers.Select((header, index) => (header!, Value: row[index])).ToDictionary(item => item.Item1, item => item.Value);
    }
    private async Task<HttpClient> AdminClientAsync() { var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); var token = (await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery")).GetProperty("token").GetString()!; using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) }; request.Headers.Add("X-CSRF-TOKEN", token); Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode); return client; }
}
