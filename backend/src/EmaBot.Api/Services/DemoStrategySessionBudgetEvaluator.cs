using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

public sealed record DemoStrategySessionBudgetEvidence(
    decimal InitialAllocation,
    string? AccountCurrency,
    decimal? RealizedPnl,
    decimal? UnrealizedPnl,
    decimal? Balance,
    decimal? Equity,
    bool EvidenceReady,
    string? Reason);

// Shared read-only projection used by the operator view and the forensic export.
// It intentionally delegates every monetary decision to the existing evidence policy/evaluator.
public static class DemoStrategySessionBudgetEvaluator
{
    public static DemoStrategySessionBudgetEvidence Evaluate(decimal initialAllocation, IEnumerable<DemoExecution> executions)
    {
        string? currency = null;
        decimal realized = 0m;
        decimal unrealized = 0m;

        foreach (var execution in executions.DistinctBy(item => item.Id))
        {
            if (execution.State is DemoExecutionState.Rejected or DemoExecutionState.Cancelled)
            {
                if (DemoStrategyBudgetEvidencePolicy.IsConclusiveNoFillTerminal(execution))
                {
                    continue;
                }

                return Unavailable(initialAllocation, "Rejected/cancelled execution has ambiguous broker exposure or monetary evidence.");
            }

            var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);
            if (!evidence.Available || evidence.Amount is null || string.IsNullOrWhiteSpace(evidence.AccountCurrency))
            {
                return Unavailable(initialAllocation, evidence.Reason);
            }

            var currentCurrency = evidence.AccountCurrency.Trim();
            if (currency is not null && !string.Equals(currency, currentCurrency, StringComparison.Ordinal))
            {
                return Unavailable(initialAllocation, "Broker account currencies conflict.");
            }

            currency = currentCurrency;
            if (execution.State == DemoExecutionState.Closed)
            {
                realized += evidence.Amount.Value;
            }
            else
            {
                unrealized += evidence.Amount.Value;
            }
        }

        var balance = initialAllocation + realized;
        return new(initialAllocation, currency, realized, unrealized, balance, balance + unrealized, true, null);
    }

    private static DemoStrategySessionBudgetEvidence Unavailable(decimal initialAllocation, string reason) =>
        new(initialAllocation, null, null, null, null, null, false, reason);
}
