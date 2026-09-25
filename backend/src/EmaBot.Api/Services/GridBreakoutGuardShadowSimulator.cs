using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy.Grid;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

public sealed class GridGuardResearchException(string message, int statusCode = 400) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
}

// Read-only saved evidence + native hypothetical profit. No strategy/history dependency.
public sealed class GridBreakoutGuardShadowSimulator(EmaBotDbContext database, IMt5TradeCalculator calculator)
{
    private sealed record Candidate(string Id, string Description, int MinimumFilledLevel, Func<GridBacktestTelemetry, bool> Matches);
    public Task<GridBreakoutGuardShadowResult> SimulateAsync(int id, CancellationToken token)
        => SimulateCoreAsync(id, false, new[] { GridBreakoutGuardCandidate.Control }.Concat(GridBreakoutGuardCandidate.Guards)
            .Select(c => new Candidate(c.Id, c.Description, c.MinimumFilledLevel, c.Matches)).ToArray(), token);
    public Task<GridBreakoutGuardShadowResult> SimulateStrictAsync(int id, CancellationToken token)
        => SimulateCoreAsync(id, true, GridStrictBreakoutGuardCandidate.All
            .Select(c => new Candidate(c.Id, c.Description, c.MinimumFilledLevel, c.Matches)).ToArray(), token);

