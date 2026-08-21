# MT5 execution bridge protocol v2

Protocol v2 is a separate local Windows named-pipe endpoint (`ema-bot.mt5.bridge.v2`). It does not replace or widen the v1 read-only contract described in `mt5-bridge-protocol-v1.md`.

Both sides default to disabled. Enabling market data does not enable execution. A write requires all of the following on both the API and EA: protocol v2 handshake, the independently enabled execution switches, `DemoOnly=true`, account mode `Demo`, account and EA trading permissions, and exact configured account-fingerprint and server matches. Real and Contest accounts are rejected. Configuration contains no credentials; the handshake secret belongs in user-secrets/environment configuration.

The supported execution operations are `GetExecutionAccount`, `OrderCheck`, `SubmitMarketOrder`, `GetPosition`, `GetExecutionHistory`, `GetExactDeal`, `GetPositionHistory`, `ClosePosition`, and `ModifyPositionProtection`. Every order carries an immutable client execution ID, configured magic number, and correlation marker. Exact position ownership for close, read, and protection modification is native `PositionTicket + PositionIdentifier + MagicNumber + BrokerSymbol + original Side`; comments and markers are never ownership keys.

`GetExecutionHistory` is reconciliation-only. The API supplies an exact client ID/marker, magic, symbol, side, expected volume, and a bounded UTC window around the persisted submit time. The EA executes `HistorySelect` only for that window and returns normalized deal evidence only when magic, deal comment marker, symbol, entry side, and volume are compatible. .NET independently repeats those checks, groups evidence by broker position identifier, and adopts it only when exactly one deterministic owned position exists. No evidence, multiple positions, a manual trade, another EA, a wrong magic, or a wrong marker leaves the execution `ReconciliationRequired`.

Exact-history read evidence (`GetExecutionHistory`, `GetExactDeal`, and `GetPositionHistory`) may additionally carry `nativeReason`, normalized from the native `DEAL_REASON` of that exact deal. It is additive under protocol version `2`; no write operation is added. `GetExactDeal` remains an entry-deal ownership operation and never establishes an exit reason. The API persists a non-null exit reason only from the terminal exit selected by either exact-position-history or already-strict bounded-history reconciliation, never from prices, comments, request data, or an entry deal. Differing later exact terminal reasons preserve the audit value but permanently set `NativeExitReasonConflicted`, making the classification unusable for automated strategy decisions.

The API persists a `DemoExecution` intent before bridge I/O. `OrderCheck` is performed before a submit. It persists `Submitting`/`CloseRequested` before the broker write. A timeout, disconnect, or malformed response after a write becomes `ReconciliationRequired`; it is never automatically re-submitted. The same client execution ID returns the existing record and is reconciled rather than sent again. A unique database constraint and insert-race handling ensure concurrent reuse of that ID has only one broker-submission owner. Startup/reconnect recovery is reconciliation-only and searches history; it never submits or closes.

## E11.6B1 protected position-management primitive

`ModifyPositionProtection` is v2-only and remains behind the same Demo-only account, EA, fingerprint/server, and magic-number gates as other v2 broker writes. It accepts the exact native ownership tuple and both final `StopLoss` and `TakeProfit` values. The EA reads the exact native position, validates every ownership field, requires a native tick grid (falling back to point only when necessary), validates stop-side/stops-level/freeze-level constraints, then re-reads native protections immediately before `OrderCheck`/`OrderSend` so a racing external change cannot be weakened. Its response contains actual broker-read ticket, identifier, stop loss, and take profit after the operation; it does not echo request expectations as evidence.

The API records each management request in `DemoExecutionManagementActions`, keyed uniquely by `ClientManagementActionId`. The action first records the broker-observed protection, resolves any omitted side of a request to the current native value, and persists `Submitting` before the one native write. It never removes either protection, never weakens a stop/target, and treats equal values as an applied no-op. Protection prices must already be on the native `TickSize` (or `PointSize`) grid; they are not silently rounded into a different risk value. `DemoExecution.RequestedStopLoss` and `RequestedTakeProfit` remain the original entry request; broker-derived current protection is stored separately and an exact read clears a current value when the broker reports it absent.

