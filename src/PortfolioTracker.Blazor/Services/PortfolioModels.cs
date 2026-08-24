namespace PortfolioTracker.Blazor.Services;

public class PortfolioItemDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Shares { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal Commission { get; set; }
    public string? AlternativeSymbol { get; set; }
    public bool UseAlternativeSymbol { get; set; }
    public bool PriceAvailable { get; set; } = true;
    public decimal CurrentPriceUsd { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal GainLoss { get; set; }
    public decimal GainLossPercent { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SearchResultDto
{
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Exchange { get; set; }
}

public class CreatePortfolioItemRequest
{
    public Guid UserId { get; set; }
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public decimal Shares { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal Commission { get; set; }
    public string? AlternativeSymbol { get; set; }
    public bool UseAlternativeSymbol { get; set; }
}

public class UpdatePortfolioItemRequest
{
    public decimal Shares { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal Commission { get; set; }
    public string? Name { get; set; }
    public string? AlternativeSymbol { get; set; }
    public bool UseAlternativeSymbol { get; set; }
}

public class PortfolioPerformanceDto
{
    public decimal Daily { get; set; }
    public decimal Weekly { get; set; }
    public decimal Monthly { get; set; }
    public decimal Ytd { get; set; }
    public decimal Yearly { get; set; }
}

public class PortfolioDashboardDto
{
    public List<PortfolioItemDto> Items { get; set; } = [];
    public PortfolioPerformanceDto Performance { get; set; } = new();
}

public class ApiErrorResponse
{
    public string? Error { get; set; }
}

public class PortfolioHistoryResponseDto
{
    public List<HistoryPointDto> Total { get; set; } = [];
    public Dictionary<string, List<HistoryPointDto>> Items { get; set; } = [];
}

public class HistoryPointDto
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
}

public class BackfillResultDto
{
    public List<BackfillItemResultDto> Items { get; set; } = [];
    public int TotalPointsInserted { get; set; }
}

public class BackfillItemResultDto
{
    public string Symbol { get; set; } = "";
    public string Source { get; set; } = "";
    public int PointsInserted { get; set; }
    public string Status { get; set; } = "";
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

public class TransferRequest
{
    public Guid FromItemId { get; set; }
    public Guid ToItemId { get; set; }
    public decimal AmountEur { get; set; }
    public DateTime? Date { get; set; }
}

public class PeriodPerformanceDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal SimplePct { get; set; }
    public decimal? MwrPct { get; set; }
}

public class DetailedPerformanceDto
{
    public List<PeriodPerformanceDto> Periods { get; set; } = [];
    public decimal? XirrSinceInceptionPct { get; set; }
}