    private async Task<GridBreakoutGuardShadowResult> SimulateCoreAsync(int id, bool strict, IReadOnlyList<Candidate> catalog, CancellationToken token)
    {
        var run = await database.GridBacktestRuns.AsNoTracking()
            .Include(r => r.Cycles).ThenInclude(c => c.Baskets).ThenInclude(b => b.Legs)
            .Include(r => r.Cycles).ThenInclude(c => c.Telemetry).AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, token)
            ?? throw new GridGuardResearchException("Grid breakout research source run was not found.", 404);
        if (strict && (run.StrategyId != GridRangeSettings.StrategyId || !run.Cycles.Any(c => c.Telemetry.Count > 0)))
            throw new GridGuardResearchException("Strict Grid guard research requires a telemetry-enabled GRID_RANGE_V1 backtest.");
        GridGuardResearchSourceValidation.Validate(run);
        var economics = new GridHistoricalEconomics(calculator, run.AccountCurrency);
        // Exact complete request keys; scoped to THIS invocation and never persisted.
        var cache = new Dictionary<Mt5CalculateProfitRequest, decimal>();
        var rows = new List<GridBreakoutGuardBasketResult>();
        var aggregates = new List<GridBreakoutGuardCandidateResult>();
        foreach (var candidate in catalog)
        {
            var callsBefore = economics.Calls;
            var candidateRequests = new HashSet<Mt5CalculateProfitRequest>();
            var candidateRows = new List<GridBreakoutGuardBasketResult>();
            foreach (var cycle in run.Cycles.OrderBy(c => c.Sequence))
            foreach (var basket in cycle.Baskets.OrderBy(b => b.ExitTimeUtc))
            {
                token.ThrowIfCancellationRequested();
                // A completed-candle guard must precede the actual exit. In particular,
                // same-candle TP/stop always wins, and final EndOfData is not invented anew.
                var trigger = cycle.Telemetry.OrderBy(t => t.TimeUtc)
                    .FirstOrDefault(t => t.TimeUtc < basket.ExitTimeUtc && candidate.Matches(t));
                var row = new GridBreakoutGuardBasketResult
                {
                    RunId = run.Id, CycleId = cycle.Id, BasketId = basket.Id, CandidateId = candidate.Id,
                    StrategyId = run.StrategyId, Symbol = run.BrokerSymbol, Interval = run.Interval, Direction = basket.Direction,
                    ActualExitReason = basket.ExitReason, ActualExitTimeUtc = basket.ExitTimeUtc,
                    ActualExitPrice = basket.ExitPrice, ActualNetPnl = basket.NetPnl,
                    ShadowGrossPnl = basket.GrossPnl, ShadowEntryCommission = basket.Legs.Sum(l => l.EntryCommission),
                    ShadowExitCommission = basket.Legs.Sum(l => l.ExitCommission), ShadowNetPnl = basket.NetPnl,
                    Classification = "NoTrigger"
                };
                if (trigger is not null)
                {
                    var legs = basket.Legs.Where(l => l.FillTimeUtc <= trigger.TimeUtc).OrderBy(l => l.LevelNumber).ToArray();
                    var exit = trigger.BidClose + (basket.Direction == "Short" ? trigger.SpreadPrice : 0m);
                    decimal gross = 0m;
                    foreach (var leg in legs)
                    {
                        token.ThrowIfCancellationRequested();
                        var request = new Mt5CalculateProfitRequest(run.BrokerSymbol, basket.Direction, leg.Lots, leg.FillPrice, exit);
                        candidateRequests.Add(request);
                        if (!cache.TryGetValue(request, out var profit))
                        {
                            try { profit = (await economics.CalculateProfitAsync(request, token)).Profit; }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception) { throw new GridGuardResearchException("Grid breakout research native profit is unavailable. No research workbook was produced.", 503); }
                            cache.Add(request, profit);
                        }
                        gross += profit;
                    }
                    var entryCommission = legs.Sum(l => l.EntryCommission);
                    var exitCommission = legs.Sum(l => l.Lots * run.CommissionPerLotPerSide);
                    var net = gross - entryCommission - exitCommission;
                    row = row with
                    {
                        Triggered = true, TriggerTimeUtc = trigger.TimeUtc, TriggerLevel = candidate.MinimumFilledLevel,
                        MaxFilledLevelAtTrigger = trigger.MaxFilledLevelAfterBar, TriggerBidClose = trigger.BidClose,
                        TriggerSpreadPrice = trigger.SpreadPrice, ShadowExitPrice = exit,
                        TriggerAdx = trigger.CurrentAdx, TriggerAdxDelta = trigger.AdxDeltaFromQualification,
                        TriggerAtr = trigger.CurrentAtr, TriggerAtrRatio = trigger.AtrRatioToQualification,
                        TriggerTrueRangeAtrRatio = trigger.CandleTrueRangeAtrRatio, TriggerBodyAtrRatio = trigger.CandleBodyAtrRatio,
                        TriggerCloseBeyondBoundary = trigger.CloseBeyondFrozenBoundary, TriggerExtremeBeyondBoundary = trigger.AdverseExtremeBeyondFrozenBoundary,
                        TriggerConsecutiveAdverseCloses = trigger.ConsecutiveAdverseCloses, TriggerConsecutiveBoundaryCloses = trigger.ConsecutiveClosesBeyondFrozenBoundary,
                        ShadowOpenLegCount = legs.Length, ShadowGrossPnl = gross, ShadowEntryCommission = entryCommission,
                        ShadowExitCommission = exitCommission, ShadowNetPnl = net, DeltaVsActualNetPnl = net - basket.NetPnl,
                        Classification = basket.ExitReason switch { "EmergencyStop" => "EmergencyStopIntercepted", "TakeProfit" => "WinnerCutEarly", _ => "EndOfDataIntercepted" }
                    };
                }
                candidateRows.Add(row);
            }
            rows.AddRange(candidateRows);
            var aggregate = Aggregate(candidate, candidateRows, economics.Calls - callsBefore, candidateRequests.Count) with
            {
                ActualGrossProfit = run.Cycles.SelectMany(c => c.Baskets).Where(b => b.GrossPnl > 0m).Sum(b => b.GrossPnl),
                ActualGrossLoss = -run.Cycles.SelectMany(c => c.Baskets).Where(b => b.GrossPnl < 0m).Sum(b => b.GrossPnl),
                ActualGrossProfitFactor = run.GrossProfitFactor, ActualNetProfitFactor = run.NetProfitFactor
            };
            // Preserve the stored control PF exactly after precision-aware reconciliation.
            if (candidate.Id == GridBreakoutGuardCandidate.Control.Id) aggregate = aggregate with { FixedPathShadowProfitFactor = run.NetProfitFactor };
            aggregates.Add(aggregate);
        }
        return new(run, aggregates.AsReadOnly(), rows.AsReadOnly(), economics.Calls, cache.Count);
    }

    private static GridBreakoutGuardCandidateResult Aggregate(Candidate candidate,
        IReadOnlyList<GridBreakoutGuardBasketResult> rows, int calls, int uniqueRequests)
    {
        var triggered = rows.Count(r => r.Triggered);
        return new()
        {
            CandidateId = candidate.Id, Description = candidate.Description, MinimumFilledLevel = candidate.MinimumFilledLevel,
            ResultType = GridBreakoutGuardShadowResult.ResultType, BasketCount = rows.Count,
            TriggeredBasketCount = triggered, NoTriggerBasketCount = rows.Count - triggered,
            EmergencyStopInterceptedCount = rows.Count(r => r.Classification == "EmergencyStopIntercepted"),
            WinnerCutEarlyCount = rows.Count(r => r.Classification == "WinnerCutEarly"),
            EndOfDataInterceptedCount = rows.Count(r => r.Classification == "EndOfDataIntercepted"),
            ActualNetPnl = rows.Sum(r => r.ActualNetPnl), FixedPathShadowNetPnl = rows.Sum(r => r.ShadowNetPnl),
            FixedPathDeltaVsActual = rows.Sum(r => r.DeltaVsActualNetPnl),
            SavedLossAmount = rows.Where(r => r.ActualExitReason == "EmergencyStop" && r.DeltaVsActualNetPnl > 0m).Sum(r => r.DeltaVsActualNetPnl),
            LostWinnerProfitAmount = -rows.Where(r => r.ActualExitReason == "TakeProfit" && r.DeltaVsActualNetPnl < 0m).Sum(r => r.DeltaVsActualNetPnl),
            TriggeredActualLossCount = rows.Count(r => r.Triggered && r.ActualNetPnl < 0m),
            TriggeredActualWinnerCount = rows.Count(r => r.Triggered && r.ActualNetPnl > 0m),
            AverageDeltaPerTriggeredBasket = triggered == 0 ? null : rows.Sum(r => r.DeltaVsActualNetPnl) / triggered,
            PositiveDeltaBasketCount = rows.Count(r => r.DeltaVsActualNetPnl > 0m), NegativeDeltaBasketCount = rows.Count(r => r.DeltaVsActualNetPnl < 0m),
            ZeroDeltaBasketCount = rows.Count(r => r.DeltaVsActualNetPnl == 0m),
            // Profit factor is based on net basket outcomes, matching source NetProfitFactor.
            FixedPathShadowGrossProfit = rows.Where(r => r.ShadowNetPnl > 0m).Sum(r => r.ShadowNetPnl),
            FixedPathShadowGrossLoss = -rows.Where(r => r.ShadowNetPnl < 0m).Sum(r => r.ShadowNetPnl),
            FixedPathShadowProfitFactor = GridGuardResearchSourceValidation.ProfitFactor(rows.Select(r => r.ShadowNetPnl)),
            NativeProfitCallCount = calls, UniqueNativeProfitRequestCount = uniqueRequests
        };
    }
}
