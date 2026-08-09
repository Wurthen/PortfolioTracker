namespace PortfolioTracker.Api.Models;

public class PortfolioPerformanceDto
{
    public decimal Daily { get; set; }
    public decimal Weekly { get; set; }
    public decimal Monthly { get; set; }
    public decimal Ytd { get; set; }
    public decimal Yearly { get; set; }
}
