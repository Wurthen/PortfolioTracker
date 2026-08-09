namespace PortfolioTracker.Api.Models;

public class SymbolPrice
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string Provider { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
