using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public enum TrendDirection { Neutral, Up, Down }
public enum SignalDirection { None, Long, Short }
public enum SignalStatus { None, BullishCrossover, BearishCrossover, AwaitingConfirmation, ConfirmationFailed, RejectedByEma100Filter, LongSignal, ShortSignal }
public enum GapState { Unchanged, Expanding, Contracting }

public sealed record IndicatorSnapshot(DateTimeOffset Time, decimal Close, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, GapState GapState, TrendDirection TrendDirection);
public sealed record StrategyEvent(DateTimeOffset Time, SignalDirection Direction, SignalStatus Status, IndicatorSnapshot Snapshot);
public sealed record StrategyEvaluation(IReadOnlyList<IndicatorSnapshot> Snapshots, IReadOnlyList<StrategyEvent> Events);

public static class TrailingStopDocumentation
{
    // Future only: at 50/60/70/80/90/100% favorable progress, move SL to +20/+30/+40/+50/+60/+70%; extend original TP once by 10% at 70%.
    public const string FutureRule = "Documented only; trade management is intentionally not implemented.";
}
