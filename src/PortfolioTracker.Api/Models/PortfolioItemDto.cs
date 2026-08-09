namespace PortfolioTracker.Api.Models;

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
    public decimal CurrentPriceUsd { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal GainLoss { get; set; }
    public decimal GainLossPercent { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
