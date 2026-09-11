using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class CreateTransactionRequest
{
    public Guid ItemId { get; set; }
    // Buy | Sell
    [Required]
    public string Type { get; set; } = "Buy";
    public DateTime? Date { get; set; }
    public decimal Shares { get; set; }
    public decimal AmountEur { get; set; }
    public decimal Commission { get; set; }
}

public class CreateSafeBackRequest
{
    public Guid ItemId { get; set; }
    public DateTime? Date { get; set; }
    public decimal AmountEur { get; set; }
}

public class TransferRequest
{
    public Guid FromItemId { get; set; }
    public Guid ToItemId { get; set; }
    public decimal AmountEur { get; set; }
    public DateTime? Date { get; set; }
}

public class TransactionDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string Symbol { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal Shares { get; set; }
    public decimal AmountEur { get; set; }
    public decimal Commission { get; set; }
}

public class PeriodPerformanceDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public class DetailedPerformanceDto
{
    public List<PeriodPerformanceDto> Periods { get; set; } = [];
    public decimal? XirrSinceInceptionPct { get; set; }
}

public class TransactionService
{
    private readonly PortfolioDbContext _context;
    private readonly YahooFinanceService _yahooFinanceService;
    private readonly CurrencyService _currencyService;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(PortfolioDbContext context, YahooFinanceService yahooFinanceService, CurrencyService currencyService, ILogger<TransactionService> logger)
    {
        _context = context;
        _yahooFinanceService = yahooFinanceService;
        _currencyService = currencyService;
        _logger = logger;
    }

    // Creates one synthetic Buy per existing position when no transaction history exists,
    // so MWR/XIRR and history replay work without re-importing historical FIFO lots.
    public async Task EnsureSeededAsync(Guid userId)
    {
        var hasAny = await _context.PortfolioTransactions.AnyAsync(t => t.UserId == userId);
        if (hasAny)
            return;

        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var seeds = items.Select(item => new PortfolioTransaction
        {
            UserId = userId,
            ItemId = item.Id,
            Type = "Buy",
            Date = DateTime.SpecifyKind(item.PurchaseDate?.Date ?? item.CreatedAt.Date, DateTimeKind.Utc),
            Shares = item.Shares,
            AmountEur = decimal.Round(item.Shares * item.PurchasePrice, 2),
            Commission = item.Commission
        }).ToList();

        if (seeds.Count == 0)
            return;

        _context.PortfolioTransactions.AddRange(seeds);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Seeded {Count} synthetic Buy transactions for user {UserId}", seeds.Count, userId);
    }

