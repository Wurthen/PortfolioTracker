using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class SymbolPriceService
{
    private readonly IDbContextFactory<PortfolioDbContext> _contextFactory;

    public SymbolPriceService(IDbContextFactory<PortfolioDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // Short-lived contexts per operation: these calls run concurrently from
    // parallel price fetches, so a shared scoped DbContext would explode.
    public async Task<SymbolPrice?> GetLatestPriceAsync(string symbol)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.SymbolPrices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Symbol == symbol.ToUpperInvariant());
    }

    public async Task SavePriceAsync(string symbol, decimal price, string provider, string currency = "USD")
    {
        var normalizedSymbol = symbol.ToUpperInvariant();
        await using var db = await _contextFactory.CreateDbContextAsync();
        var existing = await db.SymbolPrices
            .FirstOrDefaultAsync(s => s.Symbol == normalizedSymbol);

        if (existing != null)
        {
            existing.Price = price;
            existing.Provider = provider;
            existing.Currency = currency;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.SymbolPrices.Add(new SymbolPrice
            {
                Symbol = normalizedSymbol,
                Price = price,
                Provider = provider,
                Currency = currency,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
