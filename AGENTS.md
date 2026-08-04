# PortfolioTracker

## Stack
- .NET 10 + Blazor Server (WebAssembly not used)
- Minimal API with Entity Framework Core
- PostgreSQL via Docker
- Yahoo Finance API for stock/ETF/fund quotes

## Build & Run

```bash
# Development (local without Docker)
dotnet build PortfolioTracker.slnx
dotnet run --project src/PortfolioTracker.Api
dotnet run --project src/PortfolioTracker.Blazor

# Docker Compose (full stack)
docker-compose down && docker-compose up --build
```

## Architecture

```
src/
├── PortfolioTracker.Api/      # Minimal API + EF Core + PostgreSQL
│   ├── Models/
│   ├── Services/
│   ├── Data/
│   └── Program.cs
└── PortfolioTracker.Blazor/ # Blazor Server UI
    ├── Components/
    │   ├── Pages/
    │   └── Dialogs/
    ├── Services/
    └── Program.cs
```

## Key Patterns

### Blazor Server UserId
- `UserId` stored in `localStorage` via `UserIdService`
- Access `localStorage` in `OnAfterRenderAsync`, NOT `OnInitializedAsync` (JS interop unavailable during static rendering)
- Always initialize `UserIdService.UserId` before any API calls

### Dialog Component Binding
- Use `@bind-IsVisible="variable"` with two-way binding
- Component MUST have `EventCallback<bool> IsVisibleChanged` parameter
- Close dialog: `await IsVisibleChanged.InvokeAsync(false)`

### Docker Build Context
- Each project (Api, Blazor) has its own Dockerfile
- Docker Compose context must point to the project subdirectory, NOT root:
  ```yaml
  blazor:
    build:
      context: ./src/PortfolioTracker.Blazor
      dockerfile: Dockerfile
  ```

### Antiforgery
- Disabled for this simple app (no login system)
- If re-enabled: must use persistent key via environment or config

### NuGet Packages
- Yahoo.Finance 2.0.4 is not available; use version 3.0.0
- Npgsql.EntityFrameworkCore.PostgreSQL version 10.0.0 for .NET 10

## Common Issues

1. **"localStorage not available"** - Move JS interop to `OnAfterRenderAsync`
2. **"IsVisibleChanged not found"** - Add `EventCallback<bool> IsVisibleChanged` parameter to dialog
3. **"RemoteNavigationManager initialized"** - Usually caused by duplicate component registration
4. **Docker 404s on static files** - Verify `app.UseStaticFiles()` is called before `MapRazorComponents`
5. **Yahoo Finance API fails** - Free tier has rate limits; wrap calls in try/catch

## Testing the API

```bash
# Health check
curl http://localhost:8080/health

# Get portfolio
curl http://localhost:8080/api/portfolio/{userId}

# Search symbol
curl "http://localhost:8080/api/search?query=AAPL"
```
