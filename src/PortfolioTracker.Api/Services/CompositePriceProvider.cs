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
        var normalizedSymbol = NormalizeFundSymbol(symbol);
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

        var providers = IsFundSymbol(normalizedSymbol) ? _fundProviders : _stockProviders;

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

    private static bool IsFundSymbol(string symbol)
    {
        if (symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase))
            return true;

        if (symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
