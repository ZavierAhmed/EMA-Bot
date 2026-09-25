using EmaBot.Api.Models;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Services;

internal static class GridGuardResearchSourceValidation
{
    public static decimal? ProfitFactor(IEnumerable<decimal> values)
    {
        decimal gain = 0m, loss = 0m;
        foreach (var value in values) { if (value > 0m) gain += value; else loss -= value; }
        return loss == 0m ? null : gain / loss;
    }
    private static bool SameFactor(decimal? a, decimal? b) => a is null || b is null ? a == b
        : decimal.Round(a.Value, 12, MidpointRounding.AwayFromZero) == decimal.Round(b.Value, 12, MidpointRounding.AwayFromZero);
    private static void Require(bool valid)
    {
        if (!valid) throw new GridGuardResearchException("Grid breakout research source evidence is incomplete or inconsistent.");
    }
    public static void Validate(GridBacktestRun run)
    {
        if (run.Status != "Completed") throw new GridGuardResearchException("Grid breakout research requires a completed Grid run.");
        if (run.StrategyId is not (GridRangeSettings.StrategyId or GridHistoricalStrategyProfile.ResearchStrategyId))
            throw new GridGuardResearchException("Grid guard-shadow research requires an unguarded historical Grid profile; already breakout-guarded runs are not eligible.");
        GridHistoricalStrategyProfile profile;
        try { profile = GridHistoricalStrategyProfile.Resolve(run.StrategyId); }
        catch (ArgumentException) { throw new GridGuardResearchException("Grid breakout research requires an approved Grid historical profile."); }
        if (!run.Cycles.Any(c => c.Telemetry.Count > 0))
            throw new GridGuardResearchException("Grid breakout research requires a telemetry-enabled Grid backtest.");
        Require(run.LevelCount == profile.Settings.LevelCount && run.StartingBalance > 0m && run.CommissionPerLotPerSide >= 0m
            && !string.IsNullOrWhiteSpace(run.BrokerSymbol) && !string.IsNullOrWhiteSpace(run.AccountCurrency)
            && !string.IsNullOrWhiteSpace(run.Interval) && run.RequestedStartUtc < run.RequestedEndUtc);
        var baskets = run.Cycles.SelectMany(c => c.Baskets).ToArray();
        Require(baskets.Length == run.BasketCount && baskets.Sum(b => b.NetPnl) == run.NetPnl
            && baskets.Sum(b => b.GrossPnl) == run.GrossPnl && baskets.Sum(b => b.Commission) == run.TotalCommission
            && run.StartingBalance + run.NetPnl == run.EndingBalance
            && SameFactor(ProfitFactor(baskets.Select(b => b.NetPnl)), run.NetProfitFactor)
            && SameFactor(ProfitFactor(baskets.Select(b => b.GrossPnl)), run.GrossProfitFactor));
        foreach (var cycle in run.Cycles)
        {
            Require(cycle.GridBacktestRunId == run.Id && cycle.Spacing > 0m && cycle.Anchor > 0m
                && cycle.RangeLow > 0m && cycle.RangeHigh >= cycle.RangeLow && cycle.Baskets.Count <= 1);
            if (cycle.Baskets.Count == 0) { Require(cycle.Telemetry.Count == 0); continue; }
            var b = cycle.Baskets.Single();
            Require(b.GridBacktestCycleId == cycle.Id && b.Direction is "Long" or "Short"
                && b.ExitReason is "TakeProfit" or "EmergencyStop" or "EndOfData"
                && b.ExitPrice > 0m && b.Legs.Count > 0 && cycle.Telemetry.Count > 0);
            var legs = b.Legs.OrderBy(l => l.LevelNumber).ToArray();
            Require(legs.Select(l => l.LevelNumber).SequenceEqual(Enumerable.Range(1, legs.Length)) && legs.Length <= run.LevelCount);
            foreach (var l in legs)
                Require(l.GridBacktestBasketId == b.Id && l.Direction == b.Direction && l.Lots > 0m && l.FillPrice > 0m
                    && l.EntryCommission >= 0m && l.ExitCommission >= 0m
                    && l.FillTimeUtc > cycle.QualificationTimeUtc && l.FillTimeUtc <= b.ExitTimeUtc);
            Require(legs.Sum(l => l.GrossPnl) == b.GrossPnl && legs.Sum(l => l.EntryCommission + l.ExitCommission) == b.Commission
                && b.GrossPnl - b.Commission == b.NetPnl);
            var telemetry = cycle.Telemetry.OrderBy(t => t.TimeUtc).ToArray();
            Require(telemetry[0].TimeUtc == legs.Min(l => l.FillTimeUtc) && telemetry[^1].TimeUtc == b.ExitTimeUtc);
            int maxBefore = 0;
            for (var i = 0; i < telemetry.Length; i++)
            {
                var t = telemetry[i];
                var filled = legs.Where(l => l.FillTimeUtc <= t.TimeUtc).ToArray();
                Require(t.GridBacktestCycleId == cycle.Id && t.Direction == b.Direction && t.Sequence == i
                    && (i == 0 || t.TimeUtc > telemetry[i - 1].TimeUtc)
                    && t.TimeUtc >= run.RequestedStartUtc && t.TimeUtc <= run.RequestedEndUtc
                    && t.BidLow > 0m && t.BidHigh >= t.BidLow && t.BidClose >= t.BidLow && t.BidClose <= t.BidHigh
                    && t.BidOpen >= t.BidLow && t.BidOpen <= t.BidHigh && t.SpreadPrice >= 0m && t.SpreadPoints >= 0
                    && filled.Length > 0 && t.MaxFilledLevelBeforeBar == maxBefore && t.MaxFilledLevelAfterBar == filled.Max(l => l.LevelNumber)
                    && t.NewFillCount == filled.Count(l => l.FillTimeUtc == t.TimeUtc) && t.DeepestConfiguredLevel == run.LevelCount
                    && t.DeepestLevelFilled == (t.MaxFilledLevelAfterBar >= run.LevelCount)
                    && t.ConsecutiveAdverseCloses >= 0 && t.ConsecutiveClosesBeyondFrozenBoundary >= 0);
                var expectedExit = i == telemetry.Length - 1 && b.ExitReason != "EndOfData" ? b.ExitReason : null;
                Require(t.ExitReasonThisBar == expectedExit);
                maxBefore = t.MaxFilledLevelAfterBar;
            }
        }
    }
}
