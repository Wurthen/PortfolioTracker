using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PortfolioTracker.Api.Services;

public class FmpPriceProvider : IPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FmpPriceProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly string? _apiKey;
    public string Name => "FMP";

    public FmpPriceProvider(HttpClient httpClient, ILogger<FmpPriceProvider> logger, IMemoryCache cache, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
        _apiKey = configuration["FmpApiKey"];
    }

    public async Task<decimal?> GetPriceAsync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogDebug("FMP API key not configured, skipping");
            return null;
        }

        var cacheKey = $"fmp_price_{symbol}";

        if (_cache.TryGetValue(cacheKey, out decimal cachedPrice))
        {
            _logger.LogInformation("FMP cache hit for {Symbol}: {Price}", symbol, cachedPrice);
            return cachedPrice;
        }

        try
        {
            var url = $"https://financialmodelingprep.com/api/v3/quote/{Uri.EscapeDataString(symbol)}?apikey={_apiKey}";
            _logger.LogInformation("FMP requesting price for {Symbol}", symbol);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("FMP failed for {Symbol}: {Status}", symbol, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                _logger.LogWarning("FMP returned no results for {Symbol}", symbol);
                return null;
            }

            var quote = doc.RootElement[0];

            if (quote.TryGetProperty("price", out var priceElement) && priceElement.ValueKind == JsonValueKind.Number)
            {
                var price = priceElement.GetDecimal();
                _logger.LogInformation("FMP got price for {Symbol}: {Price}", symbol, price);

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                _cache.Set(cacheKey, price, cacheOptions);

                return price;
            }

            _logger.LogWarning("FMP could not parse price for {Symbol}", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FMP error for {Symbol}", symbol);
            return null;
        }
    }
}
