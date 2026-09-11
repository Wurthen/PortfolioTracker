namespace PortfolioTracker.Api.Models;

public class PortfolioPerformanceDto
{
    // Simple (non-time-weighted) return since inception over the cost basis of
    // currently priced positions. Single source of truth for both the dashboard
    // "Ganancia / Pérdida" KPI and the "Desde inicio" period card.
    public decimal SinceInceptionCostBasis { get; set; }
    public decimal SinceInceptionGainLoss { get; set; }
    public decimal SinceInceptionGainLossPercent { get; set; }
}
