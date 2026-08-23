using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategySessionExcelExportTests(EmaBotApiFactory factory) : IClassFixture<EmaBotApiFactory>
{
    [Fact]
    public async Task StoppedSessionExport_ContainsForensicLedgersAndExcludesSensitiveExecutionFields()
    {
        var id = await SeedAsync(DemoStrategySessionStatus.Stopped);
        using var client = await AdminClientAsync();
        using var response = await client.GetAsync($"/api/demo-strategy-sessions/{id}/export/excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"ema-bot-exness-demo-session-{id}.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var all = string.Concat(Read(await response.Content.ReadAsByteArrayAsync()).Values);
        foreach (var sheet in new[] { "SESSION SUMMARY", "INTENTS AND REASONING", "BROKER EXECUTIONS", "POSITION MANAGEMENT", "MANAGEMENT ACTIONS", "BROKER PNL EVIDENCE" }) Assert.Contains(sheet, all);
        Assert.DoesNotContain("MARKET PATH", all);
        Assert.Contains("blocked because durable broker evidence was unavailable", all); Assert.Contains("NativeExitReason", all); Assert.Contains("BrokerHistoryProfit", all); Assert.Contains("ModifyProtection", all);
        Assert.DoesNotContain("hidden-fingerprint", all); Assert.DoesNotContain("hidden-server", all); Assert.DoesNotContain("CorrelationMarker", all);
    }

    [Fact]
    public async Task ExnessDemoExplorer_ReturnsOnlyLinkedExecutionWithSafeBrokerEvidence()
    {
        var sessionId = await SeedAsync(DemoStrategySessionStatus.Stopped);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        db.DemoExecutions.Add(new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Closed, ExpectedAccountFingerprint = "standalone-fingerprint", ExpectedServer = "standalone-server", CorrelationMarker = "standalone-marker", BrokerSymbol = "BTCUSDm", Side = "Buy", VolumeLots = .01m, CreatedAtUtc = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        var linked = await db.DemoStrategyIntents.Where(item => item.DemoStrategySessionId == sessionId && item.DemoExecutionId != null).Select(item => item.DemoExecutionId!.Value).SingleAsync();
        using var client = await AdminClientAsync();
        var rows = await client.GetFromJsonAsync<JsonElement>($"/api/trades?source=ExnessDemo&sessionId={sessionId}");
        var list = rows.EnumerateArray().ToArray(); Assert.Single(list); Assert.Equal(linked, list[0].GetProperty("id").GetInt32()); Assert.Equal(sessionId, list[0].GetProperty("sessionId").GetInt32()); Assert.Equal("BTCUSDm", list[0].GetProperty("symbol").GetString()); Assert.Equal("3m", list[0].GetProperty("interval").GetString()); Assert.Equal("Closed", list[0].GetProperty("status").GetString()); Assert.Equal(4m, list[0].GetProperty("netPnl").GetDecimal());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/trades/exnessdemo/{linked}"); var json = detail.GetRawText();
        Assert.Contains("NativePositionHistory", json); Assert.Contains("brokerHistoryProfit", json); Assert.DoesNotContain("ExpectedAccountFingerprint", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("hidden-fingerprint", json); Assert.DoesNotContain("CorrelationMarker", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DemoStrategySessionStatus.Running)]
    [InlineData(DemoStrategySessionStatus.Interrupted)]
    public async Task NonterminalSessionExport_IsRejected(DemoStrategySessionStatus status)
    {
        var id = await SeedAsync(status);
        using var client = await AdminClientAsync();
        var response = await client.GetAsync($"/api/demo-strategy-sessions/{id}/export/excel");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Stop or finish the Exness Demo strategy session", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CapturedFutureSessionExport_IncludesChronologicalExactMarketPathWithOriginAndObservationTime()
    {
        var id = await SeedAsync(DemoStrategySessionStatus.Stopped); var first = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero); var second = first.AddMinutes(3);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var symbol = await db.DemoStrategySessionSymbols.SingleAsync(item => item.DemoStrategySessionId == id);
            db.DemoStrategySessionCandles.AddRange(Candle(symbol.Id, second, 78240.12345678m, DemoStrategySessionCandleObservationOrigin.LiveClosedCandle, second.AddSeconds(2)), Candle(symbol.Id, first, 78234.12345678m, DemoStrategySessionCandleObservationOrigin.BootstrapHistory, first.AddSeconds(1)));
            await db.SaveChangesAsync();
        }

        using var client = await AdminClientAsync(); using var response = await client.GetAsync($"/api/demo-strategy-sessions/{id}/export/excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sheets = Read(await response.Content.ReadAsByteArrayAsync()); var all = string.Concat(sheets.Values);
        Assert.Contains("MARKET PATH", all); var path = sheets["xl/worksheets/sheet7.xml"];
        Assert.True(path.IndexOf(first.ToString("O"), StringComparison.Ordinal) < path.IndexOf(second.ToString("O"), StringComparison.Ordinal));
        Assert.Contains("78234.12345678", path); Assert.Contains("78235.12345678", path); Assert.Contains("78233.12345678", path); Assert.Contains("78234.62345678", path); Assert.Contains("12.34567891", path);
        Assert.Contains("78234.22345678", path); Assert.Contains("78234.32345678", path); Assert.Contains("BootstrapHistory", path); Assert.Contains("LiveClosedCandle", path); Assert.Contains(first.AddSeconds(1).ToString("O"), path);
    }

    private static DemoStrategySessionCandle Candle(int symbolId, DateTimeOffset close, decimal price, DemoStrategySessionCandleObservationOrigin origin, DateTimeOffset observed) => new() { DemoStrategySessionSymbolId = symbolId, OpenTimeUtc = close.AddMinutes(-3), CloseTimeUtc = close, Open = price, High = price + 1m, Low = price - 1m, Close = price + .5m, Volume = 12.34567891m, Ema9 = price + .1m, Ema15 = price + .2m, Ema100 = null, ObservationOrigin = origin, ObservedAtUtc = observed };

    private async Task<int> SeedAsync(DemoStrategySessionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var symbol = new DemoStrategySessionSymbol { Symbol = "BTCUSDm", BrokerSymbol = "BTCUSDm" };
        var execution = new DemoExecution { ClientExecutionId = Guid.NewGuid(), State = DemoExecutionState.Closed, Provider = "MT5", ExpectedAccountFingerprint = "hidden-fingerprint", ExpectedServer = "hidden-server", CorrelationMarker = "hidden-marker", BrokerSymbol = "BTCUSDm", Side = "Buy", VolumeLots = .01m, FilledVolumeLots = .01m, AverageFillPrice = 80000m, ClosedVolumeLots = .01m, AverageClosePrice = 80100m, PositionTicket = 1, PositionIdentifier = 1, EntryDealTicket = 2, ExitDealTicket = 3, NativeExitReason = "TP", BrokerAccountCurrency = "USD", BrokerHistoryProfit = 5m, BrokerHistoryCommission = -1m, BrokerHistorySwap = 0m, BrokerHistoryFee = 0m, BrokerHistoryPnlObservedAtUtc = now, CreatedAtUtc = now, BrokerExecutedAtUtc = now, BrokerClosedAtUtc = now, ReconciliationSource = "NativePositionHistory" };
        execution.ManagementActions.Add(new DemoExecutionManagementAction { ClientManagementActionId = Guid.NewGuid(), Kind = DemoExecutionManagementActionKind.ModifyProtection, State = DemoExecutionManagementActionState.Applied, CreatedAtUtc = now });
        var session = new DemoStrategySession { Interval = "3m", Status = status, CreatedAtUtc = now, InitialAllocation = 100m, FixedLots = .01m, RiskReward = 2m, Symbols = [symbol] };
        var linked = new DemoStrategyIntent { DemoStrategySession = session, DemoStrategySessionSymbol = symbol, Direction = EmaBot.Api.Strategy.SignalDirection.Long, CrossoverTimeUtc = now, SignalTimeUtc = now, ExpectedEntryOpenUtc = now, StructuralStopLoss = 79000m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = now, IntendedTakeProfit = 82000m, IntendedVolumeLots = .01m, ClientExecutionId = execution.ClientExecutionId, Status = DemoStrategyIntentStatus.ExecutionLinked, CreatedAtUtc = now, DemoExecution = execution };
        var blocked = new DemoStrategyIntent { DemoStrategySession = session, DemoStrategySessionSymbol = symbol, Direction = EmaBot.Api.Strategy.SignalDirection.Long, CrossoverTimeUtc = now, SignalTimeUtc = now, ExpectedEntryOpenUtc = now, StructuralStopLoss = 79000m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = now, IntendedVolumeLots = .01m, ClientExecutionId = Guid.NewGuid(), Status = DemoStrategyIntentStatus.Blocked, Reason = "blocked because durable broker evidence was unavailable", CreatedAtUtc = now };
        db.AddRange(session, linked, blocked); await db.SaveChangesAsync();
        db.DemoStrategyPositionManagement.Add(new DemoStrategyPositionManagement { DemoStrategySessionId = session.Id, DemoStrategySessionSymbolId = symbol.Id, DemoStrategyIntentId = linked.Id, DemoExecutionId = execution.Id, State = DemoStrategyPositionManagementState.Active, OriginalEntryPrice = 80000m, OriginalStopLoss = 79000m, OriginalTakeProfit = 82000m, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(); return session.Id;
    }

    private static Dictionary<string, string> Read(byte[] bytes) { using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); return archive.Entries.ToDictionary(entry => entry.FullName, entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }); }
    private async Task<HttpClient> AdminClientAsync() { var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); var token = (await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery")).GetProperty("token").GetString()!; using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) }; request.Headers.Add("X-CSRF-TOKEN", token); Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode); return client; }
}
