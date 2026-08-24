using PortfolioTracker.Api.Services;
using Xunit;

namespace PortfolioTracker.Tests;

public class PerformanceCalculatorsTests
{
    [Fact]
    public void ModifiedDietz_WithoutFlows_EqualsSimpleReturn()
    {
        var result = PerformanceCalculators.ModifiedDietz(
            beginValue: 1000m,
            endValue: 1100m,
            externalFlows: [],
            periodStart: new DateTime(2026, 1, 1),
            periodEnd: new DateTime(2026, 2, 1));

        Assert.Equal(10m, result);
    }

    [Fact]
    public void ModifiedDietz_MidPeriodBuy_ReducesReturn()
    {
        // Start 1000, buy +1000 halfway, end 2050. Simple would say (2050-2000)/1000 = 5%.
        var start = new DateTime(2026, 1, 1);
        var end = new DateTime(2026, 1, 31);
        var flows = new List<(DateTime, decimal)> { (start.AddDays(15), 1000m) };

        var result = PerformanceCalculators.ModifiedDietz(1000m, 2050m, flows, start, end);

        // Weight of flow ~ (30-14)/30 => denom = 1000 + 1000*(16/30) ≈ 1533; R = 50/1533 ≈ 3.26%
        Assert.InRange(result, 3.0m, 3.5m);
    }

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
