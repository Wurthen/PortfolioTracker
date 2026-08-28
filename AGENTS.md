# PortfolioTracker

## Stack & Entrypoints

- .NET 10 Minimal API (`src/PortfolioTracker.Api/Program.cs`) + Blazor Server (`src/PortfolioTracker.Blazor/Program.cs`)
- PostgreSQL 16 via Docker; EF Core migrations run automatically on startup (`db.Database.Migrate()`)
- Tests: pure xUnit in `tests/PortfolioTracker.Tests` (no DB, no HTTP)
- `PortfolioTracker.slnx` is the solution file

## Build, Test & Run

```bash
# Build everything
dotnet build PortfolioTracker.slnx

# Run tests
dotnet test tests/PortfolioTracker.Tests

# Run a focused test
dotnet test tests/PortfolioTracker.Tests --filter "FullyQualifiedName~Xirr"

# Run locally (two terminals)
dotnet run --project src/PortfolioTracker.Api    # http://localhost:8080
dotnet run --project src/PortfolioTracker.Blazor # http://localhost:8081

# EF migrations (run from src/PortfolioTracker.Api)
dotnet ef migrations add <Name>
dotnet ef database update
```

Docker Compose is managed by the **Visual Studio** compose project. Do **not** run plain `docker compose up`; it creates a second project and collides on port 8082.

## Secrets

API keys are **never** committed. Store them in:

- `.env` (gitignored) — for Docker Compose:
  - `TWELVEDATA_API_KEY`, `FMP_API_KEY`, `EOD_API_KEY`, `ALPACA_API_KEY`, `ALPACA_SECRET_KEY`
- `dotnet user-secrets` on `src/PortfolioTracker.Api` — for `dotnet run`:
  - `TwelveDataApiKey`, `FmpApiKey`, `EodApiKey`, `AlpacaApiKey`, `AlpacaSecretKey`

CI does not need keys (tests are pure units).

## Price Providers

`PortfolioService` asks prices through `YahooFinanceService.GetCurrentPriceAsync`, which delegates to `IPriceProvider` = `CompositePriceProvider`. `CompositePriceProvider` routes by symbol pattern:

**Stocks / ETFs:** `Alpaca` → `TwelveData` → `FMP` → `EOD` → persistent cache  
**Funds (`0P*` or `.EUFUND`):** `EOD` → persistent cache only

- Alpaca covers **US stocks and ETFs only**. It never receives fund symbols (`AlpacaPriceProvider` short-circuits them as a guard).
- European ETF tickers (e.g. `PHYMF` on Xetra) will return `NotFound` from Alpaca even if the same ETF trades as `IAU` in the US. The fallback chain handles them.
- Alt-symbol funds (`UseAlternativeSymbol`) bypass the composite chain entirely; `PortfolioService` calls `EodPriceProvider` directly.
- `EodPriceProvider` has a **daily cadence gate** for funds: at most one live fetch per UTC day, plus an evening refresh after 19:00 UTC when NAVs publish. Otherwise it serves the persisted `SymbolPrices` row.
- HTTP 401/402/403/429 from EOD cuts the attempt chain immediately; failures fall back to `SymbolPrices` only if < 7 days old.

## Critical Implementation Rules

### Culture (DO NOT SKIP)

The dev machine runs `es-ES`; the API container runs invariant. **Always** parse string prices with `CultureInfo.InvariantCulture`:

```csharp
decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
```

`"119.34000"` parses as `11,934,000` under `es-ES`. `JsonElement.GetDecimal()` is safe. Regression test exists in `HistoryBackfillParseTests`.

### DbContext Threading

`PortfolioService` fetches prices with `Task.WhenAll(items.Select(...))`. Providers must **never** use the scoped `PortfolioDbContext` from within that parallel loop. `SymbolPriceService` uses `IDbContextFactory<PortfolioDbContext>` (short-lived context per call). Keep this pattern for anything new called from provider code.

### Currency

- All internal prices/calculations are **USD**.
- EOD fund prices are native currency; `.EUFUND` = EUR and is converted **EUR→USD** via `CurrencyService.GetEurToUsdRateAsync()`.
- Display layer converts USD→EUR.

### Portfolio Merge Behavior

`POST /api/portfolio` merges duplicate symbols per user (case-insensitive, unique index `(UserId, Symbol)`). On merge:
- `Name`, `Type`, and `AlternativeSymbol` from the request are **ignored**.
- `PurchaseDate` is overwritten by each request (last wins).
- Import lots sequentially, then `PUT` the item to set the official metadata.

### Performance "Desde inicio"

The first row in the performance card must **exactly** equal total gain ÷ total invested. Enforced in `TransactionService.ComputeDetailedPerformanceAsync`. Do not replace it with Dietz.

### Charting Conventions

- **1D shows no chart** — only the hero % and per-position "Hoy" column.
- YTD button label is dynamic: "Inicio" while all history is inside the current calendar year, "YTD" once pre-January data exists.
- Allocation doughnut legend shows fund names (truncated to 30 chars); tooltips show full names.

## Common Gotchas

1. **Fund "Hoy" = 0.00%** — mutual-fund NAVs publish once daily (~20:00–22:00 CET). Until then, today’s stored value equals yesterday’s. This is correct, not a bug. UI shows "NAV de hoy aún no publicado".
2. **All alt-symbol funds show N/A at once** — EOD free tier (20 calls/day) exhausted. Recovery: use persisted `SymbolPrices` (< 7 days old); quota resets at UTC midnight.
3. **"Connection refused : 8080"** — API container not ready. `PortfolioApiService` retries 10× with 1s delay. Verify: `curl http://localhost:8080/health`.
4. **Docker port 8081 already in use** — stale container. Stop/remove via Docker Desktop, then Clean Solution + Rebuild + F5.
5. **MCP pandas volume not updated** — use `docker compose -p <vs-project-name> up -d --force-recreate --no-deps mcp-pandas`. Plain `docker compose up` duplicates the project.
6. **Free TwelveData `/eod`** returns only the latest bar; stock history backfill falls back to Yahoo (rate-limit sensitive).
7. **Yahoo search by name can return the wrong instrument** (e.g. "Vanguard Emerging Markets Stock" → ETF `VWO` instead of the mutual fund). Search by ISIN when in doubt (`?query=IE0031786142`).
8. **Transfers** approximate moved shares with source average price; real Spanish *traspaso* conserves original cost basis at destination.
9. **No realized-gains tracking** for Sells yet; no transaction delete/edit UI (API-only).
10. **Rotate leaked provider API keys** — keys were committed to the repo before the secrets cleanup; replace them if still active.

## Useful Test Calls

```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/portfolio/12345678-1234-1234-1234-123456789012/dashboard
curl "http://localhost:8080/api/search?query=AAPL"
curl http://localhost:8080/api/portfolio/12345678-1234-1234-1234-123456789012/performance-detailed
```

## Database Tables

- `PortfolioItems` — one row per `(UserId, Symbol)`
- `SymbolPrices` — persistent price cache; `Provider='Manual'` rows are never overwritten by live providers
- `PortfolioHistoryPoints` — daily snapshots; `ItemId=Guid.Empty` is the total row; today’s row is upserted on every dashboard load
- `PortfolioTransactions` — Buy/Sell/TransferIn/TransferOut; synthetic legacy Buy seeded on first query

## CI

`.github/workflows/ci.yml` runs restore + build + test on push/PR. No API keys required.
