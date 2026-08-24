using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioTracker.Api.Data;
using PortfolioTracker.Api.Models;

namespace PortfolioTracker.Api.Services;

public class BackfillResultDto
{
    public List<BackfillItemResultDto> Items { get; set; } = [];
    public int TotalPointsInserted { get; set; }
}

public class BackfillItemResultDto
{
    public string Symbol { get; set; } = "";
    public string Source { get; set; } = "";
    public int PointsInserted { get; set; }
    public string Status { get; set; } = "";
}

// Preloads up to 12 months of daily position values so charts are useful immediately.
// Caveat: uses CURRENT share counts across the whole range (lots were merged into an
// average price), starting at the item's first purchase date.
public class HistoryBackfillService
{
    private readonly PortfolioDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly CurrencyService _currencyService;
    private readonly TransactionService _transactionService;
    private readonly ILogger<HistoryBackfillService> _logger;
    private readonly string _eodApiKey;
    private readonly string _twelveDataApiKey;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public HistoryBackfillService(PortfolioDbContext context, HttpClient httpClient, CurrencyService currencyService, TransactionService transactionService, IConfiguration configuration, ILogger<HistoryBackfillService> logger)
    {
        _context = context;
        _httpClient = httpClient;
        _currencyService = currencyService;
        _transactionService = transactionService;
        _logger = logger;
        _eodApiKey = configuration["EodApiKey"] ?? "";
        _twelveDataApiKey = configuration["TwelveDataApiKey"] ?? "";
    }

    public async Task<BackfillResultDto> BackfillAsync(Guid userId)
    {
        var result = new BackfillResultDto();

        // Replay needs real lot transactions; seed them first if missing.
        await _transactionService.EnsureSeededAsync(userId);

        var items = await _context.PortfolioItems
            .Where(p => p.UserId == userId)
            .ToListAsync();

        if (items.Count == 0)
            return result;

        // Existing points keyed by (date -> itemId -> value); totals tracked separately.
        var existing = await _context.PortfolioHistoryPoints
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var perItem = new Dictionary<(DateTime Date, Guid ItemId), decimal>();
        foreach (var p in existing)
        {
            perItem[(p.Date, p.ItemId)] = p.ValueEur;
        }

        var today = DateTime.UtcNow.Date;
        var usdToEur = await _currencyService.GetUsdToEurRateAsync();
        var newPoints = new List<PortfolioHistoryPoint>();

        // Real lot history per item, so historical share counts match what was actually held each day.
        var txsByItem = (await _context.PortfolioTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .ToListAsync())
            .GroupBy(t => t.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Date).ToList());

