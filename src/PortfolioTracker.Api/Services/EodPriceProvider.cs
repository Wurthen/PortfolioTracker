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
    private readonly SymbolPriceService _symbolPriceService;
    private readonly string? _apiKey;
    public string Name => "EOD";

    public EodPriceProvider(HttpClient httpClient, ILogger<EodPriceProvider> logger, IMemoryCache cache, CurrencyService currencyService, SymbolPriceService symbolPriceService, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
        _currencyService = currencyService;
        _symbolPriceService = symbolPriceService;
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

        // DAILY CADENCE GATE (free tier = 20 calls/day): live-fetch each fund at most
        // once per UTC day, plus one evening refresh after 19:00 UTC so tonight's NAV
        // can be picked up the same evening. Otherwise serve the persisted DB value.
        var persistedRow = await _symbolPriceService.GetLatestPriceAsync(symbol);
        var lastFetchUtc = persistedRow?.UpdatedAt ?? DateTime.MinValue;
        var todayUtc = DateTime.UtcNow.Date;
        var eveningWindow = DateTime.UtcNow.TimeOfDay >= TimeSpan.FromHours(19);
        var shouldFetchLive = persistedRow == null
            || lastFetchUtc.Date < todayUtc
            || (lastFetchUtc < todayUtc.AddHours(17) && eveningWindow);

        if (!shouldFetchLive && persistedRow != null)
        {
            if (persistedRow.UpdatedAt < DateTime.UtcNow.AddDays(-7))
            {
                _logger.LogWarning("EOD persisted price for {Symbol} is stale ({UpdatedAt}) and daily cadence says skip; returning null",
                    symbol, persistedRow.UpdatedAt);
                return null;
            }

            _logger.LogInformation("EOD daily budget already used for {Symbol} (last {Last:HH:mm} UTC), serving persisted {Price}",
                symbol, lastFetchUtc, persistedRow.Price);
            _cache.Set(cacheKey, persistedRow.Price, TimeSpan.FromMinutes(15));
            return persistedRow.Price;
        }

        try
        {
            var price = await TryGetRealTimePriceAsync(symbol);
            if (!price.HasValue && !QuotaBlocked)
            {
                price = await TryGetEodPriceAsync(symbol);
            }

            // 401/402/403/429 = bad key or daily quota gone: further attempts only burn
            // what little quota may remain. Fall straight to the persisted price.
            if (!price.HasValue && !QuotaBlocked && symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase))
            {
                var symbolWithoutSuffix = symbol[..^7];
                _logger.LogInformation("EOD trying without EUFUND suffix: {Symbol}", symbolWithoutSuffix);
                price = await TryGetRealTimePriceAsync(symbolWithoutSuffix);
                if (!price.HasValue && !QuotaBlocked)
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

                // Persist so quota exhaustion / outages can fall back later.
                try
                {
                    await _symbolPriceService.SavePriceAsync(symbol, price.Value, "EOD");
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Failed to persist EOD price for {Symbol}", symbol);
                }
            }
            else
            {
                // Quota exhausted or outage: last successful price beats N/A — but only if
                // reasonably fresh, otherwise a weeks-old cached price fakes a big daily move.
                var persisted = await _symbolPriceService.GetLatestPriceAsync(symbol);
                if (persisted != null && persisted.UpdatedAt >= DateTime.UtcNow.AddDays(-7))
                {
                    _logger.LogWarning("EOD live fetch failed for {Symbol}, using persisted price {Price} from {UpdatedAt}",
                        symbol, persisted.Price, persisted.UpdatedAt);
                    _cache.Set(cacheKey, persisted.Price, TimeSpan.FromMinutes(15));
                    return persisted.Price;
                }
                if (persisted != null)
                {
                    _logger.LogWarning("EOD live fetch failed for {Symbol} and persisted price is stale ({UpdatedAt}); returning null",
                        symbol, persisted.UpdatedAt);
                }
            }

            return price;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EOD error for {Symbol}", symbol);
            return null;
        }
    }

    private int _lastHttpStatus;
    private bool QuotaBlocked => _lastHttpStatus is 401 or 402 or 403 or 429;

    private async Task<decimal?> TryGetRealTimePriceAsync(string symbol)
    {
        var url = $"https://eodhistoricaldata.com/api/real-time/{Uri.EscapeDataString(symbol)}?api_token={_apiKey}&fmt=json";
        _logger.LogInformation("EOD REAL-TIME requesting for {Symbol}", symbol);

        var response = await _httpClient.GetAsync(url);
        _lastHttpStatus = (int)response.StatusCode;
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
        _lastHttpStatus = (int)response.StatusCode;
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
        // Only explicit exchange/fund suffixes imply currency. Prefix heuristics
        // misclassify US tickers like ES, IT, BE, FR...
        if (symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase))
            return "EUR";

        return "USD";
    }
}
