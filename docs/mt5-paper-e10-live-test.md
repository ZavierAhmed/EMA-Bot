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