        foreach (var item in items)
        {
            var res = new BackfillItemResultDto { Symbol = item.Symbol };
            var fromDate = today.AddDays(-365);
            if (item.PurchaseDate.HasValue && item.PurchaseDate.Value.Date > fromDate)
                fromDate = item.PurchaseDate.Value.Date;

            var priorCount = perItem.Keys.Count(k => k.ItemId == item.Id);
            txsByItem.TryGetValue(item.Id, out var itemTxs);

            try
            {
                List<KeyValuePair<DateTime, decimal>> series;

                if (item.UseAlternativeSymbol && !string.IsNullOrWhiteSpace(item.AlternativeSymbol))
                {
                    res.Source = "EOD";
                    series = await FetchEodSeriesAsync(item.AlternativeSymbol, fromDate, today, usdToEur);
                    // Fund NAVs publish with a lag; carry the last known value forward.
                    series = ForwardFill(series, fromDate, today);
                }
                else if (item.Type.Equals("Fund", StringComparison.OrdinalIgnoreCase) &&
                         item.Symbol.StartsWith("0P", StringComparison.OrdinalIgnoreCase))
                {
                    // Fund without alternative symbol (no provider coverage): flat line at seeded SymbolPrices value.
                    var seeded = await _context.SymbolPrices
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Symbol == item.Symbol.ToUpperInvariant());
                    if (seeded == null)
                    {
                        res.Status = "skipped: no manual price";
                        result.Items.Add(res);
                        continue;
                    }
                    res.Source = "Manual";
                    series = BuildFlatSeries(fromDate, today, seeded.Price * usdToEur);
                }
                else
                {
                    res.Source = "TwelveData";
                    series = await FetchTwelveDataSeriesAsync(item.Symbol, fromDate, today, usdToEur);
                    if (series.Count == 0)
                    {
                        // Free TwelveData plans return only the latest bar for /eod; fall back to Yahoo.
                        res.Source = "Yahoo";
                        series = await FetchYahooSeriesAsync(item.Symbol, fromDate, today, usdToEur);
                    }
                    series = ForwardFill(series, fromDate, today);
                }

                // All provider series are price-per-share (EUR); multiply by the shares
                // actually held each day (transaction replay) to get position values.
                if (itemTxs is { Count: > 0 })
                {
                    series = series
                        .Select(kv =>
                        {
                            var held = itemTxs.Where(t => t.Date.Date <= kv.Key)
                                .Sum(t => t.Type switch
                                {
                                    "Buy" or "TransferIn" => t.Shares,
                                    "Sell" or "TransferOut" => -t.Shares,
                                    _ => 0m
                                });
                            return new KeyValuePair<DateTime, decimal>(kv.Key, kv.Value * held);
                        })
                        .Where(kv => kv.Value > 0)
                        .ToList();
                }

                // Never insert TODAY: its row belongs to the live dashboard snapshot
                // (forward-filled NAVs would freeze today's "Hoy" change at 0.00%).
                series = series.Where(kv => kv.Key < today).ToList();

                var inserted = 0;
                foreach (var (date, value) in series)
                {
                    if (value <= 0)
                        continue;
                    var key = (date, item.Id);
                    if (perItem.ContainsKey(key))
                        continue;
                    perItem[key] = value;
                    newPoints.Add(new PortfolioHistoryPoint { UserId = userId, ItemId = item.Id, Date = date, ValueEur = decimal.Round(value, 2) });
                    inserted++;
                }

                res.PointsInserted = inserted;
                res.Status = inserted > 0 ? "ok" : priorCount > 0 ? "up-to-date" : "no data";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backfill failed for {Symbol}", item.Symbol);
                res.Status = $"error: {ex.Message}";
            }