    public async Task<List<TransactionDto>> ListAsync(Guid userId)
    {
        await EnsureSeededAsync(userId);
        return await Project(
            _context.PortfolioTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId))
            .ToListAsync();
    }

    public async Task<List<TransactionDto>> ListForItemAsync(Guid userId, Guid itemId)
    {
        await EnsureSeededAsync(userId);
        return await Project(
            _context.PortfolioTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.ItemId == itemId))
            .ToListAsync();
    }

    public async Task<TransactionDto?> AddAsync(Guid userId, CreateTransactionRequest request)
    {
        if (request.Type is not ("Buy" or "Sell"))
            return null;
        if (request.Shares <= 0 || request.AmountEur < 0 || request.Commission < 0)
            return null;

        var item = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.Id == request.ItemId && p.UserId == userId);
        if (item == null)
            return null;

        if (request.Type == "Sell" && request.Shares > item.Shares)
            return null;

        var date = DateTime.SpecifyKind((request.Date ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var tx = new PortfolioTransaction
        {
            UserId = userId,
            ItemId = item.Id,
            Type = request.Type,
            Date = date,
            Shares = request.Shares,
            AmountEur = decimal.Round(request.AmountEur, 2),
            Commission = request.Commission
        };

        if (request.Type == "Buy")
        {
            var totalShares = item.Shares + request.Shares;
            var totalCost = item.Shares * item.PurchasePrice + request.AmountEur;
            item.PurchasePrice = totalShares > 0 ? totalCost / totalShares : 0;
            item.Shares = totalShares;
            item.Commission += request.Commission;
            item.PurchaseDate ??= date;
        }
        else
        {
            item.Shares -= request.Shares;
        }

        item.UpdatedAt = DateTime.UtcNow;
        _context.PortfolioTransactions.Add(tx);
        await _context.SaveChangesAsync();

        var result = await Project(_context.PortfolioTransactions.Where(t => t.Id == tx.Id)).ToListAsync();
        return result.FirstOrDefault();
    }

    public async Task<TransactionDto?> AddSafeBackAsync(Guid userId, CreateSafeBackRequest request)
    {
        if (request.AmountEur <= 0)
            return null;

        var item = await _context.PortfolioItems
            .FirstOrDefaultAsync(p => p.Id == request.ItemId && p.UserId == userId);
        if (item == null)
            return null;

        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var useAlternative = item.UseAlternativeSymbol && !string.IsNullOrWhiteSpace(item.AlternativeSymbol);
        var currentPriceUsd = await _yahooFinanceService.GetCurrentPriceAsync(useAlternative ? item.AlternativeSymbol! : item.Symbol);
        if (!currentPriceUsd.HasValue && useAlternative)
        {
            // EOD-specific alternative symbol failed: retry the primary symbol path.
            currentPriceUsd = await _yahooFinanceService.GetCurrentPriceAsync(item.Symbol);
        }
        if (!currentPriceUsd.HasValue || currentPriceUsd.Value <= 0 || usdToEur <= 0)
            return null;

        var currentPriceEur = currentPriceUsd.Value * usdToEur;
        var shares = request.AmountEur / currentPriceEur;
        if (shares <= 0)
            return null;

        var date = DateTime.SpecifyKind((request.Date ?? DateTime.UtcNow).Date, DateTimeKind.Utc);

        var tx = new PortfolioTransaction
        {
            UserId = userId,
            ItemId = item.Id,
            Type = "SafeBack",
            Date = date,
            Shares = decimal.Round(shares, 8, MidpointRounding.AwayFromZero),
            AmountEur = decimal.Round(request.AmountEur, 2),
            Commission = 0
        };

        // SafeBack is cash received that is reinvested at market price. It increases shares
        // without increasing cost basis, so it is treated as return rather than a purchase.
        // Diluting the average price keeps Shares * PurchasePrice (cost basis) unchanged.
        item.PurchasePrice = PortfolioService.ComputeSafeBackPrice(item.Shares, item.PurchasePrice, tx.Shares);
        item.Shares += tx.Shares;
        item.SafeBackAmount += tx.AmountEur;
        item.SafeBackShares += tx.Shares;
        item.UpdatedAt = DateTime.UtcNow;

        _context.PortfolioTransactions.Add(tx);
        await _context.SaveChangesAsync();

        var dtoResult = await Project(_context.PortfolioTransactions.Where(t => t.Id == tx.Id)).ToListAsync();
        return dtoResult.FirstOrDefault();
    }

    public async Task<(bool Ok, string Error)> TransferAsync(Guid userId, TransferRequest request)
    {
        if (request.FromItemId == request.ToItemId)
            return (false, "Origen y destino no pueden ser el mismo fondo.");
        if (request.AmountEur <= 0)
            return (false, "El importe debe ser mayor que cero.");

        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId && (p.Id == request.FromItemId || p.Id == request.ToItemId))
            .ToListAsync();

        var from = items.FirstOrDefault(p => p.Id == request.FromItemId);
        var to = items.FirstOrDefault(p => p.Id == request.ToItemId);
        if (from == null || to == null)
            return (false, "Fondo no encontrado.");

        // Approximation v1: shares moved estimated with the source fund's average price.
        var sharesMoved = Math.Round(request.AmountEur / from.PurchasePrice, 8, MidpointRounding.ToZero);
        if (sharesMoved <= 0 || from.Shares - sharesMoved < -0.00000001m)
            return (false, "Participaciones insuficientes en el fondo de origen.");

        var date = DateTime.SpecifyKind((request.Date ?? DateTime.UtcNow).Date, DateTimeKind.Utc);

        var txOut = new PortfolioTransaction
        {
            UserId = userId,
            ItemId = from.Id,
            Type = "TransferOut",
            Date = date,
            Shares = sharesMoved,
            AmountEur = decimal.Round(request.AmountEur, 2)
        };
        var txIn = new PortfolioTransaction
        {
            UserId = userId,
            ItemId = to.Id,
            Type = "TransferIn",
            Date = date,
            Shares = sharesMoved,
            AmountEur = decimal.Round(request.AmountEur, 2)
        };
        txOut.LinkedTransactionId = txIn.Id;
        txIn.LinkedTransactionId = txOut.Id;

        // Source: proportional cost basis leaves; weighted average unchanged.
        from.Shares -= sharesMoved;

        // Destination: incoming money joins the weighted-average cost.
        var totalShares = to.Shares + sharesMoved;
        var totalCost = to.Shares * to.PurchasePrice + request.AmountEur;
        to.PurchasePrice = totalShares > 0 ? totalCost / totalShares : 0;
        to.Shares = totalShares;

        from.UpdatedAt = DateTime.UtcNow;
        to.UpdatedAt = DateTime.UtcNow;

        _context.PortfolioTransactions.AddRange(txOut, txIn);
        await _context.SaveChangesAsync();
        return (true, "");
    }

    /// <summary>External cash flows: Buy positive (money in), Sell negative (money out); transfers excluded.</summary>
    private async Task<List<(DateTime Date, decimal Flow)>> GetExternalFlowsAsync(Guid userId, List<PortfolioItem> items)
    {
        var itemIds = items.Select(i => i.Id).ToHashSet();
        var txs = await _context.PortfolioTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync();

        return txs
            .Where(t => itemIds.Contains(t.ItemId) && t.Type is "Buy" or "Sell")
            .Select(t => (
                Date: t.Date,
                Flow: t.Type switch
                {
                    "Buy" => t.AmountEur + t.Commission,
                    "Sell" => -t.AmountEur,
                    _ => 0m
                }))
            .OrderBy(f => f.Date)
            .ToList();
    }

    public async Task<DetailedPerformanceDto> ComputeDetailedPerformanceAsync(Guid userId)
    {
        await EnsureSeededAsync(userId);

        var dto = new DetailedPerformanceDto();
        var items = await _context.PortfolioItems.AsNoTracking().Where(p => p.UserId == userId).ToListAsync();
        var flows = await GetExternalFlowsAsync(userId, items);

        // Latest stored value per item — used by XIRR and to keep "Desde inicio"
        // exactly consistent with the dashboard's Total Gain/Loss %.
        var latestValues = await _context.PortfolioHistoryPoints
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ItemId != Guid.Empty)
            .GroupBy(p => p.ItemId)
            .Select(g => g.OrderByDescending(p => p.Date).First())
            .ToListAsync();

        var totals = await _context.PortfolioHistoryPoints
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ItemId == Guid.Empty)
            .OrderBy(p => p.Date)
            .ToListAsync();

        if (totals.Count >= 2)
        {
            var last = totals[^1];
            // Keys mirror the chart's windows so the UI can derive each card from the
            // same TWR series it plots.
            var periods = new (string Key, string Label, int Days)[]
            {
                ("1d", "1D", 1), ("7d", "1S", 7), ("1m", "1M", 30),
                ("3m", "3M", 90), ("1y", "1A", 365)
            };

            foreach (var (key, label, days) in periods)
            {
                dto.Periods.Add(BuildPeriod(key, label, totals, last, last.Date.AddDays(-days)));
            }

            var jan1 = new DateTime(last.Date.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var preYearPoint = totals.LastOrDefault(p => p.Date < jan1);
            // No data before Jan 1 => the calendar-YTD window predates the portfolio; measure since inception.
            var ytdBaselineDate = preYearPoint?.Date ?? DateTime.MinValue;
            dto.Periods.Add(BuildPeriod("ytd", preYearPoint != null ? "YTD" : "Desde inicio", totals, last, ytdBaselineDate));

            // When YTD/1A collapse into "Desde inicio" they would be identical rows; keep one per label.
            dto.Periods = dto.Periods
                .GroupBy(p => p.Label)
                .Select(g => g.First())
                .ToList();

            // NOTE: the "Ganancia / Pérdida %" KPI and the "Desde inicio" period card
            // share a single source of truth, PortfolioService.ComputeSinceInception,
            // computed from live prices in the dashboard payload. Keeping no parallel
            // formula here avoids the two numbers drifting apart.
        }

        if (flows.Count > 0)
        {
            var currentValue = latestValues.Sum(p => p.ValueEur);
            var cashflows = flows.Select(f => (f.Date, -f.Flow)).ToList();
            cashflows.Add((DateTime.UtcNow.Date, currentValue));
            dto.XirrSinceInceptionPct = PerformanceCalculators.Xirr(cashflows);
        }

        return dto;
    }

    // The period card only describes the window; its return is computed client-side from
    // the same TWR series the chart plots, so both always show the same metric.
    private static PeriodPerformanceDto BuildPeriod(string key, string label, List<PortfolioHistoryPoint> totals, PortfolioHistoryPoint last, DateTime baselineCutoff)
    {
        var baseline = totals.LastOrDefault(p => p.Date <= baselineCutoff && p.Date < last.Date);
        if (baseline == null)
        {
            // Window starts before the portfolio existed: measure from inception instead.
            baseline = totals.FirstOrDefault(p => p.Date < last.Date);
            if (baseline == null)
                return new PeriodPerformanceDto { Key = key, Label = label };
            return new PeriodPerformanceDto { Key = key, Label = $"Desde inicio ({baseline.Date:dd/MM})" };
        }

        return new PeriodPerformanceDto { Key = key, Label = label };
    }

    private IQueryable<TransactionDto> Project(IQueryable<PortfolioTransaction> query) =>
        from t in query
        join i in _context.PortfolioItems.AsNoTracking() on t.ItemId equals i.Id into itemsJoin
        from item in itemsJoin.DefaultIfEmpty()
        orderby t.Date descending, t.CreatedAt descending
        select new TransactionDto
        {
            Id = t.Id,
            ItemId = t.ItemId,
            Symbol = item != null ? item.Symbol : "?",
            ItemName = item != null ? item.Name : "?",
            Type = t.Type,
            Date = t.Date,
            Shares = t.Shares,
            AmountEur = t.AmountEur,
            Commission = t.Commission
        };
}
