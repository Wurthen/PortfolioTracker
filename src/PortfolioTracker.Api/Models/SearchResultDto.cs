namespace PortfolioTracker.Api.Models;

public class SearchResultDto
{
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Exchange { get; set; }
}
