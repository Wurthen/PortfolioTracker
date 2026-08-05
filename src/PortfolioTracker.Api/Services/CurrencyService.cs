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

        return 0.92m;
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
