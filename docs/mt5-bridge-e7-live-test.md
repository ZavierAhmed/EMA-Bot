# E7 manual MT5 Bridge validation

This is a read-only manual procedure. It must not place, modify, or close an
order.

1. Open MetaTrader 5, then choose **File -> Open Data Folder**.
2. Open `MQL5\Experts\EmaBot` and copy
   `mt5/MQL5/Experts/EmaBot/EmaBotBridge.mq5` there.
3. Open it in MetaEditor and compile with F7; confirm zero compile errors.
4. Configure backend user-secrets with `Mt5Bridge:Enabled` set to `true` and a
   new, strong `Mt5Bridge:HandshakeSecret` (at least 32 characters).
5. Start EMA-Bot and verify `GET /api/mt5/bridge/status` is `WaitingForClient`.
6. Attach `EmaBotBridge` to any MT5 chart and enter pipe name
   `ema-bot.mt5.bridge.v1` plus the exact same bridge secret. Never use an
   Exness trading password as this secret.
7. Verify status becomes `Connected`, then confirm heartbeat timestamps update.
8. POST `/api/mt5/bridge/ping` and verify a transport RTT is returned.
9. GET `/api/mt5/account`; verify server, currency, and account mode, and that
   account login is not returned.
10. GET `/api/instruments`; it should show only symbols currently selected in
    MT5 Market Watch. Select a known Market Watch symbol, then inspect its
    instrument endpoint for plausible nonzero contract, volume, and point data.
11. GET the quote endpoint and compare its bid/ask with Market Watch at
    approximately the same time.

Stop here. There is no order or trade test in E7.
