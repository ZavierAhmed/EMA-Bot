# G4B4: real historical L4 breakout-guard research

## Why this profile exists

The user reports that G4B3 shadow screening found L4_ADX15_ADVERSE2 cut fewer
eventual winners while preserving most loss interception. G4B4 tests that rule
with a genuine end-to-end historical rerun. This motivation is not an independently
verified profitability conclusion, and no such conclusion is encoded in the code.

New identity: GRID_RANGE_BREAKOUT_GUARD_L4_RESEARCH_V1.
HISTORICAL RESEARCH ONLY; not approved for Paper/Demo/Live or optimizer use.
The existing G4B2 L3 profile remains separately available and unchanged.

## Frozen settings and shared rules

Five equal-lot levels; emergency stop at level six; anchor TP; 1% total basket
price-risk; 50-bar range; ATR14 x 0.50 spacing; ADX14 <= 20 qualification;
three completed cooldown bars; no martingale.

The only new behavior is the frozen L4_ADX15_ADVERSE2 rule:

- MaxFilledLevelAfterBar >= 4
- AdxDeltaFromQualification >= 1.5
- ConsecutiveAdverseCloses >= 2

All conditions are inclusive and required. NULL ADX delta cannot trigger.
GridBreakoutGuardL4Rules provides the constants and predicate shared by the
G4B3 L4 shadow candidate and G4B4 real historical execution.
GridBreakoutGuardRules remains unchanged: L3_ADX15_ADVERSE2, level >= 3,
ADX delta >= 1.5, adverse closes >= 2.

GridHistoricalBreakoutGuard.Resolve selects immutable metadata and predicate by
profile.GuardId. No guard is inferred from level count. Baseline and four-level
profiles have no guard. Unknown guard identities are rejected. There are no
client-defined rules or editable thresholds.

## Completed-candle order

1. Update existing indicators.
2. Process normal Grid fills and validate every new fill with native margin.
3. Normal emergency stop or TP has first priority.
4. Record the active telemetry row.
5. Only if no normal exit occurred, evaluate the selected L3 or L4 guard.
6. If it matches, close the entire basket at the completed-candle executable close.
7. Mark the same telemetry row BreakoutGuard; do not duplicate it.
8. Apply native exit economics and normal cooldown.
9. Form fresh future cycles with new qualification and current-equity sizing.

The adverse counter starts at zero on the first active basket candle. Long uses
current BidClose < previous active BidClose; Short uses >. Adverse increments;
equal or favorable resets to zero. Filling L4 does not reset the counter.
Same-candle L4 and L5 fills belong to the basket closed by the guard.

Long exit = BidClose. Short exit = BidClose + SpreadPoints x PointSize.
Every filled leg uses IMt5TradeCalculator.CalculateProfitAsync through unchanged
G2.1 validation. Entry commission is retained; exit commission is lots times
the captured commission per lot per side. No homemade P/L, slippage, batching,
or MT5 bridge changes are introduced.

## Closure and genuine future path

The existing internal CloseHistoricalResearchBreakoutGuard implementation is
reused unchanged. It requires an active locked basket and current processed-bar
time, rejects an already-exited cycle, closes filled levels, cancels unfilled
levels and starts the three-bar cooldown. There is no public manual-close API.
No same-bar qualification; exactly three subsequent completed bars are blocked.
The following bar may qualify if the existing market conditions permit.

Balance changes by native net P/L. The next cycle receives that balance as
EntryEquity, targets 1%, and performs fresh native lot sizing. Later baskets,
realized drawdown and final P/L follow the resulting historical path.

Long and Short deterministic comparison fixtures prove G4B2 exits at L3 while
G4B4 stays open, then either recovers to TP or reaches L4 and exits. Multi-cycle
fixtures verify changed equity, exact next-cycle risk, new native sizing evidence
and independent future baskets versus baseline. Lot-step rounding can retain
the same lot count; fresh sizing still occurs. Closure tests verify all three
blocked cooldown bars and fresh qualification afterward.

## Persistence, API, UI and Excel

Existing tables store the new StrategyId, five levels, BreakoutGuard basket exit,
Exit event with Detail=BreakoutGuard, and one final telemetry row at the basket
exit time. The exit reason remains BreakoutGuard for both L3 and L4 profiles.
Future equity, target risk, sizing evidence and real ending balance persist
normally. No migrations or database columns are added.

POST /api/backtests routes the new identity to RunGrid with the unchanged six
request fields. The selector includes Grid Range Research — L4 Breakout Guard
alongside the existing L3 option. Saved results label RESEARCH ONLY, L4 rule,
monitor from L4, five levels, stop level six and updated future equity.

Normal Excel retains seven sheets: SUMMARY, CYCLES, BASKETS, LEGS, EVENTS,
DIAGNOSTICS, TELEMETRY. Profile/guard resolution supplies correct L3 or L4
metadata and saved-basket BreakoutGuardExits counts. Both guarded profiles have
LEGS ExitReason/ExitTimeUtc/ExitPrice columns. Baseline/4L export shapes remain
unchanged. Export reads persisted evidence without invoking the engine.

Both guarded profiles show only Export Grid Excel. Baseline retains both shadow
research actions; four-level retains G4B1 only. G4B1 explicitly whitelists only
GRID_RANGE_V1 and GRID_RANGE_4L_RESEARCH_V1. G4B3 remains baseline-only, so both
guarded profiles are rejected before native shadow calculations.

## Boundaries and verification

G4B1 catalog and G4B3 candidate identities/order/thresholds remain unchanged;
only the G4B3 L4 implementation now references the shared L4 rule. Baseline/4L
and G4B2 regression tests remain authoritative. No changes to qualification,
indicators, fill rules, risk, TP, stop, spacing, commission or cooldown.
EMA, Paper, Demo, Live and broker execution are unchanged. Only deterministic
unit/integration fixtures run during implementation; no real June/July/August
backtest or saved-run experiment is launched automatically.
