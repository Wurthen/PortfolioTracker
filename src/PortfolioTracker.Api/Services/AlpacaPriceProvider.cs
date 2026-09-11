using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PortfolioTracker.Api.Services;

public class AlpacaPriceProvider : IPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlpacaPriceProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly string? _apiKey;
    private readonly string? _secretKey;
    public string Name => "Alpaca";

    public AlpacaPriceProvider(HttpClient httpClient, ILogger<AlpacaPriceProvider> logger, IMemoryCache cache, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
        _apiKey = configuration["AlpacaApiKey"];
        _secretKey = configuration["AlpacaSecretKey"];
    }

    public async Task<decimal?> GetPriceAsync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_secretKey))
        {
            _logger.LogDebug("Alpaca API key or secret not configured, skipping");
            return null;
        }

        // Alpaca only covers US equities and ETFs; never query it for mutual funds.
        if (SymbolClassifier.IsFund(symbol))
        {
            _logger.LogDebug("Alpaca skipping fund symbol {Symbol}", symbol);
            return null;
        }

        var cacheKey = $"alpaca_price_{symbol}";

        if (_cache.TryGetValue(cacheKey, out decimal cachedPrice))
        {
            _logger.LogInformation("Alpaca cache hit for {Symbol}: {Price}", symbol, cachedPrice);
            return cachedPrice;
        }

        try
        {
            var url = $"https://data.alpaca.markets/v2/stocks/{Uri.EscapeDataString(symbol)}/snapshot";
            _logger.LogInformation("Alpaca requesting snapshot for {Symbol}", symbol);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("APCA-API-KEY-ID", _apiKey);
            request.Headers.Add("APCA-API-SECRET-KEY", _secretKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Alpaca failed for {Symbol}: {Status}", symbol, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            decimal? price = null;

            // Prefer the last executed trade price...
            if (root.TryGetProperty("latestTrade", out var latestTrade) &&
                latestTrade.ValueKind == JsonValueKind.Object &&
                latestTrade.TryGetProperty("p", out var tradePrice))
            {
                price = ParsePriceValue(tradePrice);
            }

            // ...then fall back to today's daily bar close.
            if (!price.HasValue &&
                root.TryGetProperty("dailyBar", out var dailyBar) &&
                dailyBar.ValueKind == JsonValueKind.Object &&
                dailyBar.TryGetProperty("c", out var dailyClose))
            {
                price = ParsePriceValue(dailyClose);
            }

            if (price.HasValue)
            {
                _logger.LogInformation("Alpaca got price for {Symbol}: {Price}", symbol, price.Value);

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                _cache.Set(cacheKey, price.Value, cacheOptions);

                return price.Value;
            }

            _logger.LogWarning("Alpaca could not parse price for {Symbol}", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alpaca error for {Symbol}", symbol);
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
