# E11.8G1: native historical Grid runner

## Entry point and isolation

`Services/GridHistoricalBacktestEngine.RunAsync` consumes caller-supplied
`Mt5HistoricalExecutionBar` history, `InstrumentCatalogItem`, and a
`GridHistoricalBacktestRequest`. It does not fetch history or read settings,
positions, a database, or an account. No DI, endpoint, persistence, UI, Paper,
Demo, Live, optimizer or order integration is introduced.

The request requires explicit starting balance (tests use 1000), native account
currency, and the monitored symbol's configured **PaperCommissionPerLotPerSide**
snapshot. Future integration must supply those values from the native environment.
There is no TradingSettings, SimulatedAccountBalanceUsdt, FeePercentPerSide, or
EMA signal/stop/target input. The existing native instrument validator is reused
only for its strategy-neutral Bid-chart and instrument evidence checks.

## Architecture

- `Models/GridHistoricalModels.cs`: request/result, cycle, planned level, filled
  leg, basket, event, diagnostic and attributable fatal execution exception models.
  None are EF entities. The result retains the instrument and request snapshots.
- `Services/GridHistoricalBacktestEngine.cs`: chronological G0 orchestration,
  broker direction eligibility, fill economics, basket accounting and reporting.
- `Services/GridHistoricalEconomics.cs`: per-run native call timing/counts,
  response identity/currency validation, and authoritative per-level evidence.
  It delegates once per request with no retries; R5A.2 remains the sole retry owner.
- `Strategy/Grid/GridRangeIndicatorWindow.cs`: incremental Wilder ATR/ADX plus
  a bounded 50-candle range. Exact decimal operation order matches G0; every
  prefix of a deterministic 300-candle test is compared with the G0 calculators.
- `GridRangeEngine` gains an internal snapshot overload. Both public G0 candle
  calls and internal G1 snapshot calls use the same qualification, ownership,
  cooldown and sizing gates. G0 thresholds, sizing and fill rules are unchanged.

## Timeline and reporting range

Closed input bars are sorted by open time. Duplicate/overlapping bars, invalid
OHLC, negative captured spread, and symbol/timeframe mismatches fail before
economics. Native chart mode must be Bid; point size and native instrument
economics must pass the established validator. Current/unclosed bars are ignored.

The reporting interval includes whole bars with open >= requested start and
close <= requested end. Bars closing after the end are excluded entirely. Earlier
supplied bars warm indicators but cannot qualify a cycle or fill a basket. A bar
straddling the start is indicator-only; there is no partial-bar execution. No
warmup is fetched automatically. ActualStart/End and CandleCount describe reporting
bars; WarmupCandleCount describes supplied indicator-only bars.

Each reporting bar first updates the indicators, then advances G0 fills/cooldown,
then attempts a new qualification when idle. A new cycle cannot fill on its own
qualification candle. The three bars AFTER closure cannot qualify; the fourth
may qualify from its newly completed snapshot. Realized net balance is supplied
fresh to G0 risk sizing at each attempt, with balance as available free margin.
Outside long/short exposure is explicitly zero: this isolated historical run has
no knowledge of live broker positions.

## Trade mode and frozen economics

Disabled, CloseOnly and Unknown modes do not create cycles or call the calculator.
Full permits both candidates and retains symmetric five-level preflight: common
lots must be safe for either side. E11.8G1.1 maps LongOnly/ShortOnly to the domain's
GridAllowedDirections policy BEFORE sizing. Only the allowed side is calculated,
used to normalize lots, and tested against risk/margin limits. Prohibited-side
economics cannot constrain or reject a cycle and are never called. The domain
cancels prohibited candidates at construction, without pre-locking direction or
fabricating ambiguity. Default domain policy remains Both for existing G0 callers.
An empty/invalid domain policy fails closed without economics calls.

