using System.Collections.Concurrent;
using System.Text.Json;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public sealed record StrategyOptimizerGrid(IReadOnlyList<decimal> RiskRewards, IReadOnlyList<decimal> MinEmaGapPercents, IReadOnlyList<decimal> MaxStopDistancePercents, IReadOnlyList<bool> WaitForConfirmationCandles, IReadOnlyList<bool> UseEma100Filters, IReadOnlyList<bool> TrailingStopEnableds);
public sealed record StrategyOptimizerStartRequest(IReadOnlyList<string> Symbols, IReadOnlyList<string> Timeframes, DateTimeOffset StartUtc, DateTimeOffset EndUtc, StrategyOptimizerGrid Grid);
public sealed record StrategyOptimizerOptions(IReadOnlyList<string> EnabledSymbols, IReadOnlyList<string> SupportedTimeframes, TradingSettings Assumptions, StrategyOptimizerGrid DefaultGrid);

public sealed class StrategyOptimizationService(IServiceScopeFactory scopeFactory, BacktestEngine engine, TimeProvider clock, ILogger<StrategyOptimizationService> logger)
{
    public static readonly StrategyOptimizerGrid DefaultGrid = new([.90m, 1m, 1.10m, 1.25m, 1.50m], [0m, .005m, .01m, .02m], [0m, .30m, .50m, .70m], [true, false], [true, false], [true, false]);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> cancellations = new();

