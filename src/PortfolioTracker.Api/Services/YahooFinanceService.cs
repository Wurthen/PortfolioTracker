using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class YahooFinanceService
{
    private readonly HttpClient _httpClient;

    public YahooFinanceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
        try
        {
            var quoteInfo = await GetQuoteInfoAsync(symbol);
            return quoteInfo.Price;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(decimal? Price, string? Name, string? Type)> GetQuoteInfoAsync(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";

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

            var price = meta.TryGetProperty("regularMarketPrice", out var mp) ? mp.GetDecimal() : (decimal?)null;
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
        catch
        {
            return (null, null, null);
        }
    }
}
