# G4A: Four-level historical research profile

## Motivation and scope

The user-reported June, July and August BTCUSDm baseline samples were all
negative, with repeated failures in baskets reaching Level 5. These observations
motivate an experiment; they are not independently verified here or encoded as
application behavior.

| Contract | Baseline | Research |
| --- | --- | --- |
| StrategyId | GRID_RANGE_V1 | GRID_RANGE_4L_RESEARCH_V1 |
| Levels per side | 5 | 4 |
| Entries | Anchor +/- n * spacing, n=1..5 | Anchor +/- n * spacing, n=1..4 |
| Hard stop | Anchor +/- 6 * spacing | Anchor +/- 5 * spacing |

Both retain 1% **total basket price-risk**, equal normalized lots, 50-bar range,
ATR14 * 0.50 spacing, ADX14 <= 20, frozen-anchor TP and three-bar cooldown.
Native MT5 profit/margin authority, TradeMode filtering, Bid/Ask fills, conservative
same-bar sequencing, direction lock, commission and EndOfData behavior are shared.
No martingale, arbitrary parameter API, batching or caching is introduced.

**Research only. Not approved or registered for Paper/Demo/Live.**

## Implementation and hard-coded assumption audit

- `GridHistoricalStrategyProfile` owns the two immutable, server-selected profiles.
  Missing direct historical profile identity defaults to baseline; the HTTP API
  still defaults omitted StrategyId to EMA. Explicit profile/settings mismatches
  fail validation. Strategy identity is carried in the request/result, never
  inferred after execution from level count.
- Baseline-only defaults: `GridRangeSettings` defaults to five levels; existing
  baseline tests still assert ten planned prices and stop level six. The old
  domain invalid-settings test now rejects three levels instead of approved four.
- Profile-dependent: cycle stops, risk stops and the positive-stop qualification
  guard use LevelCount + 1. ATR, ADX and qualification thresholds are unchanged.
  Volume limits and the shared fill engine already iterate actual levels.
- Presentation: persistence reads actual request settings; existing schema and
  saved rows are unchanged. Excel SUMMARY and dynamic planned-price columns read
  persisted settings (10 baseline columns, 8 research columns). Saved UI results
  show actual identity and level count, with an explicit research warning.
- The existing 40-operation workload budget remains a conservative five-level
  upper bound for both profiles; retry/protocol behavior is unchanged.
- Grid list/detail/delete routes already use the persisted strategy identity.
  Export filenames are profile-neutral. No execution paths are modified.

## Required manual experiment after implementation

The user will manually run **BTCUSDm, 3m, USD 1,000 starting balance**, selecting
`GRID_RANGE_4L_RESEARCH_V1`, for:

- June 1–30, 2026
- July 1–31, 2026
- August 1–31, 2026

Use full UTC start/end dates and compare exported evidence with the corresponding
baseline samples. This milestone does not automatically run these experiments,
apply migrations, place orders, or begin G4B.