            result.Items.Add(res);
        }

        // Rebuild missing TOTAL points (ItemId == Guid.Empty) for every known date.
        var dates = perItem.Keys.Select(k => k.Date).Distinct();
        foreach (var date in dates)
        {
            var total = perItem.Where(kv => kv.Key.Date == date && kv.Key.ItemId != Guid.Empty).Sum(kv => kv.Value);
            if (total <= 0)
                continue;
            if (perItem.ContainsKey((date, Guid.Empty)))
                continue;
            newPoints.Add(new PortfolioHistoryPoint { UserId = userId, ItemId = Guid.Empty, Date = date, ValueEur = decimal.Round(total, 2) });
            result.TotalPointsInserted++;
        }

        if (newPoints.Count > 0)
        {
            _context.PortfolioHistoryPoints.AddRange(newPoints);
            await _context.SaveChangesAsync();
        }

        result.TotalPointsInserted += result.Items.Sum(i => i.PointsInserted);
        return result;
    }

    private async Task<List<KeyValuePair<DateTime, decimal>>> FetchEodSeriesAsync(string symbol, DateTime from, DateTime to, decimal usdToEur)
    {
        var url = $"https://eodhistoricaldata.com/api/eod/{Uri.EscapeDataString(symbol)}?api_token={_eodApiKey}&period=d&fmt=json" +
                  $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        _logger.LogInformation("Backfill EOD requesting {Symbol} from {From} to {To}", symbol, from, to);

        var json = await _httpClient.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var isEur = symbol.EndsWith(".EUFUND", StringComparison.OrdinalIgnoreCase);
        var list = new List<KeyValuePair<DateTime, decimal>>();

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var bar in doc.RootElement.EnumerateArray())
        {
            if (!bar.TryGetProperty("date", out var dateEl) || dateEl.ValueKind != JsonValueKind.String)
                continue;
            if (!DateTime.TryParse(dateEl.GetString(), Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                continue;

            JsonElement priceEl;
            if (!bar.TryGetProperty("adjusted_close", out priceEl) &&
                !bar.TryGetProperty("close", out priceEl))
                continue;

            var price = ParseDecimal(priceEl);
            if (price is null or <= 0)
                continue;

            // EOD returns the fund's native currency; .EUFUND is already EUR.
            // Price-per-share only; shares are applied later via transaction replay.
            var valueEur = isEur ? price.Value : price.Value * usdToEur;
            list.Add(new KeyValuePair<DateTime, decimal>(date, valueEur));
        }

        return list;
    }

    private async Task<List<KeyValuePair<DateTime, decimal>>> FetchTwelveDataSeriesAsync(string symbol, DateTime from, DateTime to, decimal usdToEur)
    {
        var url = $"https://api.twelvedata.com/eod?symbol={Uri.EscapeDataString(symbol)}&apikey={_twelveDataApiKey}" +
                  $"&start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}";
        _logger.LogInformation("Backfill TwelveData requesting {Symbol} from {From} to {To}", symbol, from, to);

        var json = await _httpClient.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var list = new List<KeyValuePair<DateTime, decimal>>();

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("values", out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Backfill TwelveData unexpected response for {Symbol}", symbol);
            return list;
        }

        foreach (var bar in values.EnumerateArray())
        {
            if (!bar.TryGetProperty("datetime", out var dateEl) || dateEl.ValueKind != JsonValueKind.String)
                continue;
            if (!DateTime.TryParse(dateEl.GetString(), Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                continue;
            if (!bar.TryGetProperty("close", out var priceEl))
                continue;

            var price = ParseDecimal(priceEl);
            if (price is null or <= 0)
                continue;

            list.Add(new KeyValuePair<DateTime, decimal>(date, price.Value * usdToEur));
        }

        return list;
    }

    private async Task<List<KeyValuePair<DateTime, decimal>>> FetchYahooSeriesAsync(string symbol, DateTime from, DateTime to, decimal usdToEur)
    {
        var period1 = ((DateTimeOffset)from).ToUnixTimeSeconds();
        var period2 = ((DateTimeOffset)to).ToUnixTimeSeconds();
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&period1={period1}&period2={period2}";
        _logger.LogInformation("Backfill Yahoo requesting {Symbol} from {From} to {To}", symbol, from, to);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Backfill Yahoo failed for {Symbol}: HTTP {Status}", symbol, (int)response.StatusCode);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var list = new List<KeyValuePair<DateTime, decimal>>();
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var resultArr) ||
            resultArr.ValueKind != JsonValueKind.Array ||
            resultArr.GetArrayLength() == 0)
        {
            return list;
        }

        var first = resultArr[0];
        if (!first.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
            return list;

        if (!first.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.ValueKind != JsonValueKind.Array ||
            quotes.GetArrayLength() == 0 ||
            !quotes[0].TryGetProperty("close", out var closes) ||
            closes.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        var n = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        for (var i = 0; i < n; i++)
        {
            var tsEl = timestamps[i];
            var closeEl = closes[i];
            if (tsEl.ValueKind != JsonValueKind.Number || closeEl.ValueKind != JsonValueKind.Number)
                continue;

            var date = DateTimeOffset.FromUnixTimeSeconds(tsEl.GetInt64()).UtcDateTime.Date;
            var close = closeEl.GetDecimal();
            if (close <= 0)
                continue;

            list.Add(new KeyValuePair<DateTime, decimal>(date, close * usdToEur));
        }

        return list;
    }

    // Carries the last known value forward over business days so every position
    // covers the same calendar window (unpublished NAV = previous NAV still valid).
    private static List<KeyValuePair<DateTime, decimal>> ForwardFill(List<KeyValuePair<DateTime, decimal>> series, DateTime from, DateTime to)
    {
        var result = new List<KeyValuePair<DateTime, decimal>>();
        if (series.Count == 0)
            return result;

        var map = new Dictionary<DateTime, decimal>();
        foreach (var kv in series)
            map[kv.Key] = kv.Value;

        decimal last = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            if (!map.TryGetValue(d, out var v))
            {
                if (last == 0)
                    continue;
                v = last;
            }

            last = v;
            result.Add(new KeyValuePair<DateTime, decimal>(d, v));
        }

        return result;
    }

    private static List<KeyValuePair<DateTime, decimal>> BuildFlatSeries(DateTime from, DateTime to, decimal priceEur)
    {
        var list = new List<KeyValuePair<DateTime, decimal>>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            list.Add(new KeyValuePair<DateTime, decimal>(d, priceEur));
        }
        return list;
    }

    public static decimal? ParseDecimal(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Number, Inv, out var v) => v,
            _ => null
        };
    }
}
