# EMA Bot

EMA Bot is a small private administration application for an EMA trading bot. Milestone 1 adds public Binance USDⓈ-M Futures market data, Admin-managed monitored symbols, persisted global trading settings, and a diagnostic EMA 9/15/100 signal preview.

No real trades are placed, no paper trades exist, and no historical candles are permanently stored. Binance API keys are not required: this milestone uses only public market-data endpoints.

## Backtesting

Backtests fetch Binance USDⓈ-M candles on demand with paginated requests; there is no candle warehouse. Signals enter at the next candle open. Stops use the latest confirmed 2-left/2-right swing before the crossover, falling back to the previous ten completed candles. Position size is fixed USDT notional, targets use the saved R:R, and a configurable per-side simulation fee is applied at entry and exit.

The same-bar SL/TP ambiguity is conservative: stop loss wins. Trailing mode advances stop levels from 50% through 100% target progress and extends TP once to 110% at 70%; changes earned during a candle become active on the next candle. Funding, slippage, leverage, liquidation, and exchange execution are not modeled.

## Prerequisites

- .NET SDK 10.0.x (the project targets .NET 10 LTS)
- Node.js 22 LTS or newer with npm
- MySQL 8.4 LTS (or a compatible current MySQL server)

## Configure local secrets

`EmaBot.Api` already includes a committed `UserSecretsId`, so from `backend/src/EmaBot.Api` set a MySQL connection plus the one-time initial Admin credentials:

```powershell
dotnet user-secrets set "ConnectionStrings:EmaBotDatabase" "Server=localhost;Port=3306;Database=emabot;User ID=YOUR_MYSQL_USER;Password=YOUR_MYSQL_PASSWORD;"
dotnet user-secrets set "BootstrapAdmin:UserName" "admin"
dotnet user-secrets set "BootstrapAdmin:Email" "admin@example.com"
dotnet user-secrets set "BootstrapAdmin:Password" "Use-a-unique-strong-password-123!"
```

Alternatively, set `ConnectionStrings__EmaBotDatabase`, `BootstrapAdmin__UserName`, `BootstrapAdmin__Email`, and `BootstrapAdmin__Password` as environment variables. Never commit real values. On application startup, the database is migrated and an Admin is created only when the user table is empty. Later starts never reset an existing password.

The Admin password policy requires 12+ characters with uppercase, lowercase, digit, and non-alphanumeric characters. Five unsuccessful sign-in attempts lock the account for 15 minutes.

## Run locally

Start the API (it listens on `http://localhost:5158` in the Development profile):

```powershell
dotnet run --project backend/src/EmaBot.Api
```

In another terminal, start the Vite UI. Its development proxy forwards `/api` to the API so secure HttpOnly cookie authentication works locally without frontend secrets:

```powershell
cd frontend
npm install
npm run dev
```

Open the Vite address shown in the terminal, normally `http://localhost:5173`.

## Binance market data and strategy

The API uses `https://fapi.binance.com/fapi/v1/exchangeInfo` and `/fapi/v1/klines` for active USDT-margined perpetual contracts only. Supported strategy intervals are `3m`, `5m`, `15m`, `30m`, `1h`, `2h`, `4h`, `6h`, `8h`, `12h`, `1d`, `3d`, `1w`, and `1M` (one month).

EMA values use an SMA seed for their first full period, then the standard `2 / (N + 1)` multiplier. Only completed candles participate in crossover and confirmation evaluation. A crossover is either emitted immediately or confirmed by the next completed candle, according to the saved setting. The optional EMA 100 filter applies on the actual signal candle. Preview output also provides the normalized EMA 9/15 gap and its expanding/contracting state.

The Symbols page lets an Admin choose individual eligible contracts. The Settings page persists one global risk/reward, fixed USDT order size, confirmation, EMA 100, and future trailing-stop toggle. Trailing-stop behavior is documented only; it is not implemented.

## Database migration

Migrations are committed at `backend/src/EmaBot.Api/Migrations`. The API applies pending migrations at startup. To apply migrations explicitly:

```powershell
dotnet ef database update --project backend/src/EmaBot.Api --startup-project backend/src/EmaBot.Api
```

To add a future migration:

```powershell
dotnet ef migrations add MigrationName --project backend/src/EmaBot.Api --startup-project backend/src/EmaBot.Api
```

`dotnet-ef` is recorded as a local tool. Restore it once with `dotnet tool restore` from the repository root.

## Verify

```powershell
$env:EMA_BOT_NUGET_PACKAGES = "$PWD\.nuget\packages"
dotnet restore backend/EmaBot.sln --configfile NuGet.Config
dotnet build backend/EmaBot.sln --no-restore
dotnet test backend/EmaBot.sln --no-build

cd frontend
npm install
npm run lint
npm run build
```

The backend tests use an in-memory test database and mocked Binance HTTP responses. They cover auth, Binance metadata/kline parsing and rate-limit handling, EMA warmup, crossovers, confirmation expiry, EMA 100 filtering, and closed-candle behavior. For a full MySQL smoke test, configure the secrets above, run the API, sign in through the UI, and call `GET /api/health`.

## Configuration defaults

`backend/src/EmaBot.Api/appsettings.json` contains only non-secret future defaults:

- `Binance:Environment` = `Futures`
- `Trading:DefaultRiskReward` = `2.0`
- `Trading:DefaultFixedOrderSizeUsdt` = `100`

They seed the first persisted global settings record only; no trade execution behavior is implemented.
