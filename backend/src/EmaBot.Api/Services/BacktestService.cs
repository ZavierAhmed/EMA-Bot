using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace EmaBot.Api.Services;

public sealed class BacktestService
{
    private readonly EmaBotDbContext database; private readonly IHistoricalMarketDataProvider historical; private readonly TradingSettingsService settingsService; private readonly BacktestEngine engine; private readonly ILogger<BacktestService>? logger;
    private readonly Mt5BridgeHistoricalMarketDataProvider? mt5Historical; private readonly Mt5HistoricalBacktestEngine? mt5Engine; private readonly IInstrumentCatalogProvider? instruments; private readonly IMt5AccountReader? accountReader;
    // Preserved for legacy test fixtures and persisted-artifact reproduction only.
    public BacktestService(EmaBotDbContext database, IHistoricalMarketDataProvider historical, TradingSettingsService settingsService, BacktestEngine engine, ILogger<BacktestService>? logger = null)
        => (this.database, this.historical, this.settingsService, this.engine, this.logger) = (database, historical, settingsService, engine, logger);
    public BacktestService(EmaBotDbContext database, Mt5BridgeHistoricalMarketDataProvider historical, TradingSettingsService settingsService, BacktestEngine engine, Mt5HistoricalBacktestEngine mt5Engine, IInstrumentCatalogProvider instruments, IMt5AccountReader accountReader, ILogger<BacktestService>? logger = null)
    {
        this.database = database; this.historical = historical; this.settingsService = settingsService; this.engine = engine; this.mt5Historical = historical; this.mt5Engine = mt5Engine; this.instruments = instruments; this.accountReader = accountReader; this.logger = logger;
    }
    public async Task<BacktestRun> RunAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        // Persisted/compatibility symbols remain readable and testable. A newly requested enabled MT5
        // symbol never falls through to this path.
        if (mt5Historical is not null && await database.MonitoredSymbols.AsNoTracking().AnyAsync(item => item.Source == MarketDataSource.Mt5Exness && item.IsEnabled && item.Symbol == symbol, token)) return await RunMt5NativeAsync(symbol, interval, start, end, token);
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
        var calculation = engine.Run(candles, settings, context, token); var trades = calculation.Trades;
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

