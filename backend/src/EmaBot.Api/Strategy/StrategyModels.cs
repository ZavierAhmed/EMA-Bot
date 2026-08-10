using EmaBot.Api.Models;

namespace EmaBot.Api.Strategy;

public enum TrendDirection { Neutral, Up, Down }
public enum SignalDirection { None, Long, Short }
public enum PositionSizingMode { FixedNotional, MarginPercent }
public enum SignalStatus { None, BullishCrossover, BearishCrossover, AwaitingConfirmation, ConfirmationFailed, RejectedByEma100Filter, RejectedByEmaGap, RejectedByStopDistance, RejectedByFees, RejectedByInsufficientMargin, LongSignal, ShortSignal, ReentryLongSignal, ReentryShortSignal }
public enum GapState { Unchanged, Expanding, Contracting }

public sealed record IndicatorSnapshot(DateTimeOffset Time, decimal Close, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, GapState GapState, TrendDirection TrendDirection, decimal Open = 0m);
public sealed record StrategyEvent(DateTimeOffset Time, SignalDirection Direction, SignalStatus Status, IndicatorSnapshot Snapshot);
public sealed record StrategyEvaluation(IReadOnlyList<IndicatorSnapshot> Snapshots, IReadOnlyList<StrategyEvent> Events);

public static class TrailingStopDocumentation
{
    public const string Rule = "At 50/60/70/80/90/100% favorable progress, lock +20/+30/+40/+50/+60/+70% of original target distance and extend TP once to 110% at 70%.";
}
