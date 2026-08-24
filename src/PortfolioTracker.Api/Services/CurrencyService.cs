using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;

namespace PortfolioTracker.Api.Services;

public class CurrencyService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CurrencyService> _logger;
    private const string CacheKey = "USD_EUR_Rate";
    private const int CacheDurationMinutes = 60;

    public CurrencyService(HttpClient httpClient, IMemoryCache cache, ILogger<CurrencyService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<decimal> GetUsdToEurRateAsync()
    {
        var rate = await FetchRateAsync();
        return rate;
    }

    public async Task<decimal> GetEurToUsdRateAsync()
    {
        var rate = await FetchRateAsync();
        return rate > 0 ? 1 / rate : 1.09m;
    }

    private async Task<decimal> FetchRateAsync()
    {
        if (_cache.TryGetValue(CacheKey, out decimal cachedRate))
        {
            return cachedRate;
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<ExchangeRateResponse>(
                "https://api.exchangerate-api.com/v4/latest/USD");

            if (response?.Rates?.EUR != null)
            {
                var rate = (decimal)response.Rates.EUR;
                _cache.Set(CacheKey, rate, TimeSpan.FromMinutes(CacheDurationMinutes));
                _logger.LogInformation("USD/EUR rate fetched: {Rate}", rate);
                return rate;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching exchange rate");
        }

        // Cache the fallback briefly so an outage does not trigger one HTTP call per conversion.
        var fallback = 0.92m;
        _cache.Set(CacheKey, fallback, TimeSpan.FromMinutes(5));
        return fallback;
    }

    private class ExchangeRateResponse
    {
        public ExchangeRateRates? Rates { get; set; }
    }

    private class ExchangeRateRates
    {
        public double EUR { get; set; }
    }
}
