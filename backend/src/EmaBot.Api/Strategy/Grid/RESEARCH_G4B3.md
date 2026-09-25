# G4B3: stricter Grid guard shadow screening

The user reports that G4B2 reduced losses but still cut too many recovering
L3/L4 baskets. This motivates screening; it is not an independently verified
profitability claim. G4B3 changes no trading strategy.

## Separate frozen catalog

| Output order / candidate | Minimum filled level | Minimum ADX delta | Minimum consecutive adverse closes |
| --- | --- | --- | --- |
| NO_GUARD_CONTROL | 0 (never triggers) | unavailable | unavailable |
| CURRENT_L3_ADX15_ADVERSE2 | 3 | 1.5 | 2 |
| L4_ADX15_ADVERSE2 | 4 | 1.5 | 2 |
| L3_ADX20_ADVERSE2 | 3 | 2.0 | 2 |
| L3_ADX15_ADVERSE3 | 3 | 1.5 | 3 |
| L4_ADX20_ADVERSE2 | 4 | 2.0 | 2 |

All three thresholds must be met inclusively. NULL ADX delta cannot trigger.
The current reference uses GridBreakoutGuardRules constants and predicate, so
it stays identical to the real G4B2 guard. The four stricter candidates test
waiting for L4, stronger ADX, another adverse close, or L4 plus stronger ADX.

The catalog is separate from G4B1's seven guards. HTTP clients cannot supply a
catalog or thresholds. Both shadow entry points use one private simulation
core with a server-selected catalog; the real historical engine is not invoked.

## Source and timing

Only telemetry-enabled GRID_RANGE_V1 runs are accepted. Four-level, guarded,
and old runs are rejected. All existing source integrity checks still apply.
Runs 8 (July), 9 (August) and 10 (June) are intended future sources; this milestone
does not automatically simulate those runs or launch historical backtests.

For each candidate/basket, use the earliest matching completed telemetry candle
strictly before actual exit. Same-candle TP/stop wins; final EndOfData timestamp
is also excluded. Include all legs with FillTimeUtc <= trigger time, including
same-candle fills and excluding later fills.

Long exit = persisted BidClose. Short exit = persisted BidClose + SpreadPrice.
Every included leg uses native CalculateProfit with unchanged G2.1 validation.
Net P/L subtracts persisted entry commissions and lots times saved per-side
commission for the shadow exit. No original later exit commission is reused.
The exact full-request cache (symbol, direction, lots, open price, close price)
lives only within one invocation. Native counts keep the G4B1 definitions.

No-trigger rows retain actual net P/L, zero delta and null trigger fields.
Control matches saved basket count, net P/L and net profit factor. Candidate
net/delta aggregates sum their basket rows. Profit factor uses net basket gains
and losses; zero loss denominator is blank. No ranking or winner is selected.

## Export and UI

Admin-only GET /api/backtests/grid/{id}/research/strict-breakout-guards/export/excel
rejects all query parameters. Filename: grid-strict-guard-research-{id}.xlsx.
Sheets: SUMMARY, CANDIDATES, BASKET_RESULTS, TRIGGERS. CANDIDATES has exactly six
data rows in catalog order, with all aggregate fields and explicit thresholds.
A strict-specific DTO adds threshold evidence without changing G4B1 columns.

SUMMARY identifies STRICT BREAKOUT GUARD FIXED-PATH SHADOW SCREENING and warns:

> SCREENING ONLY. This is not a real strategy backtest. Future balance, sizing,
> cooldown, qualification and later baskets were not rerun.

A promising candidate still requires a real end-to-end historical rerun.

Saved baseline results have normal Excel, Guard Research and Strict Guard
Research actions. Four-level results keep normal Excel and G4B1 Guard Research.
Guarded G4B2 results have normal Excel only. No threshold controls are added.

G4B1 catalog, output shape and semantics remain unchanged. Normal Grid export,
G4B2 guard, baseline/4L rules, indicators, risk, cooldown, EMA and Paper/Demo/Live
are unchanged. There are no migrations, schema changes, simulator SaveChanges,
market-history calls or broker orders.
