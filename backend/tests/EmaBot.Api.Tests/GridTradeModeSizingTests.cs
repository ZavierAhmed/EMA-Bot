using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Tests;

public sealed class GridTradeModeSizingTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    private static Mt5HistoricalExecutionBar Bar(int i, decimal low = 98m, decimal high = 102m, decimal close = 100m)
        => new("TESTm", "3m", Epoch.AddMinutes(3 * i), Epoch.AddMinutes(3 * (i + 1)).AddMilliseconds(-1), close, high, low, close, 100, 0, true);
    private static InstrumentCatalogItem Instrument(InstrumentTradeMode mode)
        => new(new("Exness", "TESTm", "TEST", AssetClass.Forex, 2, .01m, 100000m, .01m, 10m, .01m,
            "EUR", "USD", "USD", HistoricalChartMode: HistoricalChartMode.Bid), null, null, true, true, mode);
    private static Task<GridHistoricalBacktestResult> Run(InstrumentTradeMode mode, Calculator calculator, decimal balance = 1000m, bool fill = true)
    {
        var bars = Enumerable.Range(0, 50).Select(i => Bar(i)).ToList();
        if (fill) bars.Add(Bar(50, 97.8m, 102.2m)); // both touched, only eligible side may fill
        return new GridHistoricalBacktestEngine(calculator).RunAsync(bars,
            new("TESTm", "3m", balance, "USD", 2m), Instrument(mode));
    }
    private static string Allowed(InstrumentTradeMode mode) => mode == InstrumentTradeMode.LongOnly ? "Long" : "Short";

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly, "Profit")]
    [InlineData(InstrumentTradeMode.LongOnly, "Margin")]
    [InlineData(InstrumentTradeMode.ShortOnly, "Profit")]
    [InlineData(InstrumentTradeMode.ShortOnly, "Margin")]
    public async Task ProhibitedCalculatorFailureIsNeverCalledAndCannotReject(InstrumentTradeMode mode, string operation)
    {
        var allowed = Allowed(mode);
        var calc = new Calculator { ExpensiveSide = allowed == "Long" ? "Short" : "Long", ThrowOperation = operation };
        var r = await Run(mode, calc); var b = Assert.Single(r.Baskets);
        Assert.Equal(allowed, b.Direction.ToString()); Assert.Single(b.Legs);
        Assert.Equal(.03m, b.Cycle.Sizing.Lots); Assert.Equal(0, r.Diagnostics.AmbiguousFirstSideCount);
        Assert.All(calc.Profit, p => Assert.Equal(allowed, p.Direction));
        Assert.All(calc.Margin, m => Assert.Equal(allowed, m.Direction));
        Assert.Equal(11, calc.Profit.Count); Assert.Equal(6, calc.Margin.Count); Assert.Equal(17, r.EconomicsCallCount);
        var sizing = b.Cycle.Sizing;
        Assert.Equal(9m, allowed == "Long" ? sizing.LongRisk : sizing.ShortRisk);
        Assert.Equal(15m, allowed == "Long" ? sizing.LongMargin : sizing.ShortMargin);
        Assert.Null(allowed == "Long" ? sizing.ShortRisk : sizing.LongRisk);
        Assert.Null(allowed == "Long" ? sizing.ShortMargin : sizing.LongMargin);
        Assert.All(b.Cycle.PlannedLevels.Where(l => !l.Allowed), l => { Assert.Null(l.InitialStopRisk); Assert.Null(l.RequiredMargin); });
        Assert.All(b.Cycle.PlannedLevels.Where(l => l.Allowed), l => { Assert.NotNull(l.InitialStopRisk); Assert.NotNull(l.RequiredMargin); });
    }

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly)] [InlineData(InstrumentTradeMode.ShortOnly)]
    public async Task ProhibitedRiskCannotShrinkLotsButFullStillUsesMaximumRisk(InstrumentTradeMode mode)
    {
        var expensive = Allowed(mode) == "Long" ? "Short" : "Long";
        var single = await Run(mode, new() { ExpensiveSide = expensive, RiskMultiplier = 2m }, fill: false);
        var fullCalc = new Calculator { ExpensiveSide = expensive, RiskMultiplier = 2m };
        var full = await Run(InstrumentTradeMode.Full, fullCalc, fill: false);
        Assert.Equal(.03m, Assert.Single(single.Cycles).Sizing.Lots);
        var both = Assert.Single(full.Cycles).Sizing;
        Assert.Equal(.01m, both.Lots);
        Assert.True(both.LongRisk <= 10m); Assert.True(both.ShortRisk <= 10m);
        Assert.Equal(30, full.EconomicsCallCount);
        Assert.Equal(10, fullCalc.Profit.Count(p => p.Direction == "Long"));
        Assert.Equal(10, fullCalc.Profit.Count(p => p.Direction == "Short"));
        Assert.Equal(5, fullCalc.Margin.Count(p => p.Direction == "Long"));
        Assert.Equal(5, fullCalc.Margin.Count(p => p.Direction == "Short"));
    }

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly)] [InlineData(InstrumentTradeMode.ShortOnly)]
    public async Task ProhibitedMarginCannotRejectButFullStillPreflightsBoth(InstrumentTradeMode mode)
    {
        var expensive = Allowed(mode) == "Long" ? "Short" : "Long";
        var single = await Run(mode, new() { ExpensiveSide = expensive, MarginMultiplier = 1000m }, fill: false);
        Assert.Single(single.Cycles); Assert.Equal(0, single.Diagnostics.InsufficientMarginCount);
        var full = await Run(InstrumentTradeMode.Full, new() { ExpensiveSide = expensive, MarginMultiplier = 1000m }, fill: false);
        Assert.Empty(full.Cycles); Assert.Equal(1, full.Diagnostics.InsufficientMarginCount);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly)] [InlineData(InstrumentTradeMode.ShortOnly)]
    public async Task MinimumRiskDependsOnlyOnAllowedSide(InstrumentTradeMode mode)
    {
        var allowed = Allowed(mode); var blocked = allowed == "Long" ? "Short" : "Long";
        var single = await Run(mode, new() { ExpensiveSide = blocked, RiskMultiplier = 100m }, balance: 300m, fill: false);
        Assert.Equal(.01m, Assert.Single(single.Cycles).Sizing.Lots);
        Assert.Equal(0, single.Diagnostics.RiskBelowMinimumVolumeCount);
        var full = await Run(InstrumentTradeMode.Full, new() { ExpensiveSide = blocked, RiskMultiplier = 100m }, balance: 300m, fill: false);
        Assert.Equal(1, full.Diagnostics.RiskBelowMinimumVolumeCount);
        var allowedOverRisk = await Run(mode, new() { ExpensiveSide = allowed, RiskMultiplier = 2m }, balance: 300m, fill: false);
        Assert.Equal(1, allowedOverRisk.Diagnostics.RiskBelowMinimumVolumeCount); Assert.Empty(allowedOverRisk.Cycles);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.LongOnly, "Profit")]
    [InlineData(InstrumentTradeMode.ShortOnly, "Margin")]
    public async Task AllowedSideFailureStillFailsClosed(InstrumentTradeMode mode, string operation)
    {
        var r = await Run(mode, new() { ExpensiveSide = Allowed(mode), ThrowOperation = operation }, fill: false);
        Assert.Empty(r.Cycles);
        Assert.Equal(1, operation == "Profit" ? r.Diagnostics.RiskCalculationUnavailableCount : r.Diagnostics.MarginCalculationUnavailableCount);
    }

    [Theory]
    [InlineData(InstrumentTradeMode.Disabled)] [InlineData(InstrumentTradeMode.CloseOnly)] [InlineData(InstrumentTradeMode.Unknown)]
    public async Task BlockedModesCallNeitherAuthority(InstrumentTradeMode mode)
    {
        var calc = new Calculator(); var r = await Run(mode, calc);
        Assert.Empty(r.Cycles); Assert.Empty(r.Baskets); Assert.Empty(calc.Profit); Assert.Empty(calc.Margin);
        Assert.Equal(0, r.EconomicsCallCount);
    }

    [Theory]
    [InlineData(GridAllowedDirections.Long)] [InlineData(GridAllowedDirections.Short)]
    public async Task PureDomainPolicyCancelsProhibitedLevelsAndIgnoresTheirExposure(GridAllowedDirections policy)
    {
        var calc = new Calculator(); var engine = new GridRangeEngine("TESTm", new(calc));
        var account = new GridRiskAccount(1000m, 1000m, .01m, 10m, .01m, .20m,
            ExistingLongVolume: policy == GridAllowedDirections.Short ? .20m : 0m,
            ExistingShortVolume: policy == GridAllowedDirections.Long ? .20m : 0m, AllowedDirections: policy);
        var candles = Enumerable.Range(0, 50).Select(i => Bar(i).ToCandle()).ToArray();
        var result = await engine.TryCreateAsync(candles, candles[^1].CloseTimeUtc, new(), account);
        Assert.Null(result.Failure); Assert.Equal(.03m, result.Cycle!.Sizing.Lots);
        Assert.All(result.Cycle.Levels.Where(l => !result.Cycle.Sizing.Allows(l.Direction)), l => Assert.Equal(GridLevelStatus.Canceled, l.Status));
        var fills = engine.ProcessBar(new(Bar(50, 97.8m, 102.2m).ToCandle(), 0, .01m));
        Assert.Single(fills.NewFills); Assert.Null(fills.Diagnostic);
        Assert.Equal(policy.ToString(), fills.NewFills[0].Direction.ToString());
    }

    [Theory]
    [InlineData(GridAllowedDirections.None)] [InlineData((GridAllowedDirections)4)]
    public async Task EmptyOrInvalidDomainPolicyFailsWithoutCalls(GridAllowedDirections policy)
    {
        var calc = new Calculator();
        var q = GridRangeIndicators.Evaluate(new(Epoch, 102m, 98m, 100m, 4m, 0m, 50));
        var r = await new GridRangeRiskSizer(calc).SizeAsync("TESTm", q, new(), new(1000m, 1000m, .01m, 10m, .01m, AllowedDirections: policy));
        Assert.Equal(GridCycleDiagnostics.RiskCannotBeSafelySized, r.Failure);
        Assert.Empty(calc.Profit); Assert.Empty(calc.Margin);
    }

    private sealed class Calculator : IMt5TradeCalculator
    {
        public List<Mt5CalculateProfitRequest> Profit { get; } = [];
        public List<Mt5CalculateMarginRequest> Margin { get; } = [];
        public string? ExpensiveSide { get; init; }
        public string? ThrowOperation { get; init; }
        public decimal RiskMultiplier { get; init; } = 1m;
        public decimal MarginMultiplier { get; init; } = 1m;
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Profit.Add(r);
            if (r.Direction == ExpensiveSide && ThrowOperation == "Profit") throw new InvalidOperationException("Forced directional profit failure");
            var value = (r.ClosePrice - r.OpenPrice) * r.VolumeLots * 10m * (r.Direction == "Long" ? 1m : -1m)
                * (r.Direction == ExpensiveSide ? RiskMultiplier : 1m);
            return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice, value, "USD"));
        }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            Margin.Add(r);
            if (r.Direction == ExpensiveSide && ThrowOperation == "Margin") throw new InvalidOperationException("Forced directional margin failure");
            return Task.FromResult(new Mt5MarginCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice,
                r.VolumeLots * 100m * (r.Direction == ExpensiveSide ? MarginMultiplier : 1m), "USD"));
        }
    }
}
