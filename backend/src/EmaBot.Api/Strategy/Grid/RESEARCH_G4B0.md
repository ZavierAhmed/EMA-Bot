# G4B0: Deep Grid telemetry

## Research purpose

The user reports that G4A did not solve the June/July/August baseline losses:
removing Level 5 moved the recurring failure area to the deepest Level 4 area.
These are user-supplied observations, not conclusions computed by this code.
G4B0 changes **no trading rules**. It captures evolving evidence to compare deep
baskets that recover with deep baskets that stop out. No breakout decision,
early exit, cancellation, trend filter or G4B trading logic is implemented.

Both GRID_RANGE_V1 and GRID_RANGE_4L_RESEARCH_V1 keep their frozen settings,
native economics, fills, exits, commission and TradeMode behavior. No observer
value flows back to the domain engine. Paper/Demo/Live and EMA are untouched.

## Sampling contract

One row per real completed reporting candle with a locked direction and at
least one filled level. Includes first fill, ordinary active bars, additional
fills, TP and emergency-stop candles. Excludes warmup, qualification-only bars,
ambiguous unlocked bars and candles after a basket already closed.

TimeUtc is the candle close time. Sequence is zero-based per cycle. Before/after
level evidence uses actual fill timestamps, including levels closed on this
candle. Indicator values come from the existing incremental indicator window
including the current completed candle; there is no second ATR/ADX calculation.

EndOfData adds no row and does not overwrite the final bar's exit evidence.
Its final real bar has NULL ExitReasonThisBar unless it had a normal transition.
Join cycle -> basket for final exit reason and P/L; neither is copied into every
telemetry row.

## Formulas

Let A be frozen anchor, S frozen spacing, C/O/H/L current Bid close/open/high/low,
P the previous real completed Bid close (including warmup), and B the frozen
qualification boundary: RangeLow for Long, RangeHigh for Short.

- DistanceFromAnchorSpacings = abs(C - A) / S.
- AdverseDistanceFromAnchorSpacings = (A - C) / S for Long, (C - A) / S
  for Short; negative favorable distances remain negative.
- CloseBeyondFrozenBoundary = C < B for Long; C > B for Short.
- AdverseExtremeBeyondFrozenBoundary = L < B for Long; H > B for Short.
  Equality is not a crossing. These structural measures always use Bid.
- BreakoutDistanceSpacings = max(0, B - C) / S for Long;
  max(0, C - B) / S for Short.
- AdxDeltaFromQualification = CurrentAdx - QualificationAdx.
- AtrRatioToQualification = CurrentAtr / QualificationAtr.
- CandleBody = abs(C - O).
- CandleTrueRange = max(H - L, abs(H - P), abs(L - P)); NULL if P unavailable.
- CandleBodyAtrRatio = CandleBody / CurrentAtr.
- CandleTrueRangeAtrRatio = CandleTrueRange / CurrentAtr.
- Missing indicators or unavailable/nonpositive denominators produce NULL ratios,
  never fabricated zero. Measured zeros are retained.
- ConsecutiveAdverseCloses compares C with the preceding **active** bar's close:
  lower for Long, higher for Short. First active bar starts at zero; equal or
  favorable closes reset to zero.
- ConsecutiveClosesBeyondFrozenBoundary increments on strict boundary-close
  crossings and resets otherwise. Both counters and sequence reset on a new cycle.
- DeepestLevelFilled = MaxFilledLevelAfterBar >= DeepestConfiguredLevel (5 or 4).

## Persistence and exact fields

One child table `GridBacktestTelemetry` under `GridBacktestCycles`, with cascade
on delete and unique indexes on (GridBacktestCycleId, Sequence) and
(GridBacktestCycleId, TimeUtc). Decimal fields use existing Grid precision (28,12).
Rows are collected in memory, mapped to cycles by qualification timestamp and
saved in the existing single SaveChanges transaction. No per-candle queries,
network or additional native economics calls are introduced.

Persisted fields (nullable fields marked `?`):

- Identity: Id, GridBacktestCycleId, Sequence, TimeUtc, Direction.
- Bar: BidOpen, BidHigh, BidLow, BidClose, SpreadPoints, SpreadPrice.
- Indicators: CurrentAtr?, CurrentAdx?, CurrentRangeHigh, CurrentRangeLow,
  AdxDeltaFromQualification?, AtrRatioToQualification?.
- Distances: DistanceFromAnchorSpacings, AdverseDistanceFromAnchorSpacings,
  FrozenBoundaryPrice, BreakoutDistanceSpacings.
- Impulse: CandleBody, CandleTrueRange?, CandleBodyAtrRatio?, CandleTrueRangeAtrRatio?.
- Structure/counters: CloseBeyondFrozenBoundary, AdverseExtremeBeyondFrozenBoundary,
  ConsecutiveAdverseCloses, ConsecutiveClosesBeyondFrozenBoundary.
- Fills/exit: MaxFilledLevelBeforeBar, MaxFilledLevelAfterBar, NewFillCount,
  DeepestConfiguredLevel, DeepestLevelFilled, ExitReasonThisBar?.

Migration: `20260925032713_AddGridDeepTelemetry`. Generated only; **not applied**.
It adds this table and its indexes; no prior tables, columns or saved values change.

## Excel and API

Seventh sheet TELEMETRY exports 40 columns in cycle/sequence order. It uses the
persisted fields above (CycleId replaces the FK name and row Id is omitted), plus
QualificationAtr, QualificationAdx, FrozenRangeHigh, FrozenRangeLow, Anchor and
Spacing joined from the persisted cycle. SUMMARY adds TelemetryRows and
ActiveCyclesWithTelemetry. Blank cells preserve NULL; numeric zero stays zero.
Old runs export headers with zero telemetry data rows. Export never calls MT5,
history providers, indicators or the engine. Existing six sheets remain present.

Normal list/detail API contracts and frontend are unchanged; normal detail queries
do not load telemetry. Excel explicitly loads it, and deletion loads it for cascade
handling. No large telemetry array is sent to the frontend.

## No-behavior-change evidence

Twelve deterministic fixtures were run against commit 370af8a **before** telemetry
was added: both profiles, both directions, TP/stop/EndOfData. Golden SHA256 values
in GridDeepTelemetryTests cover every basket/leg, fill and exit time/price/reason,
lots, risk/margin, P/L, ending balance, drawdown, cycles, events, diagnostics and
all native economics request evidence/call counts. Wall-clock timing is excluded.
All twelve must continue to match exactly. These are fake-calculator unit tests,
not manual historical runs against MT5.

No manual historical experiment, broker order or next milestone is started here.
