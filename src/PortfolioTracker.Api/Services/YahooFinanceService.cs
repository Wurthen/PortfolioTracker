using System.Text.Json;
using Microsoft.Extensions.Logging;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class YahooFinanceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YahooFinanceService> _logger;
    private readonly IPriceProvider _priceProvider;

    public YahooFinanceService(HttpClient httpClient, ILogger<YahooFinanceService> logger, IPriceProvider priceProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _priceProvider = priceProvider;
    }

    public async Task<SearchResultDto?> SearchSymbolAsync(string query)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=1&newsCount=0";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.GetArrayLength() == 0)
                return null;

            var quote = quotes[0];
            var symbol = quote.GetProperty("symbol").GetString() ?? "";
            var shortName = quote.TryGetProperty("shortname", out var sn) ? sn.GetString() : null;
            var longName = quote.TryGetProperty("longname", out var ln) ? ln.GetString() : null;
            var quoteType = quote.TryGetProperty("quoteType", out var qt) ? qt.GetString() : null;
            var exchange = quote.TryGetProperty("exchange", out var ex) ? ex.GetString() : null;

            var type = quoteType?.ToUpperInvariant() switch
            {
                "EQUITY" => "Stock",
                "ETF" => "ETF",
                "MUTUALFUND" => "Fund",
                _ => quoteType ?? "Unknown"
            };

            // Normalize fund symbols by removing exchange suffixes like .F, .DE, .MI
            if (string.Equals(quoteType, "MUTUALFUND", StringComparison.OrdinalIgnoreCase) && symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase))
            {
                var dotIndex = symbol.LastIndexOf('.');
                if (dotIndex > 0)
                {
                    symbol = symbol[..dotIndex];
                }
            }

            return new SearchResultDto
            {
                Symbol = symbol,
                Name = shortName ?? longName ?? symbol,
                Type = type,
                Exchange = exchange
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<decimal?> GetCurrentPriceAsync(string symbol)
    {
        return await _priceProvider.GetPriceAsync(symbol);
    }

    private static string NormalizeFundSymbol(string symbol)
    {
        if (symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase))
        {
            var dotIndex = symbol.LastIndexOf('.');
            if (dotIndex > 0)
            {
                return symbol[..dotIndex];
            }
        }
        return symbol;
    }

    public async Task<(decimal? Price, string? Name, string? Type)> GetQuoteInfoAsync(string symbol)
    {
        symbol = NormalizeFundSymbol(symbol);
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d&includePrePost=true";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return (null, null, null);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result");
            if (result.GetArrayLength() == 0)
                return (null, null, null);

            var firstResult = result[0];

            if (!firstResult.TryGetProperty("meta", out var meta))
                return (null, null, null);

            if (meta.TryGetProperty("regularMarketPrice", out var rmp) && rmp.ValueKind == JsonValueKind.Number)
            {
                var price = rmp.GetDecimal();
                var symbolName = meta.TryGetProperty("shortName", out var sn) ? sn.GetString() : null;
                var quoteType = meta.TryGetProperty("instrumentType", out var it) ? it.GetString() : null;

                var type = quoteType?.ToUpperInvariant() switch
                {
                    "EQUITY" => "Stock",
                    "ETF" => "ETF",
                    "MUTUALFUND" => "Fund",
                    _ => quoteType ?? "Unknown"
                };

                return (price, symbolName, type);
            }

            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }

    public async Task<decimal?> GetHistoricalPriceAsync(string symbol, string range)
    {
        symbol = NormalizeFundSymbol(symbol);

        // Skip historical price for funds to avoid Yahoo rate limits; fallback to current price will be used.
        if (symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range={range}";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result");
            if (result.GetArrayLength() == 0)
                return null;

            var firstResult = result[0];

            if (!firstResult.TryGetProperty("indicators", out var indicators))
                return null;

            if (!indicators.TryGetProperty("quote", out var quotes) || quotes.GetArrayLength() == 0)
                return null;

            var quote = quotes[0];
            if (!quote.TryGetProperty("close", out var closes) || closes.GetArrayLength() == 0)
                return null;

            for (int i = closes.GetArrayLength() - 1; i >= 0; i--)
            {
                if (closes[i].ValueKind == JsonValueKind.Number)
                {
                    return closes[i].GetDecimal();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
