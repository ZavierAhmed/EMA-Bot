using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmaBot.Api.Auth;
using EmaBot.Api.Binance;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EmaBot.Api.Tests;

public sealed class TradeExplorerApiTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory factory;
    public TradeExplorerApiTests(EmaBotApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Trades_ListDetailAndChart_UseExistingTradeSourcesAndChartOpenTimes()
    {
        var (backtestId, paperId, start) = await SeedAsync();
        using var client = await AdminClientAsync();
        var all = await client.GetFromJsonAsync<JsonElement>("/api/trades");
        Assert.Equal(2, all.GetArrayLength());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/trades?source=Backtest")).EnumerateArray());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/trades?source=Paper")).EnumerateArray());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/trades?status=Open")).EnumerateArray());
        foreach (var value in new[] { "source=invalid", "direction=None", "status=bad", "outcome=bad" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/trades?{value}")).StatusCode);
        var backtest = await client.GetFromJsonAsync<JsonElement>($"/api/trades/backtest/{backtestId}");
        Assert.Equal("Backtest", backtest.GetProperty("summary").GetProperty("source").GetString()); Assert.Equal(3m, backtest.GetProperty("riskReward").GetDecimal()); Assert.Equal(101m, backtest.GetProperty("signalEma9").GetDecimal()); Assert.Single(backtest.GetProperty("events").EnumerateArray());
        var paper = await client.GetFromJsonAsync<JsonElement>($"/api/trades/paper/{paperId}");
        Assert.Equal("Paper", paper.GetProperty("summary").GetProperty("source").GetString()); Assert.Equal(4m, paper.GetProperty("riskReward").GetDecimal());
        factory.BinanceClient.Klines = Candles(start.AddHours(-7), 180);
        var chart = await client.GetFromJsonAsync<JsonElement>($"/api/trades/backtest/{backtestId}/chart");
        var candleTimes = chart.GetProperty("candles").EnumerateArray().Select(item => item.GetProperty("openTimeUtc").GetDateTimeOffset()).ToHashSet();
        foreach (var name in new[] { "ema9", "ema15", "ema100" }) foreach (var point in chart.GetProperty(name).EnumerateArray()) Assert.Contains(point.GetProperty("timeUtc").GetDateTimeOffset(), candleTimes);
        factory.BinanceClient.KlinesException = new BinanceApiException("down"); Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync($"/api/trades/backtest/{backtestId}/chart")).StatusCode); factory.BinanceClient.KlinesException = null;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/trades/backtest/999999")).StatusCode);
    }

    private async Task<(int BacktestId, int PaperId, DateTimeOffset Start)> SeedAsync()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-4); using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var run = new BacktestRun { Symbol = "BTCUSDT", Interval = "3m", RequestedStartUtc = start, RequestedEndUtc = start.AddHours(1), CreatedAtUtc = start, RiskReward = 3m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = .1m };
        var backtest = new BacktestTrade { Direction = SignalDirection.Long, CrossoverTimeUtc = start, SignalTimeUtc = start.AddMinutes(3).AddMilliseconds(-1), EntryTimeUtc = start.AddMinutes(6), ExitTimeUtc = start.AddMinutes(12).AddMilliseconds(-1), EntryPrice = 100m, ExitPrice = 110m, Quantity = 1m, EntryNotionalUsdt = 100m, InitialStopLoss = 90m, FinalStopLoss = 95m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = start, OriginalTakeProfit = 120m, FinalTakeProfit = 122m, ExitReason = BacktestExitReason.TakeProfit, EntryFeeUsdt = .1m, ExitFeeUsdt = .1m, TotalFeesUsdt = .2m, GrossPnlUsdt = 10m, NetPnlUsdt = 9.8m, NetPnlPercent = 9.8m, GrossRMultiple = 1m, NetRMultiple = .98m, SignalClose = 100m, SignalEma9 = 101m, SignalEma15 = 99m, SignalGapState = GapState.Expanding, Events = [new BacktestTradeEvent { TimeUtc = start.AddMinutes(9).AddMilliseconds(-1), EffectiveTimeUtc = start.AddMinutes(12), Type = BacktestTradeEventType.TrailingStopMoved, MarketPrice = 110m, NewStop = 95m }] }; run.Trades.Add(backtest);
        var session = new PaperSession { Interval = "3m", Status = PaperSessionStatus.Stopped, CreatedAtUtc = start, StartedAtUtc = start, RiskReward = 4m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = .1m }; var symbol = new PaperSessionSymbol { Symbol = "ETHUSDT" }; session.Symbols.Add(symbol); var paper = new PaperTrade { PaperSessionSymbol = symbol, Symbol = "ETHUSDT", Interval = "3m", Status = PaperTradeStatus.Open, Direction = SignalDirection.Short, CrossoverTimeUtc = start, SignalTimeUtc = start, EntryTimeUtc = start.AddMinutes(6), EntryPrice = 100m, Quantity = 1m, EntryNotionalUsdt = 100m, InitialStopLoss = 110m, CurrentStopLoss = 110m, StopSourceType = StopSourceType.Pivot, StopSourceTimeUtc = start, OriginalTakeProfit = 80m, CurrentTakeProfit = 80m, EntryFeeUsdt = .1m, TotalFeesUsdt = .1m, SignalGapState = GapState.Expanding, Events = [new PaperTradeEvent { TimeUtc = start.AddMinutes(6), Type = PaperTradeEventType.Entry, MarketPrice = 100m }] }; session.Trades.Add(paper); db.AddRange(run, session); await db.SaveChangesAsync(); return (backtest.Id, paper.Id, start);
    }
    private async Task<HttpClient> AdminClientAsync() { var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }); var token = await Token(client); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) }; request.Headers.Add("X-CSRF-TOKEN", token); Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode); return client; }
    private static async Task<string> Token(HttpClient client) { var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery"); return response.GetProperty("token").GetString()!; }
    private static IReadOnlyList<Candle> Candles(DateTimeOffset start, int count) => Enumerable.Range(0, count).Select(index => { var open = start.AddMinutes(index * 3); return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), 100m + index, 101m + index, 99m + index, 100m + index, 1m, true); }).ToArray();
}
