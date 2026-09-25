using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Services;

// Isolated historical runner: caller supplies captured native bars, instrument,
// currency and PaperCommissionPerLotPerSide snapshot. No fetch, settings service,
// persistence, DI registration, live positions, orders, or EMA engine dependency.
public sealed class GridHistoricalBacktestEngine(IMt5TradeCalculator calculator)
{
    public async Task<GridHistoricalBacktestResult> RunAsync(IReadOnlyList<Mt5HistoricalExecutionBar> input,
        GridHistoricalBacktestRequest request, InstrumentCatalogItem instrument, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ValidateRequest(request, instrument);
        var settings = request.Settings ?? GridHistoricalStrategyProfile.Resolve(request.StrategyId).Settings;
        request = request with { Settings = settings };
        var spec = instrument.Spec;
        var bars = input.Where(b => b.IsClosed && (request.RequestedEndUtc is null || b.CloseTimeUtc <= request.RequestedEndUtc))
            .OrderBy(b => b.OpenTimeUtc).ToArray();
        DateTimeOffset? previousClose = null;
        foreach (var bar in bars)
        {
            token.ThrowIfCancellationRequested();
            if (bar.BrokerSymbol != request.Symbol || bar.Timeframe != request.Interval || bar.OpenTimeUtc < previousClose
                || !new GridHistoricalBar(bar.ToCandle(), bar.SpreadPoints, spec.PointSize).IsValid)
                throw Failure("InvalidNativeBar", bar.CloseTimeUtc, "Invalid, overlapping or mismatched native Bid bar/spread evidence.");
            previousClose = bar.CloseTimeUtc;
        }
        var economics = new GridHistoricalEconomics(calculator, request.AccountCurrency);
        var engine = new GridRangeEngine(request.Symbol, new(economics));
        var window = new GridRangeIndicatorWindow();
        var telemetry = new GridDeepTelemetryObserver();
        decimal? previousBidClose = null;
        var baskets = new List<GridHistoricalBasket>();
        var cycles = new List<GridHistoricalCycleSnapshot>();
        var entries = new List<GridHistoricalDiagnostic>();
        var events = new List<GridHistoricalBasketEvent>();
        var cycleEvents = new List<GridHistoricalBasketEvent>();
        var legs = new List<GridHistoricalLeg>();
        GridHistoricalCycleSnapshot? snapshot = null;
        var balance = request.StartingBalance; var peak = balance; decimal drawdown = 0m;
        int qualified = 0, rejected = 0, ambiguous = 0, noFill = 0, modeBlocked = 0, count = 0, warmup = 0;
        Mt5HistoricalExecutionBar? first = null, last = null;
        var allowedDirections = instrument.TradeMode switch
        {
            InstrumentTradeMode.Full => GridAllowedDirections.Both,
            InstrumentTradeMode.LongOnly => GridAllowedDirections.Long,
            InstrumentTradeMode.ShortOnly => GridAllowedDirections.Short,
            _ => GridAllowedDirections.None
        };
        var canOpen = allowedDirections != GridAllowedDirections.None;

        void Event(GridHistoricalBasketEvent item) { events.Add(item); cycleEvents.Add(item); }
        async Task CloseAsync(GridHistoricalExitReason reason, decimal exit, Mt5HistoricalExecutionBar bar)
        {
            var cycle = engine.Cycle!;
            var closed = new List<GridHistoricalLeg>();
            foreach (var leg in legs)
            {
                token.ThrowIfCancellationRequested();
                decimal profit;
                try
                {
                    profit = (await economics.CalculateProfitAsync(new(request.Symbol, cycle.Direction!.Value.ToString(), leg.Lots, leg.FillPrice, exit), token)).Profit;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw Failure("ProfitCalculationUnavailable", bar.CloseTimeUtc, ex.Message, snapshot,
                        new("CalculateProfit", request.Symbol, cycle.Direction!.Value, leg.Lots, leg.FillPrice, exit, ex.Message), ex);
                }
                closed.Add(leg with { GrossPnl = profit, ExitCommission = leg.Lots * request.PaperCommissionPerLotPerSide });
            }
            balance += closed.Sum(l => l.NetPnl);
            peak = Math.Max(peak, balance); drawdown = Math.Max(drawdown, peak - balance);
            Event(new(bar.CloseTimeUtc, GridHistoricalEventType.Exit, ExecutablePrice: exit, Detail: reason.ToString()));
            baskets.Add(new(snapshot!, cycle.Direction!.Value, closed.AsReadOnly(), reason, bar.CloseTimeUtc,
                exit, bar.SpreadPoints * spec.PointSize, balance, Array.AsReadOnly(cycleEvents.ToArray())));
            legs.Clear();
        }

        foreach (var bar in bars)
        {
            token.ThrowIfCancellationRequested();
            var indicator = window.Append(bar.ToCandle());
            var priorBidClose = previousBidClose; previousBidClose = bar.Close;
            // Whole bars only in reporting interval. Earlier supplied bars warm the
            // indicators but cannot qualify/enter a cycle. No partial-bar inference.
            if (request.RequestedStartUtc is { } start && bar.OpenTimeUtc < start) { warmup++; continue; }
            first ??= bar; last = bar; count++;
            if (!canOpen)
            {
                if (modeBlocked++ == 0) entries.Add(new(bar.CloseTimeUtc, "TradeModeBlocked", Detail: instrument.TradeMode.ToString()));
                continue;
            }
            // Capture transitions without feeding any observer value into the engine.
            var closedBeforeBar = engine.Cycle?.ExitReason is not null;
            var maxBeforeBar = engine.Cycle?.Levels.Where(l => l.FillTime is not null).Select(l => l.Number).DefaultIfEmpty(0).Max() ?? 0;
            var transition = engine.ProcessBar(new(bar.ToCandle(), bar.SpreadPoints, spec.PointSize));
            if (transition.Diagnostic == GridCycleDiagnostics.AmbiguousFirstSide)
            {
                ambiguous++;
                Event(new(bar.CloseTimeUtc, GridHistoricalEventType.AmbiguousFirstSide));
            }
            foreach (var fill in transition.NewFills)
            {
                var planned = snapshot!.PlannedLevels.Single(l => l.Direction == fill.Direction && l.Number == fill.Number);
                if (!planned.Allowed || planned.RequiredMargin is not { } plannedMargin || planned.InitialStopRisk is not { } plannedRisk)
                    throw Failure("MissingAllowedSideEvidence", bar.CloseTimeUtc, "A filled level requires allowed-side native risk/margin evidence.", snapshot);
                decimal margin;
                try
                {
                    margin = (await economics.CalculateMarginAsync(new(request.Symbol, fill.Direction.ToString(), fill.Lots, fill.Price), token)).RequiredMargin;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw Failure("MarginCalculationUnavailable", bar.CloseTimeUtc, ex.Message, snapshot,
                        new("CalculateMargin", request.Symbol, fill.Direction, fill.Lots, fill.Price,
                            fill.Direction == GridBasketDirection.Long ? snapshot.LongStop : snapshot.ShortStop, ex.Message), ex);
                }
                if (Math.Abs(margin - plannedMargin) > .00000001m)
                    throw Failure("MarginPreflightMismatch", bar.CloseTimeUtc,
                        $"Level {fill.Number}: preflight {planned.RequiredMargin}, actual {margin}.", snapshot);
                if (legs.Sum(l => l.RequiredMargin) + margin > balance)
                    throw Failure("InsufficientActualMargin", bar.CloseTimeUtc, "Filled aggregate margin exceeds simulated free balance.", snapshot);
                legs.Add(new(fill.Number, fill.FillTime!.Value, fill.Price, fill.Lots, margin, plannedRisk,
                    fill.Lots * request.PaperCommissionPerLotPerSide, 0m, 0m));
                Event(new(bar.CloseTimeUtc, GridHistoricalEventType.Fill, fill.Number, fill.Price));
            }
            if (!closedBeforeBar && engine.Cycle is { Direction: { } locked } observed
                && observed.Levels.Any(l => l.FillTime is not null))
            {
                var maxAfterBar = observed.Levels.Where(l => l.FillTime is not null).Max(l => l.Number);
                telemetry.Observe(bar, indicator, spec.PointSize, priorBidClose, locked,
                    maxBeforeBar, maxAfterBar, transition.NewFills.Count, transition.ExitReason);
            }
            if (transition.ExitReason is { } reason)
                await CloseAsync(reason == GridExitReason.TakeProfit ? GridHistoricalExitReason.TakeProfit : GridHistoricalExitReason.EmergencyStop,
                    engine.Cycle!.ExitPrice!.Value, bar);

            // G0 rejects active/cooldown/stale qualification times before economics.
            if (engine.Cycle is { ExitReason: null } || engine.CooldownRemaining > 0) continue;
            economics.ClearEvidence();
            var creation = await engine.TryCreateAsync(indicator, settings,
                new(balance, balance, spec.VolumeMin, spec.VolumeMax, spec.VolumeStep, spec.VolumeLimit,
                    ExistingLongVolume: 0m, ExistingShortVolume: 0m, AllowedDirections: allowedDirections), token);
            if (creation.Cycle is not { } accepted)
            {
                if (creation.Failure is GridCycleDiagnostics.ActiveCycle or GridCycleDiagnostics.Cooldown) continue;
                if (creation.Sizing is null) rejected++; else qualified++;
                entries.Add(new(bar.CloseTimeUtc, creation.Failure!.Value.ToString(), creation.Failure, creation.Sizing?.Diagnostic));
                events.Add(new(bar.CloseTimeUtc, GridHistoricalEventType.Rejected, Detail: creation.Failure.ToString()));
                continue;
            }
            qualified++;
            // Policy is mapped before sizing. Prohibited candidates are already
            // canceled by the domain and intentionally have no economics evidence.
            var plannedLevels = accepted.Levels.Select(l => new GridHistoricalPlannedLevel(l.Number, l.Direction, l.Price, l.Lots,
                accepted.Sizing.Allows(l.Direction) ? decimal.Abs(economics.Profits[new(request.Symbol, l.Direction.ToString(), l.Lots, l.Price,
                    l.Direction == GridBasketDirection.Long ? accepted.LongStop : accepted.ShortStop)]) : null,
                accepted.Sizing.Allows(l.Direction) ? economics.Margins[new(request.Symbol, l.Direction.ToString(), l.Lots, l.Price)] : null,
                accepted.Sizing.Allows(l.Direction))).ToArray();
            snapshot = new(accepted.Snapshot, accepted.Anchor, accepted.Spacing, accepted.LongStop, accepted.ShortStop,
                settings.GridBasketRiskPercent, balance, accepted.Sizing, Array.AsReadOnly(plannedLevels));
            cycles.Add(snapshot); cycleEvents = []; legs = [];
            telemetry.StartCycle(snapshot, settings.LevelCount);
            Event(new(bar.CloseTimeUtc, GridHistoricalEventType.Qualified));
        }
        if (last is not null && engine.Cycle is { ExitReason: null } open)
        {
            if (legs.Count > 0)
                await CloseAsync(GridHistoricalExitReason.EndOfData,
                    open.Direction == GridBasketDirection.Long ? last.Close : last.Close + last.SpreadPoints * spec.PointSize, last);
            else
            {
                noFill++;
                Event(new(last.CloseTimeUtc, GridHistoricalEventType.CanceledWithoutFills));
                entries.Add(new(last.CloseTimeUtc, "NoFillsAtEndOfData"));
            }
            // Cancellation is local to this historical run; do not add an EndOfData
            // exit to the G0 contract. G1 results retain the historical exit identity.
            open.Levels = Array.AsReadOnly(open.Levels.Select(l => l with
                { Status = l.FillTime is null ? GridLevelStatus.Canceled : GridLevelStatus.Closed }).ToArray());
        }
        token.ThrowIfCancellationRequested();
        return new(request, instrument, first?.OpenTimeUtc, last?.CloseTimeUtc, count, warmup, balance, drawdown,
            baskets.AsReadOnly(), cycles.AsReadOnly(), new(qualified, rejected, ambiguous, noFill, modeBlocked, entries.AsReadOnly()),
            events.AsReadOnly(), economics.Calls, economics.ElapsedMilliseconds) { Telemetry = telemetry.Rows };
    }

