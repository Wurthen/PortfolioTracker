using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class SymbolPriceService
{
    private readonly PortfolioDbContext _context;

    public SymbolPriceService(PortfolioDbContext context)
    {
        _context = context;
    }

    public async Task<SymbolPrice?> GetLatestPriceAsync(string symbol)
    {
        return await _context.SymbolPrices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Symbol == symbol.ToUpperInvariant());
    }

    public async Task SavePriceAsync(string symbol, decimal price, string provider, string currency = "USD")
    {
        var normalizedSymbol = symbol.ToUpperInvariant();
        var existing = await _context.SymbolPrices
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
            _context.SymbolPrices.Add(new SymbolPrice
            {
                Symbol = normalizedSymbol,
                Price = price,
                Provider = provider,
                Currency = currency,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }
}