    public async Task<Mt5HistoricalBacktestEconomicsPreview> GetMt5EconomicsPreviewAsync(string symbol, CancellationToken token)
    {
        var monitored = await database.MonitoredSymbols.AsNoTracking().SingleOrDefaultAsync(item => item.Source == MarketDataSource.Mt5Exness && item.IsEnabled && item.Symbol == symbol, token);
        if (monitored is null) return new(false, "The exact MT5 instrument must be monitored and enabled.");
        if (monitored.PaperCommissionPerLotPerSide is null) return new(false, "MT5 simulation commission per lot per side is not configured for this symbol.", BrokerSymbol: symbol);
        if (instruments is null || accountReader is null) return new(false, "MT5-native economics are unavailable in this compatibility environment.", BrokerSymbol: symbol);
        InstrumentCatalogItem? instrument;
        try { instrument = await instruments.GetAsync(symbol, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(false, "MT5 instrument specification is unavailable.", BrokerSymbol: symbol); }
        if (instrument is null) return new(false, "MT5 instrument specification is unavailable.", BrokerSymbol: symbol);
        if (!string.Equals(instrument.Spec.BrokerSymbol, symbol, StringComparison.Ordinal)) return new(false, "MT5 instrument specification did not match the exact requested broker symbol.", BrokerSymbol: symbol);
        if (Mt5HistoricalBacktestEngine.ValidateNativeInstrument(instrument.Spec) is { } invalidInstrument) return new(false, invalidInstrument, BrokerSymbol: symbol, ChartMode: instrument.Spec.HistoricalChartMode.ToString());
        Mt5AccountPayload account;
        try { account = await accountReader.GetAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(false, "MT5 account evidence is unavailable.", BrokerSymbol: symbol); }
        if (string.IsNullOrWhiteSpace(account.Currency)) return new(false, "MT5 account currency is unavailable.", BrokerSymbol: symbol);
        var settings = await settingsService.GetAsync(token);
        return new(
            Ready: true,
            Reason: null,
            BrokerSymbol: instrument.Spec.BrokerSymbol,
            AccountCurrency: account.Currency,
            StartingBalance: settings.PaperStartingBalance,
            SizingMode: settings.PaperPositionSizingMode,
            FixedLots: settings.PaperFixedLots,
            MarginPerTradePercent: settings.PaperMarginPerTradePercent,
            CommissionPerLotPerSide: monitored.PaperCommissionPerLotPerSide,
            HistoricalSpreadModel: Mt5HistoricalBacktestEngine.SpreadModel,
            ChartMode: instrument.Spec.HistoricalChartMode.ToString(),
            ContractSize: instrument.Spec.ContractSize,
            VolumeMin: instrument.Spec.VolumeMin,
            VolumeMax: instrument.Spec.VolumeMax,
            VolumeStep: instrument.Spec.VolumeStep,
            VolumeLimit: instrument.Spec.VolumeLimit,
            StopsLevelPoints: instrument.Spec.StopsLevelPoints,
            TradeMode: instrument.TradeMode.ToString(),
            PointSize: instrument.Spec.PointSize);
    }

    private async Task<BacktestRun> RunMt5NativeAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        var monitored = await database.MonitoredSymbols.AsNoTracking().SingleOrDefaultAsync(item => item.Source == MarketDataSource.Mt5Exness && item.IsEnabled && item.Symbol == symbol, token)
            ?? throw new InvalidOperationException("The requested exact MT5/Exness monitored symbol is not enabled.");
        if (monitored.PaperCommissionPerLotPerSide is null) throw new InvalidOperationException("MT5 historical Backtest requires configured commission per lot per side.");
        InstrumentCatalogItem? instrument;
        try { instrument = await instruments!.GetAsync(monitored.Symbol, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw new InvalidOperationException("MT5 instrument specification is unavailable.", exception); }
        if (instrument is null) throw new InvalidOperationException("MT5 instrument specification is unavailable.");
        if (!string.Equals(instrument.Spec.BrokerSymbol, monitored.Symbol, StringComparison.Ordinal)) throw new InvalidOperationException("MT5 instrument specification did not match the exact requested broker symbol.");
        if (Mt5HistoricalBacktestEngine.ValidateNativeInstrument(instrument.Spec) is { } invalidInstrument) throw new InvalidOperationException(invalidInstrument);
        Mt5AccountPayload account;
        try { account = await accountReader!.GetAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw new InvalidOperationException("MT5 account evidence is unavailable.", exception); }
        if (string.IsNullOrWhiteSpace(account.Currency)) throw new InvalidOperationException("MT5 account currency is unavailable.");
        var fetch = Stopwatch.StartNew();
        var bars = await mt5Historical!.GetExecutionRangeAsync(monitored.Symbol, interval, start, end, token);
        fetch.Stop();
        var candles = bars.Select(item => item.ToCandle()).ToArray();
        StrategyMarketContext? context = null;
        var settings = await settingsService.GetAsync(token);
        if (settings.UseHtfRegimeFilter && HigherTimeframeRegime.ForExecutionTimeframe(interval) is { } htf)
        {
            var htfCandles = await mt5Historical.GetRangeAsync(monitored.Symbol, htf, start - HigherTimeframeRegime.WarmupDuration(htf), end, token);
            context = new(candles, htf, htfCandles);
        }
        logger?.LogInformation("MT5-native historical execution fetch completed for {BrokerSymbol} in {ElapsedMilliseconds} ms.", symbol, fetch.ElapsedMilliseconds);
        var calculation = await mt5Engine!.RunAsync(bars, settings, instrument, monitored.PaperCommissionPerLotPerSide.Value, account.Currency, context, token);
        foreach (var trade in calculation.Trades) trade.NativePositionSizingMode = settings.PaperPositionSizingMode;
        var run = new BacktestRun
        {
            MarketDataSource = MarketDataSource.Mt5Exness, Symbol = symbol, BrokerSymbol = instrument.Spec.BrokerSymbol, Interval = interval, RequestedStartUtc = start, RequestedEndUtc = end,
            ActualStartUtc = bars.FirstOrDefault()?.OpenTimeUtc, ActualEndUtc = bars.LastOrDefault()?.CloseTimeUtc, CreatedAtUtc = DateTimeOffset.UtcNow, CompletedAtUtc = DateTimeOffset.UtcNow, CandleCount = bars.Count,
            RiskReward = settings.RiskReward, FixedOrderSizeUsdt = settings.FixedOrderSizeUsdt, MinEmaGapPercent = settings.MinEmaGapPercent, MaxStopDistancePercent = settings.MaxStopDistancePercent,
            PositionSizingMode = settings.PositionSizingMode, StartingBalanceUsdt = settings.SimulatedAccountBalanceUsdt, EndingBalanceUsdt = calculation.EndingBalance, MarginPerTradePercent = settings.MarginPerTradePercent, Leverage = settings.Leverage,
            WaitForConfirmationCandle = settings.WaitForConfirmationCandle, UseEma100Filter = settings.UseEma100Filter, UseHtfRegimeFilter = settings.UseHtfRegimeFilter, TrailingStopEnabled = settings.TrailingStopEnabled, UseAdaptiveInitialStop = settings.UseAdaptiveInitialStop,
            SameTrendReentryEnabled = settings.SameTrendReentryEnabled, MaxReentryAgeBars = settings.MaxReentryAgeBars, ExitOnOppositeCrossover = settings.ExitOnOppositeCrossover, FeePercentPerSide = settings.FeePercentPerSide, Status = BacktestRunStatus.Completed, Trades = calculation.Trades.ToList(),
            EconomicsMode = BacktestEconomicsMode.Mt5HistoricalBidAsk, AccountCurrency = account.Currency, HistoricalSpreadModel = Mt5HistoricalBacktestEngine.SpreadModel, HistoricalChartMode = instrument.Spec.HistoricalChartMode.ToString(), CommissionPerLotPerSide = monitored.PaperCommissionPerLotPerSide,
            ContractSize = instrument.Spec.ContractSize, VolumeMin = instrument.Spec.VolumeMin, VolumeMax = instrument.Spec.VolumeMax, VolumeStep = instrument.Spec.VolumeStep, VolumeLimit = instrument.Spec.VolumeLimit, PointSize = instrument.Spec.PointSize, TickSize = instrument.Spec.TickSize, TickValueProfit = instrument.Spec.TickValueProfit, TickValueLoss = instrument.Spec.TickValueLoss, StopsLevelPoints = instrument.Spec.StopsLevelPoints, TradeMode = instrument.TradeMode.ToString(), StartingBalance = settings.PaperStartingBalance, NativePositionSizingMode = settings.PaperPositionSizingMode, NativeFixedLots = settings.PaperFixedLots, NativeMarginPerTradePercent = settings.PaperMarginPerTradePercent, EndingBalance = calculation.EndingBalance, RejectedByTradingCosts = calculation.RejectedByTradingCosts, RejectedByInsufficientMargin = calculation.Diagnostics.RejectedByInsufficientMargin, RejectedByInvalidVolume = calculation.Diagnostics.RejectedByInvalidVolume, RejectedByTradeMode = calculation.Diagnostics.RejectedByTradeMode, Mt5EconomicsCallCount = calculation.EconomicsCallCount, Mt5EconomicsElapsedMilliseconds = calculation.EconomicsElapsedMilliseconds
        };
        PopulateSummary(run, calculation.Diagnostics); database.BacktestRuns.Add(run); await database.SaveChangesAsync(token); return run;
    }
    public Task<List<BacktestRun>> ListAsync(CancellationToken token) => database.BacktestRuns.AsNoTracking().OrderByDescending(run => run.CreatedAtUtc).Take(30).ToListAsync(token);
    public Task<BacktestRun?> GetAsync(int id, CancellationToken token) => database.BacktestRuns.AsNoTracking().Include(run => run.Trades).ThenInclude(trade => trade.Events).SingleOrDefaultAsync(run => run.Id == id, token);
    public async Task<bool> DeleteAsync(int id, CancellationToken token)
    {
        var run = await database.BacktestRuns.FindAsync([id], token);
        if (run is null) return false;
        // Keep deletion deterministic for the in-memory test provider as well as relying on the database cascade.
        var trades = await database.BacktestTrades.Where(trade => trade.BacktestRunId == id).Include(trade => trade.Events).ToListAsync(token);
        database.BacktestTradeEvents.RemoveRange(trades.SelectMany(trade => trade.Events));
        database.BacktestTrades.RemoveRange(trades);
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
        var gross = trades.Select(x => x.GrossPnl ?? x.GrossPnlUsdt).ToArray(); var net = trades.Select(x => x.NetPnl ?? x.NetPnlUsdt).ToArray();
        run.GrossProfitFactor = ProfitFactor(gross); run.NetProfitFactor = ProfitFactor(net);
        decimal cumulative = 0, peak = 0, drawdown = 0; foreach (var trade in trades.OrderBy(x => x.ExitTimeUtc)) { cumulative += trade.NetPnlUsdt; peak = Math.Max(peak, cumulative); drawdown = Math.Max(drawdown, peak - cumulative); } run.MaxDrawdownUsdt = drawdown;
        run.TotalCrossovers=d.TotalCrossovers; run.LongSignals=d.LongSignals; run.ShortSignals=d.ShortSignals; run.RejectedByEma100=d.RejectedByEma100; run.RejectedByEmaGap=d.RejectedByEmaGap; run.RejectedByHtfRegime=d.RejectedByHtfRegime; run.RejectedByStopDistance=d.RejectedByStopDistance; run.RejectedByFees=d.RejectedByFees; run.ConfirmationFailed=d.ConfirmationFailed; run.InvalidStopLoss=d.InvalidStopLoss; run.SkippedWhilePositionOpen=d.SkippedWhilePositionOpen; run.NoEntryCandle=d.NoEntryCandle; run.RejectedByInsufficientMargin=d.RejectedByInsufficientMargin; run.RejectedByInvalidVolume=d.RejectedByInvalidVolume; run.RejectedByTradeMode=d.RejectedByTradeMode;
    }
    private static decimal? ProfitFactor(IEnumerable<decimal> values) { var valuesArray = values.ToArray(); var loss = valuesArray.Where(value => value < 0m).Sum(value => -value); return loss == 0m ? null : valuesArray.Where(value => value > 0m).Sum() / loss; }
}

public sealed record Mt5HistoricalBacktestEconomicsPreview(bool Ready, string? Reason, string? BrokerSymbol = null, string? AccountCurrency = null, decimal? StartingBalance = null, PaperPositionSizingMode? SizingMode = null, decimal? FixedLots = null, decimal? MarginPerTradePercent = null, decimal? CommissionPerLotPerSide = null, string? HistoricalSpreadModel = null, string? ChartMode = null, decimal? ContractSize = null, decimal? VolumeMin = null, decimal? VolumeMax = null, decimal? VolumeStep = null, decimal? VolumeLimit = null, int? StopsLevelPoints = null, string? TradeMode = null, decimal? PointSize = null);
