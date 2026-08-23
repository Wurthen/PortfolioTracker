using System.Globalization;
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

            if (!price.HasValue && symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase))
            {
                var symbolWithoutSuffix = symbol[..^7];
                _logger.LogInformation("EOD trying without EUFUND suffix: {Symbol}", symbolWithoutSuffix);
                price = await TryGetRealTimePriceAsync(symbolWithoutSuffix);
                if (!price.HasValue)
                {
                    price = await TryGetEodPriceAsync(symbolWithoutSuffix);
                }
                if (price.HasValue)
                {
                    cacheKey = $"eod_price_{symbolWithoutSuffix}";
                }
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
        _logger.LogInformation("EOD REAL-TIME requesting for {Symbol}", symbol);

        var response = await _httpClient.GetAsync(url);
        _logger.LogInformation("EOD REAL-TIME response status: {Status} for {Symbol}", response.StatusCode, symbol);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("EOD REAL-TIME failed for {Symbol}: HTTP {Status}", symbol, (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("EOD REAL-TIME raw response for {Symbol}: {Json}", symbol, json.Length > 500 ? json[..500] + "..." : json);

        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("close", out var closeElement) && closeElement.ValueKind == JsonValueKind.String)
        {
            var closeStr = closeElement.GetString();
            if (closeStr == "NA" || string.IsNullOrEmpty(closeStr))
            {
                if (doc.RootElement.TryGetProperty("previousClose", out var prevCloseElement) && prevCloseElement.ValueKind == JsonValueKind.Number)
                {
                    _logger.LogInformation("EOD REAL-TIME close is NA, using previousClose: {PrevClose}", prevCloseElement.GetDecimal());
                    return await ParsePrice(symbol, prevCloseElement);
                }
                _logger.LogWarning("EOD REAL-TIME close is NA and no previousClose for {Symbol}", symbol);
                return null;
            }
        }

        if (!doc.RootElement.TryGetProperty("close", out var closeElem))
        {
            _logger.LogWarning("EOD REAL-TIME returned no close field for {Symbol}", symbol);
            return null;
        }

        return await ParsePrice(symbol, closeElem);
    }

    private async Task<decimal?> TryGetEodPriceAsync(string symbol)
    {
        var url = $"https://eodhistoricaldata.com/api/eod/{Uri.EscapeDataString(symbol)}?api_token={_apiKey}&fmt=json&limit=5";
        _logger.LogInformation("EOD EOD requesting for {Symbol}", symbol);

        var response = await _httpClient.GetAsync(url);
        _logger.LogInformation("EOD EOD response status: {Status} for {Symbol}", response.StatusCode, symbol);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("EOD EOD failed for {Symbol}: HTTP {Status}", symbol, (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("EOD EOD raw response for {Symbol}: {Json}", symbol, json.Length > 500 ? json[..500] + "..." : json);

        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            _logger.LogWarning("EOD EOD returned empty array for {Symbol}", symbol);
            return null;
        }

        var bars = doc.RootElement;
        var lastBar = bars[bars.GetArrayLength() - 1];
        if (!lastBar.TryGetProperty("close", out var closeElement))
        {
            _logger.LogWarning("EOD EOD returned no close field for {Symbol}", symbol);
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
        else if (closeElement.ValueKind == JsonValueKind.String && decimal.TryParse(closeElement.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringPrice))
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

        if (symbol.Length >= 2)
        {
            var prefix = symbol[..2].ToUpperInvariant();
            if (prefix is "ES" or "LU" or "FR" or "DE" or "IT" or "NL" or "BE" or "AT" or "FI" or "IE" or "PT" or "GR")
                return "EUR";
        }

        return "USD";
    }
}
