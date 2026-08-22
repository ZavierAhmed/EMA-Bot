using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

public sealed record DemoExecutionBrokerMoneyEvidence(bool Available, string? AccountCurrency, decimal? Amount, DateTimeOffset? ObservedAtUtc, string Reason);

// This is deliberately a pure interpretation of durable exact broker evidence.
// It makes no freshness, capability, broker, or persistence decision.
public static class DemoExecutionBrokerMoneyEvidenceEvaluator
{
    public static DemoExecutionBrokerMoneyEvidence Evaluate(DemoExecution execution) => execution.State switch
    {
        DemoExecutionState.Open or DemoExecutionState.PartiallyFilled => OpenEvidence(execution),
        DemoExecutionState.Closed => ClosedEvidence(execution),
        _ => Unavailable("Execution is not in an eligible open or closed broker-money state.")
    };

    private static DemoExecutionBrokerMoneyEvidence OpenEvidence(DemoExecution execution)
    {
        if (!HasCurrency(execution)) return Unavailable("Broker account currency is unavailable.");
        if (execution.BrokerEntryProfit is not { } entryProfit
            || execution.BrokerEntryCommission is not { } entryCommission
            || execution.BrokerEntrySwap is not { } entrySwap
            || execution.BrokerEntryFee is not { } entryFee
            || execution.BrokerEntryPnlObservedAtUtc is null
            || execution.BrokerCurrentProfit is not { } currentProfit
            || execution.BrokerCurrentSwap is not { } currentSwap
            || execution.BrokerCurrentPnlObservedAtUtc is not { } observedAtUtc)
            return Unavailable("Exact broker entry or current-position monetary evidence is incomplete.");
        return new(true, execution.BrokerAccountCurrency, entryProfit + entryCommission + entrySwap + entryFee + currentProfit + currentSwap, observedAtUtc, "Complete exact broker entry and current-position monetary evidence.");
    }

    private static DemoExecutionBrokerMoneyEvidence ClosedEvidence(DemoExecution execution)
    {
        if (!HasCurrency(execution)) return Unavailable("Broker account currency is unavailable.");
        if (execution.BrokerHistoryProfit is not { } historyProfit
            || execution.BrokerHistoryCommission is not { } historyCommission
            || execution.BrokerHistorySwap is not { } historySwap
            || execution.BrokerHistoryFee is not { } historyFee
            || execution.BrokerHistoryPnlObservedAtUtc is not { } observedAtUtc)
            return Unavailable("Exact broker position-history monetary evidence is incomplete.");
        return new(true, execution.BrokerAccountCurrency, historyProfit + historyCommission + historySwap + historyFee, observedAtUtc, "Complete exact broker position-history monetary evidence.");
    }

    private static bool HasCurrency(DemoExecution execution) => !string.IsNullOrWhiteSpace(execution.BrokerAccountCurrency);
    private static DemoExecutionBrokerMoneyEvidence Unavailable(string reason) => new(false, null, null, null, reason);
}
