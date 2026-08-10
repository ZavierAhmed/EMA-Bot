# EMA Bot

EMA Bot is a small private administration application for an EMA trading bot. Milestone 0 provides the secure application foundation only: an Admin login, MySQL persistence, API health reporting, and a light React shell. It is currently intended to grow toward backtesting and paper trading; no exchange integration, trading logic, charting, or live order execution exists yet.

## Prerequisites

- .NET SDK 10.0.x (the project targets .NET 10 LTS)
- Node.js 22 LTS or newer with npm
- MySQL 8.4 LTS (or a compatible current MySQL server)

## Configure local secrets

From `backend/src/EmaBot.Api`, initialize user-secrets and set a MySQL connection plus the one-time initial Admin credentials:

```powershell
dotnet user-secrets init
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

## Database migration

The initial migration is committed at `backend/src/EmaBot.Api/Migrations`. The API applies pending migrations at startup. To apply migrations explicitly:

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

The backend tests use an in-memory test database to verify unauthenticated access is rejected, invalid login is rejected, and a valid Admin can load `/api/auth/me`. For a full MySQL smoke test, configure the secrets above, run the API, sign in through the UI, and call `GET /api/health`.

## Configuration defaults

`backend/src/EmaBot.Api/appsettings.json` contains only non-secret future defaults:

- `Binance:Environment` = `Futures`
- `Trading:DefaultRiskReward` = `2.0`
- `Trading:DefaultFixedOrderSizeUsdt` = `100`

They are configuration placeholders only; no trading behavior is implemented.
