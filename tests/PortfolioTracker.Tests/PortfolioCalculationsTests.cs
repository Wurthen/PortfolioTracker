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
