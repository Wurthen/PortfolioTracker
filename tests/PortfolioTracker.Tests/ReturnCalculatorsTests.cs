using PortfolioTracker.Api.Models;
using PortfolioTracker.Api.Services;
using Xunit;

namespace PortfolioTracker.Tests;

public class ReturnCalculatorsTests
{
    [Fact]
    public void ComputeTwrSeries_WithoutFlows_EqualsSimpleReturn()
    {
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1100m, ItemId = Guid.Empty }
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, []);

        Assert.Equal(2, result.Count);
        Assert.Equal(0m, result[0].Value);
        Assert.Equal(10m, result[1].Value);
    }

    [Fact]
    public void ComputeTwrSeries_WithContribution_IgnoresContributionEffect()
    {
        // Day 1: 1000
        // Day 2: +10% -> 1100
        // Day 3: contribute 1000 -> 2100
        // Day 4: market -5% -> 1995
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1100m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 2100m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 4), ValueEur = 1995m, ItemId = Guid.Empty }
        };
        var flows = new Dictionary<DateTime, decimal>
        {
            [new DateTime(2026, 1, 3)] = 1000m
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, flows);

        // TWR = (1100/1000) * (1995/2100) - 1 = 1.1 * 0.95 - 1 = 4.5%
        Assert.Equal(4.5m, result[^1].Value);
    }

    [Fact]
    public void ComputeTwrSeries_FlowOnDateWithoutHistoryPoint_IsAppliedToNextPoint()
    {
        // Buy on Jan 2 (a gap: no history point that day). The +100 flow must offset
        // the value jump from 1000 to 1100, so the TWR stays flat instead of +10%.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 1100m, ItemId = Guid.Empty }
        };
        var flows = new Dictionary<DateTime, decimal>
        {
            [new DateTime(2026, 1, 2)] = 100m
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, flows);

        Assert.Equal(0m, result[^1].Value);
    }

    [Fact]
    public void SumFlowsBetween_SumsOnlyFlowsInsideWindow()
    {
        var flows = new List<KeyValuePair<DateTime, decimal>>
        {
            new(new DateTime(2026, 1, 2), 100m),
            new(new DateTime(2026, 1, 3), 50m),
            new(new DateTime(2026, 1, 6), 25m)
        };

        // Lower bound is exclusive (flow on the baseline date is already in the baseline value).
        Assert.Equal(150m, ReturnCalculators.SumFlowsBetween(flows, new DateTime(2026, 1, 1), new DateTime(2026, 1, 3)));
        Assert.Equal(50m, ReturnCalculators.SumFlowsBetween(flows, new DateTime(2026, 1, 2), new DateTime(2026, 1, 5)));
        Assert.Equal(25m, ReturnCalculators.SumFlowsBetween(flows, new DateTime(2026, 1, 3), new DateTime(2026, 1, 6)));
    }

    [Fact]
    public void ComputeTwrSeries_WithSinglePoint_ReturnsZero()
    {
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty }
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, []);

        Assert.Single(result);
        Assert.Equal(0m, result[0].Value);
    }

    [Fact]
    public void ComputeItemReturnSeries_WithoutBuys_ReturnsZero()
    {
        var itemId = Guid.NewGuid();
        var points = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = itemId }
        };
        var transactions = new List<PortfolioTransaction>();

        var result = ReturnCalculators.ComputeItemReturnSeries(points, transactions);

        Assert.Single(result[itemId.ToString()]);
        Assert.Equal(0m, result[itemId.ToString()][0].Value);
    }

    [Fact]
    public void ComputeItemReturnSeries_WithBuy_ComputesReturnOverCostBasis()
    {
        var itemId = Guid.NewGuid();
        var points = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = itemId },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1100m, ItemId = itemId }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new()
            {
                ItemId = itemId,
                Type = "Buy",
                Date = new DateTime(2026, 1, 1),
                AmountEur = 1000m,
                Shares = 10m
            }
        };

        var result = ReturnCalculators.ComputeItemReturnSeries(points, transactions);

        var series = result[itemId.ToString()];
        Assert.Equal(2, series.Count);
        Assert.Equal(0m, series[0].Value);
        Assert.Equal(10m, series[1].Value);
    }

    [Fact]
    public void ComputeItemReturnSeries_SafeBackDoesNotIncreaseCostBasis()
    {
        var itemId = Guid.NewGuid();
        // Value grows from 1000 to 1200, but a SafeBack of 200 happened on day 2.
        // Cost basis should remain 1000 (the original Buy), so return = 20%.
        var points = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = itemId },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 1200m, ItemId = itemId }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new()
            {
                ItemId = itemId,
                Type = "Buy",
                Date = new DateTime(2026, 1, 1),
                AmountEur = 1000m,
                Shares = 10m
            },
            new()
            {
                ItemId = itemId,
                Type = "SafeBack",
                Date = new DateTime(2026, 1, 2),
                AmountEur = 200m,
                Shares = 2m
            }
        };

        var result = ReturnCalculators.ComputeItemReturnSeries(points, transactions);

        var series = result[itemId.ToString()];
        Assert.Equal(20m, series[^1].Value);
    }

    [Fact]
    public void ComputeItemReturnSeries_IncludesCommissionInCostBasis()
    {
        // Buy cost 1000 + 10 commission; value 1100 => 90 / 1010 = 8.91%.
        var itemId = Guid.NewGuid();
        var points = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = itemId },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1100m, ItemId = itemId }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new()
            {
                ItemId = itemId,
                Type = "Buy",
                Date = new DateTime(2026, 1, 1),
                AmountEur = 1000m,
                Commission = 10m,
                Shares = 10m
            }
        };

        var result = ReturnCalculators.ComputeItemReturnSeries(points, transactions);

        Assert.Equal(8.91m, result[itemId.ToString()][^1].Value);
    }

    [Fact]
    public void ComputeItemReturnSeries_TransferOut_ReducesCostBasis()
    {
        // Buy 1000; transfer 500 out to another fund; value 550 => 50 / 500 = 10%.
        var itemId = Guid.NewGuid();
        var points = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = itemId },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 550m, ItemId = itemId }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new() { ItemId = itemId, Type = "Buy", Date = new DateTime(2026, 1, 1), AmountEur = 1000m, Shares = 10m },
            new() { ItemId = itemId, Type = "TransferOut", Date = new DateTime(2026, 1, 2), AmountEur = 500m, Shares = 5m }
        };

        var result = ReturnCalculators.ComputeItemReturnSeries(points, transactions);

        Assert.Equal(10m, result[itemId.ToString()][^1].Value);
    }

    [Fact]
    public void ComputeSimpleReturnSeries_WithBuy_UsesInvestedAsDenominator()
    {
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1100m, ItemId = Guid.Empty }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new() { ItemId = Guid.NewGuid(), Type = "Buy", Date = new DateTime(2026, 1, 1), AmountEur = 1000m, Shares = 10m }
        };

        var result = ReturnCalculators.ComputeSimpleReturnSeries(totals, transactions);

        Assert.Equal(0m, result[0].Value);
        Assert.Equal(10m, result[1].Value);
    }

    [Fact]
    public void ComputeSimpleReturnSeries_IncludesContributionInCostBasis()
    {
        // Day 1: invest 1000. Day 3: invest another 1000 => cost 2000; value 2200 => +10%.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1100m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 2200m, ItemId = Guid.Empty }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new() { ItemId = Guid.NewGuid(), Type = "Buy", Date = new DateTime(2026, 1, 1), AmountEur = 1000m, Shares = 10m },
            new() { ItemId = Guid.NewGuid(), Type = "Buy", Date = new DateTime(2026, 1, 3), AmountEur = 1000m, Shares = 8m }
        };

        var result = ReturnCalculators.ComputeSimpleReturnSeries(totals, transactions);

        Assert.Equal(0m, result[0].Value);   // 1000/1000 - 1
        Assert.Equal(10m, result[1].Value);  // 1100/1000 - 1
        Assert.Equal(10m, result[2].Value);  // 2200/2000 - 1
    }

    [Fact]
    public void ComputeSimpleReturnSeries_SafeBackDoesNotIncreaseCostBasis()
    {
        // Value grows from 1000 to 1200; a SafeBack of 200 (2 shares) on day 2 is return,
        // so the denominator stays at the original 1000 investment => 20%.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 1200m, ItemId = Guid.Empty }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new() { ItemId = Guid.NewGuid(), Type = "Buy", Date = new DateTime(2026, 1, 1), AmountEur = 1000m, Shares = 10m },
            new() { ItemId = Guid.NewGuid(), Type = "SafeBack", Date = new DateTime(2026, 1, 2), AmountEur = 200m, Shares = 2m }
        };

        var result = ReturnCalculators.ComputeSimpleReturnSeries(totals, transactions);

        Assert.Equal(20m, result[^1].Value);
    }
}
