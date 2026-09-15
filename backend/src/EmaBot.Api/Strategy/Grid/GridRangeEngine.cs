using EmaBot.Api.Market;

namespace EmaBot.Api.Strategy.Grid;

// One engine owns one symbol. Consumers must share this owner for that symbol.
// No DI registration, persistence, execution session, or broker order integration.
public sealed class GridRangeEngine
{
    private readonly GridRangeRiskSizer sizer;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset? lastBarTime;
    private DateTimeOffset? lastQualificationTime;
    public GridRangeEngine(string symbol, GridRangeRiskSizer sizer)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Symbol is required.", nameof(symbol));
        Symbol = symbol; this.sizer = sizer;
    }
    public string Symbol { get; }
    public GridRangeCycle? Cycle { get; private set; }
    public int CooldownRemaining { get; private set; }

    public async Task<GridCycleCreation> TryCreateAsync(IReadOnlyList<Candle> candles, DateTimeOffset asOf,
        GridRangeSettings settings, GridRiskAccount account, CancellationToken token = default)
        => await TryCreateCoreAsync(() => GridRangeIndicators.Qualify(candles, asOf), settings, account, token);

    // Historical orchestration supplies an incrementally computed snapshot. The
    // same G0 qualification, ownership, sizing and cooldown gates remain authoritative.
    internal Task<GridCycleCreation> TryCreateAsync(GridRangeIndicatorSnapshot snapshot,
        GridRangeSettings settings, GridRiskAccount account, CancellationToken token)
        => TryCreateCoreAsync(() => GridRangeIndicators.Evaluate(snapshot), settings, account, token);

    private async Task<GridCycleCreation> TryCreateCoreAsync(Func<GridRangeQualification> qualify,
        GridRangeSettings settings, GridRiskAccount account, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (Cycle is { ExitReason: null }) return new(null, GridCycleDiagnostics.ActiveCycle);
            if (CooldownRemaining > 0) return new(null, GridCycleDiagnostics.Cooldown);
            if (!settings.IsValid) return new(null, GridCycleDiagnostics.InvalidSettings);
            var qualification = qualify();
            if (!qualification.IsQualified) return new(null, qualification.Failure);
            if (qualification.Snapshot.Time <= lastQualificationTime || qualification.Snapshot.Time <= Cycle?.ExitTime
                || qualification.Snapshot.Time < lastBarTime) return new(null, GridCycleDiagnostics.Cooldown);
            var sizing = await sizer.SizeAsync(Symbol, qualification, settings, account, token);
            if (!sizing.IsSuccess) return new(null, sizing.Failure, sizing);
            Cycle = new(Symbol, settings, qualification, sizing);
            lastQualificationTime = qualification.Snapshot.Time;
            lastBarTime = qualification.Snapshot.Time;
            return new(Cycle, null, sizing);
        }
        finally { gate.Release(); }
    }

    // Feed completed bars exactly once, in chronological order. Duplicate bars do
    // not consume cooldown. Three bars after closure are blocked; the next may qualify.
    public GridBarResult ProcessBar(GridHistoricalBar bar)
    {
        gate.Wait();
        try
        {
            if (!bar.IsValid) throw new ArgumentException("Invalid historical bar.", nameof(bar));
            if (bar.Bid.CloseTimeUtc <= lastBarTime) return new([]);
            lastBarTime = bar.Bid.CloseTimeUtc;
            if (Cycle is null) return new([]);
            if (Cycle.ExitReason is not null)
            {
                if (CooldownRemaining > 0)
                {
                    CooldownRemaining--;
                    // Mark every cooldown candle ineligible, including the final one.
                    lastQualificationTime = bar.Bid.CloseTimeUtc;
                }
                return new([]);
            }
            var result = GridHistoricalFillRules.Apply(Cycle, bar);
            if (result.ExitReason is not null) CooldownRemaining = Cycle.Settings.CooldownBars;
            return result;
        }
        finally { gate.Release(); }
    }
}
