# PortfolioTracker

## Stack
- .NET 10 + Blazor Server (not WebAssembly)
- Minimal API with Entity Framework Core
- PostgreSQL via Docker
- Price providers: Twelve Data → FMP → EOD → persistent cache fallback
- Tests: xUnit in `tests/PortfolioTracker.Tests` (pure calculators only; no DB tests)

## Build & Run

```bash
# Clean, build, run
dotnet build PortfolioTracker.slnx
dotnet test tests/PortfolioTracker.Tests
dotnet run --project src/PortfolioTracker.Api
dotnet run --project src/PortfolioTracker.Blazor

# Docker Compose (full stack with hot reload via F5 in Visual Studio)
docker-compose down
# Then Clean Solution + Rebuild + F5 from Visual Studio
```

Run a single test: `dotnet test tests/PortfolioTracker.Tests --filter "FullyQualifiedName~Xirr"`

## mcp-pandas MCP server

- CSVs to analyze live in `csv/` → mounted read-only at `/csv` inside the `mcp-pandas` container (see `docker-compose.yml`). Pass MCP tool paths like `/csv/<file>.csv`; `./data` stays mounted at `/data`.
- The running stack is managed by the **Visual Studio** compose project (containers named `dockercompose*-…`, images `*:dev`). To recreate only the MCP service after editing its volumes: `docker compose -p <vs-project-name> up -d --force-recreate --no-deps mcp-pandas`. Running plain `docker compose up` creates a SECOND parallel project and collides on port 8082.

## Secrets

API keys are NOT in the repo. They live in:
- `.env` (gitignored) — used by docker-compose via `${TWELVEDATA_API_KEY}`, `${FMP_API_KEY}`, `${EOD_API_KEY}`
- `dotnet user-secrets` on project `src/PortfolioTracker.Api` (`TwelveDataApiKey`, `FmpApiKey`, `EodApiKey`) — for host `dotnet run`
- CI does not need them (tests are pure units)

Providers: Twelve Data (stocks/ETFs/funds), FMP (stocks), EOD Historical Data (mutual funds, free tier 20 calls/day).

## History, Transactions & Performance

- `PortfolioHistoryPoints`: one row/UTC-day per item + total (`ItemId=Guid.Empty`). Dashboard load **upserts today's row** with live prices (never skip: a backfill may have pre-created forward-filled rows). `POST /api/portfolio/{uid}/backfill` backfills 12m **strictly before today** (EOD funds → adjusted_close EUR native; stocks → TwelveData /eod falling back to Yahoo chart; manual-priced funds → flat line from SymbolPrices). ForwardFill carries last NAV over missing business days.
- **Backfill replays `PortfolioTransactions`**: provider series are price-per-share, multiplied daily by shares actually held (Buy/Sell/Transfer replay) — historical values match real positions. Backfill calls `EnsureSeededAsync` first; seed lots with real FIFO data (see Importing Fund Lots) for accurate MWR/XIRR.
- `PortfolioTransactions`: Buy/Sell/TransferIn/TransferOut. Synthetic seed: first transactions query creates one legacy Buy per existing position. Transfers create a linked pair and move weighted-average cost to the destination (v1 approximation; real Spanish traspaso conserves original cost basis).
- Performance card shows ONE row: **Ponderada** (Modified Dietz with external flows; transfers excluded) + XIRR footer (`GET /performance-detailed`). Do NOT re-add a "Simple" row — the user removed it after it showed nonsense like +1447% on windows full of contributions.
- **USER RULE**: the "Desde inicio (dd/MM)" period must EXACTLY equal the header's Total Gain/Loss % (total gain ÷ total invested). Enforced in `TransactionService.ComputeDetailedPerformanceAsync` — do not replace it with Dietz for that period.
- Periods whose window starts before portfolio inception are labeled "Desde inicio"; YTD/1A collapse into one row when identical.

