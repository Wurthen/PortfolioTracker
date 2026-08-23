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

static IResult? ValidateSharesPurchaseAndCommission(decimal shares, decimal purchasePrice, decimal commission, string? name)
{
    if (shares <= 0)
        return Results.BadRequest(new { error = "Shares must be greater than zero." });
    if (purchasePrice <= 0)
        return Results.BadRequest(new { error = "Purchase price must be greater than zero." });
    if (commission < 0)
        return Results.BadRequest(new { error = "Commission cannot be negative." });
    if (!string.IsNullOrEmpty(name) && name.Length > 200)
        return Results.BadRequest(new { error = "Name cannot exceed 200 characters." });
    return null;
}

app.MapPost("/api/portfolio", async (CreatePortfolioItemRequest request, PortfolioService portfolioService) =>
{
    if (string.IsNullOrWhiteSpace(request.Symbol))
        return Results.BadRequest(new { error = "Symbol is required." });
    if (request.Symbol.Length > 20)
        return Results.BadRequest(new { error = "Symbol cannot exceed 20 characters." });
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Name is required." });

    var validationResult = ValidateSharesPurchaseAndCommission(request.Shares, request.PurchasePrice, request.Commission, request.Name);
    if (validationResult != null)
        return validationResult;

    var item = await portfolioService.AddItemAsync(request);
    return item == null ? Results.Conflict() : Results.Created($"/api/portfolio/{item.Id}", item);
});

app.MapPut("/api/portfolio/{id:guid}/{userId:guid}", async (Guid id, Guid userId, UpdatePortfolioItemRequest request, PortfolioService portfolioService) =>
{
    var validationResult = ValidateSharesPurchaseAndCommission(request.Shares, request.PurchasePrice, request.Commission, request.Name);
    if (validationResult != null)
        return validationResult;

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

app.MapGet("/api/portfolio/{userId:guid}/history", async (Guid userId, PortfolioService portfolioService) =>
{
    var history = await portfolioService.GetHistoryAsync(userId);
    return Results.Ok(history);
});

app.MapGet("/health", () => Results.Ok());

app.Run();
