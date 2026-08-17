using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

public static class PaperAccounting
{
    public static decimal? PnlPercentOnMargin(decimal? netPnl, decimal? marginUsed) =>
        netPnl is { } pnl && marginUsed is > 0m ? pnl / marginUsed.Value * 100m : null;

    public static decimal? AccountReturnPercent(decimal? netPnl, decimal? accountEquityAtEntry) =>
        netPnl is { } pnl && accountEquityAtEntry is > 0m ? pnl / accountEquityAtEntry.Value * 100m : null;

    public static PaperSessionReconciliation Reconcile(PaperSession session, IEnumerable<PaperTrade> trades)
    {
        var closedNetPnl = trades.Where(trade => trade.Status == PaperTradeStatus.Closed).Sum(trade => trade.NetPnl ?? 0m);
        var expectedBalance = session.StartingBalance + closedNetPnl;
        var balanceDifference = session.CurrentBalance - expectedBalance;
        var expectedMargin = trades.Where(trade => trade.Status == PaperTradeStatus.Open).Sum(trade => Math.Max(0m, trade.MarginUsed ?? 0m));
        var marginDifference = session.UsedMargin - expectedMargin;
        const decimal tolerance = 0.0001m;
        return new PaperSessionReconciliation(decimal.Abs(balanceDifference) <= tolerance, balanceDifference, decimal.Abs(marginDifference) <= tolerance && session.UsedMargin >= 0m, marginDifference, expectedBalance, expectedMargin);
    }

    public static PaperInterruptionDiagnostics Interruptions(IEnumerable<PaperDecisionEvent> decisions)
    {
        var ordered = decisions.OrderBy(item => item.TimeUtc).ThenBy(item => item.Id).ToArray();
        var interruptions = ordered.Where(item => item.Stage == "SessionInterrupted").Select(item => item.TimeUtc).Distinct().ToArray();
        var resumes = ordered.Where(item => item.Stage == "SessionResumed").Select(item => item.TimeUtc).Distinct().ToArray();
        var resumeIndex = 0;
        var duration = TimeSpan.Zero;
        foreach (var interruption in interruptions)
        {
            while (resumeIndex < resumes.Length && resumes[resumeIndex] < interruption) resumeIndex++;
            if (resumeIndex < resumes.Length) duration += resumes[resumeIndex++] - interruption;
        }
        return new PaperInterruptionDiagnostics(interruptions.Length > 0, interruptions.Length, duration);
    }
}

public sealed record PaperSessionReconciliation(bool BalanceOk, decimal BalanceDifference, bool MarginOk, decimal MarginDifference, decimal ExpectedBalance, decimal ExpectedMargin);
public sealed record PaperInterruptionDiagnostics(bool WasInterrupted, int Count, TimeSpan TotalDuration);