    private static void ValidateRequest(GridHistoricalBacktestRequest request, InstrumentCatalogItem instrument)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol != instrument.Spec.BrokerSymbol
            || !Mt5NativeTimeframes.IsSupported(request.Interval) || string.IsNullOrWhiteSpace(request.AccountCurrency)
            || request.StartingBalance <= 0m || request.PaperCommissionPerLotPerSide < 0m
            || request.RequestedStartUtc >= request.RequestedEndUtc)
            throw Failure("InvalidRequest", null, "Explicit matching symbol, interval, currency, balance, commission and valid dates are required.");
        var settings = request.Settings ?? GridHistoricalStrategyProfile.Resolve(request.StrategyId).Settings;
        if (!settings.IsValid || settings != GridHistoricalStrategyProfile.Resolve(request.StrategyId).Settings)
            throw Failure("InvalidSettings", null, "Historical Grid settings must match the selected frozen profile.");
        // Reuse only the existing strategy-neutral native evidence validator.
        if (Mt5HistoricalBacktestEngine.ValidateNativeInstrument(instrument.Spec) is { } invalid)
            throw Failure("InvalidNativeInstrument", null, invalid);
        if (instrument.Spec.VolumeLimit < 0m)
            throw Failure("InvalidNativeInstrument", null, "Negative native volume limit.");
    }
    private static GridHistoricalExecutionException Failure(string code, DateTimeOffset? time, string detail,
        GridHistoricalCycleSnapshot? cycle = null, GridRiskDiagnostic? economics = null, Exception? inner = null)
        => new(new(time, code, Economics: economics, Detail: detail), cycle, inner);
}
