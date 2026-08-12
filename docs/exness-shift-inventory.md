# Exness shift migration inventory (E0)

## Phase E3 completed

The application core uses neutral market contracts. Historical research is
temporarily supplied by the legacy Binance kline adapter only; it remains in
place for backtests, optimizer runs, diagnostics, charts, and strategy preview.
Binance live streaming, product-facing symbol discovery, and the Binance
controller have been removed. Live market bars and new instrument discovery are
unavailable pending the MT5 provider and MT5/Exness catalog. No MT5, Exness,
Named Pipe, strategy, cost/sizing, database, or migration implementation was
added in this phase.

## Phase E4 completed

The execution foundation now has broker-neutral `PositionExposure`, deterministic
instrument-volume normalization, and pluggable percentage-notional and per-lot
commission cost models. The current application still uses its legacy notional
sizing and percentage fee behavior by default. Bid/Ask execution, spread,
swap, slippage, account-currency conversion, MT5 margin validation, and broker
instrument specifications remain deferred until a provider supplies real data.

## Phase E5 completed

The application now defines neutral instrument-catalog, quote-provider, and
provider-capability contracts, plus MT5-native timeframe capability metadata.
Legacy Binance history remains the only configured research source; there is no
MT5 connection, catalog, quote feed, live bar feed, or execution provider.
EMA-Bot's canonical `3d` timeframe remains supported, while MT5 does not
natively support it; no three-day aggregation is implemented. Named Pipe,
MQL5 bridge, actual MT5 catalog/quotes, execution, commission discovery, swap,
and account data remain deferred.

## Scope and branch safety

This is an architecture and dependency inventory only.  It was prepared on
`Exness_shift`, branched from the then-current `main` commit
`7f99ff49378628aba8fe688193f23d44d7822fe8`.  No Binance code, strategy
logic, database schema, migration, configuration value, or frontend behavior
is changed by E0.

The strategy provenance and locked decision kernel are documented separately
in [strategy-origin.md](strategy-origin.md). Broker migration must not
accidentally change the EMA9/15 crossover inequalities, closed-candle rule,
next-bar entry semantics, optional filters, or existing position-management
semantics.

`main` remains the preserved Binance USD-M Futures research implementation.
The target branch must make changes in the staged order below; it must not
attempt an MT5 or Exness connection until the broker-neutral contracts and
their tests exist.

## E2 implementation status

E2 extracted the shared market concepts into `EmaBot.Api.Market`: `Candle`,
`StrategyTimeframes`, `IHistoricalMarketDataProvider`,
`IMarketBarStreamProvider`, `MarketBarUpdate`, `InstrumentSpec`, and
`MarketQuote`. Strategy, backtest, H2, optimizer, diagnostics, trade charts,
and internal Paper now depend on those neutral contracts. E3 retains the
history-only `BinanceHistoricalMarketDataProvider` for research and registers
an unavailable neutral live-stream provider pending MT5; no MT5 or Exness
support is implied.

## Target architecture

```text
EMA-Bot .NET (strategy, risk checks, research, lifecycle, persistence)
        <-> local authenticated Named Pipe protocol <->
thin EMA-Bot MT5 Bridge EA (symbol/account/quote/order adapter)
        <-> MetaTrader 5 <-> Exness
```

The .NET application remains the sole strategy brain.  The future EA must not
duplicate EMA 9/15/100, confirmation, H2, signal, swing-stop, R:R, or re-entry
rules.  It only reports broker state, accepts validated commands, submits or
modifies/closes MT5 orders, and returns acknowledgements, fills, errors, and
execution measurements.

## Current dependency map

```text
Controllers
  BacktestsController -> BacktestService -> BinanceHistoricalCandleService
                                      -> BacktestEngine -> EmaSignalEngine/TradeMath
  StrategyOptimizerController -> StrategyOptimizationService/RegimeDiagnostics
                              -> BinanceHistoricalCandleService -> BacktestEngine
  PaperSessionsController -> PaperTradingCoordinator -> Binance historical warmup
                                                  + Binance WebSocket klines
  BinanceController -> BinanceFuturesMarketDataClient

EmaBotDbContext <- settings, symbols, backtest, paper, optimizer, Identity
Frontend api.ts <- all controller contracts; pages render Binance/USD-M labels
```

The core engine consumes the `Candle` type currently declared in the Binance
namespace.  This is an important coupling: the calculations are conceptually
broker-independent, but their input type is not yet.

