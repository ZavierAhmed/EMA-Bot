using EmaBot.Api.Models;
using EmaBot.Api.Services;

namespace EmaBot.Api.Tests;

public sealed class DemoExecutionBrokerMoneyEvidenceEvaluatorTests
{
    [Fact]
    public void CompleteOpenEvidence_ReturnsExactSignedAmount()
    {
        var currentObservedAt = DateTimeOffset.UtcNow;
        var execution = OpenExecution(); execution.BrokerCurrentProfit = 12.50m; execution.BrokerCurrentSwap = -0.10m; execution.BrokerCurrentPnlObservedAtUtc = currentObservedAt;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);

        Assert.True(evidence.Available); Assert.Equal("USD", evidence.AccountCurrency); Assert.Equal(12.03m, evidence.Amount); Assert.Equal(currentObservedAt, evidence.ObservedAtUtc); Assert.Equal("Complete exact broker entry and current-position monetary evidence.", evidence.Reason);
    }

    [Fact]
    public void CompleteOpenZeroAndNegativeEvidence_RemainsAvailableAndSigned()
    {
        var execution = OpenExecution(); execution.State = DemoExecutionState.PartiallyFilled; execution.BrokerCurrentProfit = -5m; execution.BrokerCurrentSwap = -0.10m;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);

        Assert.True(evidence.Available); Assert.Equal(-5.47m, evidence.Amount);
    }

    [Fact]
    public void OpenEvidenceMissingComponent_FailsClosed()
    {
        var execution = OpenExecution(); execution.BrokerEntryFee = null;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);

        Assert.False(evidence.Available); Assert.Null(evidence.AccountCurrency); Assert.Null(evidence.Amount); Assert.Null(evidence.ObservedAtUtc);
    }

    [Fact]
    public void ClosedEvidence_UsesExactHistoryWithoutAddingEntryAgain()
    {
        var historyObservedAt = DateTimeOffset.UtcNow;
        var execution = OpenExecution(); execution.State = DemoExecutionState.Closed; execution.BrokerEntryProfit = 100m; execution.BrokerEntryCommission = 10m; execution.BrokerEntrySwap = 1m; execution.BrokerEntryFee = 1m; execution.BrokerHistoryProfit = 20m; execution.BrokerHistoryCommission = -0.70m; execution.BrokerHistorySwap = -0.10m; execution.BrokerHistoryFee = -0.03m; execution.BrokerHistoryPnlObservedAtUtc = historyObservedAt;

        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);

        Assert.True(evidence.Available); Assert.Equal(19.17m, evidence.Amount); Assert.Equal(historyObservedAt, evidence.ObservedAtUtc); Assert.Equal("Complete exact broker position-history monetary evidence.", evidence.Reason);
    }

    [Fact]
    public void ClosedEvidenceMissingHistoryComponent_FailsClosedWithoutEntryFallback()
    {
        var execution = OpenExecution(); execution.State = DemoExecutionState.Closed; execution.BrokerHistoryProfit = 20m; execution.BrokerHistoryCommission = -0.70m; execution.BrokerHistorySwap = -0.10m; execution.BrokerHistoryFee = null; execution.BrokerHistoryPnlObservedAtUtc = DateTimeOffset.UtcNow;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);

        Assert.False(evidence.Available); Assert.Null(evidence.AccountCurrency); Assert.Null(evidence.Amount); Assert.Null(evidence.ObservedAtUtc);
    }

    [Fact]
    public void WhitespaceCurrency_FailsClosedAndStoredCurrencyIsReturnedExactly()
    {
        var whitespaceExecution = OpenExecution(); whitespaceExecution.BrokerAccountCurrency = "   ";
        var unavailable = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(whitespaceExecution);
        var available = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(OpenExecution());

        Assert.False(unavailable.Available); Assert.Null(unavailable.AccountCurrency); Assert.True(available.Available); Assert.Equal("USD", available.AccountCurrency);
    }

    [Theory]
    [InlineData(DemoExecutionState.Created)]
    [InlineData(DemoExecutionState.Submitting)]
    [InlineData(DemoExecutionState.Rejected)]
    [InlineData(DemoExecutionState.ReconciliationRequired)]
    public void NonEvidenceLifecycleStates_FailClosed(DemoExecutionState state)
    {
        var execution = OpenExecution(); execution.State = state;
        var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(execution);

        Assert.False(evidence.Available); Assert.Null(evidence.Amount); Assert.Null(evidence.AccountCurrency); Assert.Null(evidence.ObservedAtUtc);
    }

    private static DemoExecution OpenExecution() => new()
    {
        State = DemoExecutionState.Open,
        BrokerAccountCurrency = "USD",
        BrokerEntryProfit = 0m,
        BrokerEntryCommission = -0.35m,
        BrokerEntrySwap = 0m,
        BrokerEntryFee = -0.02m,
        BrokerEntryPnlObservedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        BrokerCurrentProfit = 0m,
        BrokerCurrentSwap = 0m,
        BrokerCurrentPnlObservedAtUtc = DateTimeOffset.UtcNow
    };
}
