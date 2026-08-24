using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace EmaBot.Api.Services;

public sealed class BacktestService(EmaBotDbContext database, IHistoricalMarketDataProvider historical, TradingSettingsService settingsService, BacktestEngine engine, ILogger<BacktestService>? logger = null)
{
    public async Task<BacktestRun> RunAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        var total = Stopwatch.StartNew();
        logger?.LogInformation("Backtest execution history fetch started for {BrokerSymbol} {Timeframe} from {StartUtc} to {EndUtc}.", symbol, interval, start, end);
        var executionFetch = Stopwatch.StartNew();
        var retrieved = await historical.GetRangeAsync(symbol, interval, start, end, token);
        executionFetch.Stop();
        var candles = retrieved.Where(candle => candle.OpenTimeUtc >= start && candle.CloseTimeUtc <= end && candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
        logger?.LogInformation("Backtest execution history fetch completed for {BrokerSymbol} {Timeframe} with {CandleCount} closed candles in {ElapsedMilliseconds} ms.", symbol, interval, candles.Length, executionFetch.ElapsedMilliseconds);
        var settings = await settingsService.GetAsync(token); StrategyMarketContext? context = null;
        if (settings.UseHtfRegimeFilter)
        {
            var htf = HigherTimeframeRegime.ForExecutionTimeframe(interval);
            IReadOnlyList<Candle>? htfCandles = null;
            if (htf is not null)
            {
                var htfStart = start - HigherTimeframeRegime.WarmupDuration(htf);
                logger?.LogInformation("Backtest HTF history fetch started for {BrokerSymbol} {Timeframe} from {StartUtc} to {EndUtc}.", symbol, htf, htfStart, end);
                var htfFetch = Stopwatch.StartNew();
                htfCandles = (await historical.GetRangeAsync(symbol, htf, htfStart, end, token)).Where(candle => candle.IsClosed && candle.CloseTimeUtc <= end).OrderBy(candle => candle.CloseTimeUtc).ToArray();
                htfFetch.Stop();
                logger?.LogInformation("Backtest HTF history fetch completed for {BrokerSymbol} {Timeframe} with {CandleCount} closed candles in {ElapsedMilliseconds} ms.", symbol, htf, htfCandles.Count, htfFetch.ElapsedMilliseconds);
            }
            context = new(candles, htf, htfCandles);
        }
        logger?.LogInformation("Backtest strategy engine started for {BrokerSymbol} {Timeframe} with {CandleCount} execution candles.", symbol, interval, candles.Length);
        var engineStopwatch = Stopwatch.StartNew();
        var calculation = engine.Run(candles, settings, context); var trades = calculation.Trades;
        engineStopwatch.Stop();
        logger?.LogInformation("Backtest strategy engine completed for {BrokerSymbol} {Timeframe} in {ElapsedMilliseconds} ms.", symbol, interval, engineStopwatch.ElapsedMilliseconds);
        var run = new BacktestRun { MarketDataSource = MarketDataSource.Mt5Exness, Symbol = symbol, Interval = interval, RequestedStartUtc = start, RequestedEndUtc = end, ActualStartUtc = candles.FirstOrDefault()?.OpenTimeUtc, ActualEndUtc = candles.LastOrDefault()?.CloseTimeUtc, CreatedAtUtc = DateTimeOffset.UtcNow, CompletedAtUtc = DateTimeOffset.UtcNow, CandleCount = candles.Length, RiskReward = settings.RiskReward, FixedOrderSizeUsdt = settings.FixedOrderSizeUsdt, MinEmaGapPercent = settings.MinEmaGapPercent, MaxStopDistancePercent = settings.MaxStopDistancePercent, PositionSizingMode = settings.PositionSizingMode, StartingBalanceUsdt = settings.SimulatedAccountBalanceUsdt, EndingBalanceUsdt = calculation.EndingEquityUsdt, MarginPerTradePercent = settings.MarginPerTradePercent, Leverage = settings.Leverage, WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, UseHtfRegimeFilter = settings.UseHtfRegimeFilter, TrailingStopEnabled = settings.TrailingStopEnabled, UseAdaptiveInitialStop = settings.UseAdaptiveInitialStop, SameTrendReentryEnabled = settings.SameTrendReentryEnabled, MaxReentryAgeBars = settings.MaxReentryAgeBars, ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover, FeePercentPerSide = settings.FeePercentPerSide, Status = BacktestRunStatus.Completed, Trades = trades.ToList() };
        PopulateSummary(run, calculation.Diagnostics); database.BacktestRuns.Add(run);
        logger?.LogInformation("Backtest database persistence started for {BrokerSymbol} {Timeframe}.", symbol, interval);
        var persistence = Stopwatch.StartNew();
        await database.SaveChangesAsync(token);
        persistence.Stop(); total.Stop();
        logger?.LogInformation("Backtest database persistence completed for {BrokerSymbol} {Timeframe} in {ElapsedMilliseconds} ms.", symbol, interval, persistence.ElapsedMilliseconds);
        logger?.LogInformation("Backtest completed for {BrokerSymbol} {Timeframe} from {StartUtc} to {EndUtc} in {ElapsedMilliseconds} ms.", symbol, interval, start, end, total.ElapsedMilliseconds);
        return run;
    }
    public Task<List<BacktestRun>> ListAsync(CancellationToken token) => database.BacktestRuns.AsNoTracking().OrderByDescending(run => run.CreatedAtUtc).Take(30).ToListAsync(token);
    public Task<BacktestRun?> GetAsync(int id, CancellationToken token) => database.BacktestRuns.AsNoTracking().Include(run => run.Trades).ThenInclude(trade => trade.Events).SingleOrDefaultAsync(run => run.Id == id, token);
    public async Task<bool> DeleteAsync(int id, CancellationToken token)
    {
        var run = await database.BacktestRuns.FindAsync([id], token);
        if (run is null) return false;
        // Keep deletion deterministic for the in-memory test provider as well as relying on the database cascade.
        database.BacktestTrades.RemoveRange(await database.BacktestTrades.Where(trade => trade.BacktestRunId == id).ToListAsync(token));
        database.BacktestRuns.Remove(run);
        await database.SaveChangesAsync(token);
        return true;
    }
    private static void PopulateSummary(BacktestRun run, BacktestDiagnostics d)
    {
        var trades = run.Trades;
        run.TotalTrades = trades.Count; run.WinningTrades = trades.Count(x => x.NetPnlUsdt > 0); run.LosingTrades = trades.Count(x => x.NetPnlUsdt < 0); run.BreakEvenTrades = trades.Count(x => x.NetPnlUsdt == 0); run.LongTrades = trades.Count(x => x.Direction == Strategy.SignalDirection.Long); run.ShortTrades = trades.Count - run.LongTrades;
        run.WinRatePercent = trades.Count == 0 ? 0 : (decimal)run.WinningTrades / trades.Count * 100; run.GrossPnlUsdt = trades.Sum(x => x.GrossPnlUsdt); run.NetPnlUsdt = trades.Sum(x => x.NetPnlUsdt); run.TotalFeesUsdt = trades.Sum(x => x.TotalFeesUsdt); run.AverageNetPnlUsdt = trades.Count == 0 ? 0 : run.NetPnlUsdt / trades.Count; run.AverageRMultiple = trades.Count == 0 ? 0 : trades.Average(x => x.GrossRMultiple);
        var loss = trades.Where(x => x.GrossPnlUsdt < 0).Sum(x => -x.GrossPnlUsdt); run.ProfitFactor = loss == 0 ? null : trades.Where(x => x.GrossPnlUsdt > 0).Sum(x => x.GrossPnlUsdt) / loss;
        decimal cumulative = 0, peak = 0, drawdown = 0; foreach (var trade in trades.OrderBy(x => x.ExitTimeUtc)) { cumulative += trade.NetPnlUsdt; peak = Math.Max(peak, cumulative); drawdown = Math.Max(drawdown, peak - cumulative); } run.MaxDrawdownUsdt = drawdown;
        run.TotalCrossovers=d.TotalCrossovers; run.LongSignals=d.LongSignals; run.ShortSignals=d.ShortSignals; run.RejectedByEma100=d.RejectedByEma100; run.RejectedByEmaGap=d.RejectedByEmaGap; run.RejectedByHtfRegime=d.RejectedByHtfRegime; run.RejectedByStopDistance=d.RejectedByStopDistance; run.RejectedByFees=d.RejectedByFees; run.ConfirmationFailed=d.ConfirmationFailed; run.InvalidStopLoss=d.InvalidStopLoss; run.SkippedWhilePositionOpen=d.SkippedWhilePositionOpen; run.NoEntryCandle=d.NoEntryCandle;
    }
}
