using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

public static class GridBreakoutGuardResearchExcelExport
{
    public static byte[] Create(GridBreakoutGuardShadowResult result)
        => BacktestExcelExport.Workbook(new BacktestExcelExport.Sheet[]
        {
            new("SUMMARY", Summary(result)), new("CANDIDATES", Rows(result.Candidates)),
            new("BASKET_RESULTS", Rows(result.Baskets)), new("TRIGGERS", Rows(result.Baskets.Where(b => b.Triggered)))
        });

    internal static List<object?[]> Summary(GridBreakoutGuardShadowResult result)
    {
        var run = result.Source;
        var baskets = run.Cycles.SelectMany(c => c.Baskets).ToArray();
        var summary = new List<object?[]>
        {
            new object?[] { "Research type", "FIXED-PATH SHADOW SIMULATION / SCREENING ONLY" },
            new object?[] { "Warning", GridBreakoutGuardShadowResult.Warning },
            new object?[] { "Source Run Id", run.Id }, new object?[] { "Source StrategyId", run.StrategyId },
            new object?[] { "Symbol", run.BrokerSymbol }, new object?[] { "Interval", run.Interval },
            new object?[] { "Start UTC", run.RequestedStartUtc }, new object?[] { "End UTC", run.RequestedEndUtc },
            new object?[] { "Account currency", run.AccountCurrency }, new object?[] { "Starting balance", run.StartingBalance },
            new object?[] { "Actual ending balance", run.EndingBalance }, new object?[] { "Actual net P/L", run.NetPnl },
            new object?[] { "Actual basket count", run.BasketCount }, new object?[] { "Actual profit factor (net baskets)", run.NetProfitFactor },
            new object?[] { "Actual gross P/L (before commissions)", run.GrossPnl },
            new object?[] { "Actual gross profit (before commissions)", baskets.Where(b => b.GrossPnl > 0m).Sum(b => b.GrossPnl) },
            new object?[] { "Actual gross loss (before commissions)", -baskets.Where(b => b.GrossPnl < 0m).Sum(b => b.GrossPnl) },
            new object?[] { "Actual gross profit factor", run.GrossProfitFactor },
            new object?[] { "Actual net basket gains", baskets.Where(b => b.NetPnl > 0m).Sum(b => b.NetPnl) },
            new object?[] { "Actual net basket losses", -baskets.Where(b => b.NetPnl < 0m).Sum(b => b.NetPnl) },
            new object?[] { "Telemetry rows", run.Cycles.Sum(c => c.Telemetry.Count) },
            new object?[] { "NativeProfitCallCount", result.NativeProfitCallCount },
            new object?[] { "UniqueNativeProfitRequestCount", result.UniqueNativeProfitRequestCount },
            new object?[] { "Profit factor basis", "FixedPathShadowGrossProfit/Loss are positive/negative NET basket outcomes; zero loss gives blank profit factor." },
            new object?[] { "Native request counts", "Logical calculator calls (transport retries unchanged). Candidate calls count cache misses in catalog order; candidate unique requests may overlap. Summary counts are simulation-wide." },
            new object?[] { "TriggerLevel", "Candidate monitoring threshold; MaxFilledLevelAtTrigger is the observed deepest filled level." }
        };
        return summary;
    }
    internal static IEnumerable<object?[]> Rows<T>(IEnumerable<T> values)
    {
        var properties = typeof(T).GetProperties();
        yield return properties.Select(p => (object?)p.Name).ToArray();
        foreach (var value in values) yield return properties.Select(p => p.GetValue(value)).ToArray();
    }
}
