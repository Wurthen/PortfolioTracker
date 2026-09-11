using System.Text.Json;
using PortfolioTracker.Api.Models;
using PortfolioTracker.Api.Services;
using Xunit;

namespace PortfolioTracker.Tests;

public class PortfolioServiceTests
{
    private static PortfolioItem MakeItem() => new()
    {
        UserId = Guid.NewGuid(),
        Symbol = "TEST",
        Name = "Test Fund",
        Type = "Fund",
        Shares = 10m,
        PurchasePrice = 100m,
        Commission = 5m
    };

    [Fact]
    public void MapToDto_WhenPriceUnavailable_ShowsNoFakeLoss()
    {
        var dto = PortfolioService.MapToDto(MakeItem(), currentPriceUsd: null, usdToEur: 0.86m);

        Assert.False(dto.PriceAvailable);
        Assert.Equal(0m, dto.CurrentValue);
        Assert.Equal(0m, dto.GainLoss);
        Assert.Equal(0m, dto.GainLossPercent);
    }

    [Fact]
    public void MapToDto_WithPrice_ComputesEurValuesAndGain()
    {
        var item = MakeItem(); // 10 shares @ 100 EUR + 5 commission => cost 1005
        var dto = PortfolioService.MapToDto(item, currentPriceUsd: 116.27907m, usdToEur: 0.86m);

        Assert.True(dto.PriceAvailable);
        Assert.Equal(116.27907m * 0.86m * 10m, dto.CurrentValue, 4);
        Assert.Equal(dto.CurrentValue - 1005m, dto.GainLoss, 4);
    }

    [Theory]
    [InlineData(10, 100, 10, 1200, 110)]   // (1000 + 1200) / 20
    [InlineData(0, 0, 5, 500, 100)]        // first purchase
    public void ComputeWeightedAveragePrice_MergesCorrectly(decimal existingShares, decimal existingAvg, decimal addShares, decimal addCost, decimal expected)
    {
        var avg = PortfolioService.ComputeWeightedAveragePrice(existingShares, existingAvg, addShares, addCost);
        Assert.Equal(expected, avg);
    }

    [Fact]
    public void ComputeSafeBackPrice_DilutesPriceButKeepsCostBasis()
    {
        // 10 shares @ 100 = 1000 cost; 2 SafeBack shares arrive at zero cost.
        var price = PortfolioService.ComputeSafeBackPrice(existingShares: 10m, existingAvgPrice: 100m, safeBackShares: 2m);

        Assert.Equal(1000m / 12m, price, 8);
        Assert.Equal(1000m, price * 12m, 6); // Shares * PurchasePrice is unchanged.
    }

    [Fact]
    public void ComputeSafeBackPrice_ZeroExistingShares_ReturnsZero()
    {
        Assert.Equal(0m, PortfolioService.ComputeSafeBackPrice(0m, 100m, 2m));
    }

    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("0P0000X09U.F", "0P0000X09U")]
    [InlineData("0p0000x09u.de", "0P0000X09U")]
    [InlineData("MSCIWORLD", "MSCIWORLD")]
    public void NormalizeSymbol_MapsFundsConsistently(string input, string expected)
    {
        Assert.Equal(expected, PortfolioService.NormalizeSymbol(input));
    }

    [Fact]
    public void MapToDto_IncludesSafeBackFields()
    {
        var item = MakeItem();
        item.SafeBackAmount = 25.50m;
        item.SafeBackShares = 0.75m;

        var dto = PortfolioService.MapToDto(item, currentPriceUsd: 100m, usdToEur: 0.9m);

        Assert.Equal(25.50m, dto.SafeBackAmount);
        Assert.Equal(0.75m, dto.SafeBackShares);
    }

    [Fact]
    public void ComputeSinceInception_ExcludesUnpricedFromNumeratorAndDenominator()
    {
        var priced = PortfolioService.MapToDto(MakeItem(), currentPriceUsd: 100m, usdToEur: 1m);   // 1000 value vs 1005 cost
        var unpriced = PortfolioService.MapToDto(MakeItem(), currentPriceUsd: null, usdToEur: 1m);

        var result = PortfolioService.ComputeSinceInception([priced, unpriced]);

        Assert.Equal(1005m, result.CostBasis);
        Assert.Equal(-5m, result.GainLoss);
        Assert.Equal(-5m / 1005m * 100m, result.GainLossPercent);
    }

    [Fact]
    public void ComputeSinceInception_MatchesPerItemGainLossPercent()
    {
        var dto = PortfolioService.MapToDto(MakeItem(), currentPriceUsd: 120m, usdToEur: 1m);

        var result = PortfolioService.ComputeSinceInception([dto]);

        Assert.Equal(dto.GainLossPercent, result.GainLossPercent);
    }

    [Fact]
    public void ComputeSinceInception_Empty_ReturnsZero()
    {
        var result = PortfolioService.ComputeSinceInception([]);

        Assert.Equal(0m, result.CostBasis);
        Assert.Equal(0m, result.GainLoss);
        Assert.Equal(0m, result.GainLossPercent);
    }
}

