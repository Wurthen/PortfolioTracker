namespace PortfolioTracker.Api.Services;

public interface IPriceProvider
{
    Task<decimal?> GetPriceAsync(string symbol);
    string Name { get; }
}
