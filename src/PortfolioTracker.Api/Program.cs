using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;
using PortfolioTracker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PortfolioDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default") 
        ?? "Host=localhost;Database=portfolio;Username=postgres;Password=postgres"));

builder.Services.AddHttpClient<YahooFinanceService>();
builder.Services.AddHttpClient<TwelveDataPriceProvider>();
builder.Services.AddHttpClient<FmpPriceProvider>();
builder.Services.AddHttpClient<EodPriceProvider>();
builder.Services.AddScoped<TwelveDataPriceProvider>();
builder.Services.AddScoped<FmpPriceProvider>();
builder.Services.AddScoped<EodPriceProvider>();
builder.Services.AddScoped<SymbolPriceService>();
builder.Services.AddScoped<CompositePriceProvider>();
builder.Services.AddScoped<IPriceProvider>(sp => sp.GetRequiredService<CompositePriceProvider>());
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddMemoryCache();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
    db.Database.Migrate();
}

app.UseCors();

app.MapGet("/api/search", async (string query, YahooFinanceService yahooService) =>
{
    if (string.IsNullOrWhiteSpace(query))
        return Results.BadRequest();

    var result = await yahooService.SearchSymbolAsync(query);
    return result == null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/portfolio/{userId:guid}", async (Guid userId, PortfolioService portfolioService) =>
{
    var items = await portfolioService.GetPortfolioAsync(userId);
    return Results.Ok(items);
});

app.MapPost("/api/portfolio", async (CreatePortfolioItemRequest request, PortfolioService portfolioService) =>
{
    var item = await portfolioService.AddItemAsync(request);
    return item == null ? Results.Conflict() : Results.Created($"/api/portfolio/{item.Id}", item);
});

app.MapPut("/api/portfolio/{id:guid}/{userId:guid}", async (Guid id, Guid userId, UpdatePortfolioItemRequest request, PortfolioService portfolioService) =>
{
    var item = await portfolioService.UpdateItemAsync(id, userId, request);
    return item == null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/portfolio/{id:guid}/{userId:guid}", async (Guid id, Guid userId, PortfolioService portfolioService) =>
{
    var success = await portfolioService.DeleteItemAsync(id, userId);
    return success ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/portfolio/{userId:guid}/performance", async (Guid userId, PortfolioService portfolioService) =>
{
    var performance = await portfolioService.GetPerformanceAsync(userId);
    return Results.Ok(performance);
});

app.MapGet("/api/portfolio/{userId:guid}/dashboard", async (Guid userId, PortfolioService portfolioService) =>
{
    var dashboard = await portfolioService.GetDashboardAsync(userId);
    return Results.Ok(dashboard);
});

app.MapGet("/health", () => Results.Ok());

app.Run();
