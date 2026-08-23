namespace PortfolioTracker.Api.Models;

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
