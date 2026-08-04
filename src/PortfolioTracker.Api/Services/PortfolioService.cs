using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class PortfolioService
{
    private readonly PortfolioDbContext _context;
    private readonly YahooFinanceService _yahooFinanceService;

    public PortfolioService(PortfolioDbContext context, YahooFinanceService yahooFinanceService)
    {
        _context = context;
        _yahooFinanceService = yahooFinanceService;
    }

    public async Task<List<PortfolioItemDto>> GetPortfolioAsync(Guid userId)
    {
        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Symbol)
            .ToListAsync();

        var result = new List<PortfolioItemDto>();

        foreach (var item in items)
        {
            var currentPrice = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol) ?? 0;
            var currentValue = item.Shares * currentPrice;
            var costBasis = item.Shares * item.PurchasePrice;
            var gainLoss = currentValue - costBasis;
            var gainLossPercent = costBasis > 0 ? (gainLoss / costBasis) * 100 : 0;

            result.Add(new PortfolioItemDto
            {
                Id = item.Id,
                Symbol = item.Symbol,
                Name = item.Name,
                Type = item.Type,
                Shares = item.Shares,
                PurchasePrice = item.PurchasePrice,
                PurchaseDate = item.PurchaseDate,
                CurrentPrice = currentPrice,
                CurrentValue = currentValue,
                GainLoss = gainLoss,
                GainLossPercent = gainLossPercent,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            });
        }

        return result;
    }

    public async Task<PortfolioItemDto?> AddItemAsync(CreatePortfolioItemRequest request)
    {
        var existingItem = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.Symbol == request.Symbol);

        if (existingItem != null)
            return null;

        var item = new PortfolioItem
        {
            UserId = request.UserId,
            Symbol = request.Symbol.ToUpperInvariant(),
            Name = request.Name,
            Type = request.Type,
            Shares = request.Shares,
            PurchasePrice = request.PurchasePrice,
            PurchaseDate = request.PurchaseDate
        };

        _context.PortfolioItems.Add(item);
        await _context.SaveChangesAsync();

        var currentPrice = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol) ?? 0;
        var currentValue = item.Shares * currentPrice;
        var costBasis = item.Shares * item.PurchasePrice;
        var gainLoss = currentValue - costBasis;
        var gainLossPercent = costBasis > 0 ? (gainLoss / costBasis) * 100 : 0;

        return new PortfolioItemDto
        {
            Id = item.Id,
            Symbol = item.Symbol,
            Name = item.Name,
            Type = item.Type,
            Shares = item.Shares,
            PurchasePrice = item.PurchasePrice,
            PurchaseDate = item.PurchaseDate,
            CurrentPrice = currentPrice,
            CurrentValue = currentValue,
            GainLoss = gainLoss,
            GainLossPercent = gainLossPercent,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    public async Task<PortfolioItemDto?> UpdateItemAsync(Guid id, Guid userId, UpdatePortfolioItemRequest request)
    {
        var item = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return null;

        item.Shares = request.Shares;
        item.PurchasePrice = request.PurchasePrice;
        item.PurchaseDate = request.PurchaseDate;
        item.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var currentPrice = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol) ?? 0;
        var currentValue = item.Shares * currentPrice;
        var costBasis = item.Shares * item.PurchasePrice;
        var gainLoss = currentValue - costBasis;
        var gainLossPercent = costBasis > 0 ? (gainLoss / costBasis) * 100 : 0;

        return new PortfolioItemDto
        {
            Id = item.Id,
            Symbol = item.Symbol,
            Name = item.Name,
            Type = item.Type,
            Shares = item.Shares,
            PurchasePrice = item.PurchasePrice,
            PurchaseDate = item.PurchaseDate,
            CurrentPrice = currentPrice,
            CurrentValue = currentValue,
            GainLoss = gainLoss,
            GainLossPercent = gainLossPercent,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    public async Task<bool> DeleteItemAsync(Guid id, Guid userId)
    {
        var item = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return false;

        _context.PortfolioItems.Remove(item);
        await _context.SaveChangesAsync();

        return true;
    }
}
