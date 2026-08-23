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

    private async Task<decimal?> GetCurrentPriceUsdAsync(PortfolioItem item)
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

        return await _yahooFinanceService.GetCurrentPriceAsync(symbol);
    }

    private static PortfolioItemDto MapToDto(PortfolioItem item, decimal? currentPriceUsd, decimal usdToEur)
    {
        var priceAvailable = currentPriceUsd.HasValue;
        var currentPriceEur = (currentPriceUsd ?? 0) * usdToEur;
        var costBasis = (item.Shares * item.PurchasePrice) + item.Commission;
        var currentValue = priceAvailable ? item.Shares * currentPriceEur : 0;
        var gainLoss = priceAvailable ? currentValue - costBasis : 0;
        var gainLossPercent = priceAvailable && costBasis > 0 ? (gainLoss / costBasis) * 100 : 0;

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
            PriceAvailable = priceAvailable,
            CurrentPriceUsd = currentPriceUsd ?? 0,
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
        var normalizedSymbol = request.Symbol.ToUpperInvariant();
        var existingItem = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.Symbol == normalizedSymbol);

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
                Symbol = normalizedSymbol,
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
        if (request.Name != null)
            item.Name = request.Name;
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
            currentValue += item.Shares * (currentPrice ?? 0) * usdToEur;
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

            currentValue += item.Shares * (currentPriceUsd ?? 0) * usdToEur;
        }

        result.Items = result.Items.OrderByDescending(i => i.CurrentValue).ToList();

        result.Performance = new PortfolioPerformanceDto();

        await SaveDailySnapshotAsync(userId, result.Items);

        return result;
    }

    // One snapshot per UTC day: first dashboard load of the day records each
    // priced position plus the portfolio total. Later loads the same day are no-ops.
    private async Task SaveDailySnapshotAsync(Guid userId, List<PortfolioItemDto> items)
    {
        try
        {
            var today = DateTime.UtcNow.Date;

            var alreadySnapshotted = await _context.PortfolioHistoryPoints
                .AnyAsync(p => p.UserId == userId && p.Date == today);

            if (alreadySnapshotted)
                return;

            var pricedItems = items.Where(i => i.PriceAvailable).ToList();
            if (pricedItems.Count == 0)
                return;

            var points = new List<PortfolioHistoryPoint>
            {
                new()
                {
                    UserId = userId,
                    ItemId = Guid.Empty,
                    Date = today,
                    ValueEur = pricedItems.Sum(i => i.CurrentValue)
                }
            };
            points.AddRange(pricedItems.Select(i => new PortfolioHistoryPoint
            {
                UserId = userId,
                ItemId = i.Id,
                Date = today,
                ValueEur = i.CurrentValue
            }));

            _context.PortfolioHistoryPoints.AddRange(points);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Snapshotting must never break the dashboard.
            _logger.LogError(ex, "Failed to save daily portfolio snapshot for user {UserId}", userId);
        }
    }

    public async Task<PortfolioHistoryResponseDto> GetHistoryAsync(Guid userId)
    {
        var points = await _context.PortfolioHistoryPoints
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Date)
            .ToListAsync();

        var response = new PortfolioHistoryResponseDto();

        foreach (var point in points)
        {
            var dto = new HistoryPointDto { Date = point.Date, Value = point.ValueEur };

            if (point.ItemId == Guid.Empty)
                response.Total.Add(dto);
            else
            {
                var key = point.ItemId.ToString();
                if (!response.Items.TryGetValue(key, out var list))
                    response.Items[key] = list = [];
                list.Add(dto);
            }
        }

        return response;
    }
}
