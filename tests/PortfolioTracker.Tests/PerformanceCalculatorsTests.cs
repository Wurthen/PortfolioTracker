using PortfolioTracker.Api.Services;
using Xunit;

namespace PortfolioTracker.Tests;

public class PerformanceCalculatorsTests
{
    [Fact]
    public void Xirr_OneYearTenPercent()
    {
        var t0 = new DateTime(2025, 1, 1);
        var cashflows = new List<(DateTime, decimal)>
        {
            (t0, -1000m),
            (t0.AddYears(1), 1100m)
        };

        var xirr = PerformanceCalculators.Xirr(cashflows);

        Assert.NotNull(xirr);
        Assert.InRange(xirr.Value, 9.5m, 10.5m);
    }

    [Fact]
    public void Xirr_NoSignChange_ReturnsNull()
    {
        var cashflows = new List<(DateTime, decimal)>
        {
            (new DateTime(2025, 1, 1), -100m),
            (new DateTime(2025, 6, 1), -50m)
        };

        Assert.Null(PerformanceCalculators.Xirr(cashflows));
    }
}