The result snapshots both sides' five planned prices, selected common lots,
native stop-risk and native required margin per level, side eligibility, range,
anchor, ATR/ADX, spacing, stops, target risk percent/amount and entry equity.
Actual fills retain their own time, planned executable price, lots, native margin,
initial stop-risk evidence, commission and eventual native gross/net P/L.
Prohibited-side planned levels have Allowed=false and null stop-risk/margin.
Directional sizing totals are nullable too: null means uncalculated, never a
fabricated zero. G1 never indexes calculator evidence for prohibited directions.
VolumeLimit includes existing exposure only for allowed directions; isolated G1
still explicitly supplies zero outside exposure on each side.

At each actual fill, CalculateMargin is called with the frozen executable entry
and lots. It must match the saved per-level preflight within 0.00000001 account
currency units. Aggregate used margin must fit balance with **no tolerance**.
A disagreement or unavailable actual margin terminates with an attributable
GridHistoricalExecutionException and cycle context; lots are never shrunk.

At close, CalculateProfit is called for every filled leg with its executable fill
price and the single basket exit price. A missing/mismatched native response
terminates the run rather than dropping an open basket or producing partial
successful accounting. Qualification failures retain their G0 diagnostic identity
and are eligible for a later fresh qualification attempt.

Commission for each leg is lots * configured per-lot-per-side amount at entry and
again at exit. Gross excludes commission; net subtracts both sides. Captured spread
is reconstructed as Ask = Bid + SpreadPoints * PointSize and is never charged
again. No production contract-size/tick-value/pip formula computes risk, margin
or profit.

## Fills, exits and end of data

G0 remains the fill authority: Long Ask-low limit touches, Short Bid-high touches,
frozen fill prices, nearest-to-deepest fills, direction lock, no more than five
legs, and whole-basket TP/stop. All touched same-direction levels fill before
stop processing; stop wins TP conflicts. An unlocked two-sided touch does not
fill; first-fill-bar TP is deferred. There is no inferred tick sequence.

G1 alone adds EndOfData: an open filled basket closes at final reporting Bid close
for Long or reconstructed Ask close for Short, and all remaining candidates are
canceled. This exit reason exists only in the G1 result, not G0. A cycle with no
fills is canceled and diagnosed without creating a completed basket.

Balance changes only on whole-basket realization. Drawdown is peak realized
balance minus current realized balance, with starting balance as the initial peak.
There is no intrabasket mark-to-market drawdown. Win/loss/breakeven use net basket
P/L. Gross and net profit factors independently divide summed positive basket
P/L by absolute summed negative basket P/L. Zero loss denominator returns null,
matching established native reporting. Empty average net P/L is zero.

## Diagnostics and cost bounds

QualifiedCycles counts range-qualified attempts including economic rejections;
Cycles contains accepted snapshots. RejectedQualificationCount counts failed
indicator/range attempts (including warmup within reporting), not active/cooldown
bars or economic rejections. G0 economic failure counters count individual
rejected attempts. AmbiguousFirstSideCount counts ambiguous bars. TradeModeBlockedCount
counts reporting bars blocked by mode; one detail entry records that mode.
NoFillCyclesAtEndOfData counts accepted cycles canceled without fills.

EconomicsCallCount counts calls to the shared calculator, including failures, not
its internal transport retries. EconomicsElapsedMilliseconds measures time inside
those calls, including retry delays owned by that authority. Evidence caches are
cleared between qualification attempts, retaining only bounded current-cycle data.

Sorting costs O(N log N); indicator processing costs O(50*N) with O(50) rolling
indicator storage. There are no whole-prefix rescans in production. Results/events
naturally use O(N) space. A successful Full preflight costs 30 shared calculator
calls; LongOnly/ShortOnly costs 15 (ten profit and five margin calculations);
each filled leg adds one actual-margin and one closing-profit call. A 15,000-bar
unresolved-cycle test stays at 30 total calls. Multiple-cycle tests verify fresh
equity/sizing, cooldown, range/ATR changes and exact realized metrics.