## Classification summary

| Classification | Inventory outcome |
| --- | --- |
| KEEP | Strategy rules, deterministic lifecycle concepts, authentication, EF/Identity infrastructure, application shell, generic reporting concepts. |
| KEEP + GENERALIZE | Candle/timeframe inputs, settings, symbols, backtest service and entities, paper lifecycle, sizing, costs, exports, trade explorer, optimizer research core. |
| REPLACE | Binance REST history/metadata, Binance WebSocket stream, Binance-only controller, and Binance-specific live-paper transport. |
| REMOVE | No runtime subsystem is safe to delete in E0. The future Binance-only endpoint/UI wording and excess optimizer sweep/reporting are removal candidates only after their replacements or product decision. |
| DEFER | MT5 bridge, Exness model, live execution, cost calibration, historical source, optimizer simplification, and all database migrations. |

## KEEP: broker-independent behavior

| Component | Current responsibility and coupling | Files |
| --- | --- | --- |
| EMA signal engine | Calculates EMA 9/15/100, crossovers, confirmation, EMA gap and EMA100 filters from closed candles. Its rule logic is portable; its `Candle` parameter currently imports the Binance namespace. | `Strategy/EmaSignalEngine.cs`, `EmaCalculator.cs`, `StrategyModels.cs` |
| Stop and trade management rules | Finds swing/pivot/fallback stops, R:R targets, re-entry, trailing locks, target extension, conflict recording, and deterministic next-open/intrabar OHLC behavior. The execution price model will need generalization, but the rules and invariants survive. | `Strategy/SwingStopRules.cs`, `Strategy/TradeMath.cs`, `Services/BacktestEngine.cs` |
| H2 research logic | Maps execution timeframes to a higher timeframe and uses only a closed HTF candle at or before the signal. Keep the no-lookahead rule and diagnostics, later split timeframe IDs from provider IDs. | `Services/HigherTimeframeRegime.cs`, `Services/BacktestEngine.cs`, `Services/StrategyRegimeDiagnosticsService.cs` |
| Identity and Admin | Cookie auth, role authorization, bootstrap Admin creation, and health endpoint do not depend on Binance. | `Auth/*`, `Controllers/AuthController.cs`, `Controllers/HealthController.cs`, `Services/DatabaseInitializer.cs` |
| Persistence foundation | EF Core, MySQL configuration, migrations, retry configuration, and existing audit/history concepts remain useful. Existing entities require broker fields rather than replacement in place. | `Data/*`, `Migrations/*` |
| UI shell | Protected routes, session/auth state, layout, chart rendering, and baseline health dashboard infrastructure are portable. | `frontend/src/App.tsx`, `auth.tsx`, `components/AppShell.tsx`, `components/TradeChart.tsx` |

## KEEP + GENERALIZE

