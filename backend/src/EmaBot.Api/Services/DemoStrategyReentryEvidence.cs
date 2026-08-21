using EmaBot.Api.Models;

namespace EmaBot.Api.Services;

// B3A only classifies durable broker evidence.  It creates no intent and performs
// no submission; B3B may consume this conservative predicate later.
public static class DemoStrategyReentryEvidence
{
    public static bool IsEligibleExitReason(DemoExecution execution) =>
        !execution.NativeExitReasonConflicted
        && string.Equals(execution.NativeExitReason, "SL", StringComparison.Ordinal);
}