### Fund NAV reality (why "Hoy" can be 0.00%)
- Mutual-fund NAVs publish once daily (~20:00-22:00 CET). Until tonight's NAV lands, today's stored value equals yesterday's → per-fund "Hoy" = 0.00% is CORRECT, not a bug. Exchange-traded items (BABA, ETFs) move live via TwelveData quotes.
- UI convention: a Fund whose daily change is exactly 0 renders muted with tooltip "NAV de hoy aún no publicado" (`Home.razor`, Hoy column) so it reads as "pending" instead of broken. Don't remove it or fake movement.

## Architecture

```
src/
├── PortfolioTracker.Api/      # Minimal API + EF Core + PostgreSQL
│   ├── Models/                # Domain entities and DTOs
│   ├── Services/               # Business logic and price providers
│   ├── Data/                  # DbContext and migrations
│   └── Program.cs             # DI, endpoints, middleware
├── PortfolioTracker.Blazor/   # Blazor Server UI
│   ├── Components/
│   │   ├── Pages/            # Home.razor, etc.
│   │   └── Dialogs/           # AddItemDialog, EditItemDialog, TransferDialog
│   ├── Services/              # API client, models
│   └── Program.cs
└── tests/
    └── PortfolioTracker.Tests/ # xUnit, pure calculators (no DB)
```

CI: `.github/workflows/ci.yml` runs restore + build + test on push/PR. No keys needed.

## Price Provider Chain

`CompositePriceProvider` tries in order:
1. **TwelveData** - stocks/ETFs use `/quote` (root `close`); funds (`0P*`) use `/eod` with `mic_code=XFRA` — response shape differs: price lives in `values[0].close`
2. **FMP** - stocks/ETFs, plan-dependent coverage
3. **EOD Historical Data** - mutual funds, use `/api/real-time` or `/api/eod`
4. **Persistent cache** (SymbolPrices table) - last resort fallback
5. Alt-symbol funds (`PortfolioService` → `EodPriceProvider` directly): **daily cadence gate** — live-fetch each fund at most once per UTC day, plus one evening refresh after 19:00 UTC (when NAVs publish). Otherwise serves the persisted `SymbolPrices` value. HTTP 401/402/403/429 cuts the attempt chain immediately (no suffix/eod retries). Successes persist to SymbolPrices; failures fall back to them only if <7 days old.

### Currency Handling
- EOD returns prices in fund's native currency
- `.EUFUND` suffix = EUR, convert EUR→USD using `CurrencyService.GetEurToUsdRateAsync()`
- All internal calculations assume USD; convert to EUR only at display layer

### Alternative Symbol for Funds
- Some funds (Morningstar `0P...`) are not covered by data providers directly
- User sets `AlternativeSymbol` = `<ISIN>.EUFUND` (works for ES/LU/IE ISINs, e.g. `LU1598719752.EUFUND`, `IE00BYX5NX33.EUFUND`) and enables `UseAlternativeSymbol`
- When enabled, `PortfolioService` calls **EOD directly** with the alternative symbol, bypassing other providers
- EOD real-time returns `close: "NA"` for many funds → provider falls back to `previousClose`; if that fails it retries the symbol without the `.EUFUND` suffix
- If no provider returns a price, DTO has `PriceAvailable=false` and UI shows an "N/A / no price" badge — never fabricate 0-price gains

### Importing Fund Lots (broker FIFO data)
- The app stores ONE position per symbol: single average price, no FIFO lots
- To import multiple subscriptions: `POST /api/portfolio` once per lot **sequentially** — duplicates auto-merge (shares summed, weighted avg price, commission added)
- Merge quirk: on merge the request's `Name`/`Type`/`AlternativeSymbol` are IGNORED, and `PurchaseDate` is overwritten by each POST (last wins)
- After importing lots, `PUT` the item to set the official name, first-purchase date, and alternative symbol
- Symbol duplicate check is case-insensitive (`ToUpperInvariant`); unique index `(UserId, Symbol)` enforced

