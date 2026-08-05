using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using PortfolioTracker.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8081");

var apiBaseUrl = Environment.GetEnvironmentVariable("ApiBaseUrl") ?? "http://localhost:8080";
Console.WriteLine($"ApiBaseUrl: {apiBaseUrl}");

builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<PortfolioApiService>();
builder.Services.AddScoped<UserIdService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseExceptionHandler("/Error");
app.UseHsts();
app.UseAntiforgery();

app.MapRazorComponents<PortfolioTracker.Blazor.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