public class SymbolClassifierTests
{
    [Theory]
    [InlineData("0P0000X09U", true)]
    [InlineData("0p0000x09u.f", true)]
    [InlineData("IE00B03HCZ61.EUFUND", true)]
    [InlineData("AAPL", false)]
    [InlineData("VUSA.L", false)]
    public void IsFund_ClassifiesFundSymbols(string symbol, bool expected)
        => Assert.Equal(expected, SymbolClassifier.IsFund(symbol));

    [Theory]
    [InlineData("0P0000X09U.F", "0P0000X09U")]
    [InlineData("0p0000x09u.de", "0p0000x09u")]
    [InlineData("AAPL", "AAPL")]
    [InlineData("IE00B03HCZ61.EUFUND", "IE00B03HCZ61.EUFUND")]
    public void StripExchangeSuffix_OnlyTouchesMorningstarSymbols(string input, string expected)
        => Assert.Equal(expected, SymbolClassifier.StripExchangeSuffix(input));

    [Theory]
    [InlineData("IE00B03HCZ61.EUFUND", "IE00B03HCZ61")]
    [InlineData("ie00b03hcz61.eufund", "ie00b03hcz61")]
    [InlineData("AAPL", "AAPL")]
    public void WithoutEufundSuffix_RemovesSuffixOnly(string input, string expected)
        => Assert.Equal(expected, SymbolClassifier.WithoutEufundSuffix(input));
}

public class HistoryBackfillParseTests
{
    [Theory]
    [InlineData("\"119.34000\"", 119.34)]
    [InlineData("119.34", 119.34)]
    [InlineData("\"1,234.56\"", 1234.56)]
    public void ParseDecimal_UsesInvariantCulture(string json, decimal expected)
    {
        using var doc = JsonDocument.Parse(json);
        var parsed = HistoryBackfillService.ParseDecimal(doc.RootElement);

        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed.Value);
    }

    [Fact]
    public void ParseDecimal_Invalid_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("\"not-a-number\"");
        Assert.Null(HistoryBackfillService.ParseDecimal(doc.RootElement));
    }

    [Fact]
    // Regression: es-ES culture parses "119.34000" as 11,934,000 (dot as group separator).
    public void ParseDecimal_DottedFiveDecimals_IsNotScaledByCulture()
    {
        using var doc = JsonDocument.Parse("\"89.72000\"");
        var parsed = HistoryBackfillService.ParseDecimal(doc.RootElement);

        Assert.Equal(89.72m, parsed!.Value);
        Assert.True(parsed.Value < 100m);
    }
}

public class HistoryBackfillReplayTests
{
    [Fact]
    public void SharesHeldAt_IncludesSafeBackShares()
    {
        var itemId = Guid.NewGuid();
        var txs = new List<PortfolioTransaction>
        {
            new() { ItemId = itemId, Type = "Buy", Date = new DateTime(2026, 1, 1), Shares = 10m },
            new() { ItemId = itemId, Type = "SafeBack", Date = new DateTime(2026, 1, 10), Shares = 2m }
        };

        Assert.Equal(10m, HistoryBackfillService.SharesHeldAt(txs, new DateTime(2026, 1, 5)));
        Assert.Equal(12m, HistoryBackfillService.SharesHeldAt(txs, new DateTime(2026, 1, 10)));
        Assert.Equal(12m, HistoryBackfillService.SharesHeldAt(txs, new DateTime(2026, 2, 1)));
    }

    [Fact]
    public void SharesHeldAt_SubtractsSellsAndTransfersOut()
    {
        var itemId = Guid.NewGuid();
        var txs = new List<PortfolioTransaction>
        {
            new() { ItemId = itemId, Type = "Buy", Date = new DateTime(2026, 1, 1), Shares = 10m },
            new() { ItemId = itemId, Type = "Sell", Date = new DateTime(2026, 1, 5), Shares = 3m },
            new() { ItemId = itemId, Type = "TransferOut", Date = new DateTime(2026, 1, 6), Shares = 2m }
        };

        Assert.Equal(5m, HistoryBackfillService.SharesHeldAt(txs, new DateTime(2026, 1, 7)));
    }
}
