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
    public decimal CurrentPrice { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal GainLoss { get; set; }
    public decimal GainLossPercent { get; set; }
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
}

public class UpdatePortfolioItemRequest
{
    public decimal Shares { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateTime? PurchaseDate { get; set; }
}
