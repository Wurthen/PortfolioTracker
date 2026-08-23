# PortfolioTracker

## Stack
- .NET 10 + Blazor Server (not WebAssembly)
- Minimal API with Entity Framework Core
- PostgreSQL via Docker
- Price providers: Twelve Data → FMP → EOD → persistent cache fallback

## Build & Run

```bash
# Clean, build, run
dotnet build PortfolioTracker.slnx
dotnet run --project src/PortfolioTracker.Api
dotnet run --project src/PortfolioTracker.Blazor

# Docker Compose (full stack with hot reload via F5 in Visual Studio)
docker-compose down
# Then Clean Solution + Rebuild + F5 from Visual Studio
```

## Architecture

```
src/
├── PortfolioTracker.Api/      # Minimal API + EF Core + PostgreSQL
│   ├── Models/                # Domain entities and DTOs
│   ├── Services/               # Business logic and price providers
│   ├── Data/                  # DbContext and migrations
│   └── Program.cs             # DI, endpoints, middleware
└── PortfolioTracker.Blazor/   # Blazor Server UI
    ├── Components/
    │   ├── Pages/            # Home.razor, etc.
    │   └── Dialogs/           # AddItemDialog, EditItemDialog
    ├── Services/              # API client, models
    └── Program.cs
```

## Price Provider Chain

`CompositePriceProvider` tries in order:
1. **TwelveData** - stocks/ETFs use `/quote` (root `close`); funds (`0P*`) use `/eod` with `mic_code=XFRA` — response shape differs: price lives in `values[0].close`
2. **FMP** - stocks/ETFs, plan-dependent coverage
3. **EOD Historical Data** - mutual funds, use `/api/real-time` or `/api/eod`
4. **Persistent cache** (SymbolPrices table) - last resort fallback

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
| `GET /api/portfolio/{userId}/dashboard` | Returns items + performance in **one call** (preferred over separate `/portfolio` + `/performance`) |
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

## Known Issues (do NOT re-diagnose; fix if touching that code)

- `Api/Program.cs`: `AddScoped<TwelveDataPriceProvider>()` etc. override the `AddHttpClient<T>` registrations — the named-client config is dead code
- `CompositePriceProvider`: `ConcurrentDictionary.GetOrAdd` valueFactory can run twice under contention; a Canceled task hits `throw null!` (NRE); `SavePriceAsync` failure aborts the whole dashboard
- `CurrencyService`: hardcoded `0.92m` fallback is not cached → retries dead endpoint on every conversion during outages
- `YahooFinanceService`: mutates shared `_httpClient.DefaultRequestHeaders` per call — not thread-safe under Blazor Server circuits
- `EodPriceProvider.DetectCurrency`: classifies any symbol starting ES/IT/BE/FR… as EUR (collides with US tickers like `ES`, `IT`)
- Prices are fetched sequentially per item on every dashboard load — burns EOD free quota (20 calls/day) fast
- API keys are committed in `appsettings.json` / `docker-compose.yml` — should move to user-secrets/env vars

## Configuration

API keys stored in:
- `src/PortfolioTracker.Api/appsettings.json` (local)
- `docker-compose.yml` environment variables (production)

Required keys:
- `TwelveDataApiKey` - stocks/ETFs/funds (get from https://twelvedata.com)
- `FmpApiKey` - stocks (get from https://site.financialmodelingprep.com)
- `EodApiKey` - mutual funds (get from https://eodhistoricaldata.com, free tier: 20 calls/day)

## Testing the API

```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/portfolio/12345678-1234-1234-1234-123456789012
curl "http://localhost:8080/api/search?query=AAPL"
```

## Database

- PostgreSQL 16 in Docker
- EF Core migrations applied on startup via `db.Database.Migrate()`
- Key tables: `PortfolioItems`, `SymbolPrices`
- `SymbolPrices` caches last successful price per symbol across providers
