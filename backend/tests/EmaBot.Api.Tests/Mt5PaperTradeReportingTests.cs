using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EmaBot.Api.Tests;

public sealed class Mt5PaperTradeReportingTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory factory;
    public Mt5PaperTradeReportingTests(EmaBotApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Mt5PaperSummaryAndDetail_UseBrokerEconomicsAndUsd()
    {
        var (shortTrade, longTrade) = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var controller = new TradesController(scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(), new RecordingResolver(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>()));

        var rowsResponse = await controller.List("Paper", null, null, null, null, null, 50, CancellationToken.None);
        var rows = rowsResponse.Value ?? Assert.IsAssignableFrom<IReadOnlyList<TradeSummaryResponse>>(Assert.IsType<OkObjectResult>(rowsResponse.Result).Value);
        var shortSummary = rows.Single(item => item.Id == shortTrade.Id);
        var longSummary = rows.Single(item => item.Id == longTrade.Id);
        var detailResponse = await controller.Detail("paper", shortTrade.Id, CancellationToken.None);
        var detail = detailResponse.Value ?? Assert.IsType<TradeDetailResponse>(Assert.IsType<OkObjectResult>(detailResponse.Result).Value);

        Assert.Equal(-9.97m, shortSummary.NetPnl); Assert.Equal("USD", shortSummary.AccountCurrency); Assert.Equal("MarginUsed", shortSummary.PnlPercentBasis); Assert.Null(shortSummary.NetRMultiple);
        Assert.Equal(5.03m, longSummary.NetPnl); Assert.Equal(-9.97m / 35m * 100m, shortSummary.NetPnlPercent); Assert.Equal(-9.97m / 35m * 100m, shortSummary.PnlPercentOnMargin); Assert.Equal(-9.97m / 105.03m * 100m, shortSummary.AccountReturnPercent);
        Assert.Equal(.01m, detail.Lots); Assert.Equal(35m, detail.RequiredMargin); Assert.Equal(4353.245m, detail.EntryBid); Assert.Equal(4363.215m, detail.ExitAsk); Assert.Equal("FixedLots", detail.PaperPositionSizingMode);

        var winResponse = await controller.List("Paper", null, null, null, null, "Win", 50, CancellationToken.None);
        var lossResponse = await controller.List("Paper", null, null, null, null, "Loss", 50, CancellationToken.None);
        var wins = winResponse.Value ?? Assert.IsAssignableFrom<IReadOnlyList<TradeSummaryResponse>>(Assert.IsType<OkObjectResult>(winResponse.Result).Value);
        var losses = lossResponse.Value ?? Assert.IsAssignableFrom<IReadOnlyList<TradeSummaryResponse>>(Assert.IsType<OkObjectResult>(lossResponse.Result).Value);
        Assert.Single(wins); Assert.Equal(longTrade.Id, wins.Single().Id); Assert.Single(losses); Assert.Equal(shortTrade.Id, losses.Single().Id);
    }

    [Fact]
    public async Task Mt5PaperExportRow_UsesCurrencyLotsQuotesAndNoLegacyRiskMultiple()
    {
        var (shortTrade, _) = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var trade = await scope.ServiceProvider.GetRequiredService<EmaBotDbContext>().PaperTrades.Include(item => item.PaperSession).Include(item => item.Events).SingleAsync(item => item.Id == shortTrade.Id);

        var row = TradeExportRow.From(trade);
        var pdf = System.Text.Encoding.ASCII.GetString(SimpleExports.Pdf(row));

        Assert.Equal(MarketDataSource.Mt5Exness, row.MarketDataSource); Assert.Equal("USD", row.AccountCurrency); Assert.Equal(-9.97m, row.NetPnl); Assert.Equal(.01m, row.Lots); Assert.Equal(4363.215m, row.ExitAsk); Assert.Null(row.R);
        Assert.Contains("USD", pdf); Assert.Contains("FixedLots", pdf); Assert.Contains("0.01", pdf); Assert.DoesNotContain("FixedNotional", pdf);
    }

    [Fact]
    public async Task Mt5PaperChart_ResolvesMt5HistoricalProvider()
    {
        var (shortTrade, _) = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var resolver = new RecordingResolver(factory.Services.GetRequiredService<IHistoricalMarketDataProvider>());
        var controller = new TradesController(scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(), resolver);

        await controller.Chart("paper", shortTrade.Id, CancellationToken.None);

        Assert.Equal(MarketDataSource.Mt5Exness, resolver.LastSource);
    }

    private async Task<(PaperTrade Short, PaperTrade Long)> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var start = DateTimeOffset.UnixEpoch.AddDays((await database.PaperSessions.CountAsync()) + 100);
        var symbol = new PaperSessionSymbol { Symbol = $"XAUUSDm-{Guid.NewGuid():N}", BrokerSymbol = "XAUUSDm" };
        var session = new PaperSession { MarketDataSource = MarketDataSource.Mt5Exness, Interval = "3m", Status = PaperSessionStatus.Stopped, CreatedAtUtc = start, StartedAtUtc = start, AccountCurrency = "USD", PaperPositionSizingMode = PaperPositionSizingMode.FixedLots, PaperFixedLots = .01m, StartingBalance = 100m, CurrentBalance = 95.06m, Symbols = [symbol] };
        var shortTrade = Trade(symbol, SignalDirection.Short, start, 4353.245m, 4363.215m, -9.97m, PaperExitReason.InitialStopLoss, 105.03m, 4353.245m, 4353.4m, 4363.1m, 4363.215m);
        var longTrade = Trade(symbol, SignalDirection.Long, start.AddMinutes(10), 4351.501m, 4356.532m, 5.03m, PaperExitReason.TakeProfit, 100m, 4351.3m, 4351.501m, 4356.532m, 4356.7m);
        session.Trades.Add(shortTrade); session.Trades.Add(longTrade); database.PaperSessions.Add(session); await database.SaveChangesAsync(); return (shortTrade, longTrade);
    }

    private static PaperTrade Trade(PaperSessionSymbol symbol, SignalDirection direction, DateTimeOffset at, decimal entry, decimal exit, decimal net, PaperExitReason reason, decimal equity, decimal entryBid, decimal entryAsk, decimal exitBid, decimal exitAsk) => new() { PaperSessionSymbol = symbol, Symbol = symbol.Symbol, Interval = "3m", Status = PaperTradeStatus.Closed, Direction = direction, CrossoverTimeUtc = at, SignalTimeUtc = at, EntryTimeUtc = at, ExitTimeUtc = at.AddMinutes(3), EntryPrice = entry, ExitPrice = exit, Quantity = 1m, InitialStopLoss = direction == SignalDirection.Long ? entry - 2m : entry + 2m, CurrentStopLoss = direction == SignalDirection.Long ? entry - 2m : entry + 2m, FinalStopLoss = direction == SignalDirection.Long ? entry - 2m : entry + 2m, StopSourceType = StopSourceType.FallbackLookback, StopSourceTimeUtc = at, OriginalTakeProfit = direction == SignalDirection.Long ? entry + 4m : entry - 4m, CurrentTakeProfit = direction == SignalDirection.Long ? entry + 4m : entry - 4m, FinalTakeProfit = direction == SignalDirection.Long ? entry + 4m : entry - 4m, ExitReason = reason, Lots = .01m, RequiredMargin = 35m, MarginUsed = 35m, AccountEquityAtEntry = equity, RoundTripCommission = 0m, GrossPnl = net, NetPnl = net, NetPnlPercent = net / equity * 100m, EntryBid = entryBid, EntryAsk = entryAsk, EntrySpread = entryAsk - entryBid, ExitBid = exitBid, ExitAsk = exitAsk, ExitSpread = exitAsk - exitBid };
    private sealed class RecordingResolver(IHistoricalMarketDataProvider provider) : IHistoricalMarketDataProviderResolver { public MarketDataSource? LastSource { get; private set; } public IHistoricalMarketDataProvider Resolve(MarketDataSource source) { LastSource = source; return provider; } }
}
