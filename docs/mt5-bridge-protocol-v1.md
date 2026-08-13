# MT5 Bridge protocol v1

Phase E6 adds a disabled-by-default, local Windows Named Pipe server to
EMA-Bot. EMA-Bot .NET is the server; the future thin MT5 Bridge EA is the one
local client. This direction keeps protocol policy, authorization, session
state, and request lifecycle inside EMA-Bot.

## Configuration and local security

The logical pipe name defaults to `ema-bot.mt5.bridge.v1`. EMA-Bot creates the
Windows pipe itself; clients must not provide a `\\.\pipe\...` path. The enabled
server uses `PipeDirection.InOut`, byte transmission mode, asynchronous I/O,
one server instance, and `PipeOptions.CurrentUserOnly`. It is local IPC only:
there is no TCP listener, remote pipe server, or HTTP MT5 transport.

`Mt5Bridge:Enabled` is `false` by default. Enabling it requires Windows, a
safe logical pipe name, valid positive timeouts/frame limit, and a handshake
secret of at least 32 characters supplied through user-secrets or environment
configuration (`Mt5Bridge__HandshakeSecret`). The secret is never committed,
logged, serialized, returned by an API, or retained after authentication.

## Version and framing

`ProtocolVersion` is exactly `1`; there is no fallback or downgrade
negotiation. Every frame is deterministic binary framing:

```text
[4-byte unsigned little-endian UTF-8 JSON payload length][JSON payload]
```

The default maximum payload is 1,048,576 bytes. A receiver reads partial
stream reads until it has the complete header/payload, rejects zero or
oversized lengths before allocation, and rejects malformed UTF-8 or JSON.
There is no newline/delimiter framing, compression, or binary formatter.

Envelopes contain protocol version, kind (`Hello`, `HelloAck`, `Heartbeat`,
`Request`, `Response`, or `Error`), operation, optional GUID request ID, UTC
send timestamp, and a typed JSON payload.

## Authentication and sessions

The first client frame must be `Hello` / `Hello`. It carries the secret,
client version, and terminal instance ID; optional terminal/account metadata
can follow. EMA-Bot hashes both secrets with SHA-256 and uses
`CryptographicOperations.FixedTimeEquals`. Successful authentication returns
only a `HelloAck` with protocol version, session ID, server version, connected
time, and heartbeat timeout. Authentication failures are generic and close the
client.

Hello must complete within the configured timeout (five seconds by default).
Second Hello frames, malformed frames, unsupported versions, and missing
required fields are protocol violations. Only one authenticated client may be
active. A disconnect, rejected handshake, or stale heartbeat returns the
server to `WaitingForClient` for a new authenticated connection.

After Hello, the client periodically sends `Heartbeat` / `Heartbeat`. EMA-Bot
tracks last message and last heartbeat separately. If no heartbeat arrives by
the configured timeout (15 seconds by default), the session is marked stale,
closed, and recycled. Heartbeats are runtime-only and are not stored in the
database.

## Read-only requests

The only server-initiated request operations in v1 are:

- `Ping`
- `GetAccount`
- `GetInstruments`
- `GetInstrument`
- `GetQuote`

Each request has a unique GUID request ID. Responses and errors must echo that
same ID, so overlapping requests are correlated by ID rather than stream order.
Writes are serialized so frame bytes cannot interleave. Request timeouts remove
their pending entry; a client disconnect fails every pending request promptly.
`Ping` records only transport round-trip time, not broker execution latency.

Future account, instrument/spec/catalog, and bid/ask quote DTOs are present for
E7 compatibility. Broker symbol spelling is preserved exactly. No account data
is persisted or exposed through a public account API in E6.

The protocol deliberately contains no execution operation: no order placement,
order sending, position modification, closing, buying, or selling is allowed.
This bridge connection is not execution readiness and does not enable trading.

## E7 client requirements

E7 will add the thin MQL5 client, not strategy logic. It must use binary
read/write access to this duplex pipe, implement this exact v1 framing and
Hello-first handshake, preserve request IDs and terminal symbol spelling, and
perform the required flush/seek transition whenever it switches between
writing and reading. It must not add EMA, signal, sizing, or execution policy.

## E7 implementation notes and golden JSON

E7 implements the single-file, timer-driven MQL5 client at
`mt5/MQL5/Experts/EmaBot/EmaBotBridge.mq5`. It opens only the local logical
pipe as `\\.\pipe\<name>` in binary read/write mode, uses UTF-8 bytes without a
terminating zero byte, and processes a bounded number of frames per timer tick.
It reports only the selected instruments in Market Watch and never selects a
symbol or submits a trade.

These synthetic examples are protocol-v1 compatibility fixtures; they contain
no secret or real terminal/account value.

```json
{"protocolVersion":1,"kind":"Hello","operation":"Hello","requestId":null,"sentAtUtc":"2026-08-13T12:00:00Z","payload":{"secret":"<strong-secret>","clientVersion":"EMA-Bot-MT5-Bridge/1","terminalInstanceId":"mt5-ABC","terminalName":"MetaTrader 5","terminalCompany":"MetaQuotes","terminalBuild":5000,"accountServer":"Demo","accountCurrency":"USD","accountMode":"Demo"}}
{"protocolVersion":1,"kind":"Heartbeat","operation":"Heartbeat","requestId":null,"sentAtUtc":"2026-08-13T12:00:05Z","payload":{"clientTimeUtc":"2026-08-13T12:00:05Z"}}
{"protocolVersion":1,"kind":"Request","operation":"GetQuote","requestId":"11111111-1111-1111-1111-111111111111","sentAtUtc":"2026-08-13T12:00:10Z","payload":{"brokerSymbol":"terminal.symbol"}}
{"protocolVersion":1,"kind":"Response","operation":"GetQuote","requestId":"11111111-1111-1111-1111-111111111111","sentAtUtc":"2026-08-13T12:00:10Z","payload":{"brokerSymbol":"terminal.symbol","timeUtc":"2026-08-13T12:00:10Z","bid":100.0,"ask":100.2,"last":null,"volume":null}}
{"protocolVersion":1,"kind":"Request","operation":"GetInstrument","requestId":"22222222-2222-2222-2222-222222222222","sentAtUtc":"2026-08-13T12:00:11Z","payload":{"brokerSymbol":"terminal.symbol"}}
{"protocolVersion":1,"kind":"Response","operation":"GetAccount","requestId":"33333333-3333-3333-3333-333333333333","sentAtUtc":"2026-08-13T12:00:12Z","payload":{"login":123456,"server":"Demo","currency":"USD","balance":1000.0,"equity":1000.0,"margin":0.0,"freeMargin":1000.0,"marginLevel":0.0,"tradeMode":"Demo"}}
```