    public async Task<StrategyOptimizerOptions> GetOptionsAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var symbols = await db.MonitoredSymbols.Where(symbol => symbol.IsEnabled).OrderBy(symbol => symbol.Symbol).Select(symbol => symbol.Symbol).ToListAsync(token);
        var settings = await db.TradingSettings.AsNoTracking().SingleAsync(settings => settings.Id == 1, token);
        return new(symbols, BinanceIntervals.Supported.OrderBy(value => value).ToArray(), settings, DefaultGrid);
    }

    public async Task<StrategyOptimizationRun> StartAsync(StrategyOptimizerStartRequest request, CancellationToken token)
    {
        var normalized = ValidateAndNormalize(request);
        await gate.WaitAsync(token);
        try
        {
            if (cancellations.Count > 0) throw new InvalidOperationException("An optimization is already running. Cancel it or wait for it to finish.");
            await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            var enabled = await db.MonitoredSymbols.Where(symbol => symbol.IsEnabled).Select(symbol => symbol.Symbol).ToListAsync(token);
            if (normalized.Symbols.Any(symbol => !enabled.Contains(symbol, StringComparer.OrdinalIgnoreCase))) throw new ArgumentException("Choose only currently enabled monitored symbols.");
            var settings = await db.TradingSettings.AsNoTracking().SingleAsync(settings => settings.Id == 1, token);
            var combinations = CandidateSettings(normalized.Grid, settings).ToArray(); var markets = normalized.Symbols.Count * normalized.Timeframes.Count;
            if (combinations.Length > 1000) throw new ArgumentException("The parameter grid produces more than 1,000 candidates. Reduce one or more lists.");
            if (combinations.Length * markets > 20_000) throw new ArgumentException("This request exceeds the 20,000 market-backtest safety limit. Reduce markets or parameter values.");
            var run = new StrategyOptimizationRun { Status = StrategyOptimizationStatus.Queued, CreatedAtUtc = clock.GetUtcNow(), RequestedStartUtc = normalized.StartUtc, RequestedEndUtc = normalized.EndUtc, SymbolsJson = JsonSerializer.Serialize(normalized.Symbols), TimeframesJson = JsonSerializer.Serialize(normalized.Timeframes), GridJson = JsonSerializer.Serialize(normalized.Grid), BaselineSettingsJson = JsonSerializer.Serialize(settings), CandidateCount = combinations.Length, MarketCount = markets, TotalWork = combinations.Length * markets, SimulatedAccountBalanceUsdt = settings.SimulatedAccountBalanceUsdt, FixedOrderSizeUsdt = settings.FixedOrderSizeUsdt, MarginPerTradePercent = settings.MarginPerTradePercent, Leverage = settings.Leverage, FeePercentPerSide = settings.FeePercentPerSide, PositionSizingMode = settings.PositionSizingMode };
            db.StrategyOptimizationRuns.Add(run); await db.SaveChangesAsync(token);
            var cancellation = new CancellationTokenSource(); cancellations[run.Id] = cancellation;
            _ = Task.Run(() => ExecuteAsync(run.Id, cancellation.Token));
            return run;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> CancelAsync(int id, CancellationToken token)
    {
        if (cancellations.TryGetValue(id, out var cancellation)) { cancellation.Cancel(); return true; }
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var run = await db.StrategyOptimizationRuns.FindAsync([id], token);
        if (run is null || run.Status is StrategyOptimizationStatus.Completed or StrategyOptimizationStatus.Cancelled or StrategyOptimizationStatus.Failed) return false;
        run.Status = StrategyOptimizationStatus.Cancelled; run.CompletedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(token); return true;
    }

    public async Task MarkRunningAsInterruptedAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var runs = await db.StrategyOptimizationRuns.Where(run => run.Status == StrategyOptimizationStatus.Running || run.Status == StrategyOptimizationStatus.Queued).ToListAsync(token);
        foreach (var run in runs) { run.Status = StrategyOptimizationStatus.Interrupted; run.CompletedAtUtc = clock.GetUtcNow(); run.FailureMessage = "The application restarted; optimizer runs are not resumed automatically."; }
        if (runs.Count > 0) await db.SaveChangesAsync(token);
    }

    private async Task ExecuteAsync(int id, CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var historical = scope.ServiceProvider.GetRequiredService<IBinanceHistoricalCandleService>();
            var run = await db.StrategyOptimizationRuns.SingleAsync(value => value.Id == id, token); run.Status = StrategyOptimizationStatus.Running; run.StartedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(token);
            var symbols = JsonSerializer.Deserialize<string[]>(run.SymbolsJson) ?? []; var frames = JsonSerializer.Deserialize<string[]>(run.TimeframesJson) ?? []; var grid = JsonSerializer.Deserialize<StrategyOptimizerGrid>(run.GridJson) ?? DefaultGrid;
            var fixedSettings = JsonSerializer.Deserialize<TradingSettings>(run.BaselineSettingsJson) ?? new TradingSettings { Id = 1, RiskReward = 1m };
            var candidates = CandidateSettings(grid, fixedSettings).ToArray(); var cache = new Dictionary<(string Symbol, string Frame), Candle[]>();
            foreach (var symbol in symbols) foreach (var frame in frames)
            {
                token.ThrowIfCancellationRequested(); var warmupStart = run.RequestedStartUtc - WarmupDuration(frame);
                cache[(symbol, frame)] = (await historical.GetRangeAsync(symbol, frame, warmupStart, run.RequestedEndUtc, token)).Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
            }
            foreach (var parameters in candidates)
            {
                token.ThrowIfCancellationRequested(); var candidate = new StrategyOptimizationCandidate { StrategyOptimizationRunId = id, RiskReward = parameters.RiskReward, MinEmaGapPercent = parameters.MinEmaGapPercent, MaxStopDistancePercent = parameters.MaxStopDistancePercent, WaitForConfirmationCandle = parameters.WaitForConfirmationCandle, UseEma100Filter = parameters.UseEma100Filter, TrailingStopEnabled = parameters.TrailingStopEnabled, IsBaseline = IsSame(parameters, fixedSettings) };
                var fullTrades = new List<BacktestTrade>(); var developmentTrades = new List<BacktestTrade>(); var validationTrades = new List<BacktestTrade>(); var fullDiagnostics = new List<BacktestDiagnostics>();
                foreach (var symbol in symbols) foreach (var frame in frames)
                {
                    var calculation = engine.RunResearch(cache[(symbol, frame)], parameters, run.RequestedStartUtc, run.RequestedEndUtc); var split = run.RequestedStartUtc + TimeSpan.FromTicks((run.RequestedEndUtc - run.RequestedStartUtc).Ticks * 7 / 10);
                    var development = calculation.Trades.Where(trade => trade.EntryTimeUtc < split).ToArray(); var validation = calculation.Trades.Where(trade => trade.EntryTimeUtc >= split).ToArray();
                    var market = new StrategyOptimizationMarketResult { Symbol = symbol, Timeframe = frame, Full = Metrics(calculation.Trades, calculation.Diagnostics, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide), Development = Metrics(development, calculation.Diagnostics, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide), Validation = Metrics(validation, calculation.Diagnostics, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide) };
                    candidate.MarketResults.Add(market); fullTrades.AddRange(calculation.Trades); developmentTrades.AddRange(development); validationTrades.AddRange(validation); fullDiagnostics.Add(calculation.Diagnostics); run.CompletedWork++;
                }
                candidate.Full = Metrics(fullTrades, Sum(fullDiagnostics), run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide); candidate.Development = Metrics(developmentTrades, Sum(fullDiagnostics), run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide); candidate.Validation = Metrics(validationTrades, Sum(fullDiagnostics), run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide);
                var activeMarkets = candidate.MarketResults.Where(result => result.Validation.TotalTrades > 0).ToArray(); candidate.ProfitableMarketRatio = activeMarkets.Length == 0 ? 0m : (decimal)activeMarkets.Count(result => result.Validation.NetPnlUsdt > 0m) / activeMarkets.Length;
                candidate.RobustCandidate = candidate.Validation.NetPnlUsdt > 0m && (candidate.Validation.NetProfitFactor ?? 0m) >= 1.05m && candidate.Validation.TotalTrades >= 30 && candidate.ProfitableMarketRatio >= .5m && candidate.Validation.MaxDrawdownPercent <= 5m;
                db.StrategyOptimizationCandidates.Add(candidate); await db.SaveChangesAsync(token);
            }
            var saved = await db.StrategyOptimizationCandidates.Include(candidate => candidate.MarketResults).Where(candidate => candidate.StrategyOptimizationRunId == id).ToListAsync(token);
            var ranked = saved.Where(candidate => candidate.RobustCandidate).OrderByDescending(candidate => candidate.Validation.NetProfitFactor).ThenByDescending(candidate => candidate.Validation.NetReturnPercent).ThenByDescending(candidate => candidate.ProfitableMarketRatio).ThenBy(candidate => candidate.Validation.MaxDrawdownPercent).ThenByDescending(candidate => candidate.Validation.TotalTrades).ThenBy(candidate => candidate.Id).ToArray();
            for (var index = 0; index < ranked.Length; index++) ranked[index].RobustRank = index + 1;
            run.RecommendedCandidateId = ranked.FirstOrDefault()?.Id;
            foreach (var candidate in saved.OrderBy(candidate => candidate.RobustRank ?? int.MaxValue).ThenByDescending(candidate => candidate.Validation.NetProfitFactor).Take(5))
            {
                var settings = Settings(candidate, run); foreach (var symbol in symbols) foreach (var frame in frames) foreach (var trade in engine.RunResearch(cache[(symbol, frame)], settings, run.RequestedStartUtc, run.RequestedEndUtc).Trades) run.Trades.Add(ToTrade(candidate.Id, symbol, frame, trade, run.FeePercentPerSide));
            }
            run.Status = StrategyOptimizationStatus.Completed; run.CompletedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(token);
        }
        catch (OperationCanceledException)
        {
            await SetTerminalAsync(id, StrategyOptimizationStatus.Cancelled, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Strategy optimization run {RunId} failed.", id); await SetTerminalAsync(id, StrategyOptimizationStatus.Failed, exception.Message);
        }
        finally { if (cancellations.TryRemove(id, out var cancellation)) cancellation.Dispose(); }
    }

    private async Task SetTerminalAsync(int id, StrategyOptimizationStatus status, string? failure) { await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var run = await db.StrategyOptimizationRuns.FindAsync(id); if (run is not null) { run.Status = status; run.FailureMessage = failure; run.CompletedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); } }
    private static OptimizationMetrics Metrics(IEnumerable<BacktestTrade> input, BacktestDiagnostics diagnostics, decimal balance, decimal fee)
    {
        var trades = input.OrderBy(trade => trade.ExitTimeUtc).ToArray(); var gains = trades.Where(trade => trade.GrossPnlUsdt > 0).Sum(trade => trade.GrossPnlUsdt); var grossLosses = -trades.Where(trade => trade.GrossPnlUsdt < 0).Sum(trade => trade.GrossPnlUsdt); var netGains = trades.Where(trade => trade.NetPnlUsdt > 0).Sum(trade => trade.NetPnlUsdt); var netLosses = -trades.Where(trade => trade.NetPnlUsdt < 0).Sum(trade => trade.NetPnlUsdt); var net = trades.Sum(trade => trade.NetPnlUsdt); decimal cumulative = 0, peak = 0, drawdown = 0; foreach (var trade in trades) { cumulative += trade.NetPnlUsdt; peak = Math.Max(peak, cumulative); drawdown = Math.Max(drawdown, peak - cumulative); }
        var expected = trades.Select(trade => ExpectedNetTargetR(trade, fee)).OrderBy(value => value).ToArray();
        return new OptimizationMetrics { GrossPnlUsdt = trades.Sum(trade => trade.GrossPnlUsdt), TotalFeesUsdt = trades.Sum(trade => trade.TotalFeesUsdt), NetPnlUsdt = net, NetReturnPercent = balance == 0 ? 0 : net / balance * 100m, GrossProfitFactor = ProfitFactor(gains, grossLosses), NetProfitFactor = ProfitFactor(netGains, netLosses), TotalTrades = trades.Length, WinningTrades = trades.Count(trade => trade.NetPnlUsdt > 0), LosingTrades = trades.Count(trade => trade.NetPnlUsdt < 0), BreakEvenTrades = trades.Count(trade => trade.NetPnlUsdt == 0), LongTrades = trades.Count(trade => trade.Direction == SignalDirection.Long), ShortTrades = trades.Count(trade => trade.Direction == SignalDirection.Short), WinRatePercent = trades.Length == 0 ? 0 : (decimal)trades.Count(trade => trade.NetPnlUsdt > 0) / trades.Length * 100m, MaxDrawdownUsdt = drawdown, MaxDrawdownPercent = balance == 0 ? 0 : drawdown / balance * 100m, AverageNetPnl = trades.Length == 0 ? 0 : net / trades.Length, AverageNetR = trades.Length == 0 ? 0 : trades.Average(trade => trade.NetRMultiple), MedianHoldingMinutes = Median(trades.Select(trade => (decimal)(trade.ExitTimeUtc - trade.EntryTimeUtc).TotalMinutes)), MaximumHoldingMinutes = trades.Length == 0 ? 0 : trades.Max(trade => (decimal)(trade.ExitTimeUtc - trade.EntryTimeUtc).TotalMinutes), LongNetPnl = trades.Where(trade => trade.Direction == SignalDirection.Long).Sum(trade => trade.NetPnlUsdt), ShortNetPnl = trades.Where(trade => trade.Direction == SignalDirection.Short).Sum(trade => trade.NetPnlUsdt), ReentryTrades = trades.Count(trade => trade.IsReentry), ReentryNetPnl = trades.Where(trade => trade.IsReentry).Sum(trade => trade.NetPnlUsdt), MedianExpectedNetTargetR = Median(expected), MinimumExpectedNetTargetR = expected.FirstOrDefault(), AverageExpectedNetTargetR = expected.Length == 0 ? 0 : expected.Average(), TotalCrossovers = diagnostics.TotalCrossovers, LongSignals = diagnostics.LongSignals, ShortSignals = diagnostics.ShortSignals, ConfirmationFailed = diagnostics.ConfirmationFailed, RejectedByEma100 = diagnostics.RejectedByEma100, RejectedByEmaGap = diagnostics.RejectedByEmaGap, RejectedByStopDistance = diagnostics.RejectedByStopDistance, RejectedByFees = diagnostics.RejectedByFees, InvalidStopLoss = diagnostics.InvalidStopLoss, SkippedWhilePositionOpen = diagnostics.SkippedWhilePositionOpen, NoEntryCandle = diagnostics.NoEntryCandle };
    }
    private static decimal ExpectedNetTargetR(BacktestTrade trade, decimal fee) { var risk = decimal.Abs(trade.EntryPrice - trade.InitialStopLoss) * trade.Quantity; return risk == 0 ? 0 : (TradeMath.GrossPnl(trade.EntryPrice, trade.OriginalTakeProfit, trade.Quantity, trade.Direction) - TradeMath.Fee(trade.EntryPrice, trade.Quantity, fee) - TradeMath.Fee(trade.OriginalTakeProfit, trade.Quantity, fee)) / risk; }
    private static decimal? ProfitFactor(decimal gains, decimal losses) => losses == 0m ? gains > 0m ? decimal.MaxValue : null : gains / losses;
    private static decimal Median(IEnumerable<decimal> source) { var values = source.OrderBy(value => value).ToArray(); return values.Length == 0 ? 0m : values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2m; }
    private static BacktestDiagnostics Sum(IEnumerable<BacktestDiagnostics> values) => values.Aggregate(new BacktestDiagnostics(0,0,0,0,0,0,0,0,0,0,0), (a,b) => new(a.TotalCrossovers+b.TotalCrossovers,a.LongSignals+b.LongSignals,a.ShortSignals+b.ShortSignals,a.RejectedByEma100+b.RejectedByEma100,a.RejectedByEmaGap+b.RejectedByEmaGap,a.RejectedByStopDistance+b.RejectedByStopDistance,a.RejectedByFees+b.RejectedByFees,a.ConfirmationFailed+b.ConfirmationFailed,a.InvalidStopLoss+b.InvalidStopLoss,a.SkippedWhilePositionOpen+b.SkippedWhilePositionOpen,a.NoEntryCandle+b.NoEntryCandle));
    private static StrategyOptimizationTrade ToTrade(int candidateId, string symbol, string timeframe, BacktestTrade trade, decimal fee) => new() { StrategyOptimizationCandidateId = candidateId, Symbol=symbol, Timeframe=timeframe, Direction=trade.Direction, IsReentry=trade.IsReentry, EntryTimeUtc=trade.EntryTimeUtc, ExitTimeUtc=trade.ExitTimeUtc, EntryPrice=trade.EntryPrice, ExitPrice=trade.ExitPrice, InitialStopLoss=trade.InitialStopLoss, FinalStopLoss=trade.FinalStopLoss, OriginalTakeProfit=trade.OriginalTakeProfit, FinalTakeProfit=trade.FinalTakeProfit, GrossPnlUsdt=trade.GrossPnlUsdt, TotalFeesUsdt=trade.TotalFeesUsdt, NetPnlUsdt=trade.NetPnlUsdt, NetRMultiple=trade.NetRMultiple, ExitReason=trade.ExitReason, SignalEma9=trade.SignalEma9, SignalEma15=trade.SignalEma15, SignalEma100=trade.SignalEma100, SignalGapPercent=trade.SignalGapPercent, ExpectedNetTargetR=ExpectedNetTargetR(trade, fee) };
    private static TradingSettings Settings(StrategyOptimizationCandidate candidate, StrategyOptimizationRun run) => new() { Id=1,RiskReward=candidate.RiskReward,MinEmaGapPercent=candidate.MinEmaGapPercent,MaxStopDistancePercent=candidate.MaxStopDistancePercent,WaitForConfirmationCandle=candidate.WaitForConfirmationCandle,UseEma100Filter=candidate.UseEma100Filter,TrailingStopEnabled=candidate.TrailingStopEnabled,SimulatedAccountBalanceUsdt=run.SimulatedAccountBalanceUsdt,MarginPerTradePercent=run.MarginPerTradePercent,Leverage=run.Leverage,FeePercentPerSide=run.FeePercentPerSide,PositionSizingMode=run.PositionSizingMode,FixedOrderSizeUsdt=run.FixedOrderSizeUsdt };
    private static bool IsSame(TradingSettings left, TradingSettings right) => left.RiskReward==right.RiskReward && left.MinEmaGapPercent==right.MinEmaGapPercent && left.MaxStopDistancePercent==right.MaxStopDistancePercent && left.WaitForConfirmationCandle==right.WaitForConfirmationCandle && left.UseEma100Filter==right.UseEma100Filter && left.TrailingStopEnabled==right.TrailingStopEnabled;
    private static IEnumerable<TradingSettings> CandidateSettings(StrategyOptimizerGrid grid, TradingSettings fixedSettings) { var all = new Dictionary<string,TradingSettings>(); foreach(var rr in grid.RiskRewards) foreach(var gap in grid.MinEmaGapPercents) foreach(var max in grid.MaxStopDistancePercents) foreach(var confirm in grid.WaitForConfirmationCandles) foreach(var ema in grid.UseEma100Filters) foreach(var trailing in grid.TrailingStopEnableds) { var value = Clone(fixedSettings,rr,gap,max,confirm,ema,trailing); all[Key(value)] = value; } all[Key(fixedSettings)] = Clone(fixedSettings,fixedSettings.RiskReward,fixedSettings.MinEmaGapPercent,fixedSettings.MaxStopDistancePercent,fixedSettings.WaitForConfirmationCandle,fixedSettings.UseEma100Filter,fixedSettings.TrailingStopEnabled); return all.Values; }
    private static TradingSettings Clone(TradingSettings fixedSettings, decimal rr, decimal gap, decimal max, bool confirm, bool ema, bool trailing) => new() { Id=1,RiskReward=rr,MinEmaGapPercent=gap,MaxStopDistancePercent=max,WaitForConfirmationCandle=confirm,UseEma100Filter=ema,TrailingStopEnabled=trailing,FixedOrderSizeUsdt=fixedSettings.FixedOrderSizeUsdt,SimulatedAccountBalanceUsdt=fixedSettings.SimulatedAccountBalanceUsdt,MarginPerTradePercent=fixedSettings.MarginPerTradePercent,Leverage=fixedSettings.Leverage,FeePercentPerSide=fixedSettings.FeePercentPerSide,PositionSizingMode=fixedSettings.PositionSizingMode };
    private static string Key(TradingSettings settings) => $"{settings.RiskReward}|{settings.MinEmaGapPercent}|{settings.MaxStopDistancePercent}|{settings.WaitForConfirmationCandle}|{settings.UseEma100Filter}|{settings.TrailingStopEnabled}";
    private static StrategyOptimizerStartRequest ValidateAndNormalize(StrategyOptimizerStartRequest request) { var symbols=request.Symbols.Select(symbol=>symbol.Trim().ToUpperInvariant()).Where(symbol=>symbol.Length>0).Distinct().ToArray(); var frames=request.Timeframes.Distinct().ToArray(); if(symbols.Length==0||frames.Length==0||frames.Any(frame=>!BinanceIntervals.IsSupported(frame))) throw new ArgumentException("Choose at least one enabled market and supported timeframe."); if(request.StartUtc>=request.EndUtc || request.EndUtc-request.StartUtc<TimeSpan.FromDays(30) || request.EndUtc-request.StartUtc>TimeSpan.FromDays(90)) throw new ArgumentException("Choose a UTC historical period from 30 through 90 days."); var grid=new StrategyOptimizerGrid(request.Grid.RiskRewards.Distinct().Order().ToArray(),request.Grid.MinEmaGapPercents.Distinct().Order().ToArray(),request.Grid.MaxStopDistancePercents.Distinct().Order().ToArray(),request.Grid.WaitForConfirmationCandles.Distinct().ToArray(),request.Grid.UseEma100Filters.Distinct().ToArray(),request.Grid.TrailingStopEnableds.Distinct().ToArray()); if(grid.RiskRewards.Any(value=>value<=0)||grid.MinEmaGapPercents.Any(value=>value<0)||grid.MaxStopDistancePercents.Any(value=>value<0)||grid.RiskRewards.Count==0||grid.MinEmaGapPercents.Count==0||grid.MaxStopDistancePercents.Count==0||grid.WaitForConfirmationCandles.Count==0||grid.UseEma100Filters.Count==0||grid.TrailingStopEnableds.Count==0) throw new ArgumentException("Parameter lists must contain unique valid values."); return new(symbols,frames,request.StartUtc,request.EndUtc,grid); }
    private static TimeSpan WarmupDuration(string interval) => interval switch { "3m"=>TimeSpan.FromMinutes(600),"5m"=>TimeSpan.FromMinutes(1000),"15m"=>TimeSpan.FromMinutes(3000),"30m"=>TimeSpan.FromMinutes(6000),"1h"=>TimeSpan.FromHours(200),"2h"=>TimeSpan.FromHours(400),"4h"=>TimeSpan.FromHours(800),"6h"=>TimeSpan.FromHours(1200),"8h"=>TimeSpan.FromHours(1600),"12h"=>TimeSpan.FromHours(2400),"1d"=>TimeSpan.FromDays(200),"3d"=>TimeSpan.FromDays(600),"1w"=>TimeSpan.FromDays(1400),_=>TimeSpan.FromDays(6200) };
}
