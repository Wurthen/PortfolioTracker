using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PortfolioTracker.Api.Services;

public class CompositePriceProvider : IPriceProvider
{
    private readonly IPriceProvider[] _stockProviders;
    private readonly IPriceProvider[] _fundProviders;
    private readonly SymbolPriceService _symbolPriceService;
    private readonly ILogger<CompositePriceProvider> _logger;
    private static readonly ConcurrentDictionary<string, Lazy<Task<decimal?>>> _pendingRequests = new();
    public string Name => "Composite";

    public CompositePriceProvider(
        AlpacaPriceProvider alpaca,
        TwelveDataPriceProvider twelveData,
        FmpPriceProvider fmp,
        EodPriceProvider eod,
        SymbolPriceService symbolPriceService,
        ILogger<CompositePriceProvider> logger)
    {
        // Funds should never hit Alpaca; route them straight to EOD.
        _stockProviders = new IPriceProvider[] { alpaca, twelveData, fmp, eod };
        _fundProviders = new IPriceProvider[] { eod };
        _symbolPriceService = symbolPriceService;
        _logger = logger;
    }

    public Task<decimal?> GetPriceAsync(string symbol)
    {
        var normalizedSymbol = SymbolClassifier.StripExchangeSuffix(symbol);
        var cacheKey = $"req_{normalizedSymbol}";

        // Lazy ensures the valueFactory (and therefore the provider chain) runs exactly once
        // even under concurrent requests for the same symbol.
        var lazy = _pendingRequests.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<decimal?>>(() => FetchPriceAsync(normalizedSymbol, symbol)));

        try
        {
            return AwaitAndCleanupAsync(cacheKey, lazy);
        }
        catch
        {
            _pendingRequests.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<decimal?> AwaitAndCleanupAsync(string cacheKey, Lazy<Task<decimal?>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout: treat as "no price" instead of masking with an NRE.
            return null;
        }
        finally
        {
            _pendingRequests.TryRemove(cacheKey, out _);
        }
    }

    private async Task<decimal?> FetchPriceAsync(string normalizedSymbol, string originalSymbol)
    {
        _logger.LogInformation("CompositePriceProvider trying to get price for {Symbol} (normalized: {NormalizedSymbol})", originalSymbol, normalizedSymbol);

        var providers = SymbolClassifier.IsFund(normalizedSymbol) ? _fundProviders : _stockProviders;

        foreach (var provider in providers)
        {
            decimal? price;
            try
            {
                _logger.LogInformation("Trying provider: {ProviderName}", provider.Name);
                price = await provider.GetPriceAsync(normalizedSymbol);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken provider must not abort the whole chain.
                _logger.LogError(ex, "Provider {ProviderName} threw, trying next", provider.Name);
                continue;
            }

            if (price.HasValue)
            {
                _logger.LogInformation("Got price from {ProviderName}: {Price}", provider.Name, price.Value);

                try
                {
                    await _symbolPriceService.SavePriceAsync(normalizedSymbol, price.Value, provider.Name);
                }
                catch (Exception ex)
                {
                    // Cache write failures must not lose the price we already have.
                    _logger.LogError(ex, "Failed to persist price for {Symbol}", normalizedSymbol);
                }

                return price.Value;
            }

            _logger.LogWarning("Provider {ProviderName} returned null, trying next", provider.Name);
        }

        try
        {
            var cached = await _symbolPriceService.GetLatestPriceAsync(normalizedSymbol);
            if (cached != null)
            {
                _logger.LogWarning("All live providers failed for {Symbol}, returning cached price {Price} from {Provider} at {UpdatedAt}",
                    originalSymbol, cached.Price, cached.Provider, cached.UpdatedAt);
                return cached.Price;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache fallback read failed for {Symbol}", originalSymbol);
        }

        _logger.LogError("All providers failed for {Symbol} and no cached price available", originalSymbol);
        return null;
    }
}