| Area | Current responsibility / Binance coupling | Target responsibility and suggested abstraction | Files affected |
| --- | --- | --- | --- |
| Candles and timeframes | `Candle` and `BinanceIntervals` live in `EmaBot.Api.Binance`; controllers validate Binance strings. Warmup tables also use those strings. | Move a broker-neutral `MarketBar`/`ClosedBar` and strategy timeframe vocabulary into a domain namespace. Map it at an adapter boundary to MT5 timeframe IDs and broker bars/ticks. | `Binance/BinanceModels.cs`, engines/services/controllers, frontend `api.ts` and pages |
| Historical market data | `IBinanceHistoricalCandleService` pages Binance klines and filters closed candles. Backtests, optimizer, and diagnostics call it directly. | `IHistoricalMarketData` returning normalized bars plus source/spec metadata. The Exness/MT5 adapter is future work. Preserve closed-bar and warmup semantics. | `Binance/BinanceHistoricalCandleService.cs`, `BacktestService.cs`, optimizer/diagnostics |
| Symbols | `MonitoredSymbol` assumes `BTCUSDT`, base/quote assets, and an enabled flag. Discovery selects Binance USDT perpetuals. | An instrument catalog with `BrokerSymbol`, `DisplaySymbol`, `AssetClass`, `Digits`, `PointSize`, `ContractSize`, `VolumeMin`, `VolumeMax`, `VolumeStep`, `CurrencyBase`, `CurrencyProfit`, and `CurrencyMargin`. Strategy accepts a stable instrument ID, not a broker spelling. | `Models/MonitoredSymbol.cs`, `Controllers/SymbolsController.cs`, `BinanceController.cs`, `SymbolsPage.tsx` |
| Backtest service and records | `BacktestService` gets Binance historical candles; run/trade records use USDT, `Quantity`, `EntryNotionalUsdt`, fixed percentage fees, and OHLC fill assumptions. | Keep run/trade/event history but add an immutable instrument-spec and execution-cost snapshot. Model bid/ask sides, requested and fill prices, lots, commissions, swaps, and slippage. | `Services/BacktestService.cs`, `BacktestEngine.cs`, `Models/BacktestModels.cs`, responses/exports/pages |
| Trading settings | Singleton settings store fixed USDT notional or margin-percent sizing, leverage, fee percentage, EMA controls and H2. | Retain strategy settings; separate broker account/risk/volume and cost configuration. Do not let an exchange-specific unit leak into strategy settings. | `Models/TradingSettings.cs`, `Services/TradingSettingsService.cs`, `Controllers/TradingSettingsController.cs`, `SettingsPage.tsx` |
| Position sizing | `TradeMath.CalculatePositionSize` uses fixed notional or equity × margin %, then `quantity = notional / entry`. Fixed-notional mode has leverage 1 and zero recorded margin. | Pipeline: account/risk input -> selected instrument contract size -> desired exposure/risk -> raw lots -> clamp min/max -> round to volume step -> MT5 margin validation -> final order volume. Keep pure deterministic calculators, feed them an instrument/account specification. | `Strategy/TradeMath.cs`, engines, paper models/coordinator, settings/models/tests |
| Cost model | `TradeMath.Fee` is `price * quantity * feePercent / 100`; it is used for entry/exit, target eligibility and fee-aware trailing break-even. Runs, sessions, optimizer and exports persist USDT fees. | Future `ITradingCostModel` (or equivalent pure domain component) should quote spread, commission per lot, swap, slippage and currency conversion from an immutable instrument/account snapshot. Preserve fee-aware parity tests until the new model explicitly replaces them. | `TradeMath.cs`, `BacktestEngine.cs`, `PaperTradingCoordinator.cs`, optimizer/exports/models/frontend/tests |
| Paper sessions | Session start/stop/resume, persisted symbols/trades/events, diagnostics and UI are generic concepts; market warmup and stream are Binance klines. Current paper mode is internal simulation, not broker demo trading. | Keep both: internal simulation for deterministic strategy tests and MT5 Demo for broker/execution validation. Replace transport/execution data source with broker-neutral adapters and label the two modes clearly. | `Models/PaperTradingModels.cs`, `PaperTradingCoordinator.cs`, `PaperSessionsController.cs`, `PaperTradingPage.tsx` |
| Trade explorer and exports | Trade history and chart/event concepts are useful; labels/fields assume USDT/quantity and data fetches use Binance bars. Excel/PDF output is custom and reusable. | Preserve explorer/reporting, generalize contract labels and include broker, instrument-spec/cost and fill provenance. | `TradesController.cs`, `TradeExportsController.cs`, `BacktestResponses.cs`, `TradeChart.tsx`, `TradesPage.tsx` |
| Optimizer core research | Parameter candidates run deterministic backtests over Binance symbols/timeframes and persist full/development/validation metrics. H2 uses the shared logic. | Keep a bounded research runner and deterministic ranking as a later broker-aware calibration/testing tool. It must receive normalized historical data and future cost/spec snapshots. | `StrategyOptimizationService.cs`, models/controller, diagnostics/workbook, `OptimizerPage.tsx` |

## REPLACE: provider infrastructure

| Current item | Why it is provider-specific | Future target | Dependents |
| --- | --- | --- | --- |
| `BinanceHistoricalKlineClient` | Legacy REST kline JSON, pagination cursor, and provider-specific error parsing. | MT5/Exness historical adapter behind `IHistoricalMarketDataProvider`. | Legacy historical provider and parity tests |
| `BinanceHistoricalMarketDataProvider` | Temporary research-only source for normalized closed bars. | Normalized historical provider backed by MT5 history/bars, with source and completeness metadata. | Backtest service, optimizer, diagnostics, charts, preview |
| `UnavailableMarketBarStreamProvider` | Explicitly rejects live market-bar streaming until a provider exists. | MT5 bridge quote/bar events over Named Pipes. | Paper controller/coordinator |
| Instrument discovery | Temporarily unavailable; existing monitored records remain intact. | Broker-neutral MT5/Exness catalog controller. | Symbols page/API |
| Internal Paper live wiring | New starts/resumes return 503 without persisting a new session. | Broker-neutral internal-paper feed; separately an MT5 Demo execution/session adapter. | Coordinator, controller, page/tests |

