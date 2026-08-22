using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

public static class DemoStrategyBudgetEvidencePolicy
{
    public static bool IsConclusiveNoFillTerminal(DemoExecution execution) =>
        execution.State is DemoExecutionState.Rejected or DemoExecutionState.Cancelled &&
        (execution.FilledVolumeLots is null or <= 0m) &&
        (execution.ClosedVolumeLots is null or <= 0m) &&
        execution.AverageFillPrice is null &&
        execution.BrokerExecutedAtUtc is null &&
        execution.BrokerClosedAtUtc is null &&
        execution.PositionTicket is null &&
        execution.PositionIdentifier is null &&
        execution.DealTicket is null &&
        execution.EntryDealTicket is null &&
        execution.ExitDealTicket is null &&
        execution.OrderTicket is null &&
        execution.BrokerEntryPnlObservedAtUtc is null &&
        execution.BrokerCurrentPnlObservedAtUtc is null &&
        execution.BrokerHistoryPnlObservedAtUtc is null &&
        execution.BrokerEntryProfit is null &&
        execution.BrokerEntryCommission is null &&
        execution.BrokerEntrySwap is null &&
        execution.BrokerEntryFee is null &&
        execution.BrokerCurrentProfit is null &&
        execution.BrokerCurrentSwap is null &&
        execution.BrokerHistoryProfit is null &&
        execution.BrokerHistoryCommission is null &&
        execution.BrokerHistorySwap is null &&
        execution.BrokerHistoryFee is null;
}
