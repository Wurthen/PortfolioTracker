# PortfolioTracker

## Language (IMPORTANT)

- Reply to the user in **Castilian Spanish from Spain (es-ES)**. Never use Rioplatense/voseo, Argentine slang, or regional Latin-American variants.
- Generated artifacts (code, comments, UI copy, docs, commits, memory entries) default to English unless the user requests otherwise.

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

The stack runs **always-on** from the CLI under the fixed project name `portfoliotracker` (top-level `name:` in `docker-compose.yml`); every service has `restart: unless-stopped`.

```bash
docker compose up -d --build   # rebuild + start (always-on stack)
docker compose stop            # stop to free the ports for VS debugging (won't auto-restart)
docker compose up -d           # resume after debugging
```

Visual Studio (F5) launches its own compose project and **cannot coexist** with the always-on stack — the ports collide. Stop the CLI stack before debugging from VS, then bring it back up. To recreate a single service: `docker compose up -d --force-recreate --no-deps mcp-pandas`.

Compose services: API on `8080`, Blazor on `8081`, Postgres on `5432`, mcp-pandas on `8082`. The API waits for the Postgres `pg_isready` healthcheck before starting.

Both Dockerfiles use their own project directory as build context and build standalone:

```bash
docker build -t pt-api "src/PortfolioTracker.Api"
docker build -t pt-blazor "src/PortfolioTracker.Blazor"
```

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

`SymbolClassifier` is the single source of truth for fund detection (`0P*`, `.EUFUND`) and suffix stripping. Add new fund heuristics there, not inline.

Key quirks:
- Alpaca covers **US stocks and ETFs only**. European tickers like `PHYMF` return `NotFound`; the fallback chain handles them.
- Alt-symbol funds (`UseAlternativeSymbol`) try `EodPriceProvider` first; if that fails, `PortfolioService` retries the composite chain with the item's **primary** symbol.
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

- Provider quotes are normalized to **USD** (`CurrentPriceUsd`). `EodPriceProvider` converts native-EUR fund prices (`.EUFUND`) to USD via `CurrencyService.GetEurToUsdRateAsync()` so the whole chain speaks USD.
- Portfolio values, gain/loss and history are **EUR**: `MapToDto` converts USD→EUR for `CurrentPrice`/`CurrentValue`/`GainLoss`, and snapshots store EUR (`PortfolioHistoryPoint.ValueEur`). The historical return series are EUR too.
- `CurrencyService` falls back to a hardcoded rate (0.92 / 1.09) and caches it briefly when the FX API fails.

### Portfolio Merge Behavior

`POST /api/portfolio` merges duplicate symbols per user (unique index `(UserId, Symbol)`). On merge:
- `Name`, `Type`, and `AlternativeSymbol` from the request are **ignored**.
- `PurchaseDate` is overwritten (last wins), so it is **not** the first purchase date.
- Use `PUT /api/portfolio/{id}/{userId}` to set official metadata.

### History Coverage (TWR correctness)

The TWR series is only as good as the totals it compounds. Three invariants keep it sane:

- `HistoryBackfillService.ResolveFromDate` starts each item's series at `min(PurchaseDate, first transaction)`, never at `PurchaseDate` alone (the merge overwrites it). A series truncated at the last top-up makes the whole position "appear" one day with no flow (real incident: fake +22.2% on 2026-09-03 from a truncated Cobas series).
- `ComposeForwardFilledTotals` recomputes **every** total on each backfill run with per-item forward fill: a missing NAV must never remove a position from a total. A plain re-run now repairs totals (no `rebuild=true` needed for that).
- `SaveDailySnapshotAsync` carries the last known history value forward for unpriced items, so today's snapshot never drops a position.
- `ReturnCalculators.ComputeTwrSeries` absorbs no-flow daily moves over ±20% (data artifacts) instead of booking them as return. Flows still count normally.

After any change to the replay/backfill, verify per item that its first history point equals its first transaction date and that no daily TWR move exceeds ~±5%.

### Performance "Desde inicio"

The "Desde inicio" value must **exactly** equal total gain ÷ total invested. Single source of truth is `PortfolioService.ComputeSinceInception` (surfaced on the dashboard as `SinceInceptionGainLossPercent`); `Home.razor` uses it for both the KPI and the "Desde inicio" period card. Do not replace it with Dietz.

### Return Metrics (chart vs KPI)

`ComputeSinceInception`/`ComputeSimpleReturnSeries` (simple = gain ÷ invested) is the source of truth for the "Ganancia / Pérdida" KPI and every since-inception view. When the chart window covers the full history (`Inicio`, and `1A` while the portfolio is younger than a year), `Home.razor` plots `History.TotalSimpleReturn` and shows `SinceInceptionGainLossPercent`, so it matches the KPI. **TWR (`ReturnCalculators.ComputeTwrSeries`, `History.TotalReturn`) is used only for bounded windows** (1S–6M), where it removes the distorting effect of contributions made inside the period. Never show TWR for a full-history window.

