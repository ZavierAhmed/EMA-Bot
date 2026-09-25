using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy.Grid;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class GridBreakoutGuardProfileTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Id = GridHistoricalStrategyProfile.BreakoutGuardStrategyId;
    private static GridHistoricalBacktestRequest Request => TelemetryFixture.Request(5) with { StrategyId = Id };
    private static Task<GridHistoricalBacktestResult> Run(List<Mt5HistoricalExecutionBar> bars, TelemetryFixture.Native? native = null,
        InstrumentTradeMode mode = InstrumentTradeMode.Full, decimal balance = 1000m, string strategy = Id)
        => new GridHistoricalBacktestEngine(native ?? new()).RunAsync(bars,
            Request with { StrategyId = strategy, StartingBalance = balance, RequestedEndUtc = bars[^1].CloseTimeUtc },
            TelemetryFixture.Instrument with { TradeMode = mode });
    private static List<Mt5HistoricalExecutionBar> Bars(bool isLong)
    {
        var bars = TelemetryFixture.Bars(5, isLong, "EndOfData").Take(53).ToList();
        // Exactly L3 fills after two adverse closes; counter began at the L1 candle.
        bars.Add(isLong ? TelemetryFixture.Bar(53, 93.8m, 96m, 94m) : TelemetryFixture.Bar(53, 104m, 106m, 106m));
        return bars;
    }

    [Fact]
    public void FrozenProfileAndRequestHaveNoEditableGuardInputs()
    {
        var p = GridHistoricalStrategyProfile.Resolve(Id);
        Assert.Same(GridHistoricalStrategyProfile.ResearchBreakoutGuard, p);
        Assert.Equal(new GridRangeSettings(5, 1m, 3), p.Settings);
        Assert.Equal(6, p.Settings.EmergencyStopDistanceLevels);
        Assert.Equal(GridBreakoutGuardRules.Id, p.GuardId);
        Assert.Null(GridHistoricalStrategyProfile.Baseline5Level.GuardId);
        Assert.Null(GridHistoricalStrategyProfile.Research4Level.GuardId);
        Assert.Equal(new[] { "Symbol", "Interval", "StartUtc", "EndUtc", "StrategyId", "StartingBalance" },
            typeof(BacktestRequest).GetProperties().Select(p => p.Name));
    }

    [Theory]
    [InlineData(2, 1.5, 2, false)] [InlineData(3, 1.49, 2, false)]
    [InlineData(3, 1.5, 1, false)] [InlineData(3, 1.5, 2, true)]
    [InlineData(4, 1.6, 3, true)] [InlineData(5, null, 2, false)]
    public void SharedShadowAndRealPredicateParity(int level, double? delta, int adverse, bool expected)
    {
        decimal? value = delta is null ? null : (decimal)delta;
        Assert.Equal(expected, GridBreakoutGuardRules.Matches(level, value, adverse));
        var candidate = GridBreakoutGuardCandidate.Guards.Single(c => c.Id == GridBreakoutGuardRules.Id);
        Assert.Equal(expected, candidate.Matches(new() { MaxFilledLevelAfterBar = level, AdxDeltaFromQualification = value, ConsecutiveAdverseCloses = adverse }));
    }

    [Theory]
    [InlineData(true, InstrumentTradeMode.Full)] [InlineData(false, InstrumentTradeMode.Full)]
    [InlineData(true, InstrumentTradeMode.LongOnly)] [InlineData(false, InstrumentTradeMode.ShortOnly)]
    public async Task SameCandleL3NativeExitPreservesCounterLegsCommissionsAndTelemetry(bool isLong, InstrumentTradeMode mode)
    {
        var bars = Bars(isLong); var native = new TelemetryFixture.Native();
        var result = await Run(bars, native, mode);
        var basket = Assert.Single(result.Baskets);
        Assert.Equal(GridHistoricalExitReason.BreakoutGuard, basket.ExitReason);
        Assert.Equal(3, basket.Legs.Count);
        Assert.Equal(bars[^1].CloseTimeUtc, basket.ExitTime);
        Assert.Equal(bars[^1].Close + (isLong ? 0m : .2m), basket.ExitPrice);
        Assert.Equal(4, result.Telemetry.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Telemetry.Select(t => t.ConsecutiveAdverseCloses));
        Assert.Equal(new[] { 1, 1, 2, 3 }, result.Telemetry.Select(t => t.MaxFilledLevelAfterBar));
        Assert.Equal("BreakoutGuard", result.Telemetry[^1].ExitReasonThisBar);
        Assert.Equal(basket.ExitTime, result.Telemetry[^1].TimeUtc);
        Assert.Equal(4, result.Telemetry.Select(t => t.TimeUtc).Distinct().Count());
        Assert.True(result.Telemetry[^1].AdxDeltaFromQualification >= 1.5m);
        Assert.Equal(basket.ExitTime, basket.Legs[^1].FillTime);
        Assert.All(basket.Legs, l =>
        {
            Assert.Equal(l.Lots * 2m, l.EntryCommission); Assert.Equal(l.Lots * 2m, l.ExitCommission);
            Assert.Contains(native.Profits, r => r.OpenPrice == l.FillPrice && r.VolumeLots == l.Lots && r.ClosePrice == basket.ExitPrice && r.Direction == basket.Direction.ToString());
        });
        Assert.Equal(3, native.Profits.Count(r => r.ClosePrice == basket.ExitPrice));
        Assert.Equal(basket.Legs.Sum(l => l.NetPnl), basket.NetPnl);
        Assert.Equal(1000m + basket.NetPnl, result.EndingBalance);
        Assert.Equal(basket.NetPnl, basket.GrossPnl - basket.Commission);
        var exit = Assert.Single(basket.Events, e => e.Type == GridHistoricalEventType.Exit);
        Assert.Equal("BreakoutGuard", exit.Detail); Assert.Equal(basket.ExitPrice, exit.ExecutablePrice);
    }

    [Theory]
    [InlineData(true, "TakeProfit")] [InlineData(false, "TakeProfit")]
    [InlineData(true, "EmergencyStop")] [InlineData(false, "EmergencyStop")]
    public async Task NormalSameCandleExitWinsEvenWhenGuardEvidenceMatches(bool isLong, string reason)
    {
        var bars = Bars(isLong);
        bars[^1] = reason == "EmergencyStop" ? TelemetryFixture.Bar(53, 85m, 115m, isLong ? 94m : 106m)
            : isLong ? TelemetryFixture.Bar(53, 93.8m, 100m, 94m) : TelemetryFixture.Bar(53, 99m, 106m, 106m);
        var result = await Run(bars);
        Assert.Equal(reason, Assert.Single(result.Baskets).ExitReason.ToString());
        var t = result.Telemetry[^1];
        Assert.True(GridBreakoutGuardRules.Matches(t.MaxFilledLevelAfterBar, t.AdxDeltaFromQualification, t.ConsecutiveAdverseCloses));
        Assert.Equal(reason, t.ExitReasonThisBar);
    }

    [Fact]
    public async Task InternalClosureClosesCancelsAndBlocksExactlyThreeSubsequentBars()
    {
        var e = new GridRangeEngine("TESTm", new(new TelemetryFixture.Native()));
        var snapshot = new GridRangeIndicatorSnapshot(TelemetryFixture.Bar(49).CloseTimeUtc, 102m, 98m, 100m, 4m, 0m, 50);
        var account = new GridRiskAccount(1000m, 1000m, .01m, 100m, .01m);
        Assert.Throws<InvalidOperationException>(() => e.CloseHistoricalResearchBreakoutGuard(snapshot.Time, 94m));
        await e.TryCreateAsync(snapshot, new(), account, default);
        foreach (var bar in Bars(true).Skip(50)) e.ProcessBar(new(bar.ToCandle(), 2, .1m));
        var cycle = e.Cycle!;
        e.CloseHistoricalResearchBreakoutGuard(TelemetryFixture.Bar(53).CloseTimeUtc, 94m);
        Assert.Equal(GridExitReason.BreakoutGuard, cycle.ExitReason);
        Assert.Equal(3, cycle.Levels.Count(l => l.Status == GridLevelStatus.Closed));
        Assert.Equal(7, cycle.Levels.Count(l => l.Status == GridLevelStatus.Canceled));
        Assert.Equal(3, e.CooldownRemaining);
        Assert.Throws<InvalidOperationException>(() => e.CloseHistoricalResearchBreakoutGuard(TelemetryFixture.Bar(53).CloseTimeUtc, 94m));
        Assert.Null((await e.TryCreateAsync(snapshot with { Time = cycle.ExitTime!.Value }, new(), account, default)).Cycle);
        for (var n = 54; n <= 56; n++)
        {
            var bar = TelemetryFixture.Bar(n, 85m, 115m);
            Assert.Empty(e.ProcessBar(new(bar.ToCandle(), 2, .1m)).NewFills);
            Assert.Null((await e.TryCreateAsync(snapshot with { Time = bar.CloseTimeUtc }, new(), account, default)).Cycle);
            Assert.Equal(56 - n, e.CooldownRemaining);
        }
        var next = TelemetryFixture.Bar(57); e.ProcessBar(new(next.ToCandle(), 2, .1m));
        Assert.NotNull((await e.TryCreateAsync(snapshot with { Time = next.CloseTimeUtc }, new(), account, default)).Cycle);
        Assert.DoesNotContain(typeof(GridRangeEngine).GetMethods(), m => m.Name == "CloseHistoricalResearchBreakoutGuard");
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RealMultiCyclePathRecalculatesEquityRiskAndNativeSizing(bool isLong)
    {
        var bars = Bars(isLong);
        bars.AddRange(Enumerable.Range(54, 130).Select(n => TelemetryFixture.Bar(n, n % 2 == 0 ? 97.5m : 98.5m, n % 2 == 0 ? 101.5m : 102.5m)));
        var native = new TelemetryFixture.Native(); var result = await Run(bars, native);
        var baseline = await Run(bars, strategy: GridRangeSettings.StrategyId);
        Assert.Equal(GridHistoricalExitReason.BreakoutGuard, result.Baskets[0].ExitReason);
        Assert.True(result.Cycles.Count > 1);
        var next = result.Cycles[1]; var first = result.Baskets[0];
        output.WriteLine($"{first.Direction}: guard net={first.NetPnl}, next equity={next.EntryEquity}, next risk={next.Sizing.TargetRiskAmount}, next lots={next.Sizing.Lots}; baseline next equity={baseline.Cycles[1].EntryEquity}, lots={baseline.Cycles[1].Sizing.Lots}");
        Assert.Equal(1000m + first.NetPnl, next.EntryEquity);
        Assert.Equal(next.EntryEquity * .01m, next.Sizing.TargetRiskAmount);
        Assert.True(next.Indicators.Time > TelemetryFixture.Bar(56).CloseTimeUtc);
        Assert.All(next.PlannedLevels, l => Assert.Contains(native.Profits, r => r.OpenPrice == l.Price && r.VolumeLots == next.Sizing.Lots && r.ClosePrice == (l.Direction == GridBasketDirection.Long ? next.LongStop : next.ShortStop)));
        var persisted = GridBacktestPersistence.Map(result, DateTimeOffset.UtcNow);
        Assert.Equal(next.EntryEquity, persisted.Cycles[1].EntryEquity);
        Assert.Equal(next.Sizing.TargetRiskAmount, persisted.Cycles[1].TargetRiskAmount);
        Assert.Equal(next.Sizing.Lots, persisted.Cycles[1].CommonLots);
        // Each fresh cycle performs native sizing even when the lot lattice produces the same lots.
        Assert.True(native.Profits.Count > 2 * result.Cycles.Count * 5);
        Assert.NotEqual(baseline.Baskets[0].ExitTime, first.ExitTime);
        Assert.NotEqual(baseline.Cycles[1].EntryEquity, next.EntryEquity);
        Assert.NotEqual(JsonSerializer.Serialize(baseline.Baskets), JsonSerializer.Serialize(result.Baskets));
        Assert.Equal(decimal.Round(1000m + result.Baskets.Sum(b => b.NetPnl), 12), decimal.Round(result.EndingBalance, 12));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task PersistenceExcelAndShadowSourceBoundary(bool isLong)
    {
        var result = await Run(Bars(isLong));
        await using var db = new EmaBotDbContext(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var run = GridBacktestPersistence.Map(result, DateTimeOffset.UtcNow);
        db.GridBacktestRuns.Add(run); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = await db.GridBacktestRuns.Include(r => r.Cycles).ThenInclude(c => c.Baskets).SingleAsync();
        Assert.Equal(Id, loaded.StrategyId); Assert.Equal(5, loaded.LevelCount);
        Assert.Equal(result.EndingBalance, loaded.EndingBalance);
        Assert.Equal("BreakoutGuard", loaded.Cycles[0].Baskets[0].ExitReason);
        var native = new TelemetryFixture.Native();
        var error = await Assert.ThrowsAsync<GridGuardResearchException>(() => new GridBreakoutGuardShadowSimulator(db, native).SimulateAsync(run.Id, default));
        Assert.Contains("already breakout-guarded", error.Message); Assert.Empty(native.Profits);
        var workbook = (await GridBacktestExcelExport.CreateAsync(db, run.Id, default))!;
        using var zip = new ZipArchive(new MemoryStream(workbook.Bytes));
        string Sheet(int n) { using var reader = new StreamReader(zip.GetEntry($"xl/worksheets/sheet{n}.xml")!.Open()); return reader.ReadToEnd(); }
        var summary = Sheet(1);
        Assert.Contains(Id, summary); Assert.Contains(GridBreakoutGuardRules.Id, summary);
        Assert.Contains("stop level 6", summary); Assert.Contains("Minimum ADX increase", summary);
        Assert.Contains("1.5", summary); Assert.Contains("Minimum consecutive adverse closes", summary);
        var countRow = XDocument.Parse(summary).Descendants().Single(e => e.Name.LocalName == "row" && e.Elements().First().Value == "BreakoutGuardExits");
        Assert.Equal(result.Baskets.Count(b => b.ExitReason == GridHistoricalExitReason.BreakoutGuard).ToString(), countRow.Elements().Last().Value);
        Assert.Contains("BreakoutGuard", Sheet(4)); Assert.Contains("BreakoutGuard", Sheet(3)); Assert.Contains("BreakoutGuard", Sheet(5)); Assert.Contains("BreakoutGuard", Sheet(7));
        Assert.Empty(native.Profits);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task IneligibleShallowBasketRetainsEndOfData(bool isLong)
    {
        var result = await Run(Bars(isLong).Take(53).ToList());
        Assert.Equal(GridHistoricalExitReason.EndOfData, Assert.Single(result.Baskets).ExitReason);
        Assert.Null(result.Telemetry[^1].ExitReasonThisBar);
    }

    [Fact]
    public async Task MinimumVolumeRejectionUsesUnchangedSizer()
    {
        var result = await Run(Bars(true), balance: 1m);
        Assert.Empty(result.Baskets); Assert.Empty(result.Cycles);
        Assert.True(result.Diagnostics.RiskBelowMinimumVolumeCount > 0);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task CounterTwoCanExitOnSameCandleL3Fill(bool isLong)
    {
        var bars = Bars(isLong).Take(53).ToList();
        bars[^1] = isLong ? TelemetryFixture.Bar(52, 93.8m, 98m, 94m) : TelemetryFixture.Bar(52, 102m, 106m, 106m);
        var result = await Run(bars);
        Assert.Equal(GridHistoricalExitReason.BreakoutGuard, Assert.Single(result.Baskets).ExitReason);
        Assert.Equal(2, result.Telemetry[^1].ConsecutiveAdverseCloses);
        Assert.Equal(3, result.Telemetry[^1].MaxFilledLevelAfterBar);
        Assert.Equal(2, result.Telemetry[^1].NewFillCount);
        Assert.Equal(3, result.Baskets[0].Legs.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeProfitRemainsAuthoritativeAndFillMarginFailurePrecedesGuard(bool failMargin)
    {
        var native = new GuardNative { FailFillMargin = failMargin };
        var operation = new GridHistoricalBacktestEngine(native).RunAsync(Bars(true), Request, TelemetryFixture.Instrument);
        if (failMargin)
        {
            var error = await Assert.ThrowsAsync<GridHistoricalExecutionException>(() => operation);
            Assert.Equal("MarginPreflightMismatch", error.Diagnostic.Code);
            Assert.Equal(0, native.ExitCalls);
        }
        else
        {
            var result = await operation; var b = Assert.Single(result.Baskets);
            Assert.Equal(3, native.ExitCalls);
            Assert.Equal(3 * 7.123456789m, b.GrossPnl);
            Assert.Equal(b.GrossPnl - b.Commission, b.NetPnl);
        }
    }

    [Fact]
    public async Task InsufficientPreflightMarginRemainsARejectedCycle()
    {
        var native = new GuardNative { ExcessiveMargin = true };
        var result = await new GridHistoricalBacktestEngine(native).RunAsync(Bars(true), Request, TelemetryFixture.Instrument);
        Assert.Empty(result.Cycles); Assert.Empty(result.Baskets);
        Assert.True(result.Diagnostics.InsufficientMarginCount > 0);
    }

    private sealed class GuardNative : IMt5TradeCalculator
    {
        public bool FailFillMargin { get; init; }
        public bool ExcessiveMargin { get; init; }
        public int ExitCalls { get; private set; }
        private readonly TelemetryFixture.Native normal = new();
        public async Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token)
        {
            var result = await normal.CalculateProfitAsync(r, token);
            if (r.ClosePrice == 94m) { ExitCalls++; return result with { Profit = 7.123456789m }; }
            return result;
        }
        public async Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token)
        {
            var result = await normal.CalculateMarginAsync(r, token);
            // A fill margin mismatch must fail before any guard profit request.
            return result with { RequiredMargin = ExcessiveMargin ? 100000m : FailFillMargin && normal.Margins.Count > 10 ? 100000m : result.RequiredMargin };
        }
    }

}
