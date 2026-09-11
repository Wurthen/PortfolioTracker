using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;
using PortfolioTracker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<PortfolioDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Database=portfolio;Username=postgres;Password=postgres"));

builder.Services.AddHttpClient<YahooFinanceService>(client =>
{
    // Yahoo blocks default UA strings; set once instead of mutating shared headers per call.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<AlpacaPriceProvider>();
builder.Services.AddHttpClient<TwelveDataPriceProvider>();
builder.Services.AddHttpClient<FmpPriceProvider>();
builder.Services.AddHttpClient<EodPriceProvider>();
builder.Services.AddScoped<SymbolPriceService>();
builder.Services.AddScoped<CompositePriceProvider>();
builder.Services.AddScoped<IPriceProvider>(sp => sp.GetRequiredService<CompositePriceProvider>());
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddHttpClient<HistoryBackfillService>();
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

app.MapPost("/api/portfolio/{userId:guid}/backfill", async (Guid userId, bool? rebuild, HistoryBackfillService backfillService) =>
{
    var result = await backfillService.BackfillAsync(userId, rebuild ?? false);
    return Results.Ok(result);
});

app.MapGet("/api/portfolio/{userId:guid}/transactions", async (Guid userId, TransactionService txService) =>
{
    var transactions = await txService.ListAsync(userId);
    return Results.Ok(transactions);
});

app.MapGet("/api/portfolio/{userId:guid}/transactions/{itemId:guid}", async (Guid userId, Guid itemId, TransactionService txService) =>
{
    var transactions = await txService.ListForItemAsync(userId, itemId);
    return Results.Ok(transactions);
});

app.MapPost("/api/portfolio/{userId:guid}/transactions", async (Guid userId, CreateTransactionRequest request, TransactionService txService) =>
{
    if (request.Shares <= 0)
        return Results.BadRequest(new { error = "Shares must be greater than zero." });
    if (request.Type is not ("Buy" or "Sell"))
        return Results.BadRequest(new { error = "Type must be Buy or Sell." });

    var tx = await txService.AddAsync(userId, request);
    return tx == null ? Results.NotFound() : Results.Created($"/api/portfolio/{userId}/transactions", tx);
});

app.MapPost("/api/portfolio/{userId:guid}/safeback", async (Guid userId, CreateSafeBackRequest request, TransactionService txService) =>
{
    if (request.AmountEur <= 0)
        return Results.BadRequest(new { error = "Amount must be greater than zero." });

    var tx = await txService.AddSafeBackAsync(userId, request);
    return tx == null ? Results.BadRequest(new { error = "Could not register SafeBack. Verify the item exists and has a current price." }) : Results.Created($"/api/portfolio/{userId}/transactions", tx);
});

app.MapPost("/api/portfolio/{userId:guid}/transfers", async (Guid userId, TransferRequest request, TransactionService txService) =>
{
    var (ok, error) = await txService.TransferAsync(userId, request);
    return ok ? Results.Ok() : Results.BadRequest(new { error });
});

app.MapGet("/api/portfolio/{userId:guid}/performance-detailed", async (Guid userId, TransactionService txService) =>
{
    var perf = await txService.ComputeDetailedPerformanceAsync(userId);
    return Results.Ok(perf);
});

app.MapGet("/health", () => Results.Ok());

app.Run();
