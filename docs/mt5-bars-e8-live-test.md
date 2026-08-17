# E8 manual MT5 bar validation — passed

This is read-only validation; do not start Paper, place an order, or execute a trade.

1. Copy the updated `EmaBotBridgeV1.mq5` into the MT5 data-folder `MQL5\Experts\EmaBot` directory and compile it with F7 (zero errors).
2. Reattach or restart the EA and confirm bridge status is `Connected`.
3. Call `/api/mt5/market-data/latest?symbol=XAUUSDm&timeframe=3m&count=10`.
4. Compare returned ascending completed bars with the MT5 XAUUSDm three-minute chart; the newest returned candle must not be current.
5. Call `/api/mt5/market-data/snapshot?symbol=XAUUSDm&timeframe=3m` and compare current OHLC with the live MT5 bar.
6. Confirm the snapshot event timestamp advances as quotes arrive.
7. Wait through a three-minute rollover when practical. Verify the prior current bar becomes closed, the new current bar has a new open time, and `previous.CloseTimeUtc + 1ms == new.OpenTimeUtc`.
8. Call a `3d` diagnostic and confirm the adapter rejects it clearly.

Stop here. E8 does not enable Paper, orders, or trades.

## Manual Exness Demo result

E8/E8.1 read-only validation passed against Exness Demo for `XAUUSDm`.
Latest closed bars matched MT5, the snapshot matched the current MT5 M3 bar,
and event timestamps advanced. Three-minute rollover behavior passed, including
`closeTime + 1ms == next open`. The native-MT5 adapter correctly rejected
canonical `3d`. No account login, password, or bridge secret is recorded.
