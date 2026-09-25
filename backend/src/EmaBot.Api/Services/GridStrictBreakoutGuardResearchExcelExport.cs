using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

public static class GridStrictBreakoutGuardResearchExcelExport
{
    public const string ResearchType = "STRICT BREAKOUT GUARD FIXED-PATH SHADOW SCREENING";
    public const string Warning = "SCREENING ONLY. This is not a real strategy backtest. Future balance, sizing, cooldown, qualification and later baskets were not rerun.";
    public static byte[] Create(GridBreakoutGuardShadowResult result)
    {
        var summary = GridBreakoutGuardResearchExcelExport.Summary(result);
        summary[0] = new object?[] { "Research type", ResearchType };
        summary[1] = new object?[] { "Warning", Warning };
        var candidates = result.Candidates.Select(c =>
        {
            var rule = GridStrictBreakoutGuardCandidate.All.Single(r => r.Id == c.CandidateId);
            return new GridStrictBreakoutGuardCandidateResult(c, rule.MinimumAdxDelta, rule.MinimumConsecutiveAdverseCloses);
        });
        return BacktestExcelExport.Workbook(new BacktestExcelExport.Sheet[]
        {
            new("SUMMARY", summary), new("CANDIDATES", CandidateRows(candidates)),
            new("BASKET_RESULTS", GridBreakoutGuardResearchExcelExport.Rows(result.Baskets)),
            new("TRIGGERS", GridBreakoutGuardResearchExcelExport.Rows(result.Baskets.Where(b => b.Triggered)))
        });
    }
    private static IEnumerable<object?[]> CandidateRows(IEnumerable<GridStrictBreakoutGuardCandidateResult> candidates)
    {
        var properties = typeof(GridBreakoutGuardCandidateResult).GetProperties();
        yield return properties.Select(p => (object?)p.Name).Concat(new object?[] { "MinimumAdxDelta", "MinimumConsecutiveAdverseCloses" }).ToArray();
        foreach (var c in candidates)
            yield return properties.Select(p => p.GetValue(c.Result)).Concat(new object?[] { c.MinimumAdxDelta, c.MinimumConsecutiveAdverseCloses }).ToArray();
    }
}
