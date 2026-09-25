# G4B1: Grid breakout guard shadow screening

## Purpose

The user reports that G4B0 analysis found increasing ADX, stronger candles and
frozen-range pressure in deep losing baskets. G4B1 evaluates seven fixed warnings
against saved telemetry without changing actual trades. These observations are
research motivation, not independently established application conclusions.

**FIXED-PATH SHADOW RESULT / SCREENING ONLY. This is NOT a real backtest.**
Future sizing, cooldown, qualification and later baskets are not rerun. A promising
rule must later become a separate research profile and be rerun end-to-end.
No ranking, approval or automatic winner selection is performed.

## Frozen catalog

| Candidate | Monitor from | Predicate |
| --- | --- | --- |
| L3_BOUNDARY_CLOSE | L3 | CloseBeyondFrozenBoundary |
| L3_BOUNDARY_EXTREME_ADX10 | L3 | AdverseExtremeBeyondFrozenBoundary AND AdxDeltaFromQualification >= 1.0 |
| L3_ADX10_TR15 | L3 | AdxDeltaFromQualification >= 1.0 AND CandleTrueRangeAtrRatio >= 1.5 |
| L3_ADX15_TR15 | L3 | AdxDeltaFromQualification >= 1.5 AND CandleTrueRangeAtrRatio >= 1.5 |
| L3_ADX15_ADVERSE2 | L3 | AdxDeltaFromQualification >= 1.5 AND ConsecutiveAdverseCloses >= 2 |
| L3_COMPOSITE | L3 | CloseBeyondFrozenBoundary OR (AdxDeltaFromQualification >= 1.5 AND CandleTrueRangeAtrRatio >= 1.5) |
| L2_COMPOSITE | L2 | Same composite predicate, monitoring from L2 |

NO_GUARD_CONTROL is an eighth output row, not a guard. It never triggers.
A comparison with a required NULL value is false. The composite's boundary branch
can still be true independently of missing ADX/TR values. Clients cannot supply
thresholds or candidate definitions; the endpoint rejects query parameters.

## Timing, prices and economics

For each candidate/basket, choose the earliest completed telemetry candle with
sufficient MaxFilledLevelAfterBar and NULL ExitReasonThisBar. Its time must be
strictly before the actual basket exit. Existing same-candle TP or emergency stop
always wins. A final EndOfData close is not replaced at the same timestamp.

Long exit uses persisted BidClose; Short exit uses BidClose + SpreadPrice.
Include exactly the legs with FillTimeUtc <= trigger time, including same-candle
fills. Later legs are excluded. No highs/lows, invented slippage or homemade profit
formula are used. TriggerLevel means the candidate's monitoring threshold;
MaxFilledLevelAtTrigger reports the actual deepest filled level.

Each included leg uses IMt5TradeCalculator.CalculateProfitAsync with the saved
broker symbol, direction, lots, fill price and shadow exit price. Existing G2.1
validation is reused unchanged (numeric echo tolerance 0.0000000001, exact symbol,
direction and account currency). Failures produce a safe 503 and no workbook;
cancellation propagates. No margin calculation or order is submitted.

Shadow net = sum native profit - sum included persisted EntryCommission
- sum included Lots * saved CommissionPerLotPerSide. Original later exit
commissions are not reused. No-trigger rows retain actual net P/L and zero delta;
trigger-specific fields are NULL.

An exact request-record cache belongs to one SimulateAsync invocation. All five
request fields form the key. No cache crosses invocations or is persisted.
NativeProfitCallCount counts logical calls to the calculator, not internal retry
attempts. Simulation-wide unique count equals cache entries on success. Candidate
call counts attribute cache misses in catalog order; candidate unique request
counts describe that candidate's requirements and can overlap across candidates.

## Reconciliation and reporting

Source validation checks Completed status, approved historical profile, telemetry,
cycle/basket/leg ownership, direction, chronological per-cycle sequence, fill
transitions, final exits, valid prices/lots and source financial totals before any
native request. Missing/partial/inconsistent evidence fails safely. Old runs get:
"Grid breakout research requires a telemetry-enabled Grid backtest."

The control's net P/L, basket count, gross P/L and commissions reconcile exactly
with persisted source totals. Gross and net PF are validated at saved decimal
precision (12 places), and the control retains the stored net PF exactly.
No discrepancies in source money totals are silently corrected.

FixedPathShadowGrossProfit/Loss mean sums of positive/negative **net basket**
outcomes, respectively; their ratio uses the same basis as source NetProfitFactor.
A zero loss denominator produces NULL, not infinity or zero. ActualGrossProfit,
ActualGrossLoss and ActualGrossProfitFactor separately report before-commission
source evidence. Candidate totals are sums of basket rows, never a rerun balance.
AverageDeltaPerTriggeredBasket is NULL when there are no triggers.

SavedLossAmount sums positive deltas only for actual EmergencyStop baskets.
LostWinnerProfitAmount is the absolute sum of negative deltas only for actual
TakeProfit baskets. Worsened losses and cut winners remain visible.
Classifications depend on actual exit reason, independently of the sign of P/L.

Workbook: SUMMARY, CANDIDATES, BASKET_RESULTS, TRIGGERS. Catalog order is retained.
SUMMARY contains source evidence, native counts and the full screening warning.
CANDIDATES repeats the screening label for every aggregate. BASKET_RESULTS has
eight rows per actual basket; TRIGGERS contains only triggered rows. No simulated
ending balance or drawdown is reported. The normal seven-sheet export is unchanged.

## Integration and boundaries

Admin-only GET /api/backtests/grid/{id}/research/breakout-guards/export/excel.
The saved Grid result has one Export Guard Research action and a screening notice.
Existing frontend error handling displays old-run and native-unavailable errors.
The simulator depends only on EmaBotDbContext and IMt5TradeCalculator. It loads an
untracked split-query graph, performs no per-row queries and never SaveChanges.
There are no engine, indicator or market-history calls in the simulator.

Zero migrations/schema changes. Baseline, 4L, EMA, Paper/Demo/Live, bridge protocol
and retries are unchanged. No real saved runs were automatically simulated, no
manual historical backtests or broker activity occurred, and G4B2 was not started.
