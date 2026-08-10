using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public sealed class BacktestService(EmaBotDbContext database, IBinanceHistoricalCandleService historical, TradingSettingsService settingsService, BacktestEngine engine)
{
    public async Task<BacktestRun> RunAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        var retrieved = await historical.GetRangeAsync(symbol, interval, start, end, token);
        var candles = retrieved.Where(candle => candle.OpenTimeUtc >= start && candle.CloseTimeUtc <= end && candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
        var settings = await settingsService.GetAsync(token); var calculation = engine.Run(candles, settings); var trades = calculation.Trades;
        var run = new BacktestRun { Symbol = symbol, Interval = interval, RequestedStartUtc = start, RequestedEndUtc = end, ActualStartUtc = candles.FirstOrDefault()?.OpenTimeUtc, ActualEndUtc = candles.LastOrDefault()?.CloseTimeUtc, CreatedAtUtc = DateTimeOffset.UtcNow, CompletedAtUtc = DateTimeOffset.UtcNow, CandleCount = candles.Length, RiskReward = settings.RiskReward, FixedOrderSizeUsdt = settings.FixedOrderSizeUsdt, WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, TrailingStopEnabled = settings.TrailingStopEnabled, FeePercentPerSide = settings.FeePercentPerSide, Status = BacktestRunStatus.Completed, Trades = trades.ToList() };
        PopulateSummary(run, calculation.Diagnostics); database.BacktestRuns.Add(run); await database.SaveChangesAsync(token); return run;
    }
    public Task<List<BacktestRun>> ListAsync(CancellationToken token) => database.BacktestRuns.AsNoTracking().OrderByDescending(run => run.CreatedAtUtc).Take(30).ToListAsync(token);
    public Task<BacktestRun?> GetAsync(int id, CancellationToken token) => database.BacktestRuns.AsNoTracking().Include(run => run.Trades).SingleOrDefaultAsync(run => run.Id == id, token);
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
    private static void PopulateSummary(BacktestRun run, BacktestDiagnostics d) { var t = run.Trades; run.TotalTrades = t.Count; run.WinningTrades = t.Count(x => x.NetPnlUsdt > 0); run.LosingTrades = t.Count(x => x.NetPnlUsdt < 0); run.BreakEvenTrades = t.Count(x => x.NetPnlUsdt == 0); run.LongTrades = t.Count(x => x.Direction == Strategy.SignalDirection.Long); run.ShortTrades = t.Count - run.LongTrades; run.WinRatePercent = t.Count == 0 ? 0 : (decimal)run.WinningTrades / t.Count * 100; run.GrossPnlUsdt = t.Sum(x => x.GrossPnlUsdt); run.NetPnlUsdt = t.Sum(x => x.NetPnlUsdt); run.TotalFeesUsdt = t.Sum(x => x.TotalFeesUsdt); run.AverageNetPnlUsdt = t.Count == 0 ? 0 : run.NetPnlUsdt / t.Count; run.AverageRMultiple = t.Count == 0 ? 0 : t.Average(x => x.GrossRMultiple); var loss = t.Where(x => x.GrossPnlUsdt < 0).Sum(x => -x.GrossPnlUsdt); run.ProfitFactor = loss == 0 ? null : t.Where(x => x.GrossPnlUsdt > 0).Sum(x => x.GrossPnlUsdt) / loss; decimal cumulative = 0, peak = 0, drawdown = 0; foreach (var trade in t.OrderBy(x => x.ExitTimeUtc)) { cumulative += trade.NetPnlUsdt; peak = Math.Max(peak, cumulative); drawdown = Math.Max(drawdown, peak - cumulative); } run.MaxDrawdownUsdt = drawdown; run.TotalCrossovers=d.TotalCrossovers; run.LongSignals=d.LongSignals; run.ShortSignals=d.ShortSignals; run.RejectedByEma100=d.RejectedByEma100; run.ConfirmationFailed=d.ConfirmationFailed; run.InvalidStopLoss=d.InvalidStopLoss; run.SkippedWhilePositionOpen=d.SkippedWhilePositionOpen; run.NoEntryCandle=d.NoEntryCandle; }
}
