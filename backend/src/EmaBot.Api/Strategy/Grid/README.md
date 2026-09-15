# GRID_RANGE_V1 domain contract (E11.8G0)

This folder is an isolated domain engine. Nothing registers it with DI or connects
it to backtests, persistence, APIs, Paper, Demo, Live, optimization, or orders.

## Structure

- `GridRangeModels.cs`: settings, immutable indicator/qualification/risk snapshots,
  levels, direction/status/exit enums, attributable diagnostics, and cycle state.
- `GridRangeIndicators.cs`: completed-bar qualification and independently callable
  Wilder ADX14. Reuses the existing strategy-neutral `AtrCalculator.Wilder14` unchanged.
- `GridRangeRiskSizer.cs`: shared `IMt5TradeCalculator` profit/margin preflight.
- `GridHistoricalFillRules.cs`: captured spread reconstruction and pure price rules.
- `GridRangeEngine.cs`: one-symbol cycle ownership, serialized creation/transitions,
  direction lock, basket closure, chronological deduplication, and cooldown.

## Frozen rules

Only closed candles whose close time is at or before the supplied evaluation time
participate. The last 50 completed bars (including qualification candle) determine
the high/low range. ATR uses all supplied completed history with the existing
14-bar Wilder seed. ADX seeds directional sums from transitions 1 through 14,
averages DX at indices 14 through 27, then uses Wilder smoothing. Tied directional
moves contribute neither direction; zero directional movement yields ADX zero.

ADX must be at most 20, ATR positive, and qualification close within half a spacing
of midpoint anchor. Spacing is ATR * 0.50. Non-positive price geometry is rejected.
Time, range, ATR, ADX, anchor, spacing, five levels, risk percent, and stops are frozen.
V1 enforces exactly five levels: anchor +/- 1..5 spacings; stops +/- 6 spacings;
whole-basket TP is anchor. No EMA settings or entry/exit logic are involved.

Target price risk is equity * risk percent / 100 (default 1%). Each of the five
planned executable prices is passed to native CalculateProfit with the hard stop.
Both candidate directions must pass, using ONE common lot size safe for either
direction. Native minimum-volume loss estimates the candidate, rounded DOWN on
the existing native volume lattice `VolumeMin + N * VolumeStep`. Every candidate
leg is then revalidated with native profit; any unsafe revalidation fails closed.
There is no risk tolerance permitting budget overshoot, synthetic economics,
martingale, or local calculator retry. Retry remains owned by the shared calculator.

VolumeMax caps each leg. Positive VolumeLimit caps all five planned legs plus
existing same-direction volume; null/zero means no broker aggregate cap, matching
the native convention. Minimum-volume over-risk is specifically diagnosed as
RiskBelowMinimumVolume. An impossible volume or unsafe revalidation is
RiskCannotBeSafelySized. CalculateMargin sums all five entries on EACH candidate
side. Each mutually exclusive basket must fit min(equity, free margin). A shortage
rejects without reducing lots, level count, or changing sizing mode. Commission
is separate from the price-risk budget; later accounting must subtract costs from
gross results. G0 does not implement PnL or commission accounting.

## Deterministic OHLC assumptions

Bid bars retain their captured constant bar spread; Ask = Bid + spread points *
point size. Long limit touches use Ask low, short limit touches use Bid high.
Long exits use Bid; short exits use reconstructed Ask. Fill and exit prices are
the frozen level/anchor/stop prices, with no invented gap improvement.

The first fill locks direction and cancels every opposite candidate. Multiple
touched same-side candidates fill nearest-to-deepest, at most five total. All
touched same-side levels fill BEFORE emergency-stop evaluation, including on the
first fill bar. Emergency stop always wins over TP for an active basket. All
filled legs close together; unfilled candidates are canceled.

Two otherwise unspecified OHLC sequencing cases are resolved explicitly:

- Before direction lock, a bar touching both candidate sides produces
  AmbiguousFirstSide and no fills. The cycle stays eligible for a later bar.
- TP is deferred on the first-fill bar: an anchor touch could precede entry.
  Emergency-stop processing still applies on that bar.

These choices avoid inventing tick ordering; they are not tick-accurate simulation.
Deferring ambiguous first-side bars can omit trades; it is not a claim of a
worst-case PnL bound across all possible tick paths.

## Ownership and reset

Use one shared engine owner per symbol, and feed completed bars chronologically.
Duplicate/older close times are ignored. Qualification bars cannot fill their own
new cycle, and bars opening before qualification cannot fill it. Call ProcessBar
before TryCreateAsync when advancing time through cooldown. The three completed
bars AFTER the exit bar are ineligible, including the final cooldown bar; the next
completed candle may qualify. Requalification recomputes indicators, prices, risk,
and margin and creates a distinct snapshot. Stale qualification times are rejected.
G1 must provide the per-symbol owner registry and runtime integration separately.

Focused tests are in `backend/tests/EmaBot.Api.Tests/GridRangeTests.cs`, including
an independent closed-form ADX reversal reference and exact decimal volume steps.