## Conservative REMOVE candidates (not E0 deletions)

There is no safe implementation deletion yet: every Binance class above has a
future equivalent and is classified REPLACE.  The following are only later
cleanup candidates once their replacement ships.

| Candidate | Today | Safe deletion order / impact |
| --- | --- | --- |
| Binance-only API and presentation language | `BinanceController.cs`, Binance labels in `DashboardPage.tsx`, `README.md`, and client types expose one provider rather than a reusable concept. | First ship instrument catalog and broker status UX, migrate callers/tests, then remove the endpoint and words. No database impact. |
| Old optimizer parameter-sweep/reporting complexity | Candidate grids, full/dev/validation report storage and bespoke XLSX workbooks may exceed the simplified product need. | Do **not** remove core research. Decide after Exness cost/spec calibration requirements are known; then retire UI endpoints/workbooks first, migrate or archive `StrategyOptimization*` data, and delete only their tests. |
| `PlaceholderPage.tsx` | Unrouted “next milestone” component. | It has no route in `App.tsx`; remove only after confirming no external import. No DB/test impact. |

## Paper-trading assessment

Recommendation: **C — keep both internal paper simulation and MT5 Demo, for
different purposes.**  Internal paper remains valuable for deterministic
closed-bar behavior, regression tests, re-entry/SL/TP analysis, and research.
MT5 Demo validates real symbol specifications, bid/ask fills, commissions,
margin, terminal availability and latency.  It must never be presented as a
substitute for deterministic simulation.

`PaperTradingCoordinator` currently owns live runtime state, historical
warmup, kline close processing, reconnect state and simulated lifecycle.  Its
state-machine and persistence concepts can survive; its `IBinance*`
dependencies, current price semantics and USDT fee/sizing calculations must be
replaced.  `PaperSessionsController` currently blocks H2 because it is only
implemented for historical backtests; preserve that explicit capability guard
until a normalized live HTF feed supports the same no-lookahead rule.

## Backtest realism gap

The current deterministic engine deliberately enters on the next candle open
and uses a single OHLC price for SL/TP.  For Exness realism, retain that
deterministic baseline but add selectable broker-aware execution using:

- bid for long exits/short entries and ask for long entries/short exits;
- spread at decision and execution, requested versus fill price, and slippage;
- lot conversion from contract size and volume min/max/step;
- commission per lot, swaps and account-currency conversion;
- MT5 symbol-spec snapshot and broker session/tradability state;
- explicit stop/target fill-side and intrabar ambiguity policy.

These are future E4/E5/E9 changes, not a retroactive reinterpretation of
existing Binance research records.

## Cost and sizing trace

The current percentage fee is configured in `TradingSettings.FeePercentPerSide`
and copied into backtest, paper and optimizer run snapshots. `TradeMath.Fee`,
`ExpectedNetAtTarget`, `FeeBreakevenPrice` and `FeeAwareTrailingStop` use it;
`BacktestEngine` and `PaperTradingCoordinator` persist the resulting entry,
exit and total fees; exports and frontend display the USDT figures. Optimizer
metrics and diagnostics aggregate the same values. These semantics are a
temporary parity baseline, not an Exness model.

The current sizing trace is `TradingSettings` -> `TradeMath.CalculatePositionSize`
-> engine/coordinator -> trade records. Fixed size is a USDT notional;
margin-percent derives margin from simulated equity, applies configured
leverage, then computes `quantity = notional / entryPrice`. It has no contract
size, lots, rounding, broker margin calculation or volume limits.

## Planned MT5 Named Pipe Bridge

Use .NET as the local Named Pipe **server** and the single MT5 EA as the
reconnecting client. This keeps strategy availability, protocol versioning and
authorization under application control and accommodates MT5/EA restarts.

- A pipe name must be per local user/environment, ACL-restricted to that user,
  and protected by a per-install shared bootstrap secret in the handshake.
- Use length-prefixed UTF-8 JSON (or a versioned compact equivalent), one
  complete message per frame; never use newline parsing. Include protocol
  version, connection/session ID, request ID, correlation ID and UTC timestamp.
