using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Tests;

public sealed class GridRangeTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static Candle CandleAt(int index, decimal high = 102m, decimal low = 98m, decimal close = 100m, bool closed = true)
        => new(Epoch.AddMinutes(index), Epoch.AddMinutes(index + 1), close, high, low, close, 1m, closed);
    private static Candle[] Candles(int count = 50, decimal anchor = 100m, decimal width = 2m)
        => Enumerable.Range(0, count).Select(i => CandleAt(i, anchor + width, anchor - width, anchor)).ToArray();
    private static GridRangeQualification Qualification(decimal atr = 4m, decimal? adx = 0m)
        => GridRangeIndicators.Evaluate(new(Epoch.AddMinutes(50), 102m, 98m, 100m, atr, adx, 50));
    private static GridRiskAccount Account(decimal equity = 10000m, decimal max = 10m, decimal? limit = null, decimal step = .01m, decimal min = .01m)
        => new(equity, equity, min, max, step, limit);
    private static GridHistoricalBar Bar(int index, decimal low, decimal high, int spread = 0)
        => new(CandleAt(index, high, low, (high + low) / 2m), spread, .1m);
    private static async Task<GridRangeEngine> Engine(Calculator? calculator = null)
    {
        var engine = new GridRangeEngine("TEST", new(calculator ?? new()));
        var result = await engine.TryCreateAsync(Candles(), Epoch.AddMinutes(50), new(), Account());
        Assert.Null(result.Failure);
        return engine;
    }

    [Fact]
    public void Qualification_CompletedRangeFreezesMidpointAtrAdxAndSpacing()
    {
        var q = GridRangeIndicators.Qualify(Candles(), Epoch.AddMinutes(50));
        Assert.True(q.IsQualified); Assert.Equal(100m, q.Anchor); Assert.Equal(4m, q.Snapshot.Atr);
        Assert.Equal(0m, q.Snapshot.Adx); Assert.Equal(2m, q.Spacing);
        Assert.Equal(Epoch.AddMinutes(50), q.Snapshot.Time);
    }

    [Theory]
    [InlineData(49, 4, 0, 100, GridCycleDiagnostics.InsufficientWarmup)]
    [InlineData(50, 0, 0, 100, GridCycleDiagnostics.InvalidAtr)]
    [InlineData(50, -1, 0, 100, GridCycleDiagnostics.InvalidAtr)]
    [InlineData(50, 4, 21, 100, GridCycleDiagnostics.AdxAboveThreshold)]
    [InlineData(50, 4, 0, 101.00001, GridCycleDiagnostics.CloseOutsideAnchor)]
    public void Qualification_Rejections(int count, decimal atr, decimal adx, decimal close, GridCycleDiagnostics expected)
        => Assert.Equal(expected, GridRangeIndicators.Evaluate(new(Epoch, 102m, 98m, close, atr, adx, count)).Failure);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Qualification_UnavailableIndicators(bool atr)
        => Assert.Equal(atr ? GridCycleDiagnostics.AtrUnavailable : GridCycleDiagnostics.AdxUnavailable,
            GridRangeIndicators.Evaluate(new(Epoch, 102m, 98m, 100m, atr ? null : 4m, atr ? 0m : null, 50)).Failure);

    [Fact]
    public void Qualification_ThresholdsInclusiveAndInvalidAnchorRejected()
    {
        Assert.True(GridRangeIndicators.Evaluate(new(Epoch, 102m, 98m, 101m, 4m, 20m, 50)).IsQualified);
        Assert.Equal(GridCycleDiagnostics.InvalidAnchorOrSpacing, GridRangeIndicators.Evaluate(new(Epoch, 2m, 0m, 1m, 4m, 0m, 50)).Failure);
    }

    [Fact]
    public void Qualification_ExcludesOpenAndFutureBarsAndUsesLastFifty()
    {
        var bars = Candles().Concat([CandleAt(50, 1000m, 1m, 100m, false), CandleAt(51, 1000m, 1m)]).ToArray();
        Assert.Equal(102m, GridRangeIndicators.Qualify(bars, Epoch.AddMinutes(50)).Snapshot.RangeHigh);
        Assert.Equal(GridCycleDiagnostics.InsufficientWarmup, GridRangeIndicators.Qualify(Candles(49), Epoch.AddMinutes(50)).Failure);
        var history = new[] { CandleAt(-1, 110m, 90m) }.Concat(Candles()).ToArray();
        Assert.Equal(102m, GridRangeIndicators.Qualify(history, Epoch.AddMinutes(50)).Snapshot.RangeHigh);
    }

    [Fact]
    public void Qualification_DuplicateAndMalformedCandlesRejected()
    {
        Assert.Equal(GridCycleDiagnostics.InvalidCandles, GridRangeIndicators.Qualify(Candles().Append(CandleAt(0)).ToArray(), Epoch.AddMinutes(50)).Failure);
        Assert.Equal(GridCycleDiagnostics.InvalidCandles, GridRangeIndicators.Qualify(Candles().Append(CandleAt(50, 99m, 101m)).ToArray(), Epoch.AddMinutes(51)).Failure);
    }

    [Fact]
    public void Indicators_WarmupAndWilderTrendReference()
    {
        var trend = Enumerable.Range(0, 40).Select(i => CandleAt(i, 102m + i, 98m + i, 100m + i)).ToArray();
        Assert.Null(GridRangeIndicators.Adx14(trend, 26));
        Assert.Equal(100m, GridRangeIndicators.Adx14(trend, 27));
        Assert.Equal(100m, GridRangeIndicators.Adx14(trend, 39));
        Assert.Null(AtrCalculator.Wilder14(trend, 12));
        Assert.Equal(4m, AtrCalculator.Wilder14(trend, 13));
        var jump = Candles(14).Append(CandleAt(14, 112m, 108m, 110m)).ToArray();
        Assert.Equal((4m * 13m + 12m) / 14m, AtrCalculator.Wilder14(jump, 14));
    }

    [Fact]
    public void Adx_TiedMovesAndFlatBarsAreZero()
    {
        Assert.Equal(0m, GridRangeIndicators.Adx14(Candles(50, 100m, 0m), 49));
        var expanding = Enumerable.Range(0, 30).Select(i => CandleAt(i, 102m + i, 98m - i)).ToArray();
        Assert.Equal(0m, GridRangeIndicators.Adx14(expanding, 29));
    }

    [Fact]
    public void Adx_ReversalMatchesIndependentClosedFormReference()
    {
        // Fourteen +1 moves followed by fourteen -1 moves. Smoothed directional
        // sums after k reverse moves are 14*r^k and 14*(1-r^k), r=13/14.
        var bars = Enumerable.Range(0, 29).Select(i =>
        {
            var center = 100m + (i <= 14 ? i : 28 - i);
            return CandleAt(i, center + 2m, center - 2m, center);
        }).ToArray();
        static decimal Dx(int k)
        {
            decimal power = 1m;
            for (var j = 0; j < k; j++) power *= 13m / 14m;
            return 100m * Math.Abs(2m * power - 1m);
        }
        var seed = Enumerable.Range(0, 14).Sum(Dx) / 14m;
        Assert.InRange(Math.Abs(GridRangeIndicators.Adx14(bars, 27)!.Value - seed), 0m, .00000000000000000001m);
        Assert.InRange(Math.Abs(GridRangeIndicators.Adx14(bars, 28)!.Value - (13m * seed + Dx(14)) / 14m), 0m, .00000000000000000001m);
    }

    [Fact]
    public async Task Cycle_InvalidSettingsRejectUnapprovedLevelCounts()
    {
        var e = new GridRangeEngine("TEST", new(new Calculator()));
        foreach (var settings in new[] { new GridRangeSettings(LevelCount: 3), new GridRangeSettings(GridBasketRiskPercent: 0m), new GridRangeSettings(CooldownBars: -1) })
            Assert.Equal(GridCycleDiagnostics.InvalidSettings, (await e.TryCreateAsync(Candles(), Epoch.AddMinutes(50), settings, Account())).Failure);
    }

    [Fact]
    public async Task Cycle_ConcurrentCreationAcceptsOnlyOneBasket()
    {
        var e = new GridRangeEngine("TEST", new(new Calculator()));
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => e.TryCreateAsync(Candles(), Epoch.AddMinutes(50), new(), Account())));
        Assert.Single(results, r => r.Cycle is not null);
        Assert.Equal(4, results.Count(r => r.Failure == GridCycleDiagnostics.ActiveCycle));
    }

    [Fact]
    public async Task Cycle_ExactLevelsStopsAndUniformVolume()
    {
        var e = await Engine(); var c = e.Cycle!;
        Assert.Equal("GRID_RANGE_V1", GridRangeSettings.StrategyId);
        Assert.Equal(new decimal[] { 98, 96, 94, 92, 90 }, c.Levels.Where(l => l.Direction == GridBasketDirection.Long).Select(l => l.Price));
        Assert.Equal(new decimal[] { 102, 104, 106, 108, 110 }, c.Levels.Where(l => l.Direction == GridBasketDirection.Short).Select(l => l.Price));
        Assert.Equal(88m, c.LongStop); Assert.Equal(112m, c.ShortStop); Assert.Equal(100m, c.TakeProfit);
        Assert.Single(c.Levels.Select(l => l.Lots).Distinct()); Assert.Equal(5, c.Settings.LevelCount);
        Assert.Equal(1m, c.Settings.GridBasketRiskPercent);
        Assert.Equal(GridCycleDiagnostics.ActiveCycle, (await e.TryCreateAsync(Candles(51), Epoch.AddMinutes(51), new(), Account())).Failure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fill_FirstSideLocksAndCancelsOpposite(bool isLong)
    {
        var e = await Engine(); var direction = isLong ? GridBasketDirection.Long : GridBasketDirection.Short;
        var result = e.ProcessBar(isLong ? Bar(50, 97m, 99m) : Bar(50, 101m, 103m));
        Assert.Single(result.NewFills); Assert.Equal(direction, e.Cycle!.Direction);
        Assert.All(e.Cycle.Levels.Where(l => l.Direction != direction), l => Assert.Equal(GridLevelStatus.Canceled, l.Status));
        var next = e.ProcessBar(isLong ? Bar(51, 99m, 110m) : Bar(51, 90m, 101m));
        Assert.Empty(next.NewFills);
        Assert.Equal(GridExitReason.TakeProfit, next.ExitReason);
        Assert.Equal(100m, e.Cycle.ExitPrice);
    }

    [Fact]
    public void Fill_LongTouchUsesAskShortUsesBid()
    {
        var bar = Bar(50, 97.9m, 102m, 2);
        Assert.False(GridHistoricalFillRules.IsTouched(new(1, GridBasketDirection.Long, 98m, .01m), bar));
        Assert.True(GridHistoricalFillRules.IsTouched(new(1, GridBasketDirection.Short, 102m, .01m), bar));
        Assert.True(GridHistoricalFillRules.IsTouched(new(1, GridBasketDirection.Long, 98.1m, .01m), bar));
        Assert.False(GridHistoricalFillRules.IsTouched(new(1, GridBasketDirection.Short, 102.1m, .01m), bar));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fill_MultipleNearestToDeepestAtPlannedPricesNeverMoreThanFive(bool isLong)
    {
        var e = await Engine();
        var result = e.ProcessBar(isLong ? Bar(50, 89m, 99m) : Bar(50, 101m, 111m));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.NewFills.Select(l => l.Number));
        Assert.Equal(isLong ? 98m : 102m, result.NewFills[0].Price);
        Assert.Empty(e.ProcessBar(isLong ? Bar(51, 89m, 99m) : Bar(51, 101m, 111m)).NewFills);
        Assert.Equal(5, e.Cycle!.Levels.Count(l => l.Status == GridLevelStatus.Filled));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fill_NewLevelsThenStopAndStopBeatsTakeProfit(bool isLong)
    {
        var e = await Engine();
        e.ProcessBar(isLong ? Bar(50, 97m, 99m) : Bar(50, 101m, 103m));
        var result = e.ProcessBar(Bar(51, 80m, 120m));
        Assert.Equal(new[] { 2, 3, 4, 5 }, result.NewFills.Select(l => l.Number));
        Assert.Equal(GridExitReason.EmergencyStop, result.ExitReason);
        Assert.Equal(isLong ? 88m : 112m, e.Cycle!.ExitPrice);
        Assert.Equal(5, e.Cycle.Levels.Count(l => l.Status == GridLevelStatus.Closed));
        Assert.Empty(e.ProcessBar(Bar(52, 80m, 120m)).NewFills);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fill_FirstFillBarStopStillClosesAllLegs(bool isLong)
    {
        var e = await Engine();
        var result = e.ProcessBar(isLong ? Bar(50, 80m, 99m) : Bar(50, 101m, 120m));
        Assert.Equal(5, result.NewFills.Count); Assert.Equal(GridExitReason.EmergencyStop, result.ExitReason);
    }

    [Fact]
    public async Task Fill_AmbiguousFirstSideDefersAndFirstFillTpIsDeferred()
    {
        var e = await Engine();
        Assert.Equal(GridCycleDiagnostics.AmbiguousFirstSide, e.ProcessBar(Bar(50, 90m, 110m)).Diagnostic);
        Assert.Null(e.Cycle!.Direction);
        Assert.Null(e.ProcessBar(Bar(51, 97m, 101m)).ExitReason);
        Assert.Equal(GridBasketDirection.Long, e.Cycle.Direction);
    }

    [Fact]
    public async Task Fill_ShortExitUsesAskAndStopRemainsFrozen()
    {
        var e = await Engine(); e.ProcessBar(Bar(50, 101m, 103m));
        Assert.Null(e.ProcessBar(Bar(51, 99.9m, 110m, 2)).ExitReason);
        Assert.Equal(112m, e.Cycle!.ShortStop); Assert.Equal(100m, e.Cycle.Anchor);
        Assert.Equal(GridExitReason.EmergencyStop, e.ProcessBar(Bar(52, 101m, 111.9m, 2)).ExitReason);
        Assert.Equal(112m, e.Cycle.ExitPrice);
    }

    [Fact]
    public async Task Cooldown_ThreeCompletedBarsBlockThenFreshQualificationRecalculates()
    {
        var e = await Engine(); var old = e.Cycle!;
        e.ProcessBar(Bar(50, 80m, 99m));
        Assert.Equal(3, e.CooldownRemaining);
        Assert.Equal(GridCycleDiagnostics.Cooldown, (await e.TryCreateAsync(Candles(51), Epoch.AddMinutes(51), new(), Account())).Failure);
        for (var index = 51; index <= 53; index++)
        {
            e.ProcessBar(Bar(index, 99m, 101m)); e.ProcessBar(Bar(index, 99m, 101m));
            Assert.Equal(53 - index, e.CooldownRemaining);
            Assert.Equal(GridCycleDiagnostics.Cooldown, (await e.TryCreateAsync(Candles(index + 1), Epoch.AddMinutes(index + 1), new(), Account())).Failure);
        }
        var fresh = await e.TryCreateAsync(Candles(55, 200m, 3m), Epoch.AddMinutes(55), new(), Account(20000m));
        Assert.Null(fresh.Failure); Assert.NotSame(old, fresh.Cycle);
        Assert.Equal(200m, fresh.Cycle!.Anchor); Assert.Equal(6m, fresh.Cycle.Snapshot.Atr);
        Assert.Equal(197m, fresh.Cycle.Levels[0].Price); Assert.Equal(200m, fresh.Cycle.Sizing.TargetRiskAmount);
        Assert.Equal(100m, old.Anchor); Assert.Equal(88m, old.LongStop);
    }

    [Fact]
    public async Task Fill_IgnoresQualificationBarAndRejectsOpenOrInvalidSpread()
    {
        var e = await Engine(); Assert.Empty(e.ProcessBar(Bar(49, 80m, 120m)).NewFills);
        Assert.Throws<ArgumentException>(() => e.ProcessBar(Bar(50, 97m, 99m, -1)));
        Assert.Throws<ArgumentException>(() => e.ProcessBar(new(CandleAt(50, closed: false), 0, .1m)));
    }

    [Fact]
    public async Task Risk_UsesNativeProfitForAllLegsAndBothSidesWithinTotalOnePercent()
    {
        var calc = new Calculator(); var r = await new GridRangeRiskSizer(calc).SizeAsync("TEST", Qualification(), new(), Account());
        Assert.True(r.IsSuccess); Assert.Equal(.33m, r.Lots); Assert.Equal(100m, r.TargetRiskAmount);
        Assert.Equal(99m, r.LongRisk); Assert.Equal(99m, r.ShortRisk);
        Assert.All(calc.Profit, p => { Assert.Equal("TEST", p.BrokerSymbol); Assert.Equal(p.Direction == "Long" ? 88m : 112m, p.ClosePrice); });
        Assert.Equal(new decimal[] { 98, 96, 94, 92, 90 }, calc.Profit.Where(p => p.Direction == "Long" && p.VolumeLots == .33m).Select(p => p.OpenPrice));
        Assert.Equal(10, calc.Margin.Count);
        Assert.All(calc.Margin, p => Assert.Equal(.33m, p.VolumeLots));
        Assert.Equal(165m, r.LongMargin); Assert.Equal(165m, r.ShortMargin);
    }

    [Theory]
    [InlineData(10, 0, 0.33)]
    [InlineData(0.12, 0, 0.12)]
    [InlineData(10, 0.55, 0.11)]
    public async Task Risk_NormalizesDownHonorsMaxAndAggregateLimit(decimal max, decimal limit, decimal expected)
    {
        var r = await new GridRangeRiskSizer(new Calculator()).SizeAsync("TEST", Qualification(), new(), Account(max: max, limit: limit));
        Assert.True(r.IsSuccess); Assert.Equal(expected, r.Lots); Assert.True(r.LongRisk <= r.TargetRiskAmount);
    }

    [Fact]
    public async Task Risk_ExistingDirectionalVolumeCountsAgainstLimit()
    {
        var r = await new GridRangeRiskSizer(new Calculator()).SizeAsync("TEST", Qualification(), new(), Account(limit: .60m) with { ExistingLongVolume = .10m, ExistingShortVolume = .20m });
        Assert.Equal(.08m, r.Lots);
    }

    [Theory]
    [InlineData(299, GridCycleDiagnostics.RiskBelowMinimumVolume)]
    [InlineData(0, GridCycleDiagnostics.RiskCannotBeSafelySized)]
    public async Task Risk_MinimumOverRiskAndInvalidEquityFailClosed(decimal equity, GridCycleDiagnostics expected)
    {
        var r = await new GridRangeRiskSizer(new Calculator()).SizeAsync("TEST", Qualification(), new(), Account(equity));
        Assert.Equal(expected, r.Failure); Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task Risk_NoAggregateVolumeFailsClosed()
    {
        var r = await new GridRangeRiskSizer(new Calculator()).SizeAsync("TEST", Qualification(), new(), Account(limit: .049m));
        Assert.Equal(GridCycleDiagnostics.RiskCannotBeSafelySized, r.Failure);
    }

    [Theory]
    [InlineData(0.00001, 0.00003, 0.33331)]
    [InlineData(0.01, 0.02, 0.33)]
    public async Task Risk_ExactDecimalMinimumOffsetSteps(decimal min, decimal step, decimal expected)
    {
        var r = await new GridRangeRiskSizer(new Calculator()).SizeAsync("TEST", Qualification(), new(), Account(min: min, step: step));
        Assert.True(r.IsSuccess); Assert.Equal(expected, r.Lots);
        Assert.Equal(0m, (r.Lots - min) % step); Assert.True(r.LongRisk <= 100m);
    }

    [Fact]
    public async Task Risk_ExactMinimumBudgetAcceptedWithoutToleranceOvershoot()
    {
        var sizer = new GridRangeRiskSizer(new Calculator());
        Assert.True((await sizer.SizeAsync("TEST", Qualification(), new(), Account(300m))).IsSuccess);
        Assert.Equal(GridCycleDiagnostics.RiskBelowMinimumVolume, (await sizer.SizeAsync("TEST", Qualification(), new(), Account(299.99999999m))).Failure);
    }

    [Fact]
    public async Task Risk_MarginShortageRejectsAllFiveWithoutShrinking()
    {
        var calc = new Calculator { MarginPerLot = 100000m };
        var e = new GridRangeEngine("TEST", new(calc));
        var r = await e.TryCreateAsync(Candles(), Epoch.AddMinutes(50), new(), Account());
        Assert.Equal(GridCycleDiagnostics.InsufficientMargin, r.Failure); Assert.Null(e.Cycle);
        Assert.Equal(10, calc.Margin.Count); Assert.All(calc.Margin, m => Assert.Equal(.33m, m.VolumeLots));
    }

    [Fact]
    public async Task Risk_FreeMarginAndAsymmetricSidesAreRespected()
    {
        var calc = new Calculator { ShortMultiplier = 2m };
        var sizer = new GridRangeRiskSizer(calc);
        var r = await sizer.SizeAsync("TEST", Qualification(), new(), Account());
        Assert.Equal(.16m, r.Lots); Assert.Equal(96m, r.ShortRisk);
        Assert.Equal(GridCycleDiagnostics.InsufficientMargin, (await sizer.SizeAsync("TEST", Qualification(), new(), Account() with { FreeMargin = 79m })).Failure);
    }

    [Fact]
    public async Task Risk_ChangedNativeRiskFailsRevalidation()
    {
        var r = await new GridRangeRiskSizer(new Calculator { Nonlinear = true }).SizeAsync("TEST", Qualification(), new(), Account());
        Assert.Equal(GridCycleDiagnostics.RiskCannotBeSafelySized, r.Failure); Assert.False(r.IsSuccess);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Risk_CalculatorFailuresAttributableWithoutLocalRetry(bool profit)
    {
        var calc = new Calculator { ThrowProfit = profit, ThrowMargin = !profit };
        var r = await new GridRangeRiskSizer(calc).SizeAsync("TEST", Qualification(), new(), Account());
        Assert.Equal(profit ? GridCycleDiagnostics.RiskCalculationUnavailable : GridCycleDiagnostics.MarginCalculationUnavailable, r.Failure);
        Assert.Equal(profit ? "CalculateProfit" : "CalculateMargin", r.Diagnostic!.Operation);
        Assert.Equal("TEST", r.Diagnostic.Symbol);
        Assert.Equal(1, profit ? calc.Profit.Count : calc.Margin.Count);
    }

    [Fact]
    public async Task Risk_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GridRangeRiskSizer(new Calculator()).SizeAsync("TEST", Qualification(), new(), Account(), cts.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Risk_NonPositiveAuthorityValuesFailClosed(bool profit)
    {
        var calc = new Calculator { ZeroProfit = profit, MarginPerLot = profit ? 100m : 0m };
        var r = await new GridRangeRiskSizer(calc).SizeAsync("TEST", Qualification(), new(), Account());
        Assert.Equal(profit ? GridCycleDiagnostics.RiskCalculationUnavailable : GridCycleDiagnostics.MarginCalculationUnavailable, r.Failure);
    }

    [Fact]
    public async Task Risk_CommissionIsSeparateFromPriceRisk()
    {
        var e = await Engine();
        Assert.Equal(100m, e.Cycle!.Sizing.TargetRiskAmount);
        // Domain sizing exposes only entry-to-stop native loss. Later net accounting
        // subtracts costs separately; there is deliberately no commission input.
        Assert.Equal(99m, e.Cycle.Sizing.LongRisk);
        Assert.DoesNotContain(typeof(GridRiskAccount).GetProperties(), p => p.Name.Contains("Commission"));
    }

    private sealed class Calculator : IMt5TradeCalculator
    {
        public List<Mt5CalculateProfitRequest> Profit { get; } = [];
        public List<Mt5CalculateMarginRequest> Margin { get; } = [];
        public decimal MarginPerLot { get; init; } = 100m;
        public decimal ShortMultiplier { get; init; } = 1m;
        public bool Nonlinear { get; init; }
        public bool ThrowProfit { get; init; }
        public bool ThrowMargin { get; init; }
        public bool ZeroProfit { get; init; }
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Profit.Add(r); if (ThrowProfit) throw new InvalidOperationException("Test profit unavailable");
            var loss = Math.Abs(r.OpenPrice - r.ClosePrice) * r.VolumeLots * 10m * (r.Direction == "Short" ? ShortMultiplier : 1m);
            if (ZeroProfit) loss = 0m;
            if (Nonlinear && r.VolumeLots > .01m) loss *= 2m;
            return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice, -loss, "USD"));
        }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            Margin.Add(r); if (ThrowMargin) throw new InvalidOperationException("Test margin unavailable");
            return Task.FromResult(new Mt5MarginCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.VolumeLots * MarginPerLot, "USD"));
        }
    }
}
