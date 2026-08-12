using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Diagnostics;
using System.Text.Json;
using EmaBot.Api.Binance;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public sealed record StrategyOptimizerGrid(IReadOnlyList<decimal> RiskRewards, IReadOnlyList<decimal> MinEmaGapPercents, IReadOnlyList<decimal> MaxStopDistancePercents, IReadOnlyList<bool> WaitForConfirmationCandles, IReadOnlyList<bool> UseEma100Filters, IReadOnlyList<bool> TrailingStopEnableds);
public readonly record struct StrategyOptimizerCandidateKey(decimal RiskReward, decimal MinEmaGapPercent, decimal MaxStopDistancePercent, bool WaitForConfirmationCandle, bool UseEma100Filter, bool UseHtfRegimeFilter, bool TrailingStopEnabled);
public sealed record StrategyOptimizerStartRequest(IReadOnlyList<string> Symbols, IReadOnlyList<string> Timeframes, DateTimeOffset StartUtc, DateTimeOffset EndUtc, StrategyOptimizerGrid Grid, int? WorkerCount = null);
public sealed record StrategyOptimizerOptions(IReadOnlyList<string> EnabledSymbols, IReadOnlyList<string> SupportedTimeframes, TradingSettings Assumptions, StrategyOptimizerGrid DefaultGrid, int DetectedProcessors, int EffectiveWorkers);
public sealed record StrategyOptimizerRuntimeStatus(string Phase, int? FinalizationTotalMarkets, int? FinalizationCompletedMarkets);

public sealed class StrategyOptimizationService(IServiceScopeFactory scopeFactory, BacktestEngine engine, TimeProvider clock, ILogger<StrategyOptimizationService> logger)
{
    public static readonly StrategyOptimizerGrid DefaultGrid = new([.90m, 1m, 1.10m, 1.25m, 1.50m], [0m, .005m, .01m, .02m], [0m, .30m, .50m, .70m], [true, false], [true, false], [true, false]);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> cancellations = new();
    private readonly ConcurrentDictionary<int, int> workerCounts = new();
    private readonly ConcurrentDictionary<int, StrategyOptimizerRuntimeStatus> runtimeStatuses = new();
    internal Func<int, string, CancellationToken, Task>? ExecutionPhaseEnteredAsync { get; init; }
    internal Func<int, int, string, string, CancellationToken, Task>? FinalizationMarketCompletedAsync { get; init; }
    // Reporting-only tolerance: fee-aware exits can leave decimal dust around zero.
    private const decimal BreakEvenToleranceUsdt = 0.000000000001m;
    public static int EffectiveWorkerCount(int? processors = null, int? requestedWorkers = null) { if (requestedWorkers is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(requestedWorkers), "CPU workers must be from 1 through 16."); return requestedWorkers ?? Math.Clamp(((processors ?? Environment.ProcessorCount) * 2) / 3, 1, 16); }