- On `HELLO`, verify version, terminal/account identity and allowed mode before
  accepting commands. Heartbeats detect a silent terminal; reconnects must
  re-handshake and resynchronize account, positions, quote and pending command
  state.
- Every order-changing command has a durable idempotency/order-command ID. The
  EA must return the same final result for a duplicate rather than submit twice.
  Commands expire on timeout and status is reconciled with `GET_POSITIONS`.
- Conceptual commands: `HELLO`, `HEARTBEAT`, `GET_ACCOUNT`,
  `GET_SYMBOL_SPEC`, `GET_QUOTE`, `PLACE_ORDER`, `MODIFY_POSITION`,
  `CLOSE_POSITION`, `GET_POSITIONS`.
- Conceptual responses/events: `ACK`, `REJECT`, `FILL`, `POSITION_UPDATE`,
  `ACCOUNT_UPDATE`, `ERROR`.

No Named Pipe, MQL5, Exness credential, or command code belongs in E0.

## Future latency telemetry

For every future execution record capture `StrategyDecisionUtc` (T1),
`BridgeCommandSentUtc` (T2), `Mt5OrderSubmittedUtc` (T3), and
`BrokerResultReceivedUtc` (T4). Derive `StrategyToBridgeMs = T2-T1`,
`BridgeToMt5Ms = T3-T2`, `BrokerExecutionMs = T4-T3`, and
`EndToEndExecutionMs = T4-T1`.

Also retain `RequestedPrice`, `BidAtDecision`, `AskAtDecision`, `FillPrice`,
`SlippagePoints`, `SlippageCurrency`, `SpreadAtDecision`, broker order/position
IDs, command ID, source and clock-quality/sequence metadata.  The EA should
timestamp local MT5 submission/result events; the server should record receipt
timestamps rather than invent broker timestamps.

## Database and migration inventory

| Table/entity family | Classification | Migration direction |
| --- | --- | --- |
| `AspNet*`, `EmaUser` | KEEP | No broker coupling; retain Identity/Admin. |
| `TradingSettings` | GENERALIZE | Split strategy controls from future broker/account/risk/cost settings; retain singleton only if still operationally appropriate. |
| `MonitoredSymbols` | GENERALIZE | Evolve to instrument catalog/spec snapshots; do not mutate historical records in place. |
| `BacktestRuns`, `BacktestTrades`, `BacktestTradeEvents` | GENERALIZE | Retain research audit trails, add broker/source/spec/cost/fill snapshots in a future additive migration. |
| `PaperSessions`, `PaperSessionSymbols`, `PaperTrades`, `PaperTradeEvents` | GENERALIZE / REPLACE runtime | Preserve simulated records; future MT5 Demo records need explicit mode, broker and order/position IDs. |
| `StrategyOptimizationRuns`, `Candidates`, `MarketResults`, `Trades` | DEFER | Preserve; decide the minimal retained research feature before any archive/removal migration. |

Current migrations are `InitialCreate`, symbols/settings, backtesting, paper
trading, backtest events, position-sizing refinement, paper re-entry,
optimization research, and optional H2 filter. **E0 adds no migration and
changes no schema.**

## Frontend inventory and target UX

| Page/component | Classification | Future direction |
| --- | --- | --- |
| `LoginPage`, auth provider, app shell | KEEP | Preserve Admin access and navigation. |
| `DashboardPage` | GENERALIZE | Replace hard-coded Binance Futures/Simulation description with broker connection, account mode and health. |
| `SettingsPage` | GENERALIZE | Separate strategy controls from account risk, instrument volume rules and broker cost settings. |
| `SymbolsPage` | REPLACE then GENERALIZE | Replace Binance discovery with broker catalog/spec selection, display symbol and capabilities. |
| `BacktestsPage`, `TradeChart` | GENERALIZE | Keep research/chart UX; show provider, bid/ask/cost/spec provenance and avoid USDT-only wording. |
| `PaperTradingPage` | REPLACE then GENERALIZE | Present internal simulation and MT5 Demo as distinct modes with connection/terminal state. |
| `TradesPage` and exports | GENERALIZE | Preserve trade history and exports; show lots, instrument units, fills, commission/swap/slippage. |
| `OptimizerPage` | DEFER | Keep only while research/calibration scope is decided; do not promise it as a live-trading prerequisite. |
| `PlaceholderPage` | REMOVE candidate | Unrouted today; remove only after import check in its own cleanup. |

