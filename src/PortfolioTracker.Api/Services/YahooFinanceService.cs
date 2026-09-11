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
            if (string.Equals(quoteType, "MUTUALFUND", StringComparison.OrdinalIgnoreCase))
            {
                symbol = SymbolClassifier.StripExchangeSuffix(symbol);
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
}
