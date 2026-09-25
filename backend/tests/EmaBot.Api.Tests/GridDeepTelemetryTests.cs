using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Tests;

public sealed class GridDeepTelemetryTests
{
    [Theory]
    [InlineData(5, true, "TakeProfit")] [InlineData(5, false, "TakeProfit")]
    [InlineData(4, true, "TakeProfit")] [InlineData(4, false, "TakeProfit")]
    [InlineData(5, true, "EmergencyStop")] [InlineData(5, false, "EmergencyStop")]
    [InlineData(4, true, "EmergencyStop")] [InlineData(4, false, "EmergencyStop")]
    [InlineData(5, true, "EndOfData")] [InlineData(5, false, "EndOfData")]
    [InlineData(4, true, "EndOfData")] [InlineData(4, false, "EndOfData")]
    public async Task TradingOutputsMatchPreTelemetryGoldenEvidence(int levels, bool isLong, string exit)
    {
        var native = new TelemetryFixture.Native();
        var result = await TelemetryFixture.Run(levels, isLong, exit, native);
        var basket = Assert.Single(result.Baskets);
        Assert.Equal(levels, basket.Legs.Count); Assert.Equal(exit, basket.ExitReason.ToString());
        // Captures every trading output, timestamps, prices, lots, risk/margin,
        // native requests, P/L, balances, drawdown and diagnostics; excludes wall time.
        var evidence = JsonSerializer.Serialize(new { result.Request, result.Instrument, result.ActualStartUtc, result.ActualEndUtc,
            result.CandleCount, result.WarmupCandleCount, result.EndingBalance, result.MaxDrawdown,
            result.Baskets, result.Cycles, result.Diagnostics, result.Events, result.EconomicsCallCount,
            native.Profits, native.Margins });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        // Captured against commit 370af8a before adding telemetry.
        var expected = (levels, isLong, exit) switch
        {
            (4, true, "EmergencyStop") => "26D72B3BED1BE471E547218A71ECFF50859271C87AE4817E73F18FD084C48F86",
            (4, true, "TakeProfit") => "513823C79C451B31CFD809A7095DF0B3DE9249F390AD137A480D506768EBF83E",
            (4, false, "TakeProfit") => "0EC5140701070769FCC58165A686C07BF86474017125099F8E02A9481C3F9AA9",
            (5, false, "EndOfData") => "A75329A40977770D2110F7EA7FC873EC2894E2E045079A12C4F90C86AD87F933",
            (5, true, "EmergencyStop") => "51C8968FD0F42E53572FE62AF917CE47CB13A218B9221DD7B0C4C7738FCBA65C",
            (4, false, "EndOfData") => "C7DE34F91584111CE11E36454B47507514DFF8EC883FBA563F27B14FD7E6C5D3",
            (5, false, "EmergencyStop") => "ABFD71E7319DF3B1560ECB74C8DD40C0CF90481DB0A5DAE57CB4C0838976BA26",
            (5, true, "EndOfData") => "388AEF3776369854F5F9F8164AA3CDBC8830D56BE8C10F0D066B38D3D690C8DC",
            (5, false, "TakeProfit") => "6DED5D392BE60C931FB870D34A788177C00CD247A7E7D6C5653CB9101171B1EC",
            (4, false, "EmergencyStop") => "1DDEFC60EDDE80A4324C940232D34AE76E0E74C9030D9EDCAAC14C383FF81F55",
            (5, true, "TakeProfit") => "796EE407A704870D29B856EAA90A307CA4255199128AB2D2714562320C69A503",
            (4, true, "EndOfData") => "F1C62EA872BAC834C65CBCC902AA56F2F8E2F3726BED623247EFC47874B963B7",
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected, hash);
    }
    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task NewAcceptedCycleRestartsTelemetrySequenceAndCounters(int levels)
    {
        var bars = TelemetryFixture.Bars(levels, true, "TakeProfit");
        bars.AddRange(Enumerable.Range(56, 100).Select(n => TelemetryFixture.Bar(n, n % 2 == 0 ? 97.5m : 98.5m, n % 2 == 0 ? 101.5m : 102.5m)));
        bars.Add(TelemetryFixture.Bar(156, 97m, 99m, 98m));
        var result = await new GridHistoricalBacktestEngine(new TelemetryFixture.Native()).RunAsync(bars,
            TelemetryFixture.Request(levels) with { RequestedEndUtc = bars[^1].CloseTimeUtc }, TelemetryFixture.Instrument);
        var groups = result.Telemetry.GroupBy(t => t.CycleQualificationTimeUtc).ToArray();
        Assert.Equal(10, groups.Length); // Alternating post-exit bars form repeated recovered cycles.
        Assert.All(groups, group =>
        {
            Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(t => t.Sequence));
            Assert.Equal(0, group.First().ConsecutiveAdverseCloses);
            Assert.Equal(0, group.First().MaxFilledLevelBeforeBar);
        });
        var persisted = GridBacktestPersistence.Map(result, DateTimeOffset.UtcNow);
        Assert.All(persisted.Cycles, c => Assert.Equal(result.Telemetry.Count(t => t.CycleQualificationTimeUtc == c.QualificationTimeUtc), c.Telemetry.Count));
    }

    [Theory]
    [InlineData(5, true)] [InlineData(5, false)] [InlineData(4, true)] [InlineData(4, false)]
    public async Task EveryActiveCandleHasCorrectTransitionsAndSharedIndicators(int levels, bool isLong)
    {
        var result = await TelemetryFixture.Run(levels, isLong, "TakeProfit");
        Assert.Equal(49, result.WarmupCandleCount);
        Assert.Equal(6, result.Telemetry.Count);
        Assert.Equal(Enumerable.Range(0, 6), result.Telemetry.Select(t => t.Sequence));
        Assert.Equal(new[] { 0, 1, 1, 2, 4, levels }, result.Telemetry.Select(t => t.MaxFilledLevelBeforeBar));
        Assert.Equal(new[] { 1, 1, 2, 4, levels, levels }, result.Telemetry.Select(t => t.MaxFilledLevelAfterBar));
        Assert.Equal(new[] { 1, 0, 1, 2, levels - 4, 0 }, result.Telemetry.Select(t => t.NewFillCount));
        var window = new GridRangeIndicatorWindow();
        var snapshots = TelemetryFixture.Bars(levels, isLong, "TakeProfit").Select(b => window.Append(b.ToCandle())).ToDictionary(i => i.Time);
        Assert.All(result.Telemetry, t =>
        {
            Assert.True(t.TimeUtc > TelemetryFixture.Bar(49).CloseTimeUtc);
            Assert.Equal(result.Cycles[0].Indicators.Time, t.CycleQualificationTimeUtc);
            Assert.Equal(isLong ? "Long" : "Short", t.Direction);
            Assert.Equal(levels, t.DeepestConfiguredLevel);
            Assert.Equal(t.MaxFilledLevelAfterBar >= levels, t.DeepestLevelFilled);
            Assert.Equal(snapshots[t.TimeUtc].Atr, t.CurrentAtr); Assert.Equal(snapshots[t.TimeUtc].Adx, t.CurrentAdx);
            Assert.Equal(snapshots[t.TimeUtc].RangeHigh, t.CurrentRangeHigh); Assert.Equal(snapshots[t.TimeUtc].RangeLow, t.CurrentRangeLow);
            Assert.Equal(2, t.SpreadPoints); Assert.Equal(.2m, t.SpreadPrice);
        });
        Assert.All(result.Telemetry.Take(5), t => Assert.Null(t.ExitReasonThisBar));
        Assert.Equal("TakeProfit", result.Telemetry[^1].ExitReasonThisBar);
    }

    [Theory]
    [InlineData(5, "EmergencyStop")] [InlineData(4, "EmergencyStop")]
    [InlineData(5, "EndOfData")] [InlineData(4, "EndOfData")]
    public async Task ExitEvidenceIncludesRealExitBarAndNeverDuplicatesEndOfData(int levels, string exit)
    {
        var result = await TelemetryFixture.Run(levels, true, exit);
        Assert.Equal(exit == "EndOfData" ? 5 : 6, result.Telemetry.Count);
        Assert.Equal(result.Telemetry.Count, result.Telemetry.Select(t => t.TimeUtc).Distinct().Count());
        Assert.Equal(exit == "EndOfData" ? null : exit, result.Telemetry[^1].ExitReasonThisBar);
        Assert.Equal(result.Baskets[0].ExitTime, result.Telemetry[^1].TimeUtc);
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task NoRowsForWarmupQualificationOrAmbiguousUnlockedBars(int levels)
    {
        var bars = Enumerable.Range(0, 50).Select(n => TelemetryFixture.Bar(n)).ToList();
        bars.Add(TelemetryFixture.Bar(50, 99m, 101m));
        var engine = new GridHistoricalBacktestEngine(new TelemetryFixture.Native());
        var empty = await engine.RunAsync(bars, TelemetryFixture.Request(levels), TelemetryFixture.Instrument);
        Assert.Single(empty.Cycles); Assert.Empty(empty.Baskets); Assert.Empty(empty.Telemetry);
        bars.Add(TelemetryFixture.Bar(51, 97m, 103m));
        var ambiguous = await engine.RunAsync(bars, TelemetryFixture.Request(levels), TelemetryFixture.Instrument);
        Assert.Equal(1, ambiguous.Diagnostics.AmbiguousFirstSideCount); Assert.Empty(ambiguous.Telemetry);
        bars.Add(TelemetryFixture.Bar(52, 97.8m, 99m, 98m));
        var firstFill = await engine.RunAsync(bars, TelemetryFixture.Request(levels), TelemetryFixture.Instrument);
        Assert.Equal(bars[^1].CloseTimeUtc, Assert.Single(firstFill.Telemetry).TimeUtc);
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task ClosedBasketsDoNotGenerateCooldownTelemetry(int levels)
    {
        var bars = TelemetryFixture.Bars(levels, true, "TakeProfit");
        bars.AddRange(Enumerable.Range(56, 3).Select(n => TelemetryFixture.Bar(n)));
        var result = await new GridHistoricalBacktestEngine(new TelemetryFixture.Native()).RunAsync(bars, TelemetryFixture.Request(levels), TelemetryFixture.Instrument);
        Assert.Equal(6, result.Telemetry.Count);
        Assert.All(result.Telemetry, t => Assert.True(t.TimeUtc <= result.Baskets[0].ExitTime));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task BidStructureDistancesImpulseAndIndicatorChangesUseFrozenBoundary(bool isLong)
    {
        var cycle = (await TelemetryFixture.Run(5, isLong, "TakeProfit")).Cycles[0];
        var observer = new GridDeepTelemetryObserver(); observer.StartCycle(cycle, 5);
        var bar = isLong ? TelemetryFixture.Bar(60, 96m, 100m, 97m, 99m) : TelemetryFixture.Bar(60, 100m, 104m, 103m, 101m);
        var current = cycle.Indicators with { Time = bar.CloseTimeUtc, Atr = 2m, Adx = 25m, RangeHigh = 110m, RangeLow = 90m };
        observer.Observe(bar, current, 10m, isLong ? 101m : 99m, isLong ? GridBasketDirection.Long : GridBasketDirection.Short, 2, 4, 2, null);
        var t = Assert.Single(observer.Rows);
        Assert.Equal(isLong ? 98m : 102m, t.FrozenBoundaryPrice);
        Assert.True(t.CloseBeyondFrozenBoundary); Assert.True(t.AdverseExtremeBeyondFrozenBoundary);
        Assert.Equal(1.5m, t.DistanceFromAnchorSpacings); Assert.Equal(1.5m, t.AdverseDistanceFromAnchorSpacings);
        Assert.Equal(.5m, t.BreakoutDistanceSpacings);
        Assert.Equal(25m, t.AdxDeltaFromQualification); Assert.Equal(.5m, t.AtrRatioToQualification);
        Assert.Equal(2m, t.CandleBody); Assert.Equal(5m, t.CandleTrueRange);
        Assert.Equal(1m, t.CandleBodyAtrRatio); Assert.Equal(2.5m, t.CandleTrueRangeAtrRatio);
        // A huge Ask spread must not change the structural Bid boundary measurements.
        Assert.Equal(20m, t.SpreadPrice);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task CountersResetOnEqualOrFavorableCloseAndNewCycle(bool isLong)
    {
        var cycle = (await TelemetryFixture.Run(4, isLong, "TakeProfit")).Cycles[0];
        var observer = new GridDeepTelemetryObserver(); observer.StartCycle(cycle, 4);
        var direction = isLong ? GridBasketDirection.Long : GridBasketDirection.Short;
        foreach (var close in new[] { 97m, 96m, 96m, 99m, 101m, 97m })
        {
            var price = isLong ? close : 200m - close;
            var bar = TelemetryFixture.Bar(60 + observer.Rows.Count, price - 1, price + 1, price);
            observer.Observe(bar, cycle.Indicators, .1m, price, direction, 1, 1, 0, null);
        }
        Assert.Equal(new[] { 0, 1, 0, 0, 0, 1 }, observer.Rows.Select(t => t.ConsecutiveAdverseCloses));
        Assert.Equal(new[] { 1, 2, 3, 0, 0, 1 }, observer.Rows.Select(t => t.ConsecutiveClosesBeyondFrozenBoundary));
        Assert.Equal(-.5m, observer.Rows[4].AdverseDistanceFromAnchorSpacings);
        Assert.Equal(0m, observer.Rows[4].BreakoutDistanceSpacings);
        observer.StartCycle(cycle with { Indicators = cycle.Indicators with { Time = TelemetryFixture.Bar(70).CloseTimeUtc } }, 4);
        observer.Observe(TelemetryFixture.Bar(71, 95m, 105m, isLong ? 96m : 104m), cycle.Indicators, .1m, 100m, direction, 0, 1, 1, null);
        Assert.Equal(0, observer.Rows[^1].Sequence); Assert.Equal(0, observer.Rows[^1].ConsecutiveAdverseCloses);
        Assert.Equal(1, observer.Rows[^1].ConsecutiveClosesBeyondFrozenBoundary);
        Assert.NotEqual(observer.Rows[0].CycleQualificationTimeUtc, observer.Rows[^1].CycleQualificationTimeUtc);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task BoundaryEqualityIsNotBreakoutAndExtremeCanCrossWithoutClose(bool isLong)
    {
        var cycle = (await TelemetryFixture.Run(4, isLong, "TakeProfit")).Cycles[0];
        var observer = new GridDeepTelemetryObserver(); observer.StartCycle(cycle, 4);
        var boundary = isLong ? 98m : 102m;
        observer.Observe(TelemetryFixture.Bar(60, boundary - 1, boundary + 1, boundary), cycle.Indicators, .1m, 100m,
            isLong ? GridBasketDirection.Long : GridBasketDirection.Short, 1, 1, 0, null);
        var t = Assert.Single(observer.Rows);
        Assert.False(t.CloseBeyondFrozenBoundary); Assert.True(t.AdverseExtremeBeyondFrozenBoundary);
        Assert.Equal(0m, t.BreakoutDistanceSpacings); Assert.Equal(0, t.ConsecutiveClosesBeyondFrozenBoundary);
    }

    [Theory]
    [InlineData(null)] [InlineData(0)] [InlineData(-1)]
    public async Task UnavailableOrInvalidIndicatorDenominatorsStayNull(int? atr)
    {
        var cycle = (await TelemetryFixture.Run(5, true, "TakeProfit")).Cycles[0];
        cycle = cycle with { Indicators = cycle.Indicators with { Atr = atr, Adx = null } };
        var observer = new GridDeepTelemetryObserver(); observer.StartCycle(cycle, 5);
        observer.Observe(TelemetryFixture.Bar(60), cycle.Indicators, .1m, null, GridBasketDirection.Long, 1, 1, 0, null);
        var t = Assert.Single(observer.Rows);
        Assert.Null(t.AtrRatioToQualification); Assert.Null(t.AdxDeltaFromQualification);
        Assert.Null(t.CandleBodyAtrRatio); Assert.Null(t.CandleTrueRangeAtrRatio); Assert.Null(t.CandleTrueRange);
        Assert.Equal(0m, t.CandleBody); // Measured zero, distinct from unavailable.
    }
}

internal static class TelemetryFixture
{
    public static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
    public static Mt5HistoricalExecutionBar Bar(int n, decimal low = 98m, decimal high = 102m, decimal close = 100m, decimal? open = null)
        => new("TESTm", "3m", Epoch.AddMinutes(n * 3), Epoch.AddMinutes((n + 1) * 3).AddMilliseconds(-1), open ?? close, high, low, close, 100, 2, true);
    public static List<Mt5HistoricalExecutionBar> Bars(int levels, bool isLong, string exit)
    {
        var bars = Enumerable.Range(0, 50).Select(n => Bar(n)).ToList();
        bars.Add(isLong ? Bar(50, 97.8m, 99m, 98m, 99m) : Bar(50, 101m, 102m, 102m, 101m));
        bars.Add(isLong ? Bar(51, 97m, 99m, 97.5m) : Bar(51, 101m, 103m, 102.5m));
        bars.Add(isLong ? Bar(52, 95.8m, 98m, 96m) : Bar(52, 102m, 104m, 104m));
        bars.Add(isLong ? Bar(53, 91.8m, 96m, 93m) : Bar(53, 104m, 108m, 107m));
        bars.Add(isLong ? Bar(54, levels == 5 ? 89.8m : 91m, 96m, levels == 5 ? 91m : 93m) : Bar(54, 104m, levels == 5 ? 110m : 108m, levels == 5 ? 109m : 107m));
        if (exit == "TakeProfit") bars.Add(Bar(55, 99m, 101m));
        if (exit == "EmergencyStop") bars.Add(Bar(55, 85m, 115m));
        return bars;
    }
    public static InstrumentCatalogItem Instrument => new(new("Exness", "TESTm", "TEST", AssetClass.Forex, 2, .1m, 100000m, .01m, 100m, .01m,
        "EUR", "USD", "USD", VolumeLimit: 100m, HistoricalChartMode: HistoricalChartMode.Bid), null, null, true, true, InstrumentTradeMode.Full);
    public static GridHistoricalBacktestRequest Request(int levels) => new("TESTm", "3m", 1000m, "USD", 2m,
        RequestedStartUtc: Bar(49).OpenTimeUtc, RequestedEndUtc: Bar(100).CloseTimeUtc,
        StrategyId: levels == 5 ? "GRID_RANGE_V1" : "GRID_RANGE_4L_RESEARCH_V1");
    public static Task<GridHistoricalBacktestResult> Run(int levels, bool isLong, string exit, Native? native = null)
        => new GridHistoricalBacktestEngine(native ?? new()).RunAsync(Bars(levels, isLong, exit), Request(levels), Instrument);
    public sealed class Native : IMt5TradeCalculator
    {
        public List<Mt5CalculateProfitRequest> Profits { get; } = [];
        public List<Mt5CalculateMarginRequest> Margins { get; } = [];
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            Profits.Add(r); return Task.FromResult(new Mt5ProfitCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.ClosePrice,
                (r.ClosePrice - r.OpenPrice) * r.VolumeLots * 10m * (r.Direction == "Long" ? 1 : -1), "USD"));
        }
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            Margins.Add(r); return Task.FromResult(new Mt5MarginCalculationPayload(r.BrokerSymbol, r.Direction, r.VolumeLots, r.OpenPrice, r.VolumeLots * 100m, "USD"));
        }
    }
}