`frontend/src/api.ts` is the contract choke point and must be migrated with
each backend endpoint rather than by silently casting old Binance-shaped data.

## Test inventory and migration contract

| Category | Existing coverage / action |
| --- | --- |
| Broker-independent — preserve | `MarketDataAndStrategyTests`, `BacktestCorrectiveTests`, `PaperTradingUnitTests`, `PositionSizingTests`, `HigherTimeframeRegimeTests`, trade explorer/export tests, auth tests. Preserve EMA, confirmation, closed-candle/no-lookahead, next-open execution, SL/TP conflict/trailing behavior, H2, re-entry and deterministic engine parity. |
| Binance-specific — replace | `BinanceTimeoutTests`, `TestBinanceClient` portions, exchange metadata/kline parsing and stream parser/coordinator tests. Keep the behavioral intent (timeouts, malformed payloads, pagination, liveness), re-target at normalized MT5 bridge adapters. |
| Research-only — decide later | Optimizer candidate/date/finalization/format tests and regime-diagnostics workbook tests. Keep while the optimizer is retained; split core ranking correctness from optional sweep/report coverage before removal. |
| Current-provider regression | `PomeloQueryRegressionTests` remains relevant to EF/MySQL behavior, not Binance. |
| Future Exness/MT5 tests needed | Pipe framing/handshake/auth/idempotency/reconnect; bridge contract conformance; MT5 account/symbol-spec/quote parsing; volume clamp/step/margin; bid/ask and cost/slippage/swap backtest parity; command-to-fill correlation; Demo smoke test; latency timestamp arithmetic; recovery/reconciliation and live-readiness failure modes. |

## Safe cleanup sequence

| Phase | Dependencies | Files/subsystems | DB impact | Risk and required tests |
| --- | --- | --- | --- | --- |
| E1 — product-scope cleanup | E0 decision on optimizer/UI | unused placeholder, Binance wording, candidate research-only screens | none unless later data archival is approved | Low; route/import and UI smoke tests. |
| E2 — broker-neutral domain contracts | E0 inventory | normalized bars/timeframes, instrument/spec, provider and cost contracts; DI seams | none initially | Medium; compile plus golden strategy/no-lookahead parity. |
| E3 — replace Binance market-data infrastructure | E2 | `Binance/*`, controller consumers, backtest/optimizer/paper data adapters | none | High; historical paging/completeness, liveness/reconnect and closed-bar contract tests. |
| E4 — broker-neutral sizing and costs | E2 and instrument spec | `TradeMath`, settings, engines, models, exports | additive snapshot/settings migration only after model review | High; existing fee parity until replacement, lot rounding, margin and commission/swap/slippage tests. |
| E5 — MT5/Exness symbols and pricing | E2/E4 | catalog API/UI, spec/quote adapters, source metadata | additive instrument/spec snapshots | High; symbol mapping, digits/points, bid/ask/spread and unavailable-market tests. |
| E6 — Named Pipe protocol and .NET endpoint | E2/E5 | new transport project/module, DI, protocol contracts, telemetry | optional additive command/audit tables | High; framing, ACL/auth, versioning, timeout, reconnect and idempotency tests. |
| E7 — minimal MQL5 Bridge EA | E6 | new EA outside strategy domain | none initially | High; protocol conformance in MT5 test terminal; EA must contain no strategy rules. |
| E8 — MT5 Demo connection | E5–E7 | execution/session adapter, UI mode/status, reconciliation | additive broker execution/order/position fields | High; Demo smoke, duplicate-command, restart and account mismatch tests. |
| E9 — Exness-realistic backtesting | E3–E5 | backtest engine/service, historical data, cost/fill models, reports | additive immutable run/trade snapshots | High; bid/ask SL/TP, spread, lots, commission, swap, slippage and deterministic replay tests. |
| E10 — Demo validation and latency measurement | E8/E9 | telemetry, dashboards/exports and acceptance scripts | additive telemetry only if persistence is required | Medium; T1–T4 metric tests, clock/reconnect and slippage reconciliation. |
| E11 — live-readiness hardening | E8–E10 and explicit approval | safety limits, kill switch, alerting, reconciliation, credential/IPC hardening | audited additive migration only | Very high; fault injection, risk limits, recovery and prolonged Demo soak. |

## E0 exit criteria

Only this document is added.  E1 must start from the classifications and
contracts above, retain existing deterministic behavior as a baseline, and
make an explicit database migration decision only when a concrete additive
model has been reviewed.
