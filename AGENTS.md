# PortfolioTracker

## Stack & Entrypoints

- .NET 10 Minimal API (`src/PortfolioTracker.Api/Program.cs`) + Blazor Server (`src/PortfolioTracker.Blazor/Program.cs`).
- PostgreSQL 16; EF Core migrations apply automatically on API startup (`db.Database.Migrate()`).
- Tests: pure xUnit in `tests/PortfolioTracker.Tests` (no DB, no HTTP).
- Solution file: `PortfolioTracker.slnx`.
- Fixed `UserId` for local dev: `12345678-1234-1234-1234-123456789012`.

## Build, Test & Run

```bash
# Restore tools first (dotnet-ef is local)
dotnet tool restore

# Build everything
dotnet build PortfolioTracker.slnx

# Run all tests
dotnet test tests/PortfolioTracker.Tests

# Focused test
dotnet test tests/PortfolioTracker.Tests --filter "FullyQualifiedName~Xirr"
```

Run locally in two terminals:

```bash
# API
dotnet run --project src/PortfolioTracker.Api --urls "http://localhost:8080"

# UI (expects API at http://localhost:8080 by default)
dotnet run --project src/PortfolioTracker.Blazor --urls "http://localhost:5115"
```

The Blazor base address is set from the `ApiBaseUrl` environment variable; default is `http://localhost:8080` (`src/PortfolioTracker.Blazor/Program.cs`). If you run the API on a different port, set it before launching Blazor:

```bash
$env:ApiBaseUrl="http://localhost:5208"
dotnet run --project src/PortfolioTracker.Blazor
```

## EF Migrations

`dotnet-ef` is a local tool. Restore tools once, then:

```bash
dotnet tool run dotnet-ef migrations add <Name> --project src/PortfolioTracker.Api
```

Migrations apply automatically when the API starts.

## Docker / Visual Studio Compose

The compose project is managed by **Visual Studio**. Do **not** run plain `docker compose up`; it creates a second project and collides on port `8082`.

```bash
# If you must recreate just mcp-pandas
docker compose -p <vs-project-name> up -d --force-recreate --no-deps mcp-pandas
```

Compose services: API on `8080`, Blazor on `8081`, Postgres on `5432`, mcp-pandas on `8082`.

## Secrets

API keys are **never** committed. Store them in:

- `.env` (gitignored) — for Docker Compose:
  - `TWELVEDATA_API_KEY`, `FMP_API_KEY`, `EOD_API_KEY`, `ALPACA_API_KEY`, `ALPACA_SECRET_KEY`
- `dotnet user-secrets` on `src/PortfolioTracker.Api` — for `dotnet run`:
  - `TwelveDataApiKey`, `FmpApiKey`, `EodApiKey`, `AlpacaApiKey`, `AlpacaSecretKey`

CI does not need keys (tests are pure units).

## Price Provider Chain

`YahooFinanceService.GetCurrentPriceAsync` → `CompositePriceProvider`:

- **Stocks / ETFs:** `Alpaca` → `TwelveData` → `FMP` → `EOD` → persistent cache
- **Funds (`0P*` or `.EUFUND`):** `EOD` → persistent cache only

Key quirks:
- Alpaca covers **US stocks and ETFs only**. European tickers like `PHYMF` return `NotFound`; the fallback chain handles them.
- Alt-symbol funds (`UseAlternativeSymbol`) bypass the composite chain; `PortfolioService` calls `EodPriceProvider` directly.
- `EodPriceProvider` has a daily cadence gate for funds: at most one live fetch per UTC day, plus an evening refresh after 19:00 UTC when NAVs publish.
- HTTP 401/402/403/429 from EOD cuts the attempt chain immediately; failures fall back to `SymbolPrices` only if < 7 days old.
- Fund NAVs publish once daily (~20:00–22:00 CET), so funds show `0.00%` intraday. This is expected.

## Critical Implementation Rules

### Culture

The dev machine runs `es-ES`; the API container runs invariant. **Always** parse string prices with `CultureInfo.InvariantCulture`:

```csharp
decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
```

`"119.34000"` parses as `11,934,000` under `es-ES`. `JsonElement.GetDecimal()` is safe. Regression test exists in `HistoryBackfillParseTests`.

### DbContext Threading

`PortfolioService` fetches prices with `Task.WhenAll(items.Select(...))`. Providers must **never** use the scoped `PortfolioDbContext` from within that parallel loop. Use `IDbContextFactory<PortfolioDbContext>` (short-lived context per call), as `SymbolPriceService` does.

### Currency

- All internal prices/calculations are **USD**.
- EOD fund prices are native currency; `.EUFUND` = EUR and is converted **EUR→USD** via `CurrencyService.GetEurToUsdRateAsync()`.
- Display layer converts USD→EUR.

### Portfolio Merge Behavior

`POST /api/portfolio` merges duplicate symbols per user (unique index `(UserId, Symbol)`). On merge:
- `Name`, `Type`, and `AlternativeSymbol` from the request are **ignored**.
- `PurchaseDate` is overwritten (last wins).
- Use `PUT /api/portfolio/{id}/{userId}` to set official metadata.

### Performance "Desde inicio"

The first period row must **exactly** equal total gain ÷ total invested. Enforced in `TransactionService.ComputeDetailedPerformanceAsync`. Do not replace it with Dietz.

### SafeBack

SafeBack is cash received (e.g., from Trade Republic) that is automatically reinvested at market price. It is recorded as a `SafeBack` transaction, increases shares like a `Buy`, raises the cost basis, and accumulates `SafeBackAmount` for tracking. It does **not** create instant unrealized gain.

### Charting Conventions

- **1D shows no chart** — only the hero % and the per-position "Hoy" column.
- YTD button label is dynamic: "Inicio" while all history is inside the current calendar year, "YTD" once pre-January data exists.

## UI Theme

The Blazor app uses a custom dark theme built on top of Bootstrap 5. Theme tokens are CSS custom properties in `src/PortfolioTracker.Blazor/wwwroot/app.css`. Do not add new UI libraries without discussing it; prefer extending the existing token system.

## Session Context

At the start of every session, read `notes/memory.md` and any other `*.md` files in `notes/`. The global skill `session-context-recall` is configured to load this folder automatically. Use it as the primary source of recent context, decisions, bugs, features, and next steps.

## Project Scripts & Notes

- `scripts/` — maintenance SQL/scripts (e.g., `rebuild_gold.sql`)
- `notes/` — session memory and context files

## Common Gotchas

- **"Connection refused : 8080"** — API container not ready. `PortfolioApiService` retries 10× with 1s delay. Verify: `curl http://localhost:8080/health`.
- **Docker port 8081 already in use** — stale container. Stop/remove via Docker Desktop, then Clean Solution + Rebuild + F5.
- **All alt-symbol funds show N/A at once** — EOD free tier (20 calls/day) exhausted. Recovery: use persisted `SymbolPrices` (< 7 days old); quota resets at UTC midnight.
- **Yahoo search by name can return the wrong instrument**. Search by ISIN when in doubt (`?query=IE0031786142`).
- **Transfers** approximate moved shares with the source fund's average price; real Spanish *traspaso* conserves original cost basis at destination.
- **No realized-gains tracking** for Sells yet; no transaction delete/edit UI (API-only).

## CI

`.github/workflows/ci.yml` runs restore + build + test on push/PR. No API keys required.
