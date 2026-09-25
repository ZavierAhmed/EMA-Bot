using EmaBot.Api.Data;
using EmaBot.Api.Strategy.Grid;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public interface IGridHistoricalBarSource
{
    Task<IReadOnlyList<Mt5HistoricalExecutionBar>> GetAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token);
}
public sealed class GridHistoricalBarSource(Mt5BridgeHistoricalMarketDataProvider provider) : IGridHistoricalBarSource
{
    public Task<IReadOnlyList<Mt5HistoricalExecutionBar>> GetAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        => provider.GetExecutionRangeAsync(symbol, interval, start, end, token);
}
public sealed class GridNativeEconomicsUnavailableException() : InvalidOperationException("Grid native economics are unavailable. No run was saved.");

public sealed class GridBacktestService(EmaBotDbContext database, IGridHistoricalBarSource history,
    IInstrumentCatalogProvider instruments, IMt5AccountReader accountReader, GridHistoricalBacktestEngine engine, ILogger<GridBacktestService> logger)
{
    public static DateTimeOffset WarmupStart(string interval, DateTimeOffset start)
    {
        // 100 nominal bars plus a week of closure allowance; no fabricated candles.
        var span = Mt5BridgeHistoricalMarketDataProvider.TimeframeSpan(interval) * 100 + TimeSpan.FromDays(7);
        return start.UtcTicks > span.Ticks ? start - span : DateTimeOffset.MinValue;
    }
    public async Task<GridBacktestRun> RunAsync(string symbol, string interval, DateTimeOffset start, DateTimeOffset end, decimal balance, CancellationToken token, string strategyId = GridRangeSettings.StrategyId)
    {
        var profile = GridHistoricalStrategyProfile.Resolve(strategyId);
        if (!Mt5NativeTimeframes.IsSupported(interval) || start >= end || balance <= 0m || string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Grid requires an MT5-native timeframe, valid UTC dates, and positive starting balance.");
        var monitored = await database.MonitoredSymbols.AsNoTracking().SingleOrDefaultAsync(s => s.Source == MarketDataSource.Mt5Exness && s.IsEnabled && s.Symbol == symbol, token);
        if (monitored is null || !string.Equals(monitored.Symbol, symbol, StringComparison.Ordinal)) throw new ArgumentException("The exact MT5 instrument must be monitored and enabled.");
        if (monitored.PaperCommissionPerLotPerSide is not { } commission || commission < 0m) throw new ArgumentException("Configured native commission per lot per side is required.");
        var instrument = await instruments.GetAsync(symbol, token);
        if (instrument is null || instrument.Spec.BrokerSymbol != symbol) throw new GridNativeEconomicsUnavailableException();
        if (Mt5HistoricalBacktestEngine.ValidateNativeInstrument(instrument.Spec) is { } invalid) throw new ArgumentException(invalid);
        var account = await accountReader.GetAsync(token);
        if (string.IsNullOrWhiteSpace(account.Currency)) throw new GridNativeEconomicsUnavailableException();
        var created = DateTimeOffset.UtcNow;
        var bars = await history.GetAsync(symbol, interval, WarmupStart(interval, start), end, token);
        var result = await engine.RunAsync(bars, new(symbol, interval, balance, account.Currency, commission,
            Settings: profile.Settings, RequestedStartUtc: start, RequestedEndUtc: end, StrategyId: profile.StrategyId), instrument, token);
        // G1 allows diagnostic-only qualification failures. A persisted application
        // run must not report success when required native economics was unavailable.
        if (result.Diagnostics.RiskCalculationUnavailableCount > 0 || result.Diagnostics.MarginCalculationUnavailableCount > 0)
        {
            foreach (var diagnostic in result.Diagnostics.Entries.Where(d => d.DomainCode is
                Strategy.Grid.GridCycleDiagnostics.RiskCalculationUnavailable or Strategy.Grid.GridCycleDiagnostics.MarginCalculationUnavailable))
            {
                // Whitelist fields: raw exception detail may contain secrets.
                var economics = diagnostic.Economics;
                logger.LogWarning("Grid economics unavailable: {DiagnosticCode}, operation {Operation}, broker symbol {BrokerSymbol}, direction {Direction}, lots {Lots}",
                    diagnostic.Code, economics?.Operation, economics?.Symbol ?? symbol, economics?.Direction, economics?.Lots);
            }
            throw new GridNativeEconomicsUnavailableException();
        }
        token.ThrowIfCancellationRequested();
        var run = GridBacktestPersistence.Map(result, created);
        database.GridBacktestRuns.Add(run);
        try { await database.SaveChangesAsync(token); }
        catch { database.ChangeTracker.Clear(); throw; }
        // One SaveChanges transaction writes the entire Grid graph; no partial save.
        return run;
    }
    public Task<List<GridBacktestRun>> ListAsync(CancellationToken token) => database.GridBacktestRuns.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(30).ToListAsync(token);
    public static IQueryable<GridBacktestRun> Graph(EmaBotDbContext database) => database.GridBacktestRuns
        .Include(r => r.Cycles).ThenInclude(c => c.PlannedLevels)
        .Include(r => r.Cycles).ThenInclude(c => c.Baskets).ThenInclude(b => b.Legs)
        .Include(r => r.Events).Include(r => r.Diagnostics).AsSplitQuery();
    public Task<GridBacktestRun?> GetAsync(int id, CancellationToken token) => Graph(database).AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);
    public async Task<bool> DeleteAsync(int id, CancellationToken token)
    {
        var run = await Graph(database).Include(r => r.Cycles).ThenInclude(c => c.Telemetry).SingleOrDefaultAsync(r => r.Id == id, token);
        if (run is null) return false;
        database.GridBacktestRuns.Remove(run); await database.SaveChangesAsync(token); return true;
    }
}
