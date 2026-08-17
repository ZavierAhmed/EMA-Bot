# E10 MT5 broker-aware Paper live test

1. Copy the updated `mt5/MQL5/Experts/EmaBot/EmaBotBridgeV1.mq5` into the MT5
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

The active dashboard reads its bounded runtime decision cache while Paper is
running. On resume it seeds the latest persisted 25 decisions before adding the
new warmup entry; interrupted sessions use bounded persisted reads instead.

The dashboard separates values by cadence: Bid/Ask and executable exit are
live; EMA values, trend, gap, decision evaluation, and funnel totals update on
candle close; entry values and margin are fixed at entry; SL/TP, MFE, and MAE
change only when their relevant management event occurs. Current balance is
realized only—open-position unrealized P/L is deliberately not estimated.

## E10.6 Paper workspace

The active Paper workspace has four tabs: **Trades**, **Market**, **Session**,
and **Decision**. Tab selection remains local while the single active-session
poll continues in the background.

For an open MT5 Paper position, the Trades tab displays runtime-only live P/L
from MT5 `OrderCalcProfit`: Long uses current Bid and Short uses current Ask.
Net live P/L is gross P/L less the configured round-trip commission. Current
P/L % is `net P/L / simulated margin used * 100`; it is not account return or
price movement. Valuation is throttled and a temporary valuation failure never
closes or faults the position.

The Position Chart uses MT5/Exness historical closed candles plus EMA lines and
live Entry, SL, TP, and executable-current price levels. Chart history refreshes
only for a trade change or a newly closed candle, while the current level moves
with the normal Paper poll.

Before starting, Paper lists all supported MT5-native intervals and requires a
commission setting for every selected instrument. The pre-start display shows
the complete settings snapshot that will be captured. Running-session polling is
single-flight and keeps the selected workspace tab stable through refreshes.

## E10.7 outage recovery and session archive

An MT5 live-market-data `Unavailable` or `Timeout` interruption now changes a
running Paper session to `Interrupted`, records the interruption reason, and
requires an explicit Resume after MT5 reconnects. Invalid broker data and other
unexpected failures remain `Faulted`. Paper never invents prices, entries, or
exits while the application or bridge is offline.

Paper history now provides **View session** for stopped, interrupted, and
faulted sessions. Archive mode is read-only: it shows the session failure
reason, persisted market/decision state, unresolved positions, paginated trade
history, and paginated decision history. Historical open positions have no
runtime live P/L or executable quote until their interrupted session is resumed.

## E10.7.1 Trade Explorer and exports

Trade Explorer and exports use source-aware economics. MT5 Paper trades display
the persisted account currency, broker-calculated gross/net P/L, commission,
lots, margin, account equity at entry, and entry/exit Bid/Ask/spread. MT5 Paper
net P/L percent is explicitly based on entry equity; R is left unavailable when
no authoritative broker-currency initial-risk amount was persisted. Legacy
Paper and backtest USDT reporting remain unchanged.
