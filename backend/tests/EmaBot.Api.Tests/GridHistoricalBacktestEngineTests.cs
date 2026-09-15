using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Tests;

public sealed class GridHistoricalBacktestEngineTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static Mt5HistoricalExecutionBar Bar(int index, decimal low = 98m, decimal high = 102m, decimal close = 100m, int spread = 2)
        => new("TESTm", "3m", Epoch.AddMinutes(index * 3), Epoch.AddMinutes((index + 1) * 3).AddMilliseconds(-1), close, high, low, close, 100, spread, true);
    private static List<Mt5HistoricalExecutionBar> Warmup(int count = 50) => Enumerable.Range(0, count).Select(i => Bar(i)).ToList();
    private static GridHistoricalBacktestRequest Request(decimal commission = 2m, decimal balance = 1000m)
        => new("TESTm", "3m", balance, "USD", commission);
    private static InstrumentCatalogItem Instrument(InstrumentTradeMode mode = InstrumentTradeMode.Full)
        => new(new("Exness", "TESTm", "TEST", AssetClass.Forex, 2, .1m, 100000m, .01m, 100m, .01m,
            "EUR", "USD", "USD", VolumeLimit: 100m, HistoricalChartMode: HistoricalChartMode.Bid), null, null, true, true, mode);
    private static List<Mt5HistoricalExecutionBar> BasketBars(bool isLong = true, int levels = 1, bool stop = false)
    {
        var bars = Warmup();
        bars.Add(isLong ? Bar(50, 100m - 2m * levels - .2m, 99m, 99m) : Bar(50, 101m, 100m + 2m * levels, 101m));
        bars.Add(stop ? Bar(51, 80m, 120m) : isLong ? Bar(51, 99m, 100m, 100m) : Bar(51, 99.8m, 101m, 100m));
        return bars;
    }
    private static Task<GridHistoricalBacktestResult> Run(IReadOnlyList<Mt5HistoricalExecutionBar> bars, Calculator? calc = null,
        GridHistoricalBacktestRequest? request = null, InstrumentCatalogItem? instrument = null, CancellationToken token = default)
        => new GridHistoricalBacktestEngine(calc ?? new()).RunAsync(bars, request ?? Request(), instrument ?? Instrument(), token);

    [Theory]
    [InlineData(true, 1)] [InlineData(true, 3)] [InlineData(true, 5)]
    [InlineData(false, 1)] [InlineData(false, 3)] [InlineData(false, 5)]
    public async Task CompleteBaskets_PreserveNativeLegsAndFrozenContract(bool isLong, int levels)
    {
        var calc = new Calculator(); var result = await Run(BasketBars(isLong, levels), calc);
        var b = Assert.Single(result.Baskets);
        Assert.Equal(isLong ? GridBasketDirection.Long : GridBasketDirection.Short, b.Direction);
        Assert.Equal(levels, b.Legs.Count); Assert.Equal(GridHistoricalExitReason.TakeProfit, b.ExitReason);
        Assert.Equal(100m, b.ExitPrice); Assert.Equal(100m, b.Cycle.Anchor); Assert.Equal(4m, b.Cycle.Indicators.Atr);
        Assert.Equal(2m, b.Cycle.Spacing); Assert.Equal(88m, b.Cycle.LongStop); Assert.Equal(112m, b.Cycle.ShortStop);
        Assert.Equal(10m, b.Cycle.Sizing.TargetRiskAmount); Assert.True(b.PlannedWorstCasePriceRisk <= 10m);
        Assert.Equal(1000m, b.Cycle.EntryEquity); Assert.Equal(1m, b.Cycle.TargetRiskPercent);
        Assert.Equal(5, b.Cycle.PlannedLevels.Count(l => l.Direction == b.Direction));
        Assert.Equal(Enumerable.Range(1, levels), b.Legs.Select(l => l.Level));
        Assert.All(b.Legs, l => { Assert.Equal(.03m, l.Lots); Assert.Equal(0m, (l.Lots - .01m) % .01m); Assert.Equal(3m, l.RequiredMargin); });
        Assert.Equal(.6m * Enumerable.Range(1, levels).Sum(), b.GrossPnl);
        Assert.Equal(.12m * levels, b.Commission); Assert.Equal(b.GrossPnl - b.Commission, b.NetPnl);
        Assert.Equal(1000m + b.NetPnl, result.EndingBalance); Assert.Equal(result.EndingBalance, b.EndingBalance);
        Assert.Equal(isLong ? b.ExitPrice : b.ExitPrice - .2m, b.ExitBid);
        Assert.Equal(isLong ? b.ExitPrice + .2m : b.ExitPrice, b.ExitAsk);
        Assert.Equal(30 + 2 * levels, result.EconomicsCallCount);
        Assert.Equal(result.EconomicsCallCount, calc.Profit.Count + calc.Margin.Count);
        Assert.Equal("USD", result.AccountCurrency); Assert.Equal("GRID_RANGE_V1", result.StrategyId);
        Assert.Equal(52, result.CandleCount); Assert.Equal(1, result.WinningBaskets);
        Assert.Null(result.GrossProfitFactor); Assert.Null(result.NetProfitFactor);
    }

    [Fact]
    public async Task NativeRealizedProfitCannotBeReplacedWithPriceFormulaOrSpreadFee()
    {
        var calc = new Calculator { PositiveProfit = 777m };
        var result = await Run(BasketBars(), calc);
        var b = Assert.Single(result.Baskets);
        Assert.Equal(777m, b.GrossPnl); Assert.Equal(776.88m, b.NetPnl);
        Assert.Equal(.12m, b.Commission); Assert.Equal(1776.88m, result.EndingBalance);
        Assert.Equal(new Mt5CalculateProfitRequest("TESTm", "Long", .03m, 98m, 100m), calc.Profit[^1]);
        Assert.Equal(.06m, b.Legs[0].EntryCommission); Assert.Equal(.06m, b.Legs[0].ExitCommission);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task TouchAndEndOfDataUseExecutableSides(bool isLong)
    {
        var bars = Warmup();
        // Bid low below L1, but Ask remains above: no long fill. Ask high above S1,
        // but Bid remains below: no short fill.
        bars.Add(isLong ? Bar(50, 97.9m, 99m, 99m) : Bar(50, 101m, 101.9m, 101m));
        bars.Add(isLong ? Bar(51, 97.8m, 99m, 99m) : Bar(51, 101m, 102m, 101m));
        var b = Assert.Single((await Run(bars)).Baskets);
        Assert.Equal(bars[51].CloseTimeUtc, Assert.Single(b.Legs).FillTime);
        Assert.Equal(GridHistoricalExitReason.EndOfData, b.ExitReason);
        Assert.Equal(isLong ? 99m : 101.2m, b.ExitPrice);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task FirstFillTpIsDeferredUntilLaterBarOrEndOfData(bool isLong)
    {
        var bars = Warmup(); bars.Add(isLong ? Bar(50, 97.8m, 100m, 99m) : Bar(50, 99.8m, 102m, 101m));
        var b = Assert.Single((await Run(bars)).Baskets);
        Assert.Equal(GridHistoricalExitReason.EndOfData, b.ExitReason);
        Assert.Equal(isLong ? 99m : 101.2m, b.ExitPrice);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task TakeProfitRequiresCorrectExecutableExitSide(bool isLong)
    {
        var bars = BasketBars(isLong); bars.RemoveAt(51);
        bars.Add(isLong ? Bar(51, 99m, 99.9m, 99.5m) : Bar(51, 99.9m, 101m, 100.5m));
        var b = Assert.Single((await Run(bars)).Baskets);
        Assert.Equal(GridHistoricalExitReason.EndOfData, b.ExitReason);
        Assert.Equal(isLong ? 99.5m : 100.7m, b.ExitPrice);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StopRequiresCorrectExecutableExitSide(bool isLong)
    {
        var bars = BasketBars(isLong); bars.RemoveAt(51);
        bars.Add(isLong ? Bar(51, 88m, 99m, 99m) : Bar(51, 101m, 111.8m, 101m));
        var b = Assert.Single((await Run(bars)).Baskets);
        Assert.Equal(GridHistoricalExitReason.EmergencyStop, b.ExitReason);
        Assert.Equal(isLong ? 88m : 112m, b.ExitPrice);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StopWinsConflictAndNewLevelsFillBeforeStop(bool isLong)
    {
        var result = await Run(BasketBars(isLong, stop: true)); var b = Assert.Single(result.Baskets);
        Assert.Equal(GridHistoricalExitReason.EmergencyStop, b.ExitReason); Assert.Equal(5, b.Legs.Count);
        Assert.Equal(isLong ? 88m : 112m, b.ExitPrice); Assert.Equal(-9m, b.GrossPnl);
        Assert.Equal(-9.6m, b.NetPnl); Assert.Equal(990.4m, result.EndingBalance); Assert.Equal(9.6m, result.MaxDrawdown);
        var lastEvents = b.Events.Where(e => e.Time == b.ExitTime).ToArray();
        Assert.Equal(new[] { 2, 3, 4, 5 }, lastEvents.Where(e => e.Type == GridHistoricalEventType.Fill).Select(e => e.Level!.Value));
        Assert.Equal(GridHistoricalEventType.Exit, lastEvents[^1].Type);
        Assert.Equal(0m, result.GrossProfitFactor); Assert.Equal(0m, result.NetProfitFactor);
    }

    [Fact]
    public async Task AmbiguousFirstSideDoesNotFabricateFillAndNoFillCycleIsNotBasket()
    {
        var bars = Warmup(); bars.Add(Bar(50, 90m, 110m));
        var r = await Run(bars);
        Assert.Empty(r.Baskets); Assert.Equal(1, r.Diagnostics.AmbiguousFirstSideCount);
        Assert.Equal(1, r.Diagnostics.NoFillCyclesAtEndOfData); Assert.Equal(1000m, r.EndingBalance);
        Assert.Contains(r.Events, e => e.Type == GridHistoricalEventType.CanceledWithoutFills);
        Assert.Equal(30, r.EconomicsCallCount);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly, true)]
    [InlineData(InstrumentTradeMode.ShortOnly, false)]
    public async Task BrokerSingleSideDoesNotCreateArtificialAmbiguity(InstrumentTradeMode mode, bool isLong)
    {
        var bars = Warmup(); bars.Add(Bar(50, 97.8m, 102m));
        var r = await Run(bars, instrument: Instrument(mode)); var b = Assert.Single(r.Baskets);
        Assert.Equal(isLong ? GridBasketDirection.Long : GridBasketDirection.Short, b.Direction);
        Assert.Single(b.Legs); Assert.Equal(0, r.Diagnostics.AmbiguousFirstSideCount);
        Assert.All(b.Cycle.PlannedLevels.Where(l => l.Direction != b.Direction), l => Assert.False(l.Allowed));
    }

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly)] [InlineData(InstrumentTradeMode.ShortOnly)]
    public async Task BrokerDisallowedSideNeverFills(InstrumentTradeMode mode)
    {
        var bars = Warmup(); bars.Add(mode == InstrumentTradeMode.LongOnly ? Bar(50, 101m, 110m, 101m) : Bar(50, 90m, 99m, 99m));
        var r = await Run(bars, instrument: Instrument(mode)); Assert.Empty(r.Baskets);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.Disabled)] [InlineData(InstrumentTradeMode.CloseOnly)] [InlineData(InstrumentTradeMode.Unknown)]
    public async Task BrokerBlockedModesDoNotPreflightOrOpen(InstrumentTradeMode mode)
    {
        var r = await Run(BasketBars(), instrument: Instrument(mode));
        Assert.Empty(r.Baskets); Assert.Empty(r.Cycles); Assert.Equal(0, r.EconomicsCallCount);
        Assert.True(r.Diagnostics.TradeModeBlockedCount > 0);
    }

    [Fact]
    public async Task MarginPreflightRejectsBeforeAnyFillWithoutShrinking()
    {
        var calc = new Calculator { MarginPerLot = 100000m };
        var r = await Run(Warmup(), calc);
        Assert.Empty(r.Cycles); Assert.Equal(1, r.Diagnostics.InsufficientMarginCount);
        Assert.Equal(10, calc.Margin.Count); Assert.All(calc.Margin, m => Assert.Equal(.03m, m.VolumeLots));
    }

    [Fact]
    public async Task MinimumVolumeRiskIsAttributable()
    {
        var r = await Run(Warmup(), request: Request(balance: 299.99m));
        Assert.Equal(1, r.Diagnostics.RiskBelowMinimumVolumeCount); Assert.Empty(r.Cycles);
    }

    [Fact]
    public async Task ImpossibleAggregateVolumeIsAttributable()
    {
        var instrument = Instrument(); instrument = instrument with { Spec = instrument.Spec with { VolumeLimit = .049m } };
        var r = await Run(Warmup(), instrument: instrument);
        Assert.Equal(1, r.Diagnostics.RiskCannotBeSafelySizedCount); Assert.Equal(0, r.EconomicsCallCount);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task QualificationEconomicsFailurePreservesG0IdentityAndHasNoLocalRetry(bool profit)
    {
        var calc = new Calculator { ThrowProfit = profit, ThrowMargin = !profit };
        var r = await Run(Warmup(), calc);
        Assert.Equal(1, profit ? r.Diagnostics.RiskCalculationUnavailableCount : r.Diagnostics.MarginCalculationUnavailableCount);
        Assert.Equal(1, profit ? calc.Profit.Count : calc.Margin.Count);
        var diagnostic = Assert.Single(r.Diagnostics.Entries, d => d.Economics is not null);
        Assert.Equal(profit ? "CalculateProfit" : "CalculateMargin", diagnostic.Economics!.Operation);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ActualMarginFailureTerminatesRunWithCycleEvidence(bool unavailable)
    {
        var calc = new Calculator { ThrowActualMargin = unavailable, ChangedActualMargin = !unavailable };
        var ex = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => Run(BasketBars(), calc));
        Assert.Equal(unavailable ? "MarginCalculationUnavailable" : "MarginPreflightMismatch", ex.Diagnostic.Code);
        Assert.NotNull(ex.Cycle); Assert.Equal(1000m, ex.Cycle!.EntryEquity);
        Assert.Equal(11, calc.Margin.Count);
    }

    [Fact]
    public async Task ActualAggregateMarginCannotExceedBalanceEvenWithinComparisonTolerance()
    {
        var calc = new Calculator { MarginPerLot = 1000m / .15m, TinyActualMarginIncrease = true };
        var ex = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => Run(BasketBars(levels: 5), calc));
        Assert.Equal("InsufficientActualMargin", ex.Diagnostic.Code);
    }

    [Fact]
    public async Task RealizedProfitFailureDoesNotReturnSuccessfulIncompleteRun()
    {
        var ex = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => Run(BasketBars(), new Calculator { ThrowPositiveProfit = true }));
        Assert.Equal("ProfitCalculationUnavailable", ex.Diagnostic.Code); Assert.NotNull(ex.Cycle);
        Assert.Equal("CalculateProfit", ex.Diagnostic.Economics!.Operation);
    }

    [Fact]
    public async Task CurrencyMismatchFailsClosedWithoutLegacyFallback()
    {
        var r = await Run(Warmup(), new Calculator { Currency = "EUR" });
        Assert.Equal(1, r.Diagnostics.RiskCalculationUnavailableCount); Assert.Empty(r.Cycles);
    }

    [Theory]
    [InlineData("spread")] [InlineData("ohlc")] [InlineData("symbol")] [InlineData("interval")] [InlineData("duplicate")]
    public async Task InvalidNativeBarsAreRejected(string kind)
    {
        var bars = Warmup(); var bar = bars[10];
        bars[10] = kind switch { "spread" => bar with { SpreadPoints = -1 }, "ohlc" => bar with { Low = 105m },
            "symbol" => bar with { BrokerSymbol = "OTHER" }, "interval" => bar with { Timeframe = "5m" }, _ => bars[9] };
        var ex = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => Run(bars));
        Assert.Equal("InvalidNativeBar", ex.Diagnostic.Code);
    }

    [Theory]
    [InlineData("chart")] [InlineData("point")] [InlineData("volume")] [InlineData("contract")]
    public async Task NativeInstrumentValidationMatchesEstablishedPath(string kind)
    {
        var i = Instrument(); i = i with { Spec = kind switch { "chart" => i.Spec with { HistoricalChartMode = HistoricalChartMode.Last },
            "point" => i.Spec with { PointSize = 0m }, "volume" => i.Spec with { VolumeStep = 0m }, _ => i.Spec with { ContractSize = 0m } } };
        var ex = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => Run(Warmup(), instrument: i));
        Assert.Equal(Mt5HistoricalBacktestEngine.ValidateNativeInstrument(i.Spec), ex.Message);
    }

    [Fact]
    public async Task WarmupReportingBoundsAndNoFutureLookahead()
    {
        var bars = Warmup(60);
        bars.Add(Bar(60, 97.8m, 99m, 99m)); bars.Add(Bar(61, 99m, 100m));
        var request = Request() with { RequestedStartUtc = bars[59].OpenTimeUtc, RequestedEndUtc = bars[61].CloseTimeUtc };
        var first = await Run(bars, request: request);
        bars.Add(Bar(62, 1m, 10000m)); bars.Add(Bar(63) with { IsClosed = false });
        var second = await Run(bars, request: request);
        var b = Assert.Single(first.Baskets);
        Assert.Equal(bars[59].CloseTimeUtc, b.Cycle.Indicators.Time);
        Assert.True(b.Legs[0].FillTime >= request.RequestedStartUtc);
        Assert.Equal(59, first.WarmupCandleCount); Assert.Equal(3, first.CandleCount);
        Assert.Equal(bars[59].OpenTimeUtc, first.ActualStartUtc); Assert.Equal(bars[61].CloseTimeUtc, first.ActualEndUtc);
        Assert.Equal(first.GrossPnl, second.GrossPnl); Assert.Equal(first.EndingBalance, second.EndingBalance);
        Assert.Equal(first.Cycles[0], second.Cycles[0] with { PlannedLevels = first.Cycles[0].PlannedLevels });
    }

    [Fact]
    public void IncrementalIndicatorsExactlyMatchG0AtEveryPrefix()
    {
        var candles = Enumerable.Range(0, 300).Select(i =>
        {
            var center = 1.1m + (i % 17 - 8) * .0001m;
            return Bar(i, center - .0004m, center + .0003m, center).ToCandle();
        }).ToArray();
        var window = new GridRangeIndicatorWindow();
        for (var i = 0; i < candles.Length; i++)
        {
            var snapshot = window.Append(candles[i]);
            Assert.Equal(AtrCalculator.Wilder14(candles, i), snapshot.Atr);
            Assert.Equal(GridRangeIndicators.Adx14(candles, i), snapshot.Adx);
            if (i >= 49) Assert.Equal(GridRangeIndicators.Qualify(candles.Take(i + 1).ToArray(), candles[i].CloseTimeUtc).Snapshot, snapshot);
        }
    }

    [Fact]
    public async Task LongSequenceHasBoundedEconomicsAndNoHistoryDependentCallExplosion()
    {
        // At most one accepted unfilled cycle: subsequent bars touch both sides.
        var r = await Run(Warmup(15000).Select(b => b with { SpreadPoints = 0 }).ToArray());
        Assert.Equal(30, r.EconomicsCallCount); Assert.Single(r.Cycles);
        Assert.Empty(r.Baskets); Assert.Equal(49, r.Diagnostics.RejectedQualificationCount);
        Assert.True(r.EconomicsElapsedMilliseconds >= 0);
    }

    [Fact]
    public async Task EmptyInputHasExplicitCurrencyAndFiniteEmptyStatistics()
    {
        var r = await Run([]); Assert.Equal("USD", r.AccountCurrency);
        Assert.Equal(1000m, r.StartingBalance); Assert.Equal(1000m, r.EndingBalance);
        Assert.Null(r.ActualStartUtc); Assert.Null(r.NetProfitFactor); Assert.Null(r.GrossProfitFactor);
        Assert.Equal(0m, r.AverageNetPnl); Assert.Equal(0m, r.MaxDrawdown);
    }

    [Fact]
    public async Task CancellationBeforeRunAndInsideCalculatorPropagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(Warmup(), token: cts.Token));
        using var during = new CancellationTokenSource();
        var calc = new Calculator { Cancel = during };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(Warmup(), calc, token: during.Token));
        Assert.Single(calc.Profit);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FreshCycleUsesRealizedGainOrLossAndResizesAfterExactCooldown(bool loss)
    {
        var bars = Warmup();
        bars.Add(Bar(50, 98m, 99m, 99m, 0));
        // Equal outward moves preserve ADX=0 while exercising the real G0 indicator
        // path. The exit range changes, so subsequent geometry must also change.
        bars.Add(loss ? Bar(51, 77m, 120m, 99m, 0) : Bar(51, 97m, 100m, 100m, 0));
        var anchor = loss ? 98.5m : 99.5m;
        for (var i = 52; i <= 55; i++) bars.Add(Bar(i, anchor - .5m, anchor + .5m, anchor, 0));
        var q = GridRangeIndicators.Qualify(bars.Select(b => b.ToCandle()).ToArray(), bars[^1].CloseTimeUtc);
        Assert.True(q.IsQualified);
        bars.Add(Bar(56, q.Anchor - q.Spacing, q.Anchor - q.Spacing / 2m, q.Anchor - q.Spacing / 2m, 0));
        bars.Add(Bar(57, q.Anchor - q.Spacing / 2m, q.Anchor, q.Anchor, 0));
        var r = await Run(bars, new Calculator { PositiveProfit = 777m });
        Assert.Equal(2, r.BasketCount); Assert.Equal(2, r.Cycles.Count);
        var first = r.Baskets[0]; var second = r.Baskets[1];
        Assert.Equal(first.EndingBalance, second.Cycle.EntryEquity);
        Assert.Equal(first.EndingBalance / 100m, second.Cycle.Sizing.TargetRiskAmount);
        Assert.Equal(bars[55].CloseTimeUtc, second.Cycle.Indicators.Time);
        Assert.DoesNotContain(r.Events, e => e.Type == GridHistoricalEventType.Qualified && e.Time > bars[51].CloseTimeUtc && e.Time <= bars[54].CloseTimeUtc);
        Assert.Equal(q.Snapshot.Atr, second.Cycle.Indicators.Atr); Assert.Equal(q.Anchor, second.Cycle.Anchor);
        Assert.NotEqual(first.Cycle.Spacing, second.Cycle.Spacing);
        Assert.True(loss ? second.Cycle.Sizing.Lots < first.Cycle.Sizing.Lots : second.Cycle.Sizing.Lots > first.Cycle.Sizing.Lots);
        Assert.All(second.Legs, l => Assert.Equal(second.Cycle.Sizing.Lots, l.Lots));
        Assert.Equal(1000m + r.NetPnl, r.EndingBalance);
        Assert.Equal(loss ? -first.NetPnl : 0m, r.MaxDrawdown);
        if (loss)
        {
            Assert.Equal(second.GrossPnl / -first.GrossPnl, r.GrossProfitFactor);
            Assert.Equal(second.NetPnl / -first.NetPnl, r.NetProfitFactor);
            Assert.Equal(1, r.WinningBaskets); Assert.Equal(1, r.LosingBaskets);
        }
    }

    [Fact]
    public async Task GrossAndNetProfitFactorsUseSeparateBasketSigns()
    {
        var bars = Warmup(); bars.Add(Bar(50, 98m, 99m, 99m, 0)); bars.Add(Bar(51, 97m, 100m, 100m, 0));
        for (var i = 52; i <= 55; i++) bars.Add(Bar(i, 99m, 100m, 99.5m, 0));
        var q = GridRangeIndicators.Qualify(bars.Select(b => b.ToCandle()).ToArray(), bars[^1].CloseTimeUtc);
        bars.Add(Bar(56, q.Anchor - 5m * q.Spacing, q.Anchor - q.Spacing / 2m, q.Anchor - q.Spacing / 2m, 0));
        bars.Add(Bar(57, q.Anchor - q.Spacing / 2m, q.Anchor, q.Anchor, 0));
        var r = await Run(bars, request: Request(commission: 15m));
        Assert.Equal(2, r.BasketCount); Assert.All(r.Baskets, b => Assert.True(b.GrossPnl > 0m));
        Assert.True(r.Baskets[0].NetPnl < 0m); Assert.True(r.Baskets[1].NetPnl > 0m);
        Assert.Null(r.GrossProfitFactor); Assert.Equal(r.Baskets[1].NetPnl / -r.Baskets[0].NetPnl, r.NetProfitFactor);
        Assert.Equal(-r.Baskets[0].NetPnl, r.MaxDrawdown); Assert.Equal(r.NetPnl / 2m, r.AverageNetPnl);
    }

    [Fact]
    public async Task BreakEvenClassificationUsesNetAndDoesNotChargeSpreadAgain()
    {
        var r = await Run(BasketBars(), request: Request(commission: 10m));
        Assert.Equal(.6m, r.GrossPnl); Assert.Equal(.6m, r.Commission); Assert.Equal(0m, r.NetPnl);
        Assert.Equal(1, r.BreakEvenBaskets); Assert.Equal(0, r.WinningBaskets); Assert.Equal(0, r.LosingBaskets);
    }

    [Fact]
    public async Task GridHasNoSettingsServiceOrLegacyEmaConfigurationInput()
    {
        var parameters = typeof(GridHistoricalBacktestEngine).GetConstructors().SelectMany(c => c.GetParameters())
            .Concat(typeof(GridHistoricalBacktestEngine).GetMethod("RunAsync")!.GetParameters()).Select(p => p.ParameterType).ToArray();
        Assert.DoesNotContain(typeof(TradingSettings), parameters);
        Assert.DoesNotContain(typeof(TradingSettings), typeof(GridHistoricalBacktestRequest).GetProperties().Select(p => p.PropertyType));
        Assert.DoesNotContain(typeof(GridHistoricalBacktestRequest).GetProperties(), p => p.Name is "FeePercentPerSide" or "SimulatedAccountBalanceUsdt");
        var r = await Run(BasketBars()); Assert.Equal(1000m, r.StartingBalance); Assert.Equal(.12m, r.Commission);
    }

    [Theory]
    [InlineData("currency")] [InlineData("balance")] [InlineData("commission")] [InlineData("timeframe")] [InlineData("dates")] [InlineData("cooldown")]
    public async Task InvalidConfigurationCannotFallBackToLegacyDefaults(string kind)
    {
        var request = kind switch { "currency" => Request() with { AccountCurrency = "" }, "balance" => Request(balance: 0m),
            "commission" => Request(commission: -1m), "timeframe" => Request() with { Interval = "nonsense" },
            "dates" => Request() with { RequestedStartUtc = Epoch, RequestedEndUtc = Epoch },
            _ => Request() with { Settings = new(CooldownBars: 0) } };
        await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => Run(Warmup(), request: request));
    }

    private sealed class Calculator : IMt5TradeCalculator
    {
        public List<Mt5CalculateProfitRequest> Profit { get; } = [];
        public List<Mt5CalculateMarginRequest> Margin { get; } = [];
        private readonly Dictionary<Mt5CalculateMarginRequest, int> marginRepeats = [];
        public decimal MarginPerLot { get; init; } = 100m;
        public decimal? PositiveProfit { get; init; }
        public bool ThrowProfit { get; init; }
        public bool ThrowMargin { get; init; }
        public bool ThrowPositiveProfit { get; init; }
        public bool ThrowActualMargin { get; init; }
        public bool ChangedActualMargin { get; init; }
        public bool TinyActualMarginIncrease { get; init; }
        public string Currency { get; init; } = "USD";
        public CancellationTokenSource? Cancel { get; init; }
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Profit.Add(r); Cancel?.Cancel(); token.ThrowIfCancellationRequested();
            if (ThrowProfit) throw new InvalidOperationException("Test native profit unavailable");
            var pnl = (r.ClosePrice - r.OpenPrice) * r.VolumeLots * 10m * (r.Direction == "Long" ? 1m : -1m);
            if (pnl > 0m && ThrowPositiveProfit) throw new InvalidOperationException("Test realized profit unavailable");
            if (pnl > 0m && PositiveProfit is { } value) pnl = value;
            return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice, pnl, Currency));
        }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            Margin.Add(r); marginRepeats.TryGetValue(r, out var repeats); marginRepeats[r] = ++repeats;
            if (ThrowMargin || repeats > 1 && ThrowActualMargin) throw new InvalidOperationException("Test native margin unavailable");
            var amount = r.VolumeLots * MarginPerLot;
            if (repeats > 1 && ChangedActualMargin) amount *= 2m;
            if (repeats > 1 && TinyActualMarginIncrease) amount += .000000001m;
            return Task.FromResult(new Mt5MarginCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, amount, Currency));
        }
    }
}
