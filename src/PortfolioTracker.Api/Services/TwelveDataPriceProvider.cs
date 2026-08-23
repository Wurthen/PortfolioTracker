using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PortfolioTracker.Api.Services;

public class TwelveDataPriceProvider : IPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwelveDataPriceProvider> _logger;
    private readonly string _apiKey;
    private readonly IMemoryCache _cache;
    public string Name => "TwelveData";

    public TwelveDataPriceProvider(HttpClient httpClient, ILogger<TwelveDataPriceProvider> logger, IConfiguration configuration, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["TwelveDataApiKey"] ?? "demo";
        _cache = cache;
    }

    public async Task<decimal?> GetPriceAsync(string symbol)
    {
        var cacheKey = $"price_{symbol}";
        
        if (_cache.TryGetValue(cacheKey, out decimal cachedPrice))
        {
            _logger.LogInformation("Cache hit for {Symbol}: {Price}", symbol, cachedPrice);
            return cachedPrice;
        }

        try
        {
            var isFund = symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase);
            var exchangeParam = isFund ? "&mic_code=XFRA" : "";
            var endpoint = isFund ? "eod" : "quote";
            var url = $"https://api.twelvedata.com/{endpoint}?symbol={Uri.EscapeDataString(symbol)}&apikey={_apiKey}{exchangeParam}";
            _logger.LogInformation("TwelveData requesting price for {Symbol} from URL: {Url}", symbol, url.Replace(_apiKey, "***"));

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TwelveData failed for {Symbol}: {Status}", symbol, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("status", out var statusProp) &&
                statusProp.ValueKind == JsonValueKind.String &&
                statusProp.GetString() == "error")
            {
                _logger.LogWarning("TwelveData returned error status for {Symbol}: {Json}", symbol, json.Length > 300 ? json[..300] + "..." : json);
                return null;
            }

            decimal? price = null;
            if (isFund)
            {
                // /eod returns { meta, values: [ { datetime, close, ... }, ... ] } (most recent first)
                if (doc.RootElement.TryGetProperty("values", out var values) &&
                    values.ValueKind == JsonValueKind.Array &&
                    values.GetArrayLength() > 0 &&
                    values[0].ValueKind == JsonValueKind.Object &&
                    values[0].TryGetProperty("close", out var fundClose))
                {
                    price = ParsePriceValue(fundClose);
                }
            }
            else if (doc.RootElement.TryGetProperty("close", out var close))
            {
                price = ParsePriceValue(close);
            }

            if (price.HasValue)
            {
                _logger.LogInformation("TwelveData got price for {Symbol}: {Price}", symbol, price.Value);

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                _cache.Set(cacheKey, price.Value, cacheOptions);

                return price.Value;
            }

            _logger.LogWarning("TwelveData could not parse price for {Symbol}", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TwelveData error for {Symbol}", symbol);
            return null;
        }
    }

    private static decimal? ParsePriceValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringPrice))
            return stringPrice;
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetDecimal();
        return null;
    }
}
