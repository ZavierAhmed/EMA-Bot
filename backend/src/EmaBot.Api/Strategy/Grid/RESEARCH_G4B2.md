# G4B2: real historical Grid breakout-guard research

## Purpose and boundary

The user reports that G4B1 screening found L3_ADX15_ADVERSE2 improved all three
source months. This is the motivation for a real historical rerun, not an
independently verified profitability claim. No month was manually rerun during
implementation.

GRID_RANGE_BREAKOUT_GUARD_RESEARCH_V1 is HISTORICAL RESEARCH ONLY.
It is not approved for Paper/Demo/Live. There is no optimizer or execution
registration. G4B1 remains fixed-path screening; this profile actually reruns
future cycles against the balance and timing produced by earlier exits.

## Frozen profile

The profile has the same settings as GRID_RANGE_V1: five equal-lot levels,
stop at level six, anchor TP, 1% total basket price-risk, 50-bar range,
ATR14 x 0.50 spacing, ADX14 <= 20 qualification, and three completed cooldown bars.
Its immutable behavior identity is L3_ADX15_ADVERSE2, independent of level count.
GRID_RANGE_V1 and GRID_RANGE_4L_RESEARCH_V1 have no guard identity.

GridBreakoutGuardRules is the one shared predicate used by the selected G4B1
candidate and the G4B2 historical runner:

- MaxFilledLevelAfterBar >= 3
- AdxDeltaFromQualification >= 1.5
- ConsecutiveAdverseCloses >= 2

All three are required and inclusive. NULL ADX delta cannot trigger. The other
six G4B1 predicates remain unchanged. Clients supply no guard parameters.

## Completed-candle order

1. Update existing ATR/ADX/range indicators.
2. Process the normal Grid bar and all same-side fills.
3. Validate each new fill using native margin evidence.
4. Preserve the normal emergency-stop/TP decision; these exits have first priority.
5. Record the active-bar telemetry row.
6. Only when the normal transition has no exit, evaluate the shared guard.
7. On a match, close the entire basket at the completed-candle executable close,
   mark that same telemetry row BreakoutGuard, and apply native exit economics.
8. Start normal cooldown, then allow fresh qualification and sizing.

The adverse counter starts with the first active basket candle (zero comparisons).
Long compares current BidClose < previous active BidClose; Short compares >.
Adverse increments; equality or a favorable close resets to zero. Filling L3 does
not restart the counter. Every same-candle fill is included in a guard exit.

Long exit = BidClose. Short exit = BidClose + SpreadPoints x PointSize.
No high/low, anchor replacement, invented slippage, or homemade profit formula.
Every open leg uses IMt5TradeCalculator.CalculateProfitAsync through unchanged
G2.1 validation. Entry commission is retained and exit commission is lots times
the captured commission per lot per side. Bridge retries and calls are unchanged.

## Closure, equity and future cycles

The internal CloseHistoricalResearchBreakoutGuard path requires an active locked
basket, no existing exit, and the current processed-bar timestamp. It records
BreakoutGuard, time and price; closes filled levels; cancels unfilled levels; and
starts the configured cooldown. It is not a public manual-exit API.

The exit candle cannot requalify. Exactly three subsequent completed bars are
blocked; the next may qualify if the existing market conditions permit. Fresh
range, anchor, spacing, qualification and native sizing are computed normally.
Balance increases by native net leg P/L. The next cycle receives that balance as
EntryEquity and targets 1% of it. Future baskets are generated anew.

Deterministic Long and Short tests demonstrate changed first-exit timing and
next-cycle equity versus the no-guard baseline, exact next-cycle risk, new native
sizing requests and persisted cycle equity/risk/lots. A separate domain test
proves closure, cancellation and exactly three blocked bars. Lot-step rounding
may retain the same lot size; native sizing still runs for every fresh cycle.

## Persistence, API, UI and export

Existing tables store the new StrategyId, LevelCount=5, real EndingBalance,
future EntryEquity, basket BreakoutGuard exit, Exit event detail, and the final
telemetry row with the same timestamp. There is no duplicate telemetry candle.
No schema changes or migrations are needed.

POST /api/backtests accepts the new identity with only the normal symbol,
interval, dates and starting balance fields. The UI labels both selector and
saved result RESEARCH ONLY, explains the frozen rule, and exposes no thresholds.
Normal Export Excel remains available. Export Guard Research is hidden for this
profile, and the G4B1 source validator rejects guarded runs before native calls.
Shadow sources remain baseline five-level and unguarded four-level runs only.

The normal seven sheets remain SUMMARY, CYCLES, BASKETS, LEGS, EVENTS,
DIAGNOSTICS and TELEMETRY. For guarded runs, SUMMARY adds the frozen guard
thresholds and BreakoutGuardExits counted from saved baskets. LEGS appends saved
basket ExitReason, ExitTimeUtc and ExitPrice. Other profiles retain their export
columns. Export reads persisted evidence and never reruns the engine.

Baseline/4L golden trading fixtures, G4B0 telemetry assertions and G4B1 tests
remain authoritative regressions. EMA, Paper/Demo/Live, broker execution,
qualification and indicator formulas are unchanged. No real saved-run shadow
simulation, manual historical backtest or broker order is part of this milestone.
