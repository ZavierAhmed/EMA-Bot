using System.Diagnostics;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

// These tests deliberately use the public native executor rather than reproducing its quote or
// economics rules.  The calculator returns deliberately non-linear values, making accidental
// fallback to price-difference P/L observable.
public sealed class Mt5HistoricalBacktestEngineTests
{
    [Fact]
    public async Task Long_StopLoss_UsesBidSide()
    {
        var trade = Assert.Single((await new NativeHarness { ExitScenario = "LongStop" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.StopLoss, trade.ExitReason); Assert.Equal(100m, trade.ExitPrice); Assert.Equal(100m, trade.ExitBid); Assert.Equal(100.2m, trade.ExitAsk); Assert.Equal(.2m, trade.ExitSpread);
    }

    [Fact]
    public async Task Long_TakeProfit_UsesBidSide()
    {
        var trade = Assert.Single((await new NativeHarness { ExitScenario = "LongTarget" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.TakeProfit, trade.ExitReason); Assert.Equal(145.6m, trade.ExitPrice); Assert.Equal(145.6m, trade.ExitBid); Assert.Equal(145.8m, trade.ExitAsk); Assert.Equal(.2m, trade.ExitSpread);
    }

    [Fact]
    public async Task Long_EndOfData_UsesBidClose()
    {
        var trade = Assert.Single((await new NativeHarness { ExitScenario = "LongEnd" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.EndOfData, trade.ExitReason); Assert.Equal(120m, trade.ExitPrice); Assert.Equal(120m, trade.ExitBid); Assert.Equal(120.2m, trade.ExitAsk); Assert.Equal(.2m, trade.ExitSpread);
    }

    [Fact]
    public async Task Long_OppositeCrossover_UsesNextExecutableBidOpen()
    {
        var trade = Assert.Single((await new NativeHarness { ExitScenario = "LongOpposite", ExitOnOpposite = true }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.OppositeCrossover, trade.ExitReason); Assert.Equal(trade.ExitBid, trade.ExitPrice); Assert.Equal(trade.ExitBid + trade.ExitSpread, trade.ExitAsk);
    }

    [Fact]
    public async Task Short_StopLoss_UsesAskSide()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, ExitScenario = "ShortStop" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.StopLoss, trade.ExitReason); Assert.Equal(130m, trade.ExitPrice); Assert.Equal(129.8m, trade.ExitBid); Assert.Equal(130m, trade.ExitAsk); Assert.Equal(.2m, trade.ExitSpread);
    }

    [Fact]
    public async Task Short_TakeProfit_UsesAskSide()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, ExitScenario = "ShortTarget" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.TakeProfit, trade.ExitReason); Assert.Equal(85m, trade.ExitPrice); Assert.Equal(84.8m, trade.ExitBid); Assert.Equal(85m, trade.ExitAsk); Assert.Equal(.2m, trade.ExitSpread);
    }

    [Fact]
    public async Task Short_EndOfData_UsesAskClose()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, ExitScenario = "ShortEnd" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.EndOfData, trade.ExitReason); Assert.Equal(110.2m, trade.ExitPrice); Assert.Equal(110m, trade.ExitBid); Assert.Equal(110.2m, trade.ExitAsk); Assert.Equal(.2m, trade.ExitSpread);
    }

    [Fact]
    public async Task Short_OppositeCrossover_UsesNextExecutableAskOpen()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, ExitScenario = "ShortOpposite", ExitOnOpposite = true }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.OppositeCrossover, trade.ExitReason); Assert.Equal(trade.ExitAsk, trade.ExitPrice); Assert.Equal(trade.ExitBid + trade.ExitSpread, trade.ExitAsk);
    }

    [Fact]
    public async Task Short_TrailingStop_UsesAskSide()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, Trailing = true, ExitScenario = "ShortTrailing" }.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.TrailingStop, trade.ExitReason); Assert.Equal(trade.ExitAsk, trade.ExitPrice); Assert.Contains(trade.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved);
    }
    [Fact]
    public async Task NativeEngine_LongEntryUsesAskAndPersistsBidAskSpreadAndBrokerProfit()
    {
        var h = new NativeHarness();
        var result = await h.RunAsync();
        var trade = Assert.Single(result.Trades);

        Assert.Equal(SignalDirection.Long, trade.Direction);
        Assert.Equal(trade.EntryBid + .2m, trade.EntryAsk);
        Assert.Equal(trade.EntryAsk, trade.EntryPrice);
        Assert.Equal(.2m, trade.EntrySpread);
        Assert.Equal(777m, trade.GrossPnl); // configured fake MT5 result, not price * quantity
        Assert.Equal(777m, trade.GrossPnlUsdt);
        Assert.Equal("USD", h.AccountCurrency);
        Assert.NotEmpty(h.Calculator.MarginCalls);
        Assert.True(h.Calculator.ProfitCalls.Count >= 3); // target, exit, initial-stop risk
    }

    [Fact]
    public async Task NativeEngine_ZeroSpreadIsValidAndDoesNotChangeLongEntry()
    {
        var h = new NativeHarness { SpreadPoints = 0 };
        var trade = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(trade.EntryBid, trade.EntryAsk);
        Assert.Equal(0m, trade.EntrySpread);
        Assert.Equal(trade.EntryBid, trade.EntryPrice);
    }

    [Fact]
    public async Task NativeEngine_ShortEntryUsesBidAndExitEvidenceUsesReconstructedAsk()
    {
        var h = new NativeHarness { Bearish = true };
        var trade = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(SignalDirection.Short, trade.Direction);
        Assert.Equal(trade.EntryBid, trade.EntryPrice);
        Assert.Equal(trade.EntryBid + .2m, trade.EntryAsk);
        Assert.Equal(.2m, trade.ExitSpread);
        Assert.NotNull(trade.ExitAsk);
    }

    [Fact]
    public async Task NativeEngine_NegativeSpreadAndInvalidExecutionPreconditionsFailClosed()
    {
        var h = new NativeHarness { SpreadPoints = -1 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync());

        h = new NativeHarness { PointSize = 0m };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync());

        h = new NativeHarness { ChartMode = HistoricalChartMode.Unknown };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync());
    }

    [Fact]
    public async Task NativeEngine_FixedLotsUsesExactBrokerRulesAndMargin()
    {
        var h = new NativeHarness();
        var trade = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(.01m, trade.Lots);
        Assert.Equal(.01m, h.Calculator.MarginCalls.Single().VolumeLots);

        foreach (var lots in new[] { .001m, 1.01m, .015m })
        {
            h = new NativeHarness { FixedLots = lots };
            var result = await h.RunAsync();
            Assert.Empty(result.Trades);
            Assert.Empty(h.Calculator.MarginCalls);
        }

        h = new NativeHarness { VolumeLimit = .005m };
        Assert.Empty((await h.RunAsync()).Trades);
        Assert.Empty(h.Calculator.MarginCalls);
    }

    [Fact]
    public async Task NativeEngine_CommissionIsPerLotPerSideAndIgnoresLegacyFeePercent()
    {
        var h = new NativeHarness { Commission = 3m, FeePercent = 0m };
        var first = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(.03m, first.EntryCommission);
        Assert.Equal(.03m, first.ExitCommission);
        Assert.Equal(.06m, first.RoundTripCommission);
        Assert.Equal(776.94m, first.NetPnl);

        h = new NativeHarness { Commission = 3m, FeePercent = 5m };
        var second = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal((first.Lots, first.EntryPrice, first.ExitPrice, first.GrossPnl, first.RoundTripCommission, first.NetPnl),
            (second.Lots, second.EntryPrice, second.ExitPrice, second.GrossPnl, second.RoundTripCommission, second.NetPnl));
    }

    [Fact]
    public async Task NativeEngine_MarginPercentUsesBrokerMarginAndNotLegacyLeverageOrNotional()
    {
        var h = new NativeHarness { SizingMode = PaperPositionSizingMode.MarginPercent, MarginPercent = 10m, StartingBalance = 1000m, Leverage = 1m, FixedOrderSize = 1m };
        var first = Assert.Single((await h.RunAsync()).Trades);
        Assert.True(h.Calculator.MarginCalls.Count >= 2);

        h = new NativeHarness { SizingMode = PaperPositionSizingMode.MarginPercent, MarginPercent = 10m, StartingBalance = 1000m, Leverage = 100m, FixedOrderSize = 999999m };
        var second = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(first.Lots, second.Lots);
        Assert.Equal(first.RequiredMargin, second.RequiredMargin);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.Disabled)]
    [InlineData(InstrumentTradeMode.CloseOnly)]
    public async Task NativeEngine_TradeModesRejectNewEntries(InstrumentTradeMode mode)
    {
        var h = new NativeHarness { TradeMode = mode };
        Assert.Empty((await h.RunAsync()).Trades);
        Assert.Empty(h.Calculator.MarginCalls);
    }

    [Fact]
    public async Task NativeEngine_TradeModesPermitOnlyTheirConfiguredDirection()
    {
        var h = new NativeHarness { TradeMode = InstrumentTradeMode.LongOnly };
        Assert.Single((await h.RunAsync()).Trades);
        h = new NativeHarness { TradeMode = InstrumentTradeMode.ShortOnly };
        Assert.Empty((await h.RunAsync()).Trades);
    }

    [Fact]
    public async Task NativeEngine_SameBarStopAndTargetKeepsStopLossFirst()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true };
        var trade = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.StopLoss, trade.ExitReason);
        Assert.True(trade.SameCandleExitConflict);
    }

    [Fact]
    public async Task NativeEngine_ShortSameBarStopAndTargetKeepsStopLossFirst()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, ForceSameBarStopAndTarget = true }.RunAsync()).Trades);
        Assert.Equal(SignalDirection.Short, trade.Direction); Assert.Equal(BacktestExitReason.StopLoss, trade.ExitReason); Assert.True(trade.SameCandleExitConflict);
    }

    [Fact]
    public async Task NativeEngine_StopDistanceRejectsWithoutChangingStructuralStopOrUsingFeeDiagnostic()
    {
        var h = new NativeHarness { StopsLevelPoints = 1_000 };
        var result = await h.RunAsync();
        Assert.Empty(result.Trades); Assert.True(result.Diagnostics.RejectedByStopDistance > 0); Assert.Equal(0, result.Diagnostics.RejectedByFees);
        Assert.Empty(h.Calculator.MarginCalls); Assert.Empty(h.Calculator.ProfitCalls);
    }

    [Fact]
    public async Task Long_ValidBrokerStopDistances_AcceptsTrade()
    {
        var trade = Assert.Single((await new NativeHarness { StopsLevelPoints = 100 }.RunAsync()).Trades);
        Assert.Equal(100m, trade.InitialStopLoss); Assert.Equal(145.6m, trade.OriginalTakeProfit);
    }

    [Fact]
    public async Task Long_StopLossInsideStopsLevel_IsRejectedWithoutMovingStop()
    {
        var result = await new NativeHarness { StopsLevelPoints = 160, FeePercent = 5m }.RunAsync();
        Assert.Empty(result.Trades); Assert.True(result.Diagnostics.RejectedByStopDistance > 0); Assert.Equal(0, result.Diagnostics.RejectedByFees);
    }

    [Fact]
    public async Task Long_TakeProfitInsideStopsLevel_IsRejectedWithoutMovingTarget()
    {
        var result = await new NativeHarness { StopsLevelPoints = 100, RiskReward = .2m }.RunAsync();
        Assert.Empty(result.Trades); Assert.True(result.Diagnostics.RejectedByStopDistance > 0); Assert.Equal(0, result.Diagnostics.RejectedByFees);
    }

    [Fact]
    public async Task Short_ValidBrokerStopDistances_AcceptsTrade()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, StopsLevelPoints = 100 }.RunAsync()).Trades);
        Assert.Equal(130m, trade.InitialStopLoss); Assert.Equal(85m, trade.OriginalTakeProfit);
    }

    [Fact]
    public async Task Short_StopLossInsideStopsLevel_IsRejectedUsingAskSide()
    {
        var result = await new NativeHarness { Bearish = true, StopsLevelPoints = 150, FeePercent = 5m }.RunAsync();
        Assert.Empty(result.Trades); Assert.True(result.Diagnostics.RejectedByStopDistance > 0); Assert.Equal(0, result.Diagnostics.RejectedByFees);
    }

    [Fact]
    public async Task Short_TakeProfitInsideStopsLevel_IsRejectedUsingAskSide()
    {
        var result = await new NativeHarness { Bearish = true, StopsLevelPoints = 100, RiskReward = .2m }.RunAsync();
        Assert.Empty(result.Trades); Assert.True(result.Diagnostics.RejectedByStopDistance > 0); Assert.Equal(0, result.Diagnostics.RejectedByFees);
    }

    [Theory]
    [InlineData(50, 20)]
    [InlineData(60, 30)]
    [InlineData(70, 40)]
    [InlineData(80, 50)]
    [InlineData(90, 60)]
    [InlineData(100, 70)]
    public void NativeEngine_TrailingScheduleRemainsTheSharedStrategyContract(decimal progress, decimal expectedLock)
        => Assert.Equal(expectedLock, TradeMath.LockPercent(progress));

    [Fact]
    public void NativeEngine_TargetExtensionRemainsOneHundredTenPercentOfOriginalDistance()
    {
        Assert.Equal(122m, TradeMath.ExtendedTarget(100m, 120m, SignalDirection.Long));
        Assert.Equal(78m, TradeMath.ExtendedTarget(100m, 80m, SignalDirection.Short));
    }

    [Fact]
    public async Task NativeEngine_TrailingExitUsesBidAndNeverUsesLegacyFeePercent()
    {
        var h = new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, FeePercent = 0m };
        var first = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal(BacktestExitReason.TrailingStop, first.ExitReason); Assert.Equal(first.ExitBid, first.ExitPrice); Assert.NotNull(first.ExitAsk);
        h = new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, FeePercent = 5m };
        var second = Assert.Single((await h.RunAsync()).Trades);
        Assert.Equal((first.FinalStopLoss, first.ExitPrice, first.NetPnl), (second.FinalStopLoss, second.ExitPrice, second.NetPnl));
    }

    [Fact]
    public async Task Long_ZeroCommission_EconomicBreakEvenUsesNativeEntryEconomics()
    {
        var first = Assert.Single((await new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, FeePercent = 0m }.RunAsync()).Trades);
        var second = Assert.Single((await new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, FeePercent = 5m }.RunAsync()).Trades);
        var moved = Assert.Single(first.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved);
        Assert.True(moved.NewStop > first.EntryPrice); Assert.Equal(moved.NewStop, Assert.Single(second.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved).NewStop);
    }

    [Fact]
    public async Task Short_ZeroCommission_EconomicBreakEvenUsesNativeEntryEconomics()
    {
        var first = Assert.Single((await new NativeHarness { Bearish = true, Trailing = true, ExitScenario = "ShortTrailing", FeePercent = 0m }.RunAsync()).Trades);
        var second = Assert.Single((await new NativeHarness { Bearish = true, Trailing = true, ExitScenario = "ShortTrailing", FeePercent = 5m }.RunAsync()).Trades);
        var moved = Assert.Single(first.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved);
        Assert.True(moved.NewStop < first.EntryPrice); Assert.Equal(moved.NewStop, Assert.Single(second.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved).NewStop);
    }

    [Fact]
    public async Task Long_CommissionAwareBreakEvenRecoversRoundTripPerLotCommission()
    {
        var h = new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, Commission = 300m, FeePercent = 5m };
        var trade = Assert.Single((await h.RunAsync()).Trades);
        var moved = Assert.Single(trade.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved);
        Assert.Equal(trade.EntryPrice + 60m, moved.NewStop); Assert.True(moved.NewStop > trade.EntryPrice); Assert.Contains(h.Calculator.ProfitCalls, call => call.ClosePrice == moved.NewStop);
    }

    [Fact]
    public async Task Short_CommissionAwareBreakEvenRecoversRoundTripPerLotCommission()
    {
        var h = new NativeHarness { Bearish = true, Trailing = true, ExitScenario = "ShortTrailing", Commission = 300m, FeePercent = 5m };
        var trade = Assert.Single((await h.RunAsync()).Trades);
        var moved = Assert.Single(trade.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved);
        Assert.Equal(trade.EntryPrice - 60m, moved.NewStop); Assert.True(moved.NewStop < trade.EntryPrice); Assert.Contains(h.Calculator.ProfitCalls, call => call.ClosePrice == moved.NewStop);
    }

    [Fact]
    public async Task Long_TrailingUsesStrategyStopWhenMoreProtectiveThanEconomicBreakEven()
    {
        var trade = Assert.Single((await new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, Commission = 0m, FeePercent = 5m }.RunAsync()).Trades);
        var stop = Assert.Single(trade.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved).NewStop!.Value;
        Assert.True(stop > trade.EntryPrice); // zero-cost BE is entry, ordinary strategy lock is more protective
    }

    [Fact]
    public async Task Short_TrailingUsesStrategyStopWhenMoreProtectiveThanEconomicBreakEven()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, Trailing = true, ExitScenario = "ShortTrailing", Commission = 0m, FeePercent = 5m }.RunAsync()).Trades);
        var stop = Assert.Single(trade.Events, item => item.Type == BacktestTradeEventType.TrailingStopMoved).NewStop!.Value;
        Assert.True(stop < trade.EntryPrice); // zero-cost BE is entry, ordinary strategy lock is more protective
    }

    [Fact]
    public async Task Long_TrailingNeverWeakensExistingStop()
    {
        var trade = Assert.Single((await new NativeHarness { Trailing = true, ExitScenario = "LongMonotonic", FeePercent = 5m }.RunAsync()).Trades);
        var stops = trade.Events.Where(item => item.Type == BacktestTradeEventType.TrailingStopMoved).Select(item => item.NewStop!.Value).ToArray();
        Assert.True(stops.Length >= 2); Assert.True(stops.Zip(stops.Skip(1)).All(pair => pair.Second >= pair.First));
    }

    [Fact]
    public async Task Short_TrailingNeverWeakensExistingStop()
    {
        var trade = Assert.Single((await new NativeHarness { Bearish = true, Trailing = true, ExitScenario = "ShortMonotonic", FeePercent = 5m }.RunAsync()).Trades);
        var stops = trade.Events.Where(item => item.Type == BacktestTradeEventType.TrailingStopMoved).Select(item => item.NewStop!.Value).ToArray();
        Assert.True(stops.Length >= 2); Assert.True(stops.Zip(stops.Skip(1)).All(pair => pair.Second <= pair.First));
    }

    [Fact]
    public async Task NativeEngine_SameTrendReentryUsesNativeEconomicsAndPersistsEvidence()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true };
        var result = await h.RunAsync();
        var reentry = Assert.Single(result.Trades, item => item.IsReentry);
        Assert.NotNull(reentry.TrendRegimeCrossoverTimeUtc);
        Assert.NotNull(reentry.ReentryAgeBars);
        Assert.Equal(reentry.EntryBid + .2m, reentry.EntryAsk);
        Assert.Equal(reentry.EntryAsk, reentry.EntryPrice);
        Assert.True(h.Calculator.MarginCalls.Count >= 2);
        Assert.True(h.Calculator.ProfitCalls.Count >= 6);
    }

    [Fact]
    public async Task NativeEngine_DisabledReentryDoesNotAddCandidateEconomics()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = false };
        var result = await h.RunAsync();
        Assert.Single(result.Trades); Assert.DoesNotContain(result.Trades, trade => trade.IsReentry); Assert.Single(h.Calculator.MarginCalls); Assert.Equal(3, h.Calculator.ProfitCalls.Count);
    }

    [Fact]
    public async Task NativeEngine_ShortSameTrendReentryUsesBidEntryAskExitAndNativeEconomics()
    {
        var h = new NativeHarness { Bearish = true, ForceSameBarStopAndTarget = true, SameTrendReentry = true };
        var reentry = Assert.Single((await h.RunAsync()).Trades, trade => trade.IsReentry);
        Assert.Equal(SignalDirection.Short, reentry.Direction); Assert.Equal(reentry.EntryBid, reentry.EntryPrice); Assert.Equal(reentry.ExitAsk, reentry.ExitPrice);
        Assert.NotNull(reentry.TrendRegimeCrossoverTimeUtc); Assert.NotNull(reentry.ReentryAgeBars); Assert.True(h.Calculator.MarginCalls.Count >= 2); Assert.True(h.Calculator.ProfitCalls.Count >= 6);
    }

    [Fact]
    public async Task StopLossExit_AllowsSameTrendReentrySearch()
    {
        var result = await new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true }.RunAsync();
        var original = Assert.Single(result.Trades, trade => !trade.IsReentry);
        var reentry = Assert.Single(result.Trades, trade => trade.IsReentry);
        Assert.Equal(BacktestExitReason.StopLoss, original.ExitReason);
        Assert.Equal(SignalDirection.Long, reentry.Direction);
        Assert.Equal(original.CrossoverTimeUtc, reentry.TrendRegimeCrossoverTimeUtc);
        Assert.Equal(1, reentry.ReentryAgeBars);
    }

    [Fact]
    public async Task TrailingStopExit_AllowsSameTrendReentrySearch()
    {
        var result = await new NativeHarness { Trailing = true, ForceTrailingStopAt40 = true, SameTrendReentry = true, MaxReentryAgeBars = 8 }.RunAsync();
        Assert.Equal(BacktestExitReason.TrailingStop, Assert.Single(result.Trades, trade => !trade.IsReentry).ExitReason);
        Assert.Contains(result.Trades, trade => trade.IsReentry);
    }

    [Fact]
    public async Task TakeProfitExit_ReentryBehaviorMatchesLegacy()
    {
        var result = await new NativeHarness { ExitScenario = "LongTarget", SameTrendReentry = true }.RunAsync();
        Assert.Equal(BacktestExitReason.TakeProfit, Assert.Single(result.Trades).ExitReason);
        Assert.DoesNotContain(result.Trades, trade => trade.IsReentry);
    }

    [Fact]
    public async Task EndOfDataExit_ReentryBehaviorMatchesLegacy()
    {
        var result = await new NativeHarness { ExitScenario = "LongEnd", SameTrendReentry = true }.RunAsync();
        Assert.Equal(BacktestExitReason.EndOfData, Assert.Single(result.Trades).ExitReason);
        Assert.DoesNotContain(result.Trades, trade => trade.IsReentry);
    }

    [Fact]
    public async Task OppositeCrossoverExit_ReentryBehaviorMatchesLegacy()
    {
        var result = await new NativeHarness { ExitScenario = "LongOpposite", ExitOnOpposite = true, SameTrendReentry = true }.RunAsync();
        Assert.Equal(BacktestExitReason.OppositeCrossover, Assert.Single(result.Trades).ExitReason);
        Assert.DoesNotContain(result.Trades, trade => trade.IsReentry);
    }

    [Fact]
    public async Task ReentryCandidate_OneBarBeforeMaxAge_IsHandledLikeLegacy()
    {
        var reentry = Assert.Single((await new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, MaxReentryAgeBars = 2 }.RunAsync()).Trades, trade => trade.IsReentry);
        Assert.Equal(1, reentry.ReentryAgeBars);
    }

    [Fact]
    public async Task ReentryCandidate_AtMaxAgeBoundary_MatchesLegacy()
    {
        var reentry = Assert.Single((await new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, MaxReentryAgeBars = 1 }.RunAsync()).Trades, trade => trade.IsReentry);
        Assert.Equal(1, reentry.ReentryAgeBars);
    }

    [Fact]
    public async Task ReentryCandidate_AfterMaxAge_IsRejected()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, MaxReentryAgeBars = 0 };
        var result = await h.RunAsync();
        Assert.Single(result.Trades); Assert.DoesNotContain(result.Trades, trade => trade.IsReentry); Assert.Single(h.Calculator.MarginCalls);
    }

    [Fact]
    public async Task Reentry_SelectsFirstContinuationCandidateExactlyLikeLegacy()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true };
        var native = Assert.Single((await h.RunAsync()).Trades, trade => trade.IsReentry);
        var legacy = Assert.Single(new BacktestEngine(new EmaSignalEngine()).RunResearch(h.Candles(), h.Settings(), h.Candles().First().OpenTimeUtc, h.Candles().Last().CloseTimeUtc).Trades, trade => trade.IsReentry);
        Assert.Equal(legacy.TrendRegimeCrossoverTimeUtc, native.TrendRegimeCrossoverTimeUtc);
        Assert.Equal(legacy.Direction, native.Direction); Assert.Equal(legacy.SignalTimeUtc, native.SignalTimeUtc);
        Assert.Equal(legacy.ReentryAgeBars, native.ReentryAgeBars); Assert.Equal(legacy.InitialStopLoss, native.InitialStopLoss);
        Assert.Equal(legacy.EntryTimeUtc, native.EntryTimeUtc);
    }

    [Fact]
    public async Task Reentry_FirstContinuationRejectedByMargin_DoesNotUseLaterCandidate()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, MarginSequence = [100m, 10_001m] };
        var result = await h.RunAsync();
        Assert.Single(result.Trades); Assert.DoesNotContain(result.Trades, trade => trade.IsReentry);
        Assert.Equal(2, h.Calculator.MarginCalls.Count); Assert.Equal(3, h.Calculator.ProfitCalls.Count);
    }

    [Fact]
    public async Task Reentry_OppositeRegimeBeforeContinuation_InvalidatesOpportunity()
    {
        var enabled = new NativeHarness { ExitScenario = "OppositeBeforeReentry", SameTrendReentry = true, MaxReentryAgeBars = 20 };
        var enabledResult = await enabled.RunAsync();
        var disabled = new NativeHarness { ExitScenario = "OppositeBeforeReentry", SameTrendReentry = false, MaxReentryAgeBars = 20 };
        var disabledResult = await disabled.RunAsync();
        Assert.Contains(enabledResult.Trades, trade => !trade.IsReentry && trade.ExitReason == BacktestExitReason.StopLoss);
        Assert.DoesNotContain(enabledResult.Trades, trade => trade.IsReentry);
        Assert.Equal(disabledResult.Trades.Count, enabledResult.Trades.Count);
        Assert.Equal(disabled.Calculator.MarginCalls.Count, enabled.Calculator.MarginCalls.Count);
        Assert.Equal(disabled.Calculator.ProfitCalls.Count, enabled.Calculator.ProfitCalls.Count);
    }

    [Fact]
    public async Task ReentryCandidatesUseSameImmutableTradeModeGateAsNormalEntries()
    {
        var longRun = await new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, TradeMode = InstrumentTradeMode.LongOnly }.RunAsync();
        Assert.Collection(longRun.Trades, original => Assert.False(original.IsReentry), reentry => Assert.True(reentry.IsReentry));
        var shortRun = await new NativeHarness { Bearish = true, ForceSameBarStopAndTarget = true, SameTrendReentry = true, TradeMode = InstrumentTradeMode.ShortOnly }.RunAsync();
        Assert.Collection(shortRun.Trades, original => Assert.False(original.IsReentry), reentry => Assert.True(reentry.IsReentry));
    }

    [Fact]
    public void ReentryContinuation_Ema100FailureIsRejectedBeforeCandidateSelection()
    {
        var settings = new TradingSettings { UseEma100Filter = true, MinEmaGapPercent = .2m };
        var continuation = new IndicatorSnapshot(DateTimeOffset.UnixEpoch, 107m, 106m, 105m, 100m, .3m, GapState.Expanding, TrendDirection.Up, 103m);
        Assert.True(DemoStrategyReentryRules.IsContinuation(continuation, SignalDirection.Long, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(continuation with { Ema100 = 105m }, SignalDirection.Long, settings));
    }

    [Fact]
    public void ReentryContinuation_EmaGapFailureIsRejectedBeforeCandidateSelection()
    {
        var settings = new TradingSettings { UseEma100Filter = true, MinEmaGapPercent = .2m };
        var continuation = new IndicatorSnapshot(DateTimeOffset.UnixEpoch, 107m, 106m, 105m, 100m, .3m, GapState.Expanding, TrendDirection.Up, 103m);
        Assert.True(DemoStrategyReentryRules.IsContinuation(continuation, SignalDirection.Long, settings));
        Assert.False(DemoStrategyReentryRules.IsContinuation(continuation with { GapPercent = .1m }, SignalDirection.Long, settings));
    }

    [Fact]
    public async Task Reentry_AllowsAtMostOneAttemptPerCrossoverRegime()
    {
        var result = await new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true }.RunAsync();
        Assert.Equal(2, result.Trades.Count); Assert.Single(result.Trades, trade => trade.IsReentry);
    }

    [Fact]
    public async Task ReentryTradeCannotGenerateAnotherReentry_WhenLegacyForbidsRecursion()
    {
        var result = await new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true }.RunAsync();
        Assert.Collection(result.Trades, first => Assert.False(first.IsReentry), second => Assert.True(second.IsReentry));
    }

    [Fact]
    public async Task Reentry_LongTradeModeShortOnly_IsRejectedWithoutLaterCandidate()
    {
        var h = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, TradeMode = InstrumentTradeMode.ShortOnly };
        Assert.Empty((await h.RunAsync()).Trades); Assert.Empty(h.Calculator.MarginCalls);
    }

    [Fact]
    public async Task Reentry_ShortTradeModeLongOnly_IsRejectedWithoutLaterCandidate()
    {
        var h = new NativeHarness { Bearish = true, ForceSameBarStopAndTarget = true, SameTrendReentry = true, TradeMode = InstrumentTradeMode.LongOnly };
        Assert.Empty((await h.RunAsync()).Trades); Assert.Empty(h.Calculator.MarginCalls);
    }

    [Fact]
    public async Task Reentry_NativeEconomicsCallsRemainInvariantAcrossFlatTail()
    {
        var shortRun = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, TotalBars = 7_200 };
        var shortResult = await shortRun.RunAsync();
        var longRun = new NativeHarness { ForceSameBarStopAndTarget = true, SameTrendReentry = true, TotalBars = 14_400 };
        var longResult = await longRun.RunAsync();
        Assert.Equal(2, shortResult.Trades.Count); Assert.Equal(shortResult.Trades.Count, longResult.Trades.Count);
        Assert.Single(shortResult.Trades, trade => !trade.IsReentry); Assert.Single(shortResult.Trades, trade => trade.IsReentry);
        Assert.Equal(2, shortRun.Calculator.MarginCalls.Count); Assert.Equal(6, shortRun.Calculator.ProfitCalls.Count);
        Assert.Equal(shortRun.Calculator.MarginCalls.Count, longRun.Calculator.MarginCalls.Count);
        Assert.Equal(shortRun.Calculator.ProfitCalls.Count, longRun.Calculator.ProfitCalls.Count);
    }

    [Fact]
    public async Task NativeBacktest_GrossAndNetProfitFactorDivergeWhenCommissionChangesEconomics()
    {
        var h = new NativeHarness
        {
            ForceSameBarStopAndTarget = true,
            SameTrendReentry = true,
            Commission = 500m,
            BrokerProfitResolver = request => request.ClosePrice > request.OpenPrice ? 100m : request.OpenPrice < 116m ? 20m : -15m
        };
        var trades = (await h.RunAsync()).Trades;
        Assert.Equal(2, trades.Count);
        var gross = trades.Select(trade => trade.GrossPnl!.Value).ToArray();
        var net = trades.Select(trade => trade.NetPnl!.Value).ToArray();
        var grossPf = gross.Where(value => value > 0m).Sum() / -gross.Where(value => value < 0m).Sum();
        var netPf = net.Where(value => value > 0m).Sum() / -net.Where(value => value < 0m).Sum();
        Assert.Equal(20m, gross.Where(value => value > 0m).Sum()); Assert.Equal(-15m, gross.Where(value => value < 0m).Sum());
        Assert.Equal(10m, net.Where(value => value > 0m).Sum()); Assert.Equal(-25m, net.Where(value => value < 0m).Sum());
        Assert.Equal(4m / 3m, grossPf); Assert.Equal(.4m, netPf); Assert.True(grossPf > 1m); Assert.True(netPf < 1m); Assert.NotEqual(grossPf, netPf);
    }

    [Fact]
    public async Task NativeEngine_NativeEconomicsCallsScaleWithCandidatesNotCandles()
    {
        var shortRun = new NativeHarness { TotalBars = 7_200 };
        var shortWatch = Stopwatch.StartNew(); var shortResult = await shortRun.RunAsync(); shortWatch.Stop();
        var longRun = new NativeHarness { TotalBars = 14_400 };
        var longWatch = Stopwatch.StartNew(); var longResult = await longRun.RunAsync(); longWatch.Stop();

        Assert.Single(shortResult.Trades); Assert.Single(longResult.Trades);
        Assert.Single(shortRun.Calculator.MarginCalls); Assert.Equal(3, shortRun.Calculator.ProfitCalls.Count);
        Assert.Equal(shortRun.Calculator.MarginCalls.Count, longRun.Calculator.MarginCalls.Count);
        Assert.Equal(shortRun.Calculator.ProfitCalls.Count, longRun.Calculator.ProfitCalls.Count);
        Assert.True(shortWatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.True(longWatch.Elapsed < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task NativeEngine_NoSignal14400Bars_PerformsZeroMarginAndProfitCalls()
    {
        var h = new NativeHarness { NoSignals = true, TotalBars = 14_400 };

        var result = await h.RunAsync();

        Assert.Empty(result.Trades); Assert.Equal(0, result.Diagnostics.LongSignals); Assert.Equal(0, result.Diagnostics.ShortSignals);
        Assert.Empty(h.Calculator.MarginCalls); Assert.Empty(h.Calculator.ProfitCalls);
    }

    [Fact]
    public async Task NativeEngine_MultiTradeEconomicsCallsRemainInvariantAcrossFlatTail()
    {
        var baseRun = new NativeHarness { MultipleSignals = true, ExitOnOpposite = true, TotalBars = 90 };
        var baseResult = await baseRun.RunAsync();
        var tailRun = new NativeHarness { MultipleSignals = true, ExitOnOpposite = true, TotalBars = 7_290 };
        var tailResult = await tailRun.RunAsync();

        Assert.Equal(3, baseResult.Diagnostics.LongSignals + baseResult.Diagnostics.ShortSignals); Assert.Equal(2, baseResult.Trades.Count); Assert.DoesNotContain(baseResult.Trades, trade => trade.IsReentry); Assert.Equal(baseResult.Trades.Count, tailResult.Trades.Count);
        Assert.Equal(2, baseRun.Calculator.MarginCalls.Count); Assert.Equal(6, baseRun.Calculator.ProfitCalls.Count);
        Assert.Equal(baseRun.Calculator.MarginCalls.Count, tailRun.Calculator.MarginCalls.Count);
        Assert.Equal(baseRun.Calculator.ProfitCalls.Count, tailRun.Calculator.ProfitCalls.Count);
        Assert.Equal(baseResult.EndingBalance, tailResult.EndingBalance);
        Assert.Equal(baseResult.Trades.Select(trade => trade.GrossPnl), tailResult.Trades.Select(trade => trade.GrossPnl));
        Assert.Equal(baseResult.Trades.Select(trade => trade.NetPnl), tailResult.Trades.Select(trade => trade.NetPnl));
    }

    [Fact]
    public async Task NativeEngine_SequentialMarginPercentTradesCompoundNativeEquityWithoutExtraCosts()
    {
        var h = new NativeHarness { MultipleSignals = true, SizingMode = PaperPositionSizingMode.MarginPercent, StartingBalance = 100m, MarginPercent = 10m, Commission = 1m, MarginPerLot = 100m, BrokerProfit = 50m };
        var result = await h.RunAsync();
        var trades = result.Trades.Take(2).ToArray();
        Assert.Equal(2, trades.Length);
        Assert.Equal(100m, trades[0].AccountEquityAtEntry);
        Assert.Equal(10m, trades[0].RequiredMargin);
        Assert.Equal(100m + trades[0].NetPnl, trades[1].AccountEquityAtEntry);
        Assert.True(trades[1].Lots > trades[0].Lots); // second margin budget sees compounded equity
        Assert.Equal(trades.Sum(trade => trade.NetPnl), result.EndingBalance - 100m);
        Assert.All(trades, trade => Assert.Equal(trade.GrossPnl - trade.RoundTripCommission, trade.NetPnl));
        Assert.All(trades, trade => Assert.Equal(trade.EntryAsk - trade.EntryBid, trade.EntrySpread));
        Assert.Equal("USD", h.AccountCurrency);
    }

    private sealed class NativeHarness
    {
        public int SpreadPoints { get; init; } = 2;
        public decimal PointSize { get; init; } = .1m;
        public HistoricalChartMode ChartMode { get; init; } = HistoricalChartMode.Bid;
        public decimal FixedLots { get; init; } = .01m;
        public decimal Commission { get; init; }
        public decimal FeePercent { get; init; }
        public decimal StartingBalance { get; init; } = 1000m;
        public decimal MarginPercent { get; init; } = 10m;
        public decimal Leverage { get; init; } = 5m;
        public decimal FixedOrderSize { get; init; } = 100m;
        public PaperPositionSizingMode SizingMode { get; init; } = PaperPositionSizingMode.FixedLots;
        public decimal? VolumeLimit { get; init; }
        public InstrumentTradeMode TradeMode { get; init; } = InstrumentTradeMode.Full;
        public bool ForceSameBarStopAndTarget { get; init; }
        public bool SameTrendReentry { get; init; }
        public bool Bearish { get; init; }
        public bool Trailing { get; init; }
        public bool ForceTrailingStopAt40 { get; init; }
        public int? StopsLevelPoints { get; init; }
        public bool MultipleSignals { get; init; }
        public bool NoSignals { get; init; }
        public decimal MarginPerLot { get; init; } = 100m;
        public decimal BrokerProfit { get; init; } = 777m;
        public Func<Mt5CalculateProfitRequest, decimal>? BrokerProfitResolver { get; init; }
        public string? ExitScenario { get; init; }
        public bool ExitOnOpposite { get; init; }
        public decimal RiskReward { get; init; } = 2m;
        public int MaxReentryAgeBars { get; init; } = 6;
        public IReadOnlyList<decimal>? MarginSequence { get; init; }
        public int TotalBars { get; init; } = 61;
        public string AccountCurrency { get; init; } = "USD";
        public RecordingCalculator Calculator { get; } = new();

        public async Task<Mt5HistoricalBacktestCalculation> RunAsync()
        {
            Calculator.MarginPerLot = MarginPerLot; Calculator.Profit = BrokerProfit; Calculator.ProfitResolver = BrokerProfitResolver; Calculator.MarginSequence = MarginSequence is null ? null : new Queue<decimal>(MarginSequence);
            var engine = new Mt5HistoricalBacktestEngine(new EmaSignalEngine(), Calculator);
            return await engine.RunAsync(Bars(), Settings(), new InstrumentCatalogItem(new InstrumentSpec("Exness", "TESTm", "TEST", AssetClass.Crypto, 1, PointSize, 1m, .01m, 1m, .01m, "T", AccountCurrency, AccountCurrency, .1m, 1m, 1m, VolumeLimit, StopsLevelPoints, null, ChartMode), null, null, true, true, TradeMode), Commission, AccountCurrency, null, CancellationToken.None);
        }

        public TradingSettings Settings()
            => new()
            {
                WaitForConfirmationCandle = false, MinEmaGapPercent = 0m, RiskReward = RiskReward,
                PaperPositionSizingMode = SizingMode, PaperFixedLots = FixedLots, PaperMarginPerTradePercent = MarginPercent,
                PaperStartingBalance = StartingBalance, FeePercentPerSide = FeePercent, Leverage = Leverage,
                FixedOrderSizeUsdt = FixedOrderSize, MaxStopDistancePercent = 0m, SameTrendReentryEnabled = SameTrendReentry, MaxReentryAgeBars = MaxReentryAgeBars, TrailingStopEnabled = Trailing, ExitOnOppositeCrossover = ExitOnOpposite
            };

        public IReadOnlyList<Candle> Candles() => Bars().Select(bar => bar.ToCandle()).ToArray();

        private IReadOnlyList<Mt5HistoricalExecutionBar> Bars()
        {
            // 30 descending closes followed by a strong rise produces one long crossover.  Later
            // bars remain flat, so increasing TotalBars adds no candidate/trade economics work.
            var values = new List<decimal>();
            if (NoSignals)
            {
                for (var i = 0; i < TotalBars; i++) values.Add(100m);
            }
            else if (MultipleSignals)
            {
                for (var i = 0; i < 30; i++) values.Add(130m - i);
                for (var i = 0; i < 20; i++) values.Add(101m + i * 2m);
                for (var i = 0; i < 20; i++) values.Add(139m - i * 2m);
                for (var i = 0; i < 20; i++) values.Add(101m + i * 2m);
            }
            else
            {
            if (Bearish)
            {
                for (var i = 0; i < 30; i++) values.Add(100m + i);
                for (var i = 0; i < 31; i++) values.Add(129m - i * 2m);
            }
            else
            {
                for (var i = 0; i < 30; i++) values.Add(130m - i);
                for (var i = 0; i < 31; i++) values.Add(101m + i * 2m);
            }
            }
            while (values.Count < TotalBars) values.Add(values[^1]);
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return values.Select((close, index) =>
            {
                var open = index == 0 ? close : values[index - 1];
                var low = Math.Min(open, close) - 1m; var high = Math.Max(open, close) + 1m;
                if (ForceSameBarStopAndTarget && index is >= 38 and <= 40) { low = 0m; high = 1_000m; }
                if (ForceTrailingStopAt40 && !Bearish && index == 39) { low = 118m; high = 136m; }
                if (ForceTrailingStopAt40 && !Bearish && index == 40) { low = 120m; high = 125m; }
                if (ExitScenario == "LongStop" && index == 39) { low = 99m; high = 130m; }
                if (ExitScenario == "LongTarget" && index == 39) { low = 110m; high = 146m; }
                if (ExitScenario == "LongEnd" && index >= 39) { open = close = 120m; low = 119m; high = 121m; }
                if (ExitScenario == "ShortStop" && index == 39) { low = 100m; high = 129.9m; }
                if (ExitScenario == "ShortTarget" && index == 39) { low = 84.8m; high = 120m; }
                if (ExitScenario == "ShortEnd" && index >= 39) { open = close = 110m; low = 109m; high = 111m; }
                if (ExitScenario == "ShortTrailing" && index == 39) { low = 94.8m; high = 120m; }
                if (ExitScenario == "ShortTrailing" && index == 40) { low = 100m; high = 108.8m; }
                if (ExitScenario == "LongMonotonic" && index == 39) { low = 119m; high = 131m; }
                if (ExitScenario == "LongMonotonic" && index == 40) { low = 122m; high = 134m; }
                if (ExitScenario == "LongMonotonic" && index >= 41) { open = close = 130m; low = 129m; high = 131m; }
                if (ExitScenario == "ShortMonotonic" && index == 39) { low = 99m; high = 120m; }
                if (ExitScenario == "ShortMonotonic" && index == 40) { low = 96m; high = 108m; }
                if (ExitScenario == "ShortMonotonic" && index >= 41) { open = close = 100m; low = 99m; high = 101m; }
                if (ExitScenario == "LongOpposite" && index >= 39) { open = close = 120m - (index - 39) * 2m; low = close - 1m; high = close + 1m; }
                if (ExitScenario == "ShortOpposite" && index >= 39) { open = close = 110m + (index - 39) * 2m; low = close - 1m; high = close + 1m; }
                if (ExitScenario == "OppositeBeforeReentry" && index is >= 38 and < 50) { open = close = 117m - (index - 38) * 3m; low = close - 1m; high = close + 1m; }
                if (ExitScenario == "OppositeBeforeReentry" && index >= 50) { open = close = 120m + (index - 50) * 2m; low = close - 1m; high = close + 1m; }
                var at = start.AddMinutes(index * 3);
                return new Mt5HistoricalExecutionBar("TESTm", "3m", at, at.AddMinutes(3).AddMilliseconds(-1), open, high, low, close, 1, SpreadPoints, true);
            }).ToArray();
        }
    }

    private sealed class RecordingCalculator : IMt5TradeCalculator
    {
        public decimal MarginPerLot { get; set; } = 100m;
        public decimal Profit { get; set; } = 777m;
        public Func<Mt5CalculateProfitRequest, decimal>? ProfitResolver { get; set; }
        public Queue<decimal>? MarginSequence { get; set; }
        public List<Mt5CalculateMarginRequest> MarginCalls { get; } = [];
        public List<Mt5CalculateProfitRequest> ProfitCalls { get; } = [];
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken cancellationToken)
        { MarginCalls.Add(request); var margin = MarginSequence is { Count: > 0 } ? MarginSequence.Dequeue() : request.VolumeLots * MarginPerLot; return Task.FromResult(new Mt5MarginCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, margin, "USD")); }
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken cancellationToken)
        { ProfitCalls.Add(request); var profit = ProfitResolver?.Invoke(request) ?? Profit; return Task.FromResult(new Mt5ProfitCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, request.ClosePrice, profit, "USD")); }
    }
}
