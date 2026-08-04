namespace PortfolioTracker.Api.Models;

public class UpdatePortfolioItemRequest
{
    public decimal Shares { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateTime? PurchaseDate { get; set; }
}
