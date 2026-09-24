using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Tests;

public sealed class GridResearchProfileTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static GridHistoricalStrategyProfile Profile(int levels) => levels == 4 ? GridHistoricalStrategyProfile.Research4Level : GridHistoricalStrategyProfile.Baseline5Level;
    private static GridRangeIndicatorSnapshot Snapshot => new(Epoch, 102m, 98m, 100m, 4m, 0m, 50);
    private static GridRiskAccount Account(GridAllowedDirections directions = GridAllowedDirections.Both) => new(1000m, 1000m, .01m, 10m, .01m, AllowedDirections: directions);
    private static GridHistoricalBar Bar(int n, decimal low, decimal high, int spread = 0) => new(new(Epoch.AddMinutes(n), Epoch.AddMinutes(n + 1), (low + high) / 2, high, low, (low + high) / 2, 1m, true), spread, .1m);
    private static async Task<GridRangeEngine> Engine(int levels, GridAllowedDirections directions = GridAllowedDirections.Both)
    {
        var e = new GridRangeEngine("TESTm", new(new Native()));
        Assert.Null((await e.TryCreateAsync(Snapshot, Profile(levels).Settings, Account(directions), default)).Failure);
        return e;
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public void ExplicitProfilesAreFrozenAndDefaultsRemainBaseline(int levels)
    {
        var p = Profile(levels);
        Assert.Same(p, GridHistoricalStrategyProfile.Resolve(p.StrategyId));
        Assert.Equal(levels == 4 ? "GRID_RANGE_4L_RESEARCH_V1" : "GRID_RANGE_V1", p.StrategyId);
        Assert.Equal(new(levels, 1m, 3), p.Settings);
        Assert.Equal(levels + 1, p.Settings.EmergencyStopDistanceLevels);
        Assert.Equal(5, new GridRangeSettings().LevelCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(6)] [InlineData(100)]
    public void OtherLevelCountsAreInvalid(int levels) => Assert.False(new GridRangeSettings(levels).IsValid);

    [Theory]
    [InlineData("GRID_RANGE_3L_RESEARCH_V1")] [InlineData("grid_range_v1")] [InlineData("")]
    public void UnknownProfileIsRejected(string id) => Assert.Throws<ArgumentException>(() => GridHistoricalStrategyProfile.Resolve(id));

    [Theory]
    [InlineData(4, GridAllowedDirections.Both)] [InlineData(4, GridAllowedDirections.Long)] [InlineData(4, GridAllowedDirections.Short)]
    [InlineData(5, GridAllowedDirections.Both)] [InlineData(5, GridAllowedDirections.Long)] [InlineData(5, GridAllowedDirections.Short)]
    public async Task ExactLevelsStopsAndNativeWholeBasketEconomics(int levels, GridAllowedDirections directions)
    {
        var native = new Native(); var settings = Profile(levels).Settings;
        var e = new GridRangeEngine("TESTm", new(native));
        var cycle = (await e.TryCreateAsync(Snapshot, settings, Account(directions), default)).Cycle!;
        Assert.Equal(levels * 2, cycle.Levels.Count); Assert.Equal(100m, cycle.TakeProfit);
        Assert.Equal(100m - (levels + 1) * 2, cycle.LongStop); Assert.Equal(100m + (levels + 1) * 2, cycle.ShortStop);
        Assert.Equal(10m, cycle.Sizing.TargetRiskAmount);
        var lots = levels == 4 ? .05m : .03m;
        Assert.Equal(lots, cycle.Sizing.Lots); Assert.All(cycle.Levels, l => Assert.Equal(lots, l.Lots));
        foreach (var side in Enum.GetValues<GridBasketDirection>())
        {
            var allowed = cycle.Sizing.Allows(side); var sign = side == GridBasketDirection.Long ? -1 : 1;
            var entries = cycle.Levels.Where(l => l.Direction == side).ToArray();
            Assert.Equal(Enumerable.Range(1, levels).Select(n => 100m + sign * n * 2m), entries.Select(l => l.Price));
            Assert.All(entries, l => Assert.Equal(allowed ? GridLevelStatus.Candidate : GridLevelStatus.Canceled, l.Status));
            var profits = native.Profits.Where(r => r.Direction == side.ToString()).ToArray();
            var margins = native.Margins.Where(r => r.Direction == side.ToString()).ToArray();
            Assert.Equal(allowed ? 2 * levels : 0, profits.Length); Assert.Equal(allowed ? levels : 0, margins.Length);
            if (allowed)
            {
                Assert.All(profits, r => Assert.Equal(100m + sign * (levels + 1) * 2, r.ClosePrice));
                var risk = profits.TakeLast(levels).Sum(r => Math.Abs(r.ClosePrice - r.OpenPrice) * r.VolumeLots * 10m);
                Assert.True(risk <= 10m); Assert.Equal(levels == 4 ? 10m : 9m, risk);
                Assert.Equal(risk, side == GridBasketDirection.Long ? cycle.Sizing.LongRisk : cycle.Sizing.ShortRisk);
                Assert.Equal(levels * lots * 100m, side == GridBasketDirection.Long ? cycle.Sizing.LongMargin : cycle.Sizing.ShortMargin);
            }
            else
            {
                Assert.Null(side == GridBasketDirection.Long ? cycle.Sizing.LongRisk : cycle.Sizing.ShortRisk);
                Assert.Null(side == GridBasketDirection.Long ? cycle.Sizing.LongMargin : cycle.Sizing.ShortMargin);
            }
        }
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task VolumeLimitAndDownwardNormalizationUseActualLevelCount(int levels)
    {
        var settings = Profile(levels).Settings;
        var sized = await new GridRangeRiskSizer(new Native()).SizeAsync("TESTm", new(Snapshot, null), settings,
            Account() with { VolumeLimit = .13m, ExistingLongVolume = .01m });
        Assert.True(sized.IsSuccess);
        Assert.Equal(levels == 4 ? .03m : .02m, sized.Lots);
        Assert.True(sized.Lots * levels + .01m <= .13m);
        Assert.True((sized.Lots + .01m) * levels + .01m > .13m);
        var riskLimited = await new GridRangeRiskSizer(new Native()).SizeAsync("TESTm", new(Snapshot, null), settings, Account() with { Equity = 999m });
        Assert.Equal(levels == 4 ? .04m : .03m, riskLimited.Lots);
        Assert.True(riskLimited.LongRisk <= 9.99m);
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task MinimumVolumeOverRiskAndInsufficientMarginFailClosed(int levels)
    {
        var sizer = new GridRangeRiskSizer(new Native()); var settings = Profile(levels).Settings;
        var risk = await sizer.SizeAsync("TESTm", new(Snapshot, null), settings, Account() with { Equity = 100m });
        Assert.Equal(GridCycleDiagnostics.RiskBelowMinimumVolume, risk.Failure);
        var margin = await sizer.SizeAsync("TESTm", new(Snapshot, null), settings, Account() with { FreeMargin = 1m });
        Assert.Equal(GridCycleDiagnostics.InsufficientMargin, margin.Failure);
    }

    [Theory]
    [InlineData(4, true)] [InlineData(4, false)] [InlineData(5, true)] [InlineData(5, false)]
    public async Task SharedFillsRemainOrderedDirectionLockedAndStopFirst(int levels, bool isLong)
    {
        var e = await Engine(levels); var cycle = e.Cycle!;
        var first = e.ProcessBar(isLong ? Bar(0, 98m, 100m) : Bar(0, 100m, 102m));
        Assert.Single(first.NewFills); Assert.Null(first.ExitReason); // First-fill TP defer.
        Assert.Equal(isLong ? GridBasketDirection.Long : GridBasketDirection.Short, cycle.Direction);
        var exit = e.ProcessBar(Bar(1, 80m, 120m));
        Assert.Equal(Enumerable.Range(2, levels - 1), exit.NewFills.Select(l => l.Number));
        Assert.All(exit.NewFills, l => Assert.Equal(cycle.Direction, l.Direction));
        Assert.Equal(GridExitReason.EmergencyStop, exit.ExitReason);
        Assert.Equal(isLong ? cycle.LongStop : cycle.ShortStop, cycle.ExitPrice);
        Assert.Equal(levels, cycle.Levels.Count(l => l.Status == GridLevelStatus.Closed));
        Assert.DoesNotContain(cycle.Levels, l => l.Number > levels);
        Assert.All(exit.NewFills, l => Assert.Equal(100m + (isLong ? -1 : 1) * l.Number * 2m, l.Price));
        Assert.Equal(3, e.CooldownRemaining);
        for (var n = 2; n <= 4; n++)
        {
            e.ProcessBar(Bar(n, 98m, 102m));
            Assert.Equal(GridCycleDiagnostics.Cooldown, (await e.TryCreateAsync(Snapshot with { Time = Epoch.AddMinutes(n + 1) }, Profile(levels).Settings, Account(), default)).Failure);
        }
        Assert.Null((await e.TryCreateAsync(Snapshot with { Time = Epoch.AddMinutes(6) }, Profile(levels).Settings, Account(), default)).Failure);
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task AmbiguousFirstSideAndAskTouchRemainConservative(int levels)
    {
        var e = await Engine(levels);
        Assert.Equal(GridCycleDiagnostics.AmbiguousFirstSide, e.ProcessBar(Bar(0, 97m, 103m)).Diagnostic);
        Assert.Null(e.Cycle!.Direction);
        Assert.Empty(e.ProcessBar(Bar(1, 98m, 99m, spread: 2)).NewFills);
        Assert.Single(e.ProcessBar(Bar(2, 97.8m, 99m, spread: 2)).NewFills);
        Assert.Equal(98m, e.Cycle.Levels.Single(l => l.Status == GridLevelStatus.Filled).Price);
    }

    [Fact]
    public void PositiveStopGuardUsesSelectedProfileWithoutChangingBaseline()
    {
        var snapshot = Snapshot with { RangeHigh = 12m, RangeLow = 10m, Close = 11m };
        Assert.True(GridRangeIndicators.Evaluate(snapshot, Profile(4).Settings).IsQualified);
        Assert.Equal(GridCycleDiagnostics.InvalidAnchorOrSpacing, GridRangeIndicators.Evaluate(snapshot).Failure);
    }

    [Theory]
    [InlineData(4, InstrumentTradeMode.Full)] [InlineData(4, InstrumentTradeMode.LongOnly)] [InlineData(4, InstrumentTradeMode.ShortOnly)]
    [InlineData(5, InstrumentTradeMode.Full)] [InlineData(5, InstrumentTradeMode.LongOnly)] [InlineData(5, InstrumentTradeMode.ShortOnly)]
    public async Task HistoricalEndOfDataPreservesProfileAndActualLegCount(int levels, InstrumentTradeMode mode)
    {
        var p = Profile(levels); var native = new Native();
        Mt5HistoricalExecutionBar Convert(int n, decimal low, decimal high) { var b = Bar(n, low, high).Bid; return new("TESTm", "3m", b.OpenTimeUtc, b.CloseTimeUtc, b.Open, b.High, b.Low, b.Close, 1, 0, true); }
        var bars = Enumerable.Range(0, 50).Select(n => Convert(n, 98m, 102m)).ToList();
        bars.Add(mode == InstrumentTradeMode.ShortOnly ? Convert(50, 101m, 100m + 2m * levels) : Convert(50, 100m - 2m * levels, 99m));
        var instrument = new InstrumentCatalogItem(new("Exness", "TESTm", "TESTm", AssetClass.Forex, 2, .1m, 100000m, .01m, 10m, .01m, "EUR", "USD", "USD", HistoricalChartMode: HistoricalChartMode.Bid), null, null, true, true, mode);
        var result = await new GridHistoricalBacktestEngine(native).RunAsync(bars, new("TESTm", "3m", 1000m, "USD", 2m, StrategyId: p.StrategyId), instrument);
        Assert.Equal(p.StrategyId, result.StrategyId); Assert.Equal(p.Settings, result.Request.Settings);
        var basket = Assert.Single(result.Baskets); Assert.Equal(GridHistoricalExitReason.EndOfData, basket.ExitReason);
        Assert.Equal(levels, basket.Legs.Count); Assert.Equal(levels * 2, basket.Cycle.PlannedLevels.Count);
        Assert.Equal(levels * basket.Legs[0].Lots * 4m, basket.Commission);
        if (mode != InstrumentTradeMode.Full)
        {
            var allowed = mode == InstrumentTradeMode.LongOnly ? "Long" : "Short";
            Assert.All(native.Profits, r => Assert.Equal(allowed, r.Direction)); Assert.All(native.Margins, r => Assert.Equal(allowed, r.Direction));
        }
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task HistoricalIdentityCannotBeMislabeledOrSettingsOverridden(int levels)
    {
        var p = Profile(levels);
        var instrument = new InstrumentCatalogItem(new("Exness", "TESTm", "TESTm", AssetClass.Forex, 2, .1m, 100000m, .01m, 10m, .01m, "EUR", "USD", "USD", HistoricalChartMode: HistoricalChartMode.Bid), null, null, true, true, InstrumentTradeMode.Full);
        foreach (var settings in new[] { new GridRangeSettings(levels == 4 ? 5 : 4), p.Settings with { GridBasketRiskPercent = 2m }, p.Settings with { CooldownBars = 0 } })
        {
            var ex = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => new GridHistoricalBacktestEngine(new Native()).RunAsync([], new("TESTm", "3m", 1000m, "USD", 0m, settings, StrategyId: p.StrategyId), instrument));
            Assert.Equal("InvalidSettings", ex.Diagnostic.Code);
        }
    }

    private sealed class Native : IMt5TradeCalculator
    {
        public List<Mt5CalculateProfitRequest> Profits { get; } = [];
        public List<Mt5CalculateMarginRequest> Margins { get; } = [];
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Profits.Add(r);
            return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice,
                (r.ClosePrice - r.OpenPrice) * r.VolumeLots * 10m * (r.Direction == "Long" ? 1 : -1), "USD"));
        }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            Margins.Add(r);
            return Task.FromResult(new Mt5MarginCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.VolumeLots * 100m, "USD"));
        }
    }
}
