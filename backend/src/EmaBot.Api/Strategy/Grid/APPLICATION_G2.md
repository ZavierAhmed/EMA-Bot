# E11.8G2 application integration

## Scope and deployment prerequisite

G2 adds backtest-only persistence, orchestration, API, Excel and UI. G0/G1/G1.1
strategy code is unchanged. No Paper, Demo, Live, optimizer or broker-order path
uses Grid. The separate nullable-zero Paper diagnostic is untouched.

Migration: `20260916103317_AddGridHistoricalBacktests`. It creates seven dedicated
Grid tables with indexes and cascading Grid-only foreign keys. No existing table,
column or migration is changed. Decimal evidence uses decimal(28,12); prohibited
direction economics remain nullable. The model snapshot matches the migration.
The migration was generated and validated, **not applied**. It must be applied
through the application's deployment process before Grid persistence is usable.

## Architecture and changed source files

- `Models/GridBacktestEntities.cs`: Run, Cycle, PlannedLevel, Basket, Leg, Event,
  Diagnostic entities. Planned levels retain both sides and explicit Allowed flags.
- `Data/GridBacktestModelConfiguration.cs`, `Data/EmaBotDbContext.cs`: Grid schema,
  precision, uniqueness, and Grid-only cascades. Existing EMA entities are intact.
- `Migrations/20260916103317_AddGridHistoricalBacktests.cs`, its designer, and
  `Migrations/EmaBotDbContextModelSnapshot.cs`: the single additive migration.
- `Services/GridBacktestService.cs`: exact enabled MT5 symbol validation, native
  catalog/account/commission/history reads, G1.1 invocation and atomic persistence.
  IGridHistoricalBarSource delegates directly to the existing native execution-bar
  provider and provides a test seam; it does not invent spread or economics.
- `Services/GridBacktestPersistence.cs`: explicit mapping of successful results
  into the normalized graph, preserving native evidence and nullable semantics.
- `Services/GridBacktestBudget.cs`: conservative Grid-aware bounded workload budget.
- `Controllers/BacktestsController.cs`: optional StrategyId dispatch and Grid error
  mapping; omitted or explicit EMA_TREND_V1 retains the prior EMA response.
- `Controllers/GridBacktestsController.cs`, `Controllers/GridBacktestResponses.cs`:
  dedicated Admin-only Grid retrieval/list/delete/export and scalar typed DTOs.
- `Services/GridBacktestExcelExport.cs`: saved-evidence-only six-sheet export.
  `Services/BacktestExcelExport.cs` only exposes its existing low-level Sheet and
  Workbook helpers internally for reuse; its EMA export logic is unchanged.
- `Program.cs`: registers the separate Grid service, engine and history adapter.
- `frontend/src/api.ts`, `gridBacktestTypes.ts`, `backtestForm.ts`,
  `pages/BacktestsPage.tsx`, `pages/GridBacktestResult.tsx`: typed Grid client API,
  EMA-default strategy selector, explicit balance, result/leg view and history.
- `backend/tests/EmaBot.Api.Tests/GridBacktestIntegrationTests.cs` and
  `frontend/tests/gridBacktests.test.mjs`: integration and frontend regressions.

## API contracts

All routes require Admin authorization; existing antiforgery handling applies to
POST/DELETE. The client sends only research inputs:

```json
{
  "strategyId": "GRID_RANGE_V1",
  "symbol": "EURUSDm",
  "interval": "3m",
  "startUtc": "2026-07-01T00:00:00.000Z",
  "endUtc": "2026-07-31T23:59:59.999Z",
  "startingBalance": 1000
}
```

- `POST /api/backtests`: omitted StrategyId or EMA_TREND_V1 returns the existing
  EMA detail contract. GRID_RANGE_V1 requires positive StartingBalance and returns
  GridBacktestDetailResponse with top-level strategyId, run, cycles (with planned
  levels), baskets (with filled legs), events and diagnostics. Unknown IDs fail 400.
- `GET /api/backtests/grid`: latest 30 Grid scalar run responses, no EMA fields.
- `GET /api/backtests/grid/{id}`: persisted Grid detail, no native-service calls.
- `GET /api/backtests/grid/{id}/export/excel`: persisted workbook only.
- `DELETE /api/backtests/grid/{id}`: deletes only the selected Grid graph.

Server authority supplies currency, configured PaperCommissionPerLotPerSide,
instrument specification, TradeMode, captured spread, volume rules and native
calculator. Extra client economics fields are ignored; they cannot override these.
G2 always supplies the frozen default Grid settings (1%, five levels, three bars).

Invalid requests, exact-symbol/configuration failures and invalid native input
return 400. Provider rate limit/timeout/unavailability maps to 429/504/503.
Execution economics failures return 503. A server deadline returns 504; client
cancellation propagates. No failure is persisted as a completed run. G1's
diagnostic-only risk/margin-unavailable outcomes are also rejected by the service
before saving, so application history cannot claim successful missing economics.

## Warmup, atomicity and cost

The service fetches captured closed native bars from 100 nominal timeframe bars
plus seven calendar days before the requested start, through the requested end.
This allows common market closures; it cannot guarantee provider availability.
If fewer bars are available, G1 qualifies naturally only once enough exist.
Requested dates remain unchanged. G1 treats pre-start bars as indicator-only and
persists separate warmup/reporting counts and actual reporting boundaries.

One SaveChanges writes the completed run and its entire graph under EF's normal
transaction semantics. Nothing is added until the engine succeeds. Save failure
clears tracked changes. Delete loads the graph and uses only Grid cascade FKs.
Tests use isolated in-memory stores; no production migration is applied.

Grid budgeting permits up to 40 logical operations per reporting candle: 30 for
Full preflight, five actual margins and five closing profits. Rejected cycles may
retry on the next candle. Native retries are budgeted with the shared authority's
attempt count, without adding retries. History pages include warmup. The chosen
deadline remains within the configured existing maximum (30 minutes); client wait
remains 31 minutes. EMA workload budgeting is unchanged.

## Export and UI

Sheets: SUMMARY, CYCLES, BASKETS, LEGS, EVENTS, DIAGNOSTICS. Monetary units come from
the saved account currency. Blank cells mean null/unavailable; numeric zero stays
zero. CYCLES includes all ten planned prices and nullable directional totals.
SUMMARY includes the full run/native snapshot, frozen rules, commission, metrics
and call evidence. Events retain ordering; ambiguity is also listed diagnostically.
Raw provider exception text and stack traces are not saved or exported.

Backtests defaults to EMA Trend. Selecting Grid shows the frozen strategy
explanation, native preview, date-only inputs and required explicit starting
balance (initially 1000). Dates serialize as SOD/EOD UTC. There are no editable Grid
strategy parameters. Grid results show basket metrics, realized drawdown and
expandable filled legs. Separate Grid/EMA history sections identify the strategy;
Grid supports opening, exporting and deleting saved runs after reload.

Frontend validation uses the existing TypeScript/Vite/ESLint toolchain. The new
dependency-free Node test suite transpiles actual modules with the existing
TypeScript dependency and renders React components without executing effects or
requesting a real backtest: `node --test tests/gridBacktests.test.mjs` from frontend.
No manual historical Grid run or broker execution was performed during validation.
