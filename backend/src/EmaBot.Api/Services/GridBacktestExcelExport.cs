using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

// Persisted rows only. Reuses the existing XLSX packaging, never an engine or MT5.
public static class GridBacktestExcelExport
{
    public static async Task<BacktestExcelWorkbook?> CreateAsync(EmaBotDbContext database, int id, CancellationToken token)
    {
        var run = await GridBacktestService.Graph(database).AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);
        if (run is null) return null;
        var dto = GridBacktestResponses.ToDetail(run);
        var summary = new List<object?[]>
        {
            new object?[] { "Strategy", "GRID_RANGE_V1" }, new object?[] { "Market Data", "MT5 / Exness" },
            new object?[] { "Monetary units", run.AccountCurrency },
            new object?[] { "Frozen rules", "5 equal-lot levels; non-martingale; 1% TOTAL basket price-risk; 50-bar range; ATR14 x 0.50; ADX14 <= 20; anchor TP; level-6 emergency stop; 3-bar cooldown" },
            new object?[] { "Ask reconstruction", "Ask = Bid + captured SpreadPoints * PointSize; spread is not charged again" },
            new object?[] { "NULL semantics", "Blank cell = unavailable/not calculated; numeric 0 = measured zero" },
            new object?[] { "Risk definition", "Commission excluded from initial price-risk; entry and exit commission included in net P/L" }
        };
        summary.AddRange(typeof(GridRunResponse).GetProperties().Select(p => new object?[] { p.Name, p.GetValue(dto.Run) }));
        var cycleRows = Rows(dto.Cycles.Select(c => c.Cycle)).ToList();
        // Preserve all ten frozen planned prices without replacing cycle rows with legs.
        var orderedDirections = new[] { "Long", "Short" };
        var planHeaders = orderedDirections.SelectMany(d => Enumerable.Range(1, 5).Select(n => (object?)$"{d}{n}PlannedPrice")).ToArray();
        cycleRows[0] = cycleRows[0].Concat(planHeaders).ToArray();
        for (var i = 0; i < dto.Cycles.Count; i++)
            cycleRows[i + 1] = cycleRows[i + 1].Concat(orderedDirections.SelectMany(d => Enumerable.Range(1, 5)
                .Select(n => (object?)dto.Cycles[i].PlannedLevels.Single(l => l.Direction == d && l.LevelNumber == n).Price))).ToArray();
        var diagnostics = Rows(dto.Diagnostics).ToList();
        foreach (var e in dto.Events.Where(e => e.Type == "AmbiguousFirstSide"))
            diagnostics.Add(new object?[] { null, run.Id, e.Sequence, e.Time, e.Type, e.Type, null, null, null, "OHLC cannot establish first side; no fill.", null, null, null });
        var sheets = new BacktestExcelExport.Sheet[]
        {
            new("SUMMARY", summary), new("CYCLES", cycleRows), new("BASKETS", Rows(dto.Baskets.Select(b => b.Basket))),
            new("LEGS", Rows(dto.Baskets.SelectMany(b => b.Legs))), new("EVENTS", Rows(dto.Events)), new("DIAGNOSTICS", diagnostics)
        };
        return new(run.Symbol, run.Interval, BacktestExcelExport.Workbook(sheets));
    }
    private static IEnumerable<object?[]> Rows<T>(IEnumerable<T> values)
    {
        var properties = typeof(T).GetProperties();
        yield return properties.Select(p => (object?)p.Name).ToArray();
        foreach (var value in values) yield return properties.Select(p => p.GetValue(value)).ToArray();
    }
}
