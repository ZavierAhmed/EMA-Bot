# EMA-Bot strategy origin and kernel lock

## Optional adaptive initial stop (E11.0)

`Adaptive initial SL (reversal-aware)` is experimental and defaults to **Off**. Off preserves the legacy initial-stop rule exactly: the latest strict two-left/two-right pivot before the crossover, otherwise the prior-ten-completed-candle extreme. The stop is selected first and the existing R:R target formula then calculates TP; trailing thresholds and TP extension are unchanged.

When enabled, the stop uses the actual closed signal candle (the confirmation candle when confirmation is enabled, or the continuation candle for a re-entry), never a future candle. ATR14 uses Wilder RMA of True Range `max(high-low, abs(high-previousClose), abs(low-previousClose))`. Reversal power is 0–100: body/ATR (45 points), directional close location (35), and EMA-gap state (Expanding 20, Unchanged 10, Contracting 0). Weak (<45) anchors the signal candle and buffers by 0.10 ATR; Normal (45–<70) uses signal plus one prior candle with a 0.20 ATR buffer; Strong (>=70) uses signal plus two prior candles with a 0.30 ATR buffer. Long stops are below the selected low, Short stops above the selected high. If ATR14 is unavailable, the legacy structure is used and recorded as `AdaptiveLegacyFallback`.

The broker stop-level and configured maximum-stop-distance checks remain authoritative after executable entry is known. The adaptive setting is snapshotted per Paper session and backtest run, so changing global settings never alters an already-running session.

## Provenance

EMA-Bot was inspired by the project owner's supplied Pine Script strategy,
"EMA Cross Strategy." The supplied source identified its author as © kirilov,
carried a Mozilla Public License 2.0 (MPL-2.0) notice, and used Pine Script
version 4.

This document records provenance and the strategy concepts that informed
EMA-Bot. It does not reproduce or include third-party Pine source code, does
not alter this repository's licensing, and does not claim that EMA-Bot is an
exact reproduction of that strategy.

## Original Pine kernel

The original strategy used these defaults:

- Fast EMA: 10
- Slow EMA: 20
- Trade direction: Both
- Date-range controls
- Realtime evaluation enabled with `calc_on_every_tick = true`

Its signal kernel was a fast/slow EMA crossover: a crossover produced a long
entry and a crossunder produced a short entry. In Both mode, an opposite
`strategy.entry` can close and reverse an existing position. In Long-only or
Short-only mode, the opposite crossover is used as an exit condition.

## Current EMA-Bot evolution

EMA-Bot deliberately evolved into a research and simulation system with a
different, explicitly locked core kernel:

- Fast EMA: 9
- Slow EMA: 15
- Bull crossover: previous EMA9 <= previous EMA15 and current EMA9 > current
  EMA15.
- Bear crossover: previous EMA9 >= previous EMA15 and current EMA9 < current
  EMA15.
- Signals are confirmed from closed candles only.

The current optional/enhancement layers are confirmation candles, EMA100
filtering, minimum EMA gap, swing/pivot stop selection with fallback stop,
maximum stop distance, configurable R:R, trailing management, same-regime
re-entry, and the H2 higher-timeframe regime filter. These are EMA-Bot
strategy/research concepts, not Binance concepts.

## Intentional deviations

EMA-Bot is not a direct port of the original script. In particular:

| Original Pine behavior | Current EMA-Bot behavior |
| --- | --- |
| Defaults to EMA 10/20. | Uses EMA 9/15. |
| May calculate on every realtime tick. | Makes signal decisions on closed candles only. |
| In Both mode an opposite crossover may close/reverse the position. | An opposite crossover does **not** automatically close an open position; the existing SL/TP/trailing lifecycle continues. |
| Has simple direction/date controls. | Adds deterministic research, filters, stop selection, sizing/cost simulation and detailed trade lifecycle records. |

Closed-candle confirmation is intentional: it supports deterministic replay,
prevents intrabar crosses that disappear before close, gives reproducible
live/backtest behavior, and is the safer foundation for broker migration.
EMA-Bot must not add intrabar signal evaluation as an incidental execution
provider change.

## Rules locked through broker migration

Until an explicitly approved strategy-research change is made, broker work
must not change:

- the EMA9/15 crossover inequalities above;
- the closed-candle signal rule;
- next-bar entry semantics used by the deterministic engine;
- confirmation, EMA100, EMA gap, swing/fallback stop, maximum-stop-distance,
  R:R, trailing, same-regime re-entry, or H2 semantics; and
- the rule that an opposite crossover does not automatically close an open
  EMA-Bot position.

Broker adapters may change how a validated decision is priced, sized,
submitted, filled, costed and recorded. They must not silently change the
strategy decision that created it.

## Deferred strategy research

A future, opt-in research setting may consider **Opposite Crossover Behavior**
with these values:

- Ignore
- Exit Position
- Reverse Position

It is not implemented, must not change the current default behavior, and is
outside E1 and the broker-migration work.
