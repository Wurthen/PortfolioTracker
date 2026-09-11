# Portfolio Tracker

Personal investment portfolio tracker: a .NET 10 Minimal API plus a Blazor Server UI, backed by PostgreSQL 16. It stores positions, transactions and daily snapshots, fetches live prices through a multi-provider chain, and charts value and return (simple since-inception and TWR for bounded windows).

## Solution layout

| Path | Purpose |
|------|---------|
| `src/PortfolioTracker.Api` | Minimal API, EF Core, price providers, history backfill |
| `src/PortfolioTracker.Blazor` | Blazor Server UI (dark theme, Bootstrap 5) |
| `tests/PortfolioTracker.Tests` | Pure xUnit tests (no DB, no HTTP) |
| `scripts/` | Maintenance SQL scripts |
| `notes/` | Session memory and audit notes |

## Quickstart

```bash
# Restore local tools (dotnet-ef is a local tool)
dotnet tool restore

# Build and test
dotnet build PortfolioTracker.slnx
dotnet test tests/PortfolioTracker.Tests
```

Run in two terminals:

```bash
# API (migrations apply automatically on startup)
dotnet run --project src/PortfolioTracker.Api --urls "http://localhost:8080"

# UI (expects the API at http://localhost:8080 by default)
dotnet run --project src/PortfolioTracker.Blazor --urls "http://localhost:5115"
```

If the API runs on another port, set `ApiBaseUrl` before launching the UI:

```powershell
$env:ApiBaseUrl="http://localhost:5208"
dotnet run --project src/PortfolioTracker.Blazor
```

## Configuration

- Connection string: `ConnectionStrings:Default` in `src/PortfolioTracker.Api/appsettings.json`.
- Price-provider keys: store them in `dotnet user-secrets` (`TwelveDataApiKey`, `FmpApiKey`, `EodApiKey`, `AlpacaApiKey`, `AlpacaSecretKey`) or in a gitignored `.env` for Docker Compose. Without keys the providers skip live calls and fall back to persisted prices.
- Local dev uses a fixed `UserId`: `12345678-1234-1234-1234-123456789012`.

## Docker

The Compose project is managed by Visual Studio (see `docker-compose.dcproj`); use the VS profile instead of a plain `docker compose up`. Services: API on `8080`, Blazor on `8081`, PostgreSQL on `5432`, mcp-pandas on `8082`.

## Documentation

- `AGENTS.md` — conventions and gotchas for AI agents working in this repo.
- `notes/memory.md` — session log with decisions and pending work.
- `notes/audit-2026-09-10.md` — full code audit (diagnosis and backlog).
