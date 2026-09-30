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
    public void ComputeItemReturnSeries_SafeBackIncreasesCostBasis()
    {
        var itemId = Guid.NewGuid();
        // Buy 1000 on day 1; SafeBack 200 reinvested on day 2; value 1260 on day 3.
        // Cost basis is 1200, so only the market gain counts: 60 / 1200 = 5%.
        var points = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = itemId },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 1260m, ItemId = itemId }
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
        Assert.Equal(5m, series[^1].Value);
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
    public void ComputeSimpleReturnSeries_SafeBackIncreasesCostBasis()
    {
        // Buy 1000 on day 1; SafeBack 200 on day 2; value 1260 on day 3.
        // Cost basis is 1200, so the injection itself is not return: 60 / 1200 = 5%.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 3), ValueEur = 1260m, ItemId = Guid.Empty }
        };
        var transactions = new List<PortfolioTransaction>
        {
            new() { ItemId = Guid.NewGuid(), Type = "Buy", Date = new DateTime(2026, 1, 1), AmountEur = 1000m, Shares = 10m },
            new() { ItemId = Guid.NewGuid(), Type = "SafeBack", Date = new DateTime(2026, 1, 2), AmountEur = 200m, Shares = 2m }
        };

        var result = ReturnCalculators.ComputeSimpleReturnSeries(totals, transactions);

        Assert.Equal(5m, result[^1].Value);
    }

    [Fact]
    public void ComputeTwrSeries_WithSafeBackFlow_IgnoresInjection()
    {
        // The +200 value jump on day 2 is the SafeBack injection, so TWR stays flat.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1200m, ItemId = Guid.Empty }
        };
        var flows = new Dictionary<DateTime, decimal>
        {
            [new DateTime(2026, 1, 2)] = 200m
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, flows);

        Assert.Equal(0m, result[^1].Value);
    }

    [Fact]
    public void ComputeTwrSeries_NoFlowJumpOverThreshold_IsIgnoredAsDataArtifact()
    {
        // Real case: a fund's history was truncated at PurchaseDate, so the whole position
        // "appeared" one day (+22%) with no flow. The level shift must be absorbed, while
        // genuine moves before and after it keep counting.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 9, 2), ValueEur = 25000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 9, 3), ValueEur = 31000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 9, 4), ValueEur = 31310m, ItemId = Guid.Empty }
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, []);

        Assert.Equal(0m, result[1].Value);   // artifact absorbed
        Assert.Equal(1m, result[2].Value);   // +1% real move still compounds
    }

    [Fact]
    public void ComputeTwrSeries_FlowDay_IsNotClampedByArtifactGuard()
    {
        // The guard only absorbs NO-FLOW jumps: a flow explains part of the change, so
        // the residual move is booked as-is even when it exceeds the threshold.
        var totals = new List<PortfolioHistoryPoint>
        {
            new() { Date = new DateTime(2026, 1, 1), ValueEur = 1000m, ItemId = Guid.Empty },
            new() { Date = new DateTime(2026, 1, 2), ValueEur = 1360m, ItemId = Guid.Empty }
        };
        var flows = new Dictionary<DateTime, decimal>
        {
            [new DateTime(2026, 1, 2)] = 100m
        };

        var result = ReturnCalculators.ComputeTwrSeries(totals, flows);

        Assert.Equal(26m, result[^1].Value);
    }

    [Fact]
    public void BuildExternalFlowsByDate_IncludesSafeBackAndExcludesTransfers()
    {
        var transactions = new List<PortfolioTransaction>
        {
            new() { ItemId = Guid.NewGuid(), Type = "Buy", Date = new DateTime(2026, 1, 1), AmountEur = 1000m, Commission = 10m, Shares = 10m },
            new() { ItemId = Guid.NewGuid(), Type = "SafeBack", Date = new DateTime(2026, 1, 2), AmountEur = 200m, Shares = 2m },
            new() { ItemId = Guid.NewGuid(), Type = "Sell", Date = new DateTime(2026, 1, 2), AmountEur = 500m, Shares = 5m },
            new() { ItemId = Guid.NewGuid(), Type = "TransferIn", Date = new DateTime(2026, 1, 3), AmountEur = 300m, Shares = 3m },
            new() { ItemId = Guid.NewGuid(), Type = "TransferOut", Date = new DateTime(2026, 1, 4), AmountEur = 150m, Shares = 1.5m }
        };

        var flows = ReturnCalculators.BuildExternalFlowsByDate(transactions);

        Assert.Equal(1010m, flows[new DateTime(2026, 1, 1)]);  // Buy + commission
        Assert.Equal(-300m, flows[new DateTime(2026, 1, 2)]);  // SafeBack +200, Sell -500
        Assert.False(flows.ContainsKey(new DateTime(2026, 1, 3)));
        Assert.False(flows.ContainsKey(new DateTime(2026, 1, 4)));
    }
}
