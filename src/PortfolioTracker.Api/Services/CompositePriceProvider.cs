using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PortfolioTracker.Api.Services;

public class CompositePriceProvider : IPriceProvider
{
    private readonly IPriceProvider[] _providers;
    private readonly SymbolPriceService _symbolPriceService;
    private readonly ILogger<CompositePriceProvider> _logger;
    private static readonly ConcurrentDictionary<string, Task<decimal?>> _pendingRequests = new();
    public string Name => "Composite";

    public CompositePriceProvider(
        TwelveDataPriceProvider twelveData,
        FmpPriceProvider fmp,
        EodPriceProvider eod,
        SymbolPriceService symbolPriceService,
        ILogger<CompositePriceProvider> logger)
    {
        _providers = new IPriceProvider[] { twelveData, fmp, eod };
        _symbolPriceService = symbolPriceService;
        _logger = logger;
    }

    public Task<decimal?> GetPriceAsync(string symbol)
    {
        var normalizedSymbol = NormalizeFundSymbol(symbol);
        var cacheKey = $"req_{normalizedSymbol}";

        return _pendingRequests.GetOrAdd(cacheKey, _ => FetchPriceAsync(normalizedSymbol, symbol))
            .ContinueWith(t =>
            {
                _pendingRequests.TryRemove(cacheKey, out _);
                return t.IsCompletedSuccessfully ? t.Result : throw t.Exception!;
            });
    }

    private async Task<decimal?> FetchPriceAsync(string normalizedSymbol, string originalSymbol)
    {
        _logger.LogInformation("CompositePriceProvider trying to get price for {Symbol} (normalized: {NormalizedSymbol})", originalSymbol, normalizedSymbol);

        foreach (var provider in _providers)
        {
            _logger.LogInformation("Trying provider: {ProviderName}", provider.Name);

            var price = await provider.GetPriceAsync(normalizedSymbol);

            if (price.HasValue)
            {
                _logger.LogInformation("Got price from {ProviderName}: {Price}", provider.Name, price.Value);
                await _symbolPriceService.SavePriceAsync(normalizedSymbol, price.Value, provider.Name);
                return price;
            }

            _logger.LogWarning("Provider {ProviderName} returned null, trying next", provider.Name);
        }

        var cached = await _symbolPriceService.GetLatestPriceAsync(normalizedSymbol);
        if (cached != null)
        {
            _logger.LogWarning("All live providers failed for {Symbol}, returning cached price {Price} from {Provider} at {UpdatedAt}",
                originalSymbol, cached.Price, cached.Provider, cached.UpdatedAt);
            return cached.Price;
        }

        _logger.LogError("All providers failed for {Symbol} and no cached price available", originalSymbol);
        return null;
    }

    private static string NormalizeFundSymbol(string symbol)
    {
        // Morningstar fund symbols often come with an exchange suffix from Yahoo/OTC (e.g., 0P0000X09U.F, 0P0000X09U.DE)
        // Twelve Data and other providers use the symbol without the suffix.
        if (symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase))
        {
            var dotIndex = symbol.LastIndexOf('.');
            if (dotIndex > 0)
            {
                return symbol[..dotIndex];
            }
        }

        return symbol;
    }
}