### Yahoo Search Quirks
- Search by name can return the WRONG instrument (e.g. "Vanguard Emerging Markets Stock" → US ETF `VWO` instead of the mutual fund). If suspicious, search by ISIN directly (`?query=IE0031786142`) which returns the Morningstar code (e.g. `0P000060MS`)

## Key Endpoints

| Endpoint | Purpose |
|----------|---------|
| `GET /api/portfolio/{userId}/dashboard` | Items + performance in **one call** (preferred over `/portfolio` + `/performance`). Sorted by `CurrentValue` desc. Writes the daily snapshot |
| `GET /api/portfolio/{userId}/history` | Daily value series: `total[]` + per-item map (charts) |
| `POST /api/portfolio/{userId}/backfill` | Backfills 12 months of daily values (idempotent) |
| `GET /api/portfolio/{userId}/transactions` | Lists ops; first call seeds one synthetic Buy per existing position |
| `GET /api/portfolio/{userId}/transactions/{itemId}` | Ops for one position |
| `POST /api/portfolio/{userId}/transactions` | Add Buy/Sell (updates shares + weighted avg) |
| `POST /api/portfolio/{userId}/transfers` | Linked TransferOut/TransferIn pair between two positions |
| `GET /api/portfolio/{userId}/performance-detailed` | Modified-Dietz per period ("Desde inicio" forced equal to header Total Gain/Loss %) + XIRR since inception |
| `GET /api/search?query=` | Search symbols via Yahoo Finance |
| `POST /api/portfolio` | Add investment (merges if symbol exists for user) |
| `PUT /api/portfolio/{id}/{userId}` | Update investment |
| `DELETE /api/portfolio/{id}/{userId}` | Delete investment |
| `GET /health` | Health check |

Server-side validation on POST/PUT returns **400 with `{ "error": "..." }`** (Shares>0, PurchasePrice>0, Commission≥0, Name required/≤200, Symbol≤20). The Blazor client (`PortfolioApiService.ThrowApiErrorAsync`) surfaces that message in the dialogs — keep it that way instead of swallowing errors.

## Key Patterns

### UserId
- Fixed GUID for development: `12345678-1234-1234-1234-123456789012`
- Stored in `UserIdService` singleton

### Dialog Binding
- Use `@bind-IsVisible="variable"` with `EventCallback<bool> IsVisibleChanged`
- Close: `await IsVisibleChanged.InvokeAsync(false)`

### Component Render Mode
- Pages use `@rendermode InteractiveServer` for interactivity
- Load data in `OnAfterRenderAsync(firstRender)` with `await InvokeAsync(StateHasChanged)`

## Common Issues

1. **"Connection refused : 8080" on first load** - API container not ready. `PortfolioApiService` has 10 retries with 1s delay. If persists, check API health: `curl http://localhost:8080/health`

2. **Fund price shows wrong value** - Check if fund needs alternative symbol (e.g., Morningstar `0P...` funds often require EOD with `.EUFUND` suffix)

3. **Yahoo Finance TooManyRequests** - IP blocked from rate limiting. Avoid by using EOD for funds. Yahoo Finance only used for symbol search.

4. **Duplicate API calls** - `CompositePriceProvider` uses `ConcurrentDictionary` to deduplicate concurrent requests for same symbol

5. **Docker port 8081 already in use** - Container not cleaned up. Use Docker Desktop to stop/remove containers, then `Clean Solution` + Rebuild + F5

6. **Docker Desktop not running** - Everything fails ("Connection refused", daemon pipe errors). Start Docker Desktop first, verify `docker ps` works

