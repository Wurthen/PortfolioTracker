using System.Net.Http;
using System.Net.Http.Json;

namespace PortfolioTracker.Blazor.Services;

public class PortfolioApiService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PortfolioApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient Client => _httpClientFactory.CreateClient("Api");

    public async Task<List<PortfolioItemDto>> GetPortfolioAsync(Guid userId)
    {
        var response = await Client.GetAsync($"/api/portfolio/{userId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PortfolioItemDto>>() ?? [];
    }

    public async Task<SearchResultDto?> SearchSymbolAsync(string query)
    {
        var response = await Client.GetAsync($"/api/search?query={Uri.EscapeDataString(query)}");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<SearchResultDto>();
    }

    public async Task<PortfolioItemDto?> AddItemAsync(CreatePortfolioItemRequest request)
    {
        var response = await Client.PostAsJsonAsync("/api/portfolio", request);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PortfolioItemDto>();
    }

    public async Task<PortfolioItemDto?> UpdateItemAsync(Guid id, Guid userId, UpdatePortfolioItemRequest request)
    {
        var response = await Client.PutAsJsonAsync($"/api/portfolio/{id}/{userId}", request);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PortfolioItemDto>();
    }

    public async Task<bool> DeleteItemAsync(Guid id, Guid userId)
    {
        var response = await Client.DeleteAsync($"/api/portfolio/{id}/{userId}");
        return response.IsSuccessStatusCode;
    }
}