    public async Task<StrategyOptimizerOptions> GetOptionsAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var symbols = await db.MonitoredSymbols.Where(symbol => symbol.IsEnabled).OrderBy(symbol => symbol.Symbol).Select(symbol => symbol.Symbol).ToListAsync(token);
        var settings = await db.TradingSettings.AsNoTracking().SingleAsync(settings => settings.Id == 1, token);
        return new(symbols, BinanceIntervals.Supported.OrderBy(value => value).ToArray(), settings, DefaultGrid, Environment.ProcessorCount, EffectiveWorkerCount());
    }

    public async Task<StrategyOptimizationRun> StartAsync(StrategyOptimizerStartRequest request, CancellationToken token)
    {
        var normalized = ValidateAndNormalize(request, clock); var requestedWorkers = EffectiveWorkerCount(requestedWorkers: request.WorkerCount);
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
            workerCounts[run.Id] = requestedWorkers;
            runtimeStatuses[run.Id] = new("Queued", null, null);
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

    public StrategyOptimizerRuntimeStatus RuntimeStatusFor(int id, StrategyOptimizationStatus persistedStatus)
        => runtimeStatuses.TryGetValue(id, out var status) ? status : new(persistedStatus.ToString(), null, null);

    public async Task MarkRunningAsInterruptedAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
        var runs = await db.StrategyOptimizationRuns.Where(run => run.Status == StrategyOptimizationStatus.Running || run.Status == StrategyOptimizationStatus.Queued).ToListAsync(token);
        foreach (var run in runs) { run.Status = StrategyOptimizationStatus.Interrupted; run.CompletedAtUtc = clock.GetUtcNow(); run.FailureMessage = "The application restarted; optimizer runs are not resumed automatically."; runtimeStatuses.TryRemove(run.Id, out _); workerCounts.TryRemove(run.Id, out _); if (cancellations.TryRemove(run.Id, out var cancellation)) cancellation.Dispose(); }
        if (runs.Count > 0) await db.SaveChangesAsync(token);
    }

    private async Task ExecuteAsync(int id, CancellationToken token)
    {
        try
        {
            if (ExecutionPhaseEnteredAsync is not null) await ExecutionPhaseEnteredAsync(id, "Queued", token); await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var historical = scope.ServiceProvider.GetRequiredService<IBinanceHistoricalCandleService>();
            var run = await db.StrategyOptimizationRuns.SingleAsync(value => value.Id == id, token); run.Status = StrategyOptimizationStatus.Running; run.StartedAtUtc = clock.GetUtcNow(); runtimeStatuses[id] = new("LoadingHistoricalData", null, null); if (ExecutionPhaseEnteredAsync is not null) await ExecutionPhaseEnteredAsync(id, "LoadingHistoricalData", token); await db.SaveChangesAsync(token);
            var symbols = JsonSerializer.Deserialize<string[]>(run.SymbolsJson) ?? []; var frames = JsonSerializer.Deserialize<string[]>(run.TimeframesJson) ?? []; var grid = JsonSerializer.Deserialize<StrategyOptimizerGrid>(run.GridJson) ?? DefaultGrid;
            var fixedSettings = JsonSerializer.Deserialize<TradingSettings>(run.BaselineSettingsJson) ?? new TradingSettings { Id = 1, RiskReward = 1m };
            var candidates = CandidateSettings(grid, fixedSettings).ToArray(); var cache = new Dictionary<(string Symbol, string Frame), Candle[]>();
            foreach (var symbol in symbols) foreach (var frame in frames)
            {
                token.ThrowIfCancellationRequested(); var warmupStart = run.RequestedStartUtc - WarmupDuration(frame);
                cache[(symbol, frame)] = (await historical.GetRangeAsync(symbol, frame, warmupStart, run.RequestedEndUtc, token)).Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
            }
            if (fixedSettings.UseHtfRegimeFilter) foreach (var symbol in symbols) foreach (var frame in frames)
            {
                var htf = HigherTimeframeRegime.ForExecutionTimeframe(frame); if (htf is null || cache.ContainsKey((symbol, htf))) continue;
                cache[(symbol, htf)] = (await historical.GetRangeAsync(symbol, htf, run.RequestedStartUtc - HigherTimeframeRegime.WarmupDuration(htf), run.RequestedEndUtc, token)).Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToArray();
            }
            var workers = workerCounts.TryGetValue(id, out var configuredWorkers) ? configuredWorkers : EffectiveWorkerCount(); var parallelTimer = Stopwatch.StartNew(); runtimeStatuses[id] = new("EvaluatingCandidates", null, null); if (ExecutionPhaseEnteredAsync is not null) await ExecutionPhaseEnteredAsync(id, "EvaluatingCandidates", token); var channel = Channel.CreateBounded<StrategyOptimizationCandidate>(workers * 2);
            logger.LogInformation("Optimizer {RunId}: {Workers} workers for {Candidates} candidates, {Markets} markets, {Work} logical / {Segments} segment executions.", id, workers, candidates.Length, run.MarketCount, run.TotalWork, run.TotalWork * 3);
            var producer = Task.Run(async () => { Exception? error = null; try { await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = token }, async (parameters, cancellation) => { var candidate = ComputeCandidate(id, parameters, fixedSettings, symbols, frames, cache, run, cancellation); await channel.Writer.WriteAsync(candidate, cancellation); }); } catch (Exception exception) { error = exception; } finally { channel.Writer.TryComplete(error); } });
            await foreach (var candidate in channel.Reader.ReadAllAsync()) { db.StrategyOptimizationCandidates.Add(candidate); run.CompletedWork = Math.Min(run.TotalWork, run.CompletedWork + run.MarketCount); await db.SaveChangesAsync(CancellationToken.None); }
            await producer; token.ThrowIfCancellationRequested(); parallelTimer.Stop(); logger.LogInformation("Optimizer {RunId}: candidate computation and streaming persistence completed in {Elapsed}.", id, parallelTimer.Elapsed);
            var saved = await db.StrategyOptimizationCandidates.Include(candidate => candidate.MarketResults).Where(candidate => candidate.StrategyOptimizationRunId == id).ToListAsync(token);
            var ranked = saved.Where(candidate => candidate.RobustCandidate).OrderByDescending(candidate => candidate.Validation.NetProfitFactor).ThenByDescending(candidate => candidate.Validation.NetReturnPercent).ThenByDescending(candidate => candidate.ProfitableMarketRatio).ThenBy(candidate => candidate.Validation.MaxDrawdownPercent).ThenByDescending(candidate => candidate.Validation.TotalTrades).ThenBy(candidate => candidate.RiskReward).ThenBy(candidate => candidate.MinEmaGapPercent).ThenBy(candidate => candidate.MaxStopDistancePercent).ThenBy(candidate => candidate.WaitForConfirmationCandle).ThenBy(candidate => candidate.UseEma100Filter).ThenBy(candidate => candidate.TrailingStopEnabled).ToArray();
            var rankingTimer = Stopwatch.StartNew(); for (var index = 0; index < ranked.Length; index++) ranked[index].RobustRank = index + 1;
            run.RecommendedCandidateId = ranked.FirstOrDefault()?.Id;
            rankingTimer.Stop(); var topCandidates = saved.OrderBy(candidate => candidate.RobustRank ?? int.MaxValue).ThenByDescending(candidate => candidate.Validation.NetProfitFactor).Take(5).ToArray(); var finalizationTimer = Stopwatch.StartNew(); var finalizationTotal = topCandidates.Length * run.MarketCount;
            runtimeStatuses[id] = new("FinalizingTopCandidates", finalizationTotal, 0); if (ExecutionPhaseEnteredAsync is not null) await ExecutionPhaseEnteredAsync(id, "FinalizingTopCandidates", token); var completedFinalizationMarkets = 0; var finalizationTrades = new List<StrategyOptimizationTrade>();
            foreach (var candidate in topCandidates)
            {
                token.ThrowIfCancellationRequested(); var settings = Settings(candidate, run); foreach (var symbol in symbols) foreach (var frame in frames)
                {
                    token.ThrowIfCancellationRequested(); var htf = HigherTimeframeRegime.ForExecutionTimeframe(frame); var context = settings.UseHtfRegimeFilter ? new StrategyMarketContext(cache[(symbol, frame)], htf, htf is not null && cache.TryGetValue((symbol, htf), out var htfCandles) ? htfCandles : null) : null; var calculation = engine.RunResearch(cache[(symbol, frame)], settings, run.RequestedStartUtc, run.RequestedEndUtc, context); token.ThrowIfCancellationRequested();
                    finalizationTrades.AddRange(calculation.Trades.Select(trade => ToTrade(id, candidate.Id, symbol, frame, trade, run.FeePercentPerSide)));
                    if (FinalizationMarketCompletedAsync is not null) await FinalizationMarketCompletedAsync(id, candidate.Id, symbol, frame, token);
                    completedFinalizationMarkets++; runtimeStatuses[id] = new("FinalizingTopCandidates", finalizationTotal, completedFinalizationMarkets);
                }
            }
            token.ThrowIfCancellationRequested(); db.StrategyOptimizationTrades.AddRange(finalizationTrades.OrderBy(trade => trade.StrategyOptimizationCandidateId).ThenBy(trade => trade.Symbol).ThenBy(trade => trade.Timeframe).ThenBy(trade => trade.EntryTimeUtc).ThenBy(trade => trade.ExitTimeUtc)); run.Status = StrategyOptimizationStatus.Completed; run.CompletedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(token); finalizationTimer.Stop(); logger.LogInformation("Optimizer {RunId} completed: candidate evaluation {CandidateElapsed}, ranking {RankingElapsed}, finalization {FinalizationElapsed}, {FinalizationMarkets} finalization markets, total {TotalElapsed}.", id, parallelTimer.Elapsed, rankingTimer.Elapsed, finalizationTimer.Elapsed, finalizationTotal, parallelTimer.Elapsed + rankingTimer.Elapsed + finalizationTimer.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await SetTerminalAsync(id, StrategyOptimizationStatus.Cancelled, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Strategy optimization run {RunId} failed during {Phase}.", id, runtimeStatuses.TryGetValue(id, out var state) ? state.Phase : "unknown"); await SetTerminalAsync(id, StrategyOptimizationStatus.Failed, "Optimizer finalization or evaluation failed. Check the API log for details.");
        }
        finally { runtimeStatuses.TryRemove(id, out _); workerCounts.TryRemove(id, out _); if (cancellations.TryRemove(id, out var cancellation)) cancellation.Dispose(); }
    }

    private static StrategyOptimizationCandidate ComputeCandidate(int id, TradingSettings parameters, TradingSettings baseline, IReadOnlyList<string> symbols, IReadOnlyList<string> frames, IReadOnlyDictionary<(string Symbol, string Frame), Candle[]> cache, StrategyOptimizationRun run, CancellationToken cancellation)
    {
        var workerEngine = new BacktestEngine(new EmaSignalEngine()); var candidate = new StrategyOptimizationCandidate { StrategyOptimizationRunId = id, RiskReward = parameters.RiskReward, MinEmaGapPercent = parameters.MinEmaGapPercent, MaxStopDistancePercent = parameters.MaxStopDistancePercent, WaitForConfirmationCandle = parameters.WaitForConfirmationCandle, UseEma100Filter = parameters.UseEma100Filter, UseHtfRegimeFilter = parameters.UseHtfRegimeFilter, TrailingStopEnabled = parameters.TrailingStopEnabled, IsBaseline = IsSame(parameters, baseline) };
        var full = new List<(BacktestCalculation, OptimizationMetrics)>(); var development = new List<(BacktestCalculation, OptimizationMetrics)>(); var validation = new List<(BacktestCalculation, OptimizationMetrics)>(); var split = run.RequestedStartUtc + TimeSpan.FromTicks((run.RequestedEndUtc - run.RequestedStartUtc).Ticks * 7 / 10);
        foreach (var symbol in symbols) foreach (var frame in frames) { cancellation.ThrowIfCancellationRequested(); var candles = cache[(symbol, frame)]; var htf = HigherTimeframeRegime.ForExecutionTimeframe(frame); var context = parameters.UseHtfRegimeFilter ? new StrategyMarketContext(candles, htf, htf is not null && cache.TryGetValue((symbol, htf), out var htfCandles) ? htfCandles : null) : null; var all = workerEngine.RunResearch(candles, parameters, run.RequestedStartUtc, run.RequestedEndUtc, context); cancellation.ThrowIfCancellationRequested(); var dev = workerEngine.RunResearch(candles, parameters, run.RequestedStartUtc, split, context); cancellation.ThrowIfCancellationRequested(); var val = workerEngine.RunResearch(candles, parameters, split, run.RequestedEndUtc, context); cancellation.ThrowIfCancellationRequested(); var market = new StrategyOptimizationMarketResult { Symbol = symbol, Timeframe = frame, Full = Metrics(all.Trades, all.Diagnostics, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide), Development = Metrics(dev.Trades, dev.Diagnostics, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide), Validation = Metrics(val.Trades, val.Diagnostics, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide) }; candidate.MarketResults.Add(market); full.Add((all, market.Full)); development.Add((dev, market.Development)); validation.Add((val, market.Validation)); }
        cancellation.ThrowIfCancellationRequested(); candidate.Full = Aggregate(full, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide); candidate.Development = Aggregate(development, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide); candidate.Validation = Aggregate(validation, run.SimulatedAccountBalanceUsdt, run.FeePercentPerSide); var active = candidate.MarketResults.Where(result => result.Validation.TotalTrades > 0).ToArray(); candidate.ProfitableMarketRatio = active.Length == 0 ? 0m : (decimal)active.Count(result => result.Validation.NetPnlUsdt > 0m) / active.Length; candidate.RobustCandidate = candidate.Validation.NetPnlUsdt > 0m && (candidate.Validation.NetProfitFactor ?? 0m) >= 1.05m && candidate.Validation.TotalTrades >= 30 && candidate.ProfitableMarketRatio >= .5m && candidate.Validation.MaxDrawdownPercent <= 5m; return candidate;
    }
    private async Task SetTerminalAsync(int id, StrategyOptimizationStatus status, string? failure) { await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>(); var run = await db.StrategyOptimizationRuns.FindAsync(id); if (run is not null) { run.Status = status; run.FailureMessage = failure; run.CompletedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); } }
    private static OptimizationMetrics Aggregate(IReadOnlyCollection<(BacktestCalculation Calculation, OptimizationMetrics Metrics)> markets, decimal balance, decimal fee)
    {
        var allTrades = markets.SelectMany(value => value.Calculation.Trades).ToArray();
        var metrics = Metrics(allTrades, Sum(markets.Select(value => value.Calculation.Diagnostics)), balance, fee);
        var active = markets.Count(value => value.Metrics.TotalTrades > 0);
        metrics.NetReturnPercent = active == 0 || balance == 0 ? 0m : metrics.NetPnlUsdt / (balance * active) * 100m;
        var worst = markets.Select(value => value.Metrics).OrderByDescending(value => value.MaxDrawdownPercent).FirstOrDefault();
        if (worst is not null) { metrics.MaxDrawdownPercent = worst.MaxDrawdownPercent; metrics.MaxDrawdownUsdt = worst.MaxDrawdownUsdt; }
        return metrics;
    }
    private static OptimizationMetrics Metrics(IEnumerable<BacktestTrade> input, BacktestDiagnostics diagnostics, decimal balance, decimal fee)
    {
        var trades = input.OrderBy(trade => trade.ExitTimeUtc).ToArray(); var gains = trades.Where(trade => trade.GrossPnlUsdt > 0).Sum(trade => trade.GrossPnlUsdt); var grossLosses = -trades.Where(trade => trade.GrossPnlUsdt < 0).Sum(trade => trade.GrossPnlUsdt); var netGains = trades.Where(trade => trade.NetPnlUsdt > BreakEvenToleranceUsdt).Sum(trade => trade.NetPnlUsdt); var netLosses = -trades.Where(trade => trade.NetPnlUsdt < -BreakEvenToleranceUsdt).Sum(trade => trade.NetPnlUsdt); var net = trades.Sum(trade => trade.NetPnlUsdt); decimal cumulative = 0, peak = 0, drawdown = 0; foreach (var trade in trades) { cumulative += trade.NetPnlUsdt; peak = Math.Max(peak, cumulative); drawdown = Math.Max(drawdown, peak - cumulative); }
        var expected = trades.Select(trade => ExpectedNetTargetR(trade, fee)).OrderBy(value => value).ToArray();
        return new OptimizationMetrics { GrossPnlUsdt = trades.Sum(trade => trade.GrossPnlUsdt), TotalFeesUsdt = trades.Sum(trade => trade.TotalFeesUsdt), NetPnlUsdt = net, NetReturnPercent = balance == 0 ? 0 : net / balance * 100m, GrossProfitFactor = ProfitFactor(gains, grossLosses), NetProfitFactor = ProfitFactor(netGains, netLosses), TotalTrades = trades.Length, WinningTrades = trades.Count(trade => trade.NetPnlUsdt > BreakEvenToleranceUsdt), LosingTrades = trades.Count(trade => trade.NetPnlUsdt < -BreakEvenToleranceUsdt), BreakEvenTrades = trades.Count(trade => decimal.Abs(trade.NetPnlUsdt) <= BreakEvenToleranceUsdt), LongTrades = trades.Count(trade => trade.Direction == SignalDirection.Long), ShortTrades = trades.Count(trade => trade.Direction == SignalDirection.Short), WinRatePercent = trades.Length == 0 ? 0 : (decimal)trades.Count(trade => trade.NetPnlUsdt > BreakEvenToleranceUsdt) / trades.Length * 100m, MaxDrawdownUsdt = drawdown, MaxDrawdownPercent = balance == 0 ? 0 : drawdown / balance * 100m, AverageNetPnl = trades.Length == 0 ? 0 : net / trades.Length, AverageNetR = trades.Length == 0 ? 0 : trades.Average(trade => trade.NetRMultiple), MedianHoldingMinutes = Median(trades.Select(trade => (decimal)(trade.ExitTimeUtc - trade.EntryTimeUtc).TotalMinutes)), MaximumHoldingMinutes = trades.Length == 0 ? 0 : trades.Max(trade => (decimal)(trade.ExitTimeUtc - trade.EntryTimeUtc).TotalMinutes), LongNetPnl = trades.Where(trade => trade.Direction == SignalDirection.Long).Sum(trade => trade.NetPnlUsdt), ShortNetPnl = trades.Where(trade => trade.Direction == SignalDirection.Short).Sum(trade => trade.NetPnlUsdt), ReentryTrades = trades.Count(trade => trade.IsReentry), ReentryNetPnl = trades.Where(trade => trade.IsReentry).Sum(trade => trade.NetPnlUsdt), MedianExpectedNetTargetR = Median(expected), MinimumExpectedNetTargetR = expected.FirstOrDefault(), AverageExpectedNetTargetR = expected.Length == 0 ? 0 : expected.Average(), TotalCrossovers = diagnostics.TotalCrossovers, LongSignals = diagnostics.LongSignals, ShortSignals = diagnostics.ShortSignals, ConfirmationFailed = diagnostics.ConfirmationFailed, RejectedByEma100 = diagnostics.RejectedByEma100, RejectedByEmaGap = diagnostics.RejectedByEmaGap, RejectedByHtfRegime = diagnostics.RejectedByHtfRegime, RejectedByStopDistance = diagnostics.RejectedByStopDistance, RejectedByFees = diagnostics.RejectedByFees, InvalidStopLoss = diagnostics.InvalidStopLoss, SkippedWhilePositionOpen = diagnostics.SkippedWhilePositionOpen, NoEntryCandle = diagnostics.NoEntryCandle };
    }
    private static decimal ExpectedNetTargetR(BacktestTrade trade, decimal fee) { var risk = decimal.Abs(trade.EntryPrice - trade.InitialStopLoss) * trade.Quantity; return risk == 0 ? 0 : (TradeMath.GrossPnl(trade.EntryPrice, trade.OriginalTakeProfit, trade.Quantity, trade.Direction) - TradeMath.Fee(trade.EntryPrice, trade.Quantity, fee) - TradeMath.Fee(trade.OriginalTakeProfit, trade.Quantity, fee)) / risk; }
    private static decimal? ProfitFactor(decimal gains, decimal losses) => losses == 0m ? gains > 0m ? decimal.MaxValue : null : gains / losses;
    private static decimal Median(IEnumerable<decimal> source) { var values = source.OrderBy(value => value).ToArray(); return values.Length == 0 ? 0m : values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2m; }
    private static BacktestDiagnostics Sum(IEnumerable<BacktestDiagnostics> values) => values.Aggregate(new BacktestDiagnostics(0,0,0,0,0,0,0,0,0,0,0), (a,b) => new(a.TotalCrossovers+b.TotalCrossovers,a.LongSignals+b.LongSignals,a.ShortSignals+b.ShortSignals,a.RejectedByEma100+b.RejectedByEma100,a.RejectedByEmaGap+b.RejectedByEmaGap,a.RejectedByStopDistance+b.RejectedByStopDistance,a.RejectedByFees+b.RejectedByFees,a.ConfirmationFailed+b.ConfirmationFailed,a.InvalidStopLoss+b.InvalidStopLoss,a.SkippedWhilePositionOpen+b.SkippedWhilePositionOpen,a.NoEntryCandle+b.NoEntryCandle,a.RejectedByHtfRegime+b.RejectedByHtfRegime));
    private static StrategyOptimizationTrade ToTrade(int runId, int candidateId, string symbol, string timeframe, BacktestTrade trade, decimal fee) => new() { StrategyOptimizationRunId = runId, StrategyOptimizationCandidateId = candidateId, Symbol=symbol, Timeframe=timeframe, Direction=trade.Direction, IsReentry=trade.IsReentry, EntryTimeUtc=trade.EntryTimeUtc, ExitTimeUtc=trade.ExitTimeUtc, EntryPrice=trade.EntryPrice, ExitPrice=trade.ExitPrice, InitialStopLoss=trade.InitialStopLoss, FinalStopLoss=trade.FinalStopLoss, OriginalTakeProfit=trade.OriginalTakeProfit, FinalTakeProfit=trade.FinalTakeProfit, GrossPnlUsdt=trade.GrossPnlUsdt, TotalFeesUsdt=trade.TotalFeesUsdt, NetPnlUsdt=trade.NetPnlUsdt, NetRMultiple=trade.NetRMultiple, ExitReason=trade.ExitReason, SignalEma9=trade.SignalEma9, SignalEma15=trade.SignalEma15, SignalEma100=trade.SignalEma100, SignalGapPercent=trade.SignalGapPercent, ExpectedNetTargetR=ExpectedNetTargetR(trade, fee) };
    private static TradingSettings Settings(StrategyOptimizationCandidate candidate, StrategyOptimizationRun run) => new() { Id=1,RiskReward=candidate.RiskReward,MinEmaGapPercent=candidate.MinEmaGapPercent,MaxStopDistancePercent=candidate.MaxStopDistancePercent,WaitForConfirmationCandle=candidate.WaitForConfirmationCandle,UseEma100Filter=candidate.UseEma100Filter,UseHtfRegimeFilter=candidate.UseHtfRegimeFilter,TrailingStopEnabled=candidate.TrailingStopEnabled,SimulatedAccountBalanceUsdt=run.SimulatedAccountBalanceUsdt,MarginPerTradePercent=run.MarginPerTradePercent,Leverage=run.Leverage,FeePercentPerSide=run.FeePercentPerSide,PositionSizingMode=run.PositionSizingMode,FixedOrderSizeUsdt=run.FixedOrderSizeUsdt };
    private static bool IsSame(TradingSettings left, TradingSettings right) => left.RiskReward==right.RiskReward && left.MinEmaGapPercent==right.MinEmaGapPercent && left.MaxStopDistancePercent==right.MaxStopDistancePercent && left.WaitForConfirmationCandle==right.WaitForConfirmationCandle && left.UseEma100Filter==right.UseEma100Filter && left.UseHtfRegimeFilter==right.UseHtfRegimeFilter && left.TrailingStopEnabled==right.TrailingStopEnabled;
    public static IReadOnlyList<TradingSettings> CandidateSettings(StrategyOptimizerGrid grid, TradingSettings fixedSettings) { var all = new Dictionary<StrategyOptimizerCandidateKey,TradingSettings>(); foreach(var rr in grid.RiskRewards) foreach(var gap in grid.MinEmaGapPercents) foreach(var max in grid.MaxStopDistancePercents) foreach(var confirm in grid.WaitForConfirmationCandles) foreach(var ema in grid.UseEma100Filters) foreach(var trailing in grid.TrailingStopEnableds) { var value = Clone(fixedSettings,rr,gap,max,confirm,ema,trailing); all[Key(value)] = value; } var baseline = Clone(fixedSettings,fixedSettings.RiskReward,fixedSettings.MinEmaGapPercent,fixedSettings.MaxStopDistancePercent,fixedSettings.WaitForConfirmationCandle,fixedSettings.UseEma100Filter,fixedSettings.TrailingStopEnabled); all.TryAdd(Key(baseline), baseline); return all.Values.ToArray(); }
    private static TradingSettings Clone(TradingSettings fixedSettings, decimal rr, decimal gap, decimal max, bool confirm, bool ema, bool trailing) => new() { Id=1,RiskReward=rr,MinEmaGapPercent=gap,MaxStopDistancePercent=max,WaitForConfirmationCandle=confirm,UseEma100Filter=ema,UseHtfRegimeFilter=fixedSettings.UseHtfRegimeFilter,TrailingStopEnabled=trailing,FixedOrderSizeUsdt=fixedSettings.FixedOrderSizeUsdt,SimulatedAccountBalanceUsdt=fixedSettings.SimulatedAccountBalanceUsdt,MarginPerTradePercent=fixedSettings.MarginPerTradePercent,Leverage=fixedSettings.Leverage,FeePercentPerSide=fixedSettings.FeePercentPerSide,PositionSizingMode=fixedSettings.PositionSizingMode };
    private static StrategyOptimizerCandidateKey Key(TradingSettings settings) => new(settings.RiskReward, settings.MinEmaGapPercent, settings.MaxStopDistancePercent, settings.WaitForConfirmationCandle, settings.UseEma100Filter, settings.UseHtfRegimeFilter, settings.TrailingStopEnabled);
    private static StrategyOptimizerCandidateKey Key(StrategyOptimizationCandidate candidate) => new(candidate.RiskReward, candidate.MinEmaGapPercent, candidate.MaxStopDistancePercent, candidate.WaitForConfirmationCandle, candidate.UseEma100Filter, candidate.UseHtfRegimeFilter, candidate.TrailingStopEnabled);
    public static int InclusiveUtcCalendarDays(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var startDate = DateOnly.FromDateTime(startUtc.UtcDateTime); var endDate = DateOnly.FromDateTime(endUtc.UtcDateTime);
        return endDate.DayNumber - startDate.DayNumber + 1;
    }
    public static StrategyOptimizerStartRequest ValidateAndNormalize(StrategyOptimizerStartRequest request, TimeProvider? clock = null) { var symbols=request.Symbols.Select(symbol=>symbol.Trim().ToUpperInvariant()).Where(symbol=>symbol.Length>0).Distinct().ToArray(); var frames=request.Timeframes.Distinct().ToArray(); if(symbols.Length==0||frames.Length==0||frames.Any(frame=>!BinanceIntervals.IsSupported(frame))) throw new ArgumentException("Choose at least one enabled market and supported timeframe."); var startDate=DateOnly.FromDateTime(request.StartUtc.UtcDateTime); var endDate=DateOnly.FromDateTime(request.EndUtc.UtcDateTime); var latestCompletedDate=DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime).AddDays(-1); var inclusiveDays=InclusiveUtcCalendarDays(request.StartUtc,request.EndUtc); if(endDate>latestCompletedDate) throw new ArgumentException("The optimizer end date must be a fully completed UTC calendar day."); if(startDate>endDate || inclusiveDays<30 || inclusiveDays>90) throw new ArgumentException("Choose a UTC historical period from 30 through 90 days."); var grid=new StrategyOptimizerGrid(request.Grid.RiskRewards.Distinct().Order().ToArray(),request.Grid.MinEmaGapPercents.Distinct().Order().ToArray(),request.Grid.MaxStopDistancePercents.Distinct().Order().ToArray(),request.Grid.WaitForConfirmationCandles.Distinct().ToArray(),request.Grid.UseEma100Filters.Distinct().ToArray(),request.Grid.TrailingStopEnableds.Distinct().ToArray()); if(grid.RiskRewards.Any(value=>value<=0)||grid.MinEmaGapPercents.Any(value=>value<0)||grid.MaxStopDistancePercents.Any(value=>value<0)||grid.RiskRewards.Count==0||grid.MinEmaGapPercents.Count==0||grid.MaxStopDistancePercents.Count==0||grid.WaitForConfirmationCandles.Count==0||grid.UseEma100Filters.Count==0||grid.TrailingStopEnableds.Count==0) throw new ArgumentException("Parameter lists must contain unique valid values."); return new(symbols,frames,request.StartUtc,request.EndUtc,grid,request.WorkerCount); }
    private static TimeSpan WarmupDuration(string interval) => interval switch { "3m"=>TimeSpan.FromMinutes(600),"5m"=>TimeSpan.FromMinutes(1000),"15m"=>TimeSpan.FromMinutes(3000),"30m"=>TimeSpan.FromMinutes(6000),"1h"=>TimeSpan.FromHours(200),"2h"=>TimeSpan.FromHours(400),"4h"=>TimeSpan.FromHours(800),"6h"=>TimeSpan.FromHours(1200),"8h"=>TimeSpan.FromHours(1600),"12h"=>TimeSpan.FromHours(2400),"1d"=>TimeSpan.FromDays(200),"3d"=>TimeSpan.FromDays(600),"1w"=>TimeSpan.FromDays(1400),_=>TimeSpan.FromDays(6200) };
}
