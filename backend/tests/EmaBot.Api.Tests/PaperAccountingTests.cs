using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class PaperAccountingTests
{
    [Theory]
    [InlineData(0.2, 0.2, 100)]
    [InlineData(-0.2, 0.2, -100)]
    public void PnlPercentOnMargin_UsesMarginAsTheExplicitBasis(decimal netPnl, decimal margin, decimal expected)
        => Assert.Equal(expected, PaperAccounting.PnlPercentOnMargin(netPnl, margin));

    [Fact]
    public void PnlPercentOnMargin_IsUnavailableWithoutPositiveMargin()
    {
        Assert.Null(PaperAccounting.PnlPercentOnMargin(0.2m, null));
        Assert.Null(PaperAccounting.PnlPercentOnMargin(0.2m, 0m));
    }

    [Fact]
    public void Reconciliation_UsesStartingBalanceAndClosedNetPnl()
    {
        var session = new PaperSession { Interval = "5m", StartingBalance = 100m, CurrentBalance = 100.86m, UsedMargin = 0m };
        var trades = new[]
        {
            new PaperTrade { Symbol = "XAUUSDm", Interval = "5m", Status = PaperTradeStatus.Closed, NetPnl = 0.40m },
            new PaperTrade { Symbol = "XAUUSDm", Interval = "5m", Status = PaperTradeStatus.Closed, NetPnl = 0.46m }
        };

        var result = PaperAccounting.Reconcile(session, trades);

        Assert.True(result.BalanceOk);
        Assert.True(result.MarginOk);
        Assert.Equal(100.86m, result.ExpectedBalance);
        Assert.Equal(0m, result.ExpectedMargin);
    }

    [Fact]
    public void InterruptionDiagnostics_MatchesEachInterruptionToTheNextResume()
    {
        var start = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        var diagnostics = PaperAccounting.Interruptions(
        [
            new PaperDecisionEvent { TimeUtc = start, Stage = "SessionInterrupted", Message = "x" },
            new PaperDecisionEvent { TimeUtc = start.AddMinutes(7), Stage = "SessionResumed", Message = "x" },
            new PaperDecisionEvent { TimeUtc = start.AddMinutes(10), Stage = "SessionInterrupted", Message = "x" },
            new PaperDecisionEvent { TimeUtc = start.AddMinutes(13), Stage = "SessionResumed", Message = "x" }
        ]);

        Assert.True(diagnostics.WasInterrupted);
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(TimeSpan.FromMinutes(10), diagnostics.TotalDuration);
    }

    [Fact]
    public void Recovery_SelectsTheFirstValidOppositeSignal_AndNeverTreatsItAsAnEntry()
    {
        var start = DateTimeOffset.UnixEpoch;
        var snapshot = new IndicatorSnapshot(start, 1m, 1m, 1m, null, null, GapState.Unchanged, TrendDirection.Neutral, 1m);
        var events = new[]
        {
            new StrategyEvent(start.AddMinutes(1), SignalDirection.Long, SignalStatus.LongSignal, snapshot),
            new StrategyEvent(start.AddMinutes(2), SignalDirection.Short, SignalStatus.ShortSignal, snapshot),
            new StrategyEvent(start.AddMinutes(3), SignalDirection.Long, SignalStatus.LongSignal, snapshot),
            new StrategyEvent(start.AddMinutes(4), SignalDirection.Short, SignalStatus.ShortSignal, snapshot)
        };

        var recovered = PaperTradingCoordinator.FirstRecoveredOppositeSignal(events, start, SignalDirection.Long);

        Assert.NotNull(recovered);
        Assert.Equal(SignalDirection.Short, recovered!.Direction);
        Assert.Equal(start.AddMinutes(2), recovered.Time);
    }
}