7. **All alt-symbol funds show N/A "no price" at once** - EOD free tier (20 calls/day) exhausted. The daily cadence gate (`EodPriceProvider`) now caps live fetches at ~1/fund/day (+evening refresh), so this should only happen after heavy backfills. Recovery path: `EodPriceProvider` persists successes to `SymbolPrices` and falls back to them (<7 days old); seed missing rows via SQL from latest history values ÷ shares ÷ usdToEur. Quota resets at UTC midnight.
8. **`DbContext` "second operation started" under parallel price fetching** - price providers must not use the scoped `PortfolioDbContext` from parallel `Task.WhenAll` loops. `SymbolPriceService` uses `IDbContextFactory<PortfolioDbContext>` (short-lived contexts per call); keep it that way for anything new called from provider code.

## Known Issues

(Already fixed, don't re-introduce: DI double-registration, CompositePriceProvider races/cancellation NRE, per-call User-Agent mutation, DetectCurrency prefix heuristic, uncached FX fallback, sequential price fetching, committed API keys, backfill using current share counts.)

- Transfers approximate moved shares with source average price (real traspaso conserves original cost basis at destination)
- TwelveData free plan `/eod` returns only the latest bar — stock history relies on the Yahoo fallback (rate-limit sensitive)
- EOD fund NAVs publish with a lag (T+1/T+2): recent days may be forward-filled values
- No realized-gains tracking for Sells yet; no transaction delete/edit UI (API-only)
- Rotate the leaked API keys at each provider (they were committed before the secrets cleanup)
- Raw window % ignores contribution timing by definition (windows with big inflows look inflated, e.g. +22% "3M") — that's why the card shows Modified Dietz instead. Do not "simplify" it back to raw change

## Testing the API

```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/portfolio/12345678-1234-1234-1234-123456789012/dashboard
curl "http://localhost:8080/api/search?query=AAPL"
curl http://localhost:8080/api/portfolio/12345678-1234-1234-1234-123456789012/performance-detailed
```

## Database

- PostgreSQL 16 in Docker; migrations applied on startup via `db.Database.Migrate()`. Generate with `dotnet ef migrations add <Name>` then `dotnet ef database update`, both from `src/PortfolioTracker.Api`
- Tables: `PortfolioItems` (one row per symbol per user, unique `(UserId, Symbol)`), `SymbolPrices`, `PortfolioHistoryPoints`, `PortfolioTransactions`
- `SymbolPrices` rows with `Provider='Manual'` are hand-seeded fallbacks (e.g. `0P0001NCW3`) and persist because no live provider ever overwrites them — update via SQL when a manual price must change
- Dev database has REAL lot transactions seeded for the dev user (39 FIFO rows) — deleting `PortfolioTransactions` and re-running backfill regenerates consistent history
- Snapshot/history semantics are in "History, Transactions & Performance" above (unique index `(UserId, ItemId, Date)`; totals = `ItemId=Guid.Empty`; today's row is upserted live on every dashboard load)

## Culture Gotcha (IMPORTANT)

The dev machine runs es-ES culture but the API container runs invariant. NEVER call `decimal.TryParse(str)` without `CultureInfo.InvariantCulture` — `"119.34000"` parses as 11,934,000 under es-ES (dot = group separator). Providers returning string prices (TwelveData/EOD) must always use `NumberStyles.Number + InvariantCulture`. `JsonElement.GetDecimal()` is safe. Regression test exists in `HistoryBackfillParseTests`.

## Charting

- `Components/PortfolioChart.razor`: dependency-free SVG area chart fed with `List<HistoryPointDto>`; Home.razor filters points client-side by period (1D/1S/1M/3M/6M/YTD→"Inicio"/1A)
- **1D shows NO chart** (two points aren't a series): the card shows a big hero % vs last available day, and per-position daily change lives in the main table's "Hoy" column (`DailyByItem` in Home.razor). Don't reintroduce a 1D chart or a separate daily-breakdown list.
- The YTD button label is dynamic: "Inicio" while all history is inside the current calendar year, "YTD" once pre-January data exists
- Allocation doughnut (`AllocationChart.razor` + `wwwroot/charts.js`): legend shows fund NAMES (truncated to 30 chars), percentages drawn ON slices by an inline Chart.js plugin (slices <4% unlabeled), tooltip uses full name
