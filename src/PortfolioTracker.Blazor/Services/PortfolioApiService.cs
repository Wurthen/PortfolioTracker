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
            await ThrowApiErrorAsync(response);
        return await response.Content.ReadFromJsonAsync<PortfolioItemDto>();
    }

    public async Task<PortfolioItemDto?> UpdateItemAsync(Guid id, Guid userId, UpdatePortfolioItemRequest request)
    {
        var response = await Client.PutAsJsonAsync($"/api/portfolio/{id}/{userId}", request);
        if (!response.IsSuccessStatusCode)
            await ThrowApiErrorAsync(response);
        return await response.Content.ReadFromJsonAsync<PortfolioItemDto>();
    }

    public async Task<bool> DeleteItemAsync(Guid id, Guid userId)
    {
        var response = await Client.DeleteAsync($"/api/portfolio/{id}/{userId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<PortfolioDashboardDto?> GetDashboardAsync(Guid userId)
    {
        return await GetWithRetryAsync<PortfolioDashboardDto>($"/api/portfolio/{userId}/dashboard");
    }

    public async Task<PortfolioHistoryResponseDto?> GetHistoryAsync(Guid userId)
    {
        var response = await Client.GetAsync($"/api/portfolio/{userId}/history");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PortfolioHistoryResponseDto>();
    }

    public async Task<BackfillResultDto?> BackfillHistoryAsync(Guid userId, bool rebuild = false)
    {
        var url = $"/api/portfolio/{userId}/backfill" + (rebuild ? "?rebuild=true" : "");
        var response = await Client.PostAsync(url, null);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<BackfillResultDto>();
    }

    public async Task<List<TransactionDto>> GetTransactionsAsync(Guid userId)
    {
        try
        {
            return await Client.GetFromJsonAsync<List<TransactionDto>>($"/api/portfolio/{userId}/transactions") ?? [];
        }
        catch { return []; }
    }

    public async Task<List<TransactionDto>> GetItemTransactionsAsync(Guid userId, Guid itemId)
    {
        try
        {
            return await Client.GetFromJsonAsync<List<TransactionDto>>($"/api/portfolio/{userId}/transactions/{itemId}") ?? [];
        }
        catch { return []; }
    }

    public async Task<(bool Ok, string Error)> TransferAsync(Guid userId, TransferRequest request)
    {
        var response = await Client.PostAsJsonAsync($"/api/portfolio/{userId}/transfers", request);
        if (response.IsSuccessStatusCode)
            return (true, "");
        try
        {
            var err = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            return (false, err?.Error ?? "Error en el traspaso.");
        }
        catch { return (false, $"Error ({(int)response.StatusCode})."); }
    }

    public async Task<(bool Ok, string Error)> AddSafeBackAsync(Guid userId, CreateSafeBackRequest request)
    {
        var response = await Client.PostAsJsonAsync($"/api/portfolio/{userId}/safeback", request);
        if (response.IsSuccessStatusCode)
            return (true, "");
        try
        {
            var err = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            return (false, err?.Error ?? "Error al registrar SafeBack.");
        }
        catch { return (false, $"Error ({(int)response.StatusCode})."); }
    }

    public async Task<DetailedPerformanceDto?> GetDetailedPerformanceAsync(Guid userId)
    {
        try
        {
            return await Client.GetFromJsonAsync<DetailedPerformanceDto>($"/api/portfolio/{userId}/performance-detailed");
        }
        catch { return null; }
    }

    private static async Task ThrowApiErrorAsync(HttpResponseMessage response)
    {
        var message = $"Request failed ({(int)response.StatusCode}).";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            if (!string.IsNullOrWhiteSpace(body?.Error))
                message = body.Error;
        }
        catch { }
        throw new InvalidOperationException(message);
    }

    private async Task<T?> GetWithRetryAsync<T>(string url, int maxRetries = 10, int delayMs = 1000)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await Client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<T>();

                if ((int)response.StatusCode >= 500)
                {
                    await Task.Delay(delayMs);
                    continue;
                }

                return default;
            }
            catch (HttpRequestException)
            {
                if (i == maxRetries - 1) throw;
                await Task.Delay(delayMs);
            }
        }

        return default;
    }
}
