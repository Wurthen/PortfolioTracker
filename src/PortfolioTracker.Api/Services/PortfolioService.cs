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

            // The alternative symbol is EOD-specific; the composite chain must be
            // retried with the item's primary symbol, not with the same alternative.
            _logger.LogWarning("EOD failed for alternative symbol {Symbol}, falling back to composite with primary symbol {PrimarySymbol}",
                item.AlternativeSymbol, item.Symbol);
            return await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol);
        }

        return await _yahooFinanceService.GetCurrentPriceAsync(symbol);
    }

    public static decimal ComputeWeightedAveragePrice(decimal existingShares, decimal existingAvgPrice, decimal addedShares, decimal addedCostEur)
        => existingShares + addedShares > 0
            ? ((existingShares * existingAvgPrice) + addedCostEur) / (existingShares + addedShares)
            : 0;

    /// <summary>
    /// Dilutes the average purchase price when SafeBack shares are added at zero cost,
    /// so Shares * PurchasePrice (the position's cost basis) stays unchanged.
    /// </summary>
    public static decimal ComputeSafeBackPrice(decimal existingShares, decimal existingAvgPrice, decimal safeBackShares)
        => ComputeWeightedAveragePrice(existingShares, existingAvgPrice, safeBackShares, 0m);

    public static PortfolioItemDto MapToDto(PortfolioItem item, decimal? currentPriceUsd, decimal usdToEur)
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
            SafeBackAmount = item.SafeBackAmount,
            SafeBackShares = item.SafeBackShares,
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

    /// <summary>
    /// Simple (non-time-weighted) since-inception return over the cost basis of
    /// currently priced positions. Unavailable prices are excluded from both the
    /// numerator and the denominator so they never dilute or fake the return.
    /// </summary>
    public static (decimal CostBasis, decimal GainLoss, decimal GainLossPercent) ComputeSinceInception(IEnumerable<PortfolioItemDto> items)
    {
        var priced = items.Where(i => i.PriceAvailable).ToList();
        var costBasis = priced.Sum(i => (i.Shares * i.PurchasePrice) + i.Commission);
        var currentValue = priced.Sum(i => i.CurrentValue);
        var gainLoss = currentValue - costBasis;
        var gainLossPercent = costBasis > 0 ? gainLoss / costBasis * 100m : 0m;
        return (costBasis, gainLoss, gainLossPercent);
    }

    public async Task<List<PortfolioItemDto>> GetPortfolioAsync(Guid userId)
    {
        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Symbol)
            .ToListAsync();

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();

        // Prices fetched in parallel: sequential calls burn provider quotas fast.
        var dtos = await Task.WhenAll(items.Select(async item =>
            MapToDto(item, await GetCurrentPriceUsdAsync(item), usdToEur)));

        return dtos.ToList();
    }

    internal static string NormalizeSymbol(string symbol)
        // Uppercase and strip the exchange suffix so the same fund always maps to one row.
        => SymbolClassifier.StripExchangeSuffix(symbol.ToUpperInvariant());

    public async Task<PortfolioItemDto?> AddItemAsync(CreatePortfolioItemRequest request)
    {
        var normalizedSymbol = NormalizeSymbol(request.Symbol);
        var existingItem = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.Symbol == normalizedSymbol);

        PortfolioItem item;

        if (existingItem != null)
        {
            var totalShares = existingItem.Shares + request.Shares;
            existingItem.PurchasePrice = ComputeWeightedAveragePrice(existingItem.Shares, existingItem.PurchasePrice, request.Shares, request.Shares * request.PurchasePrice);
            existingItem.Shares = totalShares;
            existingItem.Commission += request.Commission;
            existingItem.UpdatedAt = DateTime.UtcNow;

            if (request.PurchaseDate.HasValue)
            {
                existingItem.PurchaseDate = DateTime.SpecifyKind(request.PurchaseDate.Value, DateTimeKind.Utc);
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

    public async Task<PortfolioDashboardDto> GetDashboardAsync(Guid userId)
    {
        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Symbol)
            .ToListAsync();

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var result = new PortfolioDashboardDto();

        var prices = await Task.WhenAll(items.Select(GetCurrentPriceUsdAsync));

        for (var i = 0; i < items.Count; i++)
        {
            result.Items.Add(MapToDto(items[i], prices[i], usdToEur));
        }

        result.Items = result.Items.OrderByDescending(i => i.CurrentValue).ToList();

        await SaveDailySnapshotAsync(userId, result.Items);

        var sinceInception = ComputeSinceInception(result.Items);
        result.Performance.SinceInceptionCostBasis = sinceInception.CostBasis;
        result.Performance.SinceInceptionGainLoss = sinceInception.GainLoss;
        result.Performance.SinceInceptionGainLossPercent = sinceInception.GainLossPercent;

        return result;
    }

    // Upserts today's snapshot on every dashboard load so the current day always
    // reflects live prices (a backfill may have pre-created forward-filled rows).
    private async Task SaveDailySnapshotAsync(Guid userId, List<PortfolioItemDto> items)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var pricedItems = items.Where(i => i.PriceAvailable).ToList();
            if (pricedItems.Count == 0)
                return;

            var todaysRows = await _context.PortfolioHistoryPoints
                .Where(p => p.UserId == userId && p.Date == today)
                .ToListAsync();

            void Upsert(Guid itemId, decimal value)
            {
                var row = todaysRows.FirstOrDefault(p => p.ItemId == itemId);
                if (row != null)
                {
                    row.ValueEur = decimal.Round(value, 2);
                    return;
                }
                var np = new PortfolioHistoryPoint { UserId = userId, ItemId = itemId, Date = today, ValueEur = decimal.Round(value, 2) };
                todaysRows.Add(np);
                _context.PortfolioHistoryPoints.Add(np);
            }

            foreach (var item in pricedItems)
            {
                Upsert(item.Id, item.CurrentValue);
            }
            Upsert(Guid.Empty, pricedItems.Sum(i => i.CurrentValue));

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

        var transactions = await _context.PortfolioTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
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

        var totals = points.Where(p => p.ItemId == Guid.Empty).OrderBy(p => p.Date).ToList();
        var flowsByDate = transactions
            .Where(t => t.Type is "Buy" or "Sell")
            .GroupBy(t => t.Date.Date)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(t => t.Type == "Buy" ? t.AmountEur + t.Commission : -t.AmountEur));

        response.TotalReturn = ReturnCalculators.ComputeTwrSeries(totals, flowsByDate);
        response.TotalSimpleReturn = ReturnCalculators.ComputeSimpleReturnSeries(totals, transactions);
        response.ItemReturns = ReturnCalculators.ComputeItemReturnSeries(points, transactions);

        return response;
    }
}
