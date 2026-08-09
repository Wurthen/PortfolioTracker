using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PortfolioTracker.Api.Services;

public class EodPriceProvider : IPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EodPriceProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly CurrencyService _currencyService;
    private readonly string? _apiKey;
    public string Name => "EOD";

    public EodPriceProvider(HttpClient httpClient, ILogger<EodPriceProvider> logger, IMemoryCache cache, CurrencyService currencyService, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
        _currencyService = currencyService;
        _apiKey = configuration["EodApiKey"];
    }

    public async Task<decimal?> GetPriceAsync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogDebug("EOD API key not configured, skipping");
            return null;
        }

        var cacheKey = $"eod_price_{symbol}";

        if (_cache.TryGetValue(cacheKey, out decimal cachedPrice))
        {
            _logger.LogInformation("EOD cache hit for {Symbol}: {Price}", symbol, cachedPrice);
            return cachedPrice;
        }

        try
        {
            var price = await TryGetRealTimePriceAsync(symbol);
            if (!price.HasValue)
            {
                price = await TryGetEodPriceAsync(symbol);
            }

            if (price.HasValue)
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                _cache.Set(cacheKey, price.Value, cacheOptions);
            }

            return price;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EOD error for {Symbol}", symbol);
            return null;
        }
    }

    private async Task<decimal?> TryGetRealTimePriceAsync(string symbol)
    {
        var url = $"https://eodhistoricaldata.com/api/real-time/{Uri.EscapeDataString(symbol)}?api_token={_apiKey}&fmt=json";
        _logger.LogInformation("EOD requesting real-time price for {Symbol}", symbol);

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("EOD real-time failed for {Symbol}: {Status}", symbol, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("close", out var closeElement))
        {
            _logger.LogWarning("EOD real-time returned no close for {Symbol}", symbol);
            return null;
        }

        return await ParsePrice(symbol, closeElement);
    }

    private async Task<decimal?> TryGetEodPriceAsync(string symbol)
    {
        var url = $"https://eodhistoricaldata.com/api/eod/{Uri.EscapeDataString(symbol)}?api_token={_apiKey}&fmt=json&limit=1";
        _logger.LogInformation("EOD requesting EOD price for {Symbol}", symbol);

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("EOD EOD failed for {Symbol}: {Status}", symbol, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            _logger.LogWarning("EOD EOD returned no data for {Symbol}", symbol);
            return null;
        }

        var lastBar = doc.RootElement[0];
        if (!lastBar.TryGetProperty("close", out var closeElement))
        {
            _logger.LogWarning("EOD EOD returned no close for {Symbol}", symbol);
            return null;
        }

        return await ParsePrice(symbol, closeElement);
    }

    private async Task<decimal?> ParsePrice(string symbol, JsonElement closeElement)
    {
        decimal? rawPrice = null;
        if (closeElement.ValueKind == JsonValueKind.Number)
        {
            rawPrice = closeElement.GetDecimal();
        }
        else if (closeElement.ValueKind == JsonValueKind.String && decimal.TryParse(closeElement.GetString(), out var stringPrice))
        {
            rawPrice = stringPrice;
        }

        if (!rawPrice.HasValue)
        {
            return null;
        }

        var currency = DetectCurrency(symbol);
        _logger.LogInformation("EOD got raw price for {Symbol}: {Price} {Currency}", symbol, rawPrice.Value, currency);

        if (currency == "EUR")
        {
            var eurToUsd = await _currencyService.GetEurToUsdRateAsync();
            var converted = rawPrice.Value * eurToUsd;
            _logger.LogInformation("EOD converted {Symbol} from EUR to USD: {Price}", symbol, converted);
            return converted;
        }

        return rawPrice.Value;
    }

    private static string DetectCurrency(string symbol)
    {
        if (symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase))
            return "EUR";

        return "USD";
    }
}
