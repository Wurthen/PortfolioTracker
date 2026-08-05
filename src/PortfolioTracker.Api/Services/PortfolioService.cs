using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class PortfolioService
{
    private readonly PortfolioDbContext _context;
    private readonly YahooFinanceService _yahooFinanceService;
    private readonly CurrencyService _currencyService;

    public PortfolioService(PortfolioDbContext context, YahooFinanceService yahooFinanceService, CurrencyService currencyService)
    {
        _context = context;
        _yahooFinanceService = yahooFinanceService;
        _currencyService = currencyService;
    }

    public async Task<List<PortfolioItemDto>> GetPortfolioAsync(Guid userId)
    {
        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Symbol)
            .ToListAsync();

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var result = new List<PortfolioItemDto>();

        foreach (var item in items)
        {
            var currentPriceUsd = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol) ?? 0;
            var currentPriceEur = currentPriceUsd * usdToEur;
            var currentValue = item.Shares * currentPriceEur;
            var costBasis = (item.Shares * item.PurchasePrice) + item.Commission;
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
                Commission = item.Commission,
                CurrentPriceUsd = currentPriceUsd,
                CurrentPrice = currentPriceEur,
                CurrentValue = currentValue,
                GainLoss = gainLoss,
                GainLossPercent = gainLossPercent,
                Currency = "EUR",
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

        PortfolioItem item;
        
        if (existingItem != null)
        {
            var totalShares = existingItem.Shares + request.Shares;
            var totalCost = (existingItem.Shares * existingItem.PurchasePrice) + (request.Shares * request.PurchasePrice);
            existingItem.PurchasePrice = totalCost / totalShares;
            existingItem.Shares = totalShares;
            existingItem.Commission += request.Commission;
            existingItem.UpdatedAt = DateTime.UtcNow;
            
            if (request.PurchaseDate.HasValue)
            {
                existingItem.PurchaseDate = request.PurchaseDate;
            }
            
            await _context.SaveChangesAsync();
            item = existingItem;
        }
        else
        {
            item = new PortfolioItem
            {
                UserId = request.UserId,
                Symbol = request.Symbol.ToUpperInvariant(),
                Name = request.Name,
                Type = request.Type,
                Shares = request.Shares,
                PurchasePrice = request.PurchasePrice,
                PurchaseDate = request.PurchaseDate.HasValue
                    ? DateTime.SpecifyKind(request.PurchaseDate.Value, DateTimeKind.Utc)
                    : null,
                Commission = request.Commission
            };

            _context.PortfolioItems.Add(item);
            await _context.SaveChangesAsync();
        }

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var currentPriceUsd = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol) ?? 0;
        var currentPriceEur = currentPriceUsd * usdToEur;
        var currentValue = item.Shares * currentPriceEur;
        var costBasis = (item.Shares * item.PurchasePrice) + item.Commission;
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
            Commission = item.Commission,
            CurrentPriceUsd = currentPriceUsd,
            CurrentPrice = currentPriceEur,
            CurrentValue = currentValue,
            GainLoss = gainLoss,
            GainLossPercent = gainLossPercent,
            Currency = "EUR",
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
        item.PurchaseDate = request.PurchaseDate.HasValue
            ? DateTime.SpecifyKind(request.PurchaseDate.Value, DateTimeKind.Utc)
            : null;
        item.Commission = request.Commission;
        item.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var currentPriceUsd = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol) ?? 0;
        var currentPriceEur = currentPriceUsd * usdToEur;
        var currentValue = item.Shares * currentPriceEur;
        var costBasis = (item.Shares * item.PurchasePrice) + item.Commission;
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
            Commission = item.Commission,
            CurrentPriceUsd = currentPriceUsd,
            CurrentPrice = currentPriceEur,
            CurrentValue = currentValue,
            GainLoss = gainLoss,
            GainLossPercent = gainLossPercent,
            Currency = "EUR",
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
