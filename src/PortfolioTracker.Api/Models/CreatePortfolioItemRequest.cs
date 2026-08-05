namespace PortfolioTracker.Api.Models;

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
}
