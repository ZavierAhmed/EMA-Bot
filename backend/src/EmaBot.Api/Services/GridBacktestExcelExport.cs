using EmaBot.Api.Controllers;
using EmaBot.Api.Strategy.Grid;
using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

// Persisted rows only. Reuses the existing XLSX packaging, never an engine or MT5.
public static class GridBacktestExcelExport
{
    public static async Task<BacktestExcelWorkbook?> CreateAsync(EmaBotDbContext database, int id, CancellationToken token)
    {
        var run = await GridBacktestService.Graph(database).Include(r => r.Cycles).ThenInclude(c => c.Telemetry).AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);
        if (run is null) return null;
        var dto = GridBacktestResponses.ToDetail(run);
        var summary = new List<object?[]>
        {
            new object?[] { "Strategy", run.StrategyId }, new object?[] { "Market Data", "MT5 / Exness" },
            new object?[] { "Monetary units", run.AccountCurrency },
            new object?[] { "TelemetryRows", run.Cycles.Sum(c => c.Telemetry.Count) },
            new object?[] { "ActiveCyclesWithTelemetry", run.Cycles.Count(c => c.Telemetry.Count > 0) },
            new object?[] { "Frozen rules", $"{run.LevelCount} equal-lot levels; non-martingale; {run.GridBasketRiskPercent}% TOTAL basket price-risk; {run.RangeLookback}-bar range; ATR{run.AtrPeriod} x {run.AtrSpacingMultiplier}; ADX{run.AdxPeriod} <= {run.AdxThreshold}; anchor TP; emergency stop one spacing beyond deepest configured level (stop level {run.LevelCount + 1}); {run.CooldownBars}-bar cooldown" },
            new object?[] { "Ask reconstruction", "Ask = Bid + captured SpreadPoints * PointSize; spread is not charged again" },
            new object?[] { "NULL semantics", "Blank cell = unavailable/not calculated; numeric 0 = measured zero" },
            new object?[] { "Risk definition", "Commission excluded from initial price-risk; entry and exit commission included in net P/L" }
        };
        if (run.StrategyId == GridHistoricalStrategyProfile.BreakoutGuardStrategyId)
            summary.AddRange(new object?[][]
            {
                ["Research", "HISTORICAL RESEARCH ONLY; not approved for Paper/Demo/Live"],
                ["Breakout guard", GridBreakoutGuardRules.Id],
                ["Monitor from level", GridBreakoutGuardRules.MinimumFilledLevel],
                ["Minimum ADX increase", GridBreakoutGuardRules.MinimumAdxDelta],
                ["Minimum consecutive adverse closes", GridBreakoutGuardRules.MinimumConsecutiveAdverseCloses],
                ["BreakoutGuardExits", run.Cycles.SelectMany(c => c.Baskets).Count(b => b.ExitReason == "BreakoutGuard")]
            });
        summary.AddRange(typeof(GridRunResponse).GetProperties().Select(p => new object?[] { p.Name, p.GetValue(dto.Run) }));
        var cycleRows = Rows(dto.Cycles.Select(c => c.Cycle)).ToList();
        // Preserve all configured frozen planned prices without replacing cycle rows with legs.
        var orderedDirections = new[] { "Long", "Short" };
        var planHeaders = orderedDirections.SelectMany(d => Enumerable.Range(1, run.LevelCount).Select(n => (object?)$"{d}{n}PlannedPrice")).ToArray();
        cycleRows[0] = cycleRows[0].Concat(planHeaders).ToArray();
        for (var i = 0; i < dto.Cycles.Count; i++)
            cycleRows[i + 1] = cycleRows[i + 1].Concat(orderedDirections.SelectMany(d => Enumerable.Range(1, run.LevelCount)
                .Select(n => (object?)dto.Cycles[i].PlannedLevels.Single(l => l.Direction == d && l.LevelNumber == n).Price))).ToArray();
        var diagnostics = Rows(dto.Diagnostics).ToList();
        foreach (var e in dto.Events.Where(e => e.Type == "AmbiguousFirstSide"))
            diagnostics.Add(new object?[] { null, run.Id, e.Sequence, e.Time, e.Type, e.Type, null, null, null, "OHLC cannot establish first side; no fill.", null, null, null });
        var legRows = Rows(dto.Baskets.SelectMany(b => b.Legs)).ToList();
        if (run.StrategyId == GridHistoricalStrategyProfile.BreakoutGuardStrategyId)
        {
            // Guarded research legs expose their basket exit without changing legacy exports.
            legRows[0] = legRows[0].Concat(new object?[] { "ExitReason", "ExitTimeUtc", "ExitPrice" }).ToArray();
            var index = 1;
            foreach (var basket in dto.Baskets)
            foreach (var leg in basket.Legs)
            {
                legRows[index] = legRows[index].Concat(new object?[] { basket.Basket.ExitReason, basket.Basket.ExitTimeUtc, basket.Basket.ExitPrice }).ToArray();
                index++;
            }
        }
        var sheets = new BacktestExcelExport.Sheet[]
        {
            new("SUMMARY", summary), new("CYCLES", cycleRows), new("BASKETS", Rows(dto.Baskets.Select(b => b.Basket))),
            new("LEGS", legRows), new("EVENTS", Rows(dto.Events)), new("DIAGNOSTICS", diagnostics),
            new("TELEMETRY", Rows(run.Cycles.OrderBy(c => c.Sequence).SelectMany(c => c.Telemetry.OrderBy(t => t.Sequence).Select(t => new
            {
                CycleId = c.Id, t.Sequence, t.TimeUtc, t.Direction,
                t.BidOpen, t.BidHigh, t.BidLow, t.BidClose, t.SpreadPoints, t.SpreadPrice,
                t.CurrentAtr, t.CurrentAdx, QualificationAtr = c.Atr, QualificationAdx = c.Adx,
                t.AdxDeltaFromQualification, t.AtrRatioToQualification,
                FrozenRangeHigh = c.RangeHigh, FrozenRangeLow = c.RangeLow, t.CurrentRangeHigh, t.CurrentRangeLow,
                c.Anchor, c.Spacing, t.FrozenBoundaryPrice,
                t.DistanceFromAnchorSpacings, t.AdverseDistanceFromAnchorSpacings, t.BreakoutDistanceSpacings,
                t.CandleBody, t.CandleTrueRange, t.CandleBodyAtrRatio, t.CandleTrueRangeAtrRatio,
                t.CloseBeyondFrozenBoundary, t.AdverseExtremeBeyondFrozenBoundary,
                t.ConsecutiveAdverseCloses, t.ConsecutiveClosesBeyondFrozenBoundary,
                t.MaxFilledLevelBeforeBar, t.MaxFilledLevelAfterBar, t.NewFillCount,
                t.DeepestConfiguredLevel, t.DeepestLevelFilled, t.ExitReasonThisBar
            }))))
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