An explicit broker rejection produces a terminal `Rejected` management action. An accepted native write is `Applied` only when the post-write response proves the exact ticket/identifier and a canonically equivalent final SL/TP pair. An accepted write with missing, wrong, or otherwise insufficient post-write evidence is `ReconciliationRequired`, never `Rejected`, and is never retried. A timeout or disconnect is handled the same way. Reconciliation performs only the exact owned-position read: matching final SL/TP proves `Applied`; differing/insufficient evidence remains `ReconciliationRequired`; a closed position is reconciled through the existing execution authority and is never modified. Startup recovery also reconciles only—unsubmitted `Created` actions fail closed and are never submitted late.

E11.6B1 provides the protected position-management primitive only. Strategy-driven trailing, target extension, opposite close and re-entry are not enabled by this milestone.

No public automatic protection-modification HTTP route is exposed by E11.6B1.

## E11.6B2 automated Demo strategy management

E11.6B2 uses the existing `DemoExecutionService` primitives only: an active Demo strategy session may reconcile its own exact linked execution, submit one durable B1 `ModifyProtectionAsync` action for a higher trailing tier and/or the one-time 70% target extension, or delegate one exact opposite-signal `CloseAsync`. It never calls the bridge directly, never manages manual or previous-session executions, and introduces no public broker-write endpoint.

Management is independently disabled by default through `DemoStrategyAutomation:ManagementEnabled`; it also requires the existing automation gate and execution readiness. Progress is based only on a later executable quote—Bid for Buy and Ask for Sell—and on the broker-derived actual fill plus immutable original requested target. Generated strategy prices are conservatively aligned to `TickSize` (falling back to `PointSize`) before B1 validates them. An ambiguous B1 action blocks new actions and is reconciled only; an opposite close is similarly one-shot. Stopping a session never changes broker protection or closes a position. After restart/resume, pre-interruption B2 management is suspended fail-closed; E11.6B3 is reserved for deliberate restart-safe recovery and re-entry.

Historical entry/exit evidence can recover missing tickets and prove an ambiguous close completed. The ledger retains separate order, entry-deal, exit-deal, position identifier, fill/close prices and volumes, broker times, and reconciliation source. `BrokerAccepted` means request acknowledgement only; `Open`, `PartiallyFilled`, and `Closed` require corresponding broker evidence.

No strategy, Paper session, optimizer, or UI live-trading path invokes this endpoint. E11.5 provides explicit manual submit, reconcile, and close calls under `/api/demo-executions`; E11.6B1 deliberately adds no public protection-modification endpoint.

## MT5 deployment

- Attach `EmaBotBridgeV1` (`mt5/MQL5/Experts/EmaBot/EmaBotBridgeV1.mq5`) on pipe `ema-bot.mt5.bridge.v1` for read-only market data. That source has no broker-write operations.
- Attach `EmaBotExecutionBridgeV2` (`mt5/MQL5/Experts/EmaBot/EmaBotExecutionBridgeV2.mq5`) on a separate chart on pipe `ema-bot.mt5.bridge.v2` for Demo execution validation. Keep `InpEnableDemoExecution=false` until an explicitly authorized Demo test.
- Do not compile or deploy the v2 EA in place of the v1 EA. MT5 restart would otherwise leave market data on the wrong protocol adapter.

## Required validation before any Demo order

1. Configure v2 and Demo execution explicitly with a long handshake secret held outside source control.
2. Confirm `/api/demo-executions/readiness` reports the expected Demo server and safe account fingerprint.
3. Verify the EA's `InpEnableDemoExecution`, expected fingerprint/server, and magic number match the API configuration.
4. Perform one controlled Demo-only order with a fresh client execution ID and then reconcile it. No order was placed as part of E11.5 implementation.
