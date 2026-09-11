namespace PortfolioTracker.Api.Services;

/// <summary>
/// Single source of truth for fund-symbol classification and normalization.
/// Morningstar funds start with 0P; EOD uses a .EUFUND suffix for European funds.
/// </summary>
public static class SymbolClassifier
{
    public static bool IsFund(string symbol)
        => IsMorningstarFund(symbol) || HasEufundSuffix(symbol);

    public static bool IsMorningstarFund(string symbol)
        => symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase);

    public static bool HasEufundSuffix(string symbol)
        => symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the exchange suffix Morningstar funds may carry from Yahoo
    /// (e.g. 0P0000X09U.F, 0P0000X09U.DE) so the same fund maps to one row.
    /// </summary>
    public static string StripExchangeSuffix(string symbol)
    {
        if (IsMorningstarFund(symbol))
        {
            var dotIndex = symbol.LastIndexOf('.');
            if (dotIndex > 0)
                return symbol[..dotIndex];
        }
        return symbol;
    }

    /// <summary>Removes the trailing .EUFUND suffix, if present.</summary>
    public static string WithoutEufundSuffix(string symbol)
        => HasEufundSuffix(symbol) ? symbol[..^7] : symbol;
}
