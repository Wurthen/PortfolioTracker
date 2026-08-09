using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class PortfolioService
{
    private readonly PortfolioDbContext _context;
    private readonly YahooFinanceService _yahooFinanceService;
    private readonly EodPriceProvider _eodPriceProvider;
    private readonly CurrencyService _currencyService;
    private readonly ILogger<PortfolioService> _logger;

    public PortfolioService(PortfolioDbContext context, YahooFinanceService yahooFinanceService, EodPriceProvider eodPriceProvider, CurrencyService currencyService, ILogger<PortfolioService> logger)
    {
        _context = context;
        _yahooFinanceService = yahooFinanceService;
        _eodPriceProvider = eodPriceProvider;
        _currencyService = currencyService;
        _logger = logger;
    }

    private static string GetPriceSymbol(PortfolioItem item)
    {
        if (item.UseAlternativeSymbol && !string.IsNullOrWhiteSpace(item.AlternativeSymbol))
        {
            return item.AlternativeSymbol;
        }
        return item.Symbol;
    }

    private async Task<decimal> GetCurrentPriceUsdAsync(PortfolioItem item)
    {
        var symbol = GetPriceSymbol(item);
        _logger.LogInformation("Getting price for {Symbol} using symbol {PriceSymbol} (UseAlternative={UseAlternative})",
            item.Symbol, symbol, item.UseAlternativeSymbol);

        if (item.UseAlternativeSymbol && !string.IsNullOrWhiteSpace(item.AlternativeSymbol))
        {
            var eodPrice = await _eodPriceProvider.GetPriceAsync(item.AlternativeSymbol);
            if (eodPrice.HasValue)
            {
                _logger.LogInformation("Using EOD price for alternative symbol {Symbol}: {Price}", item.AlternativeSymbol, eodPrice.Value);
                return eodPrice.Value;
            }

            _logger.LogWarning("EOD failed for alternative symbol {Symbol}, falling back to composite", item.AlternativeSymbol);
        }

        return await _yahooFinanceService.GetCurrentPriceAsync(symbol) ?? 0;
    }

    private static PortfolioItemDto MapToDto(PortfolioItem item, decimal currentPriceUsd, decimal usdToEur)
    {
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
            AlternativeSymbol = item.AlternativeSymbol,
            UseAlternativeSymbol = item.UseAlternativeSymbol,
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
            var currentPriceUsd = await GetCurrentPriceUsdAsync(item);
            result.Add(MapToDto(item, currentPriceUsd, usdToEur));
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
                Commission = request.Commission,
                AlternativeSymbol = request.AlternativeSymbol,
                UseAlternativeSymbol = request.UseAlternativeSymbol
            };

            _context.PortfolioItems.Add(item);
            await _context.SaveChangesAsync();
        }

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var currentPriceUsd = await GetCurrentPriceUsdAsync(item);
        return MapToDto(item, currentPriceUsd, usdToEur);
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
        item.AlternativeSymbol = request.AlternativeSymbol;
        item.UseAlternativeSymbol = request.UseAlternativeSymbol;
        item.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var currentPriceUsd = await GetCurrentPriceUsdAsync(item);
        return MapToDto(item, currentPriceUsd, usdToEur);
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

    public async Task<PortfolioPerformanceDto> GetPerformanceAsync(Guid userId)
    {
        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var performance = new PortfolioPerformanceDto();

        if (items.Count == 0)
            return performance;

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();

        var currentValue = 0m;

        foreach (var item in items)
        {
            var currentPrice = await GetCurrentPriceUsdAsync(item);
            currentValue += item.Shares * currentPrice * usdToEur;
        }

        // Without reliable historical data providers, performance metrics default to 0.
        // This avoids Yahoo Finance rate limits and keeps the app responsive.
        return performance;
    }

    public async Task<PortfolioDashboardDto> GetDashboardAsync(Guid userId)
    {
        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Symbol)
            .ToListAsync();

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var result = new PortfolioDashboardDto();

        var currentValue = 0m;

        foreach (var item in items)
        {
            var currentPriceUsd = await GetCurrentPriceUsdAsync(item);
            var dto = MapToDto(item, currentPriceUsd, usdToEur);
            result.Items.Add(dto);

            currentValue += item.Shares * currentPriceUsd * usdToEur;
        }

        result.Performance = new PortfolioPerformanceDto();

        return result;
    }
}
