# E10 MT5 broker-aware Paper live test

1. Copy the updated `mt5/MQL5/Experts/EmaBot/EmaBotBridge.mq5` into the MT5
   data folder, compile it in MetaEditor, and reattach the EA.
2. Restart the API and confirm the bridge dashboard status is `Connected`.
3. Confirm the E10 migration applied; confirm the existing `XAUUSDm` row and
   prior historical backtest remain readable.
4. On Symbols, explicitly set `XAUUSDm` Paper commission per lot per side.
   Use `0` only for a confirmed commission-free account/instrument.
5. Call the admin margin diagnostic with `XAUUSDm`, `Long`, `0.01`, and the
   current Ask. Confirm a positive required margin and the MT5 account
   currency. Call the profit diagnostic with differing open/close prices and
   confirm the sign follows direction. Confirm no MT5 order was created.
6. Start `XAUUSDm` / `3m` Paper. Confirm live Bid, Ask, spread, current-bar,
   and EMA state update. Wait for a rollover and confirm the baseline does not
   replay a duplicate signal.
7. If a trade occurs, verify Ask long / Bid short entry, lots, required margin,
   configured round-trip commission, structural SL/TP, executable exit side,
   and MT5-calculated gross/net profit.
8. Stop Paper and confirm clean shutdown plus no broker order in MT5.

Paper uses observed-quote fills only: additional slippage and swap financing
are not simulated.

## API restart recovery

If the API restarts while Paper is running, the session is intentionally marked
`Interrupted`; it does not reconnect automatically. Return to the Paper page and
confirm the restart reason plus the `Resume session` and `End session` actions.

- Use **Resume session** only after MT5 is connected. It clears any pending entry
  tied to the prior process, reconnects the live stream, and restores live
  Bid/Ask without a browser refresh.
- Use **End session** only when there is no open simulated position. It marks the
  interrupted session stopped and returns to the start form/history.
- If a simulated position is open, ending is rejected deliberately. Resume first,
  then use the normal Stop action so Paper can obtain an executable Bid/Ask exit.

No broker order is created in either recovery path.

## Decision observability validation

The active Paper dashboard shows the session settings snapshot, decision funnel,
live market/EMA state, pending-entry details, and an open-position card. Trend
(`EMA9 > EMA15` or `EMA9 < EMA15`) is not a signal to trade; the current
decision explains what the latest closed candle actually did.

Every processed live closed candle now writes a decision ledger event, including
the no-action outcome. The active dashboard shows the latest 25 persisted
decisions per symbol; the full Admin history is available from
`GET /api/paper-sessions/{sessionId}/decisions` with optional symbol and paging.
Decision history survives restart from E10.5 onward. History before this change
cannot be reconstructed.

The dashboard separates values by cadence: Bid/Ask and executable exit are
live; EMA values, trend, gap, decision evaluation, and funnel totals update on
candle close; entry values and margin are fixed at entry; SL/TP, MFE, and MAE
change only when their relevant management event occurs. Current balance is
realized only—open-position unrealized P/L is deliberately not estimated.