The "Rentabilidad por periodo" cards do not carry their own metric: `Home.razor` derives each bounded window from the same TWR series the chart plots (`PeriodReturn` + `FilterSeries`), and shows `SinceInceptionGainLossPercent` for "Desde inicio". Modified Dietz was removed; do not reintroduce a second metric per window.

### SafeBack

SafeBack is cash received (e.g., from Trade Republic) that is automatically reinvested at market price. It is treated as an **external contribution** (as if the user deposited the money): the `SafeBack` transaction adds shares **and cost basis**, so the injection itself never shows as return — only the market performance of those shares counts afterwards. It accumulates `SafeBackAmount`/`SafeBackShares` for tracking and the UI surfaces a flat `+X € SafeBack` badge (row) and total (KPI card). `HistoryBackfillService.SharesHeldAt` must count `SafeBack` shares so historical values stay consistent with the live snapshot. Use `POST /api/portfolio/{userId}/backfill?rebuild=true` after changing the replay logic: a plain re-run skips existing item rows and will not rewrite them (totals are recomputed either way). Mind the EOD free tier (20 calls/day) before triggering a full rebuild.

`TransactionService.AddSafeBackAsync` merges the injection via `PortfolioService.ComputeWeightedAveragePrice` with `addedCostEur = AmountEur`. SafeBack is an external cash flow in both TWR (`ReturnCalculators.BuildExternalFlowsByDate`) and XIRR (`TransactionService.GetExternalFlowsAsync`), and it adds to the invested cost in `ComputeSimpleReturnSeries`/`ComputeItemReturnSeries`. `scripts/recalculate_safeback_costbasis.sql` recalculates `PurchasePrice` as `SUM(Buy + TransferIn + SafeBack AmountEur) / Shares` for items with historical SafeBacks; it is idempotent, so re-run it after restoring a backup or changing the cost model.

### Charting Conventions

- **1D shows no chart** — only the hero % and the per-position "Hoy" column.
- YTD button label is dynamic: "Inicio" while all history is inside the current calendar year, "YTD" once pre-January data exists.

## UI Theme

The Blazor app uses a custom dark theme built on top of Bootstrap 5. Theme tokens are CSS custom properties in `src/PortfolioTracker.Blazor/wwwroot/app.css`. Do not add new UI libraries without discussing it; prefer extending the existing token system.

## Session Context

At the start of every session, read `notes/memory.md` and any other `*.md` files in `notes/`. The global skill `session-context-recall` is configured to load this folder automatically. Use it as the primary source of recent context, decisions, bugs, features, and next steps.

## Project Scripts & Notes

- `scripts/` — maintenance SQL (`rebuild_gold.sql`); `recalculate_safeback_costbasis.sql` recalculates cost basis for items with historical SafeBacks (idempotent).
- `scripts/open_portfoliotracker.ps1` — one-click launcher (desktop shortcut `PortfolioTracker.lnk`): opens `http://localhost:8081`, starting Docker Desktop and the compose stack first if needed. `scripts/portfoliotracker.ico` is its icon.
- `notes/` — session memory plus `notes/audit-2026-09-10.md` (full code audit with a resolution status section added on 11/09) and `notes/plan-multi-portfolio-import.md` (agreed plan for multi-portfolio support and broker CSV/PDF import).

## Common Gotchas

- **"Connection refused : 8080"** — API container not ready. `PortfolioApiService` retries 10× with 1s delay. Verify: `curl http://localhost:8080/health`.
- **Docker port 8081 already in use** — stale container. Stop/remove via Docker Desktop, then Clean Solution + Rebuild + F5.
- **All alt-symbol funds show N/A at once** — EOD free tier (20 calls/day) exhausted. Recovery: use persisted `SymbolPrices` (< 7 days old); quota resets at UTC midnight.
- **Yahoo search by name can return the wrong instrument**. Search by ISIN when in doubt (`?query=IE0031786142`).
- **Transfers** approximate moved shares with the source fund's average price; real Spanish *traspaso* conserves original cost basis at destination.
- **No realized-gains tracking** for Sells yet; no transaction delete/edit UI (API-only).
- **Every `GET /dashboard` writes today's snapshot and live-fetches a price for every position** — repeated reloads burn provider quota (EOD free tier is 20 calls/day).
- **Without secrets, providers skip live calls** — `appsettings.json` ships empty API keys, and every provider (including TwelveData) returns null when its key is empty; the chain falls back to persisted `SymbolPrices`.
- **`GET /performance` was removed** — the UI uses `/dashboard` (KPIs) and `/performance-detailed` (periods and XIRR).

## CI

`.github/workflows/ci.yml` runs restore + build + test on push/PR. No API keys required.
