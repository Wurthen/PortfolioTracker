namespace PortfolioTracker.Api.Models;

public class PortfolioDashboardDto
{
    public List<PortfolioItemDto> Items { get; set; } = [];
    public PortfolioPerformanceDto Performance { get; set; } = new();
}
