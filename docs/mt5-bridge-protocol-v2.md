# MT5 execution bridge protocol v2

Protocol v2 is a separate local Windows named-pipe endpoint (`ema-bot.mt5.bridge.v2`). It does not replace or widen the v1 read-only contract described in `mt5-bridge-protocol-v1.md`.

Both sides default to disabled. Enabling market data does not enable execution. A write requires all of the following on both the API and EA: protocol v2 handshake, the independently enabled execution switches, `DemoOnly=true`, account mode `Demo`, account and EA trading permissions, and exact configured account-fingerprint and server matches. Real and Contest accounts are rejected. Configuration contains no credentials; the handshake secret belongs in user-secrets/environment configuration.

The supported execution operations are `GetExecutionAccount`, `OrderCheck`, `SubmitMarketOrder`, `GetPosition`, `GetExecutionHistory`, and `ClosePosition`. Every order carries an immutable client execution ID, configured magic number, and correlation marker. The EA uses `PositionSelectByTicket` for a close and verifies magic/comment ownership; it never selects a position by symbol for an execution close.

The API persists a `DemoExecution` intent before bridge I/O. `OrderCheck` is performed before a submit. It persists `Submitting`/`CloseRequested` before the broker write. A timeout, disconnect, or malformed response after a write becomes `ReconciliationRequired`; it is never automatically re-submitted. The same client execution ID returns the existing record and is reconciled rather than sent again. Startup/reconnect recovery is reconciliation-only.

No strategy, Paper session, optimizer, or UI live-trading path invokes this endpoint. An Administrator must make an explicit API call to the manual endpoints under `/api/demo-executions`.

## Required validation before any Demo order

1. Configure v2 and Demo execution explicitly with a long handshake secret held outside source control.
2. Confirm `/api/demo-executions/readiness` reports the expected Demo server and safe account fingerprint.
3. Verify the EA's `InpEnableDemoExecution`, expected fingerprint/server, and magic number match the API configuration.
4. Perform one controlled Demo-only order with a fresh client execution ID and then reconcile it. No order was placed as part of E11.5 implementation.
