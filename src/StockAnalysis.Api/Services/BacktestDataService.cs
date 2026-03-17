using System.Text.Json;
using StockAnalysis.Api.Models;
using StockAnalysis.Api.Models.Backtest;

namespace StockAnalysis.Api.Services;

public class BacktestDataService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<BacktestDataService> _logger;

    public BacktestDataService(HttpClient http, IConfiguration config, ILogger<BacktestDataService> logger)
    {
        _http = http;
        _apiKey = config["Polygon:ApiKey"] ?? throw new InvalidOperationException("Polygon:ApiKey not configured");
        _logger = logger;
    }

    public async Task<List<Bar>> FetchAsync(
        string symbol,
        BacktestTimeframe timeframe,
        DateTime from,
        DateTime to,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var (multiplier, timespan) = TimeframeHelper.ToPolygon(timeframe);
        var label = TimeframeHelper.Label(timeframe);
        var chunkMonths = TimeframeHelper.FetchChunkMonths(timeframe);
        var allBars = new List<Bar>();
        var cursor = from;

        while (cursor < to && !ct.IsCancellationRequested)
        {
            var chunkEnd = cursor.AddMonths(chunkMonths);
            if (chunkEnd > to) chunkEnd = to;

            progress?.Report($"Fetching {symbol} {label} {cursor:MMM yyyy}…");
            _logger.LogInformation("Fetching {Symbol} {Label} {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
                symbol, label, cursor, chunkEnd);

            var bars = await FetchChunkAsync(symbol, multiplier, timespan, cursor, chunkEnd, ct);
            allBars.AddRange(bars);

            cursor = chunkEnd.AddDays(1);

            // Respect Polygon rate limits
            await Task.Delay(250, ct);
        }

        return allBars.OrderBy(b => b.Time).DistinctBy(b => b.Time).ToList();
    }

    private async Task<List<Bar>> FetchChunkAsync(
        string symbol, int multiplier, string timespan,
        DateTime from, DateTime to,
        CancellationToken ct)
    {
        var allBars = new List<Bar>();
        var url = BuildUrl(symbol, multiplier, timespan, from, to);

        while (url != null && !ct.IsCancellationRequested)
        {
            try
            {
                var response = await _http.GetStringAsync(url, ct);
                var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    foreach (var r in results.EnumerateArray())
                    {
                        allBars.Add(new Bar(
                            symbol,
                            DateTimeOffset.FromUnixTimeMilliseconds(r.GetProperty("t").GetInt64()).UtcDateTime,
                            r.GetProperty("o").GetDecimal(),
                            r.GetProperty("h").GetDecimal(),
                            r.GetProperty("l").GetDecimal(),
                            r.GetProperty("c").GetDecimal(),
                            (long)r.GetProperty("v").GetDouble()
                        ));
                    }
                }

                // Follow next_url pagination if present
                url = doc.RootElement.TryGetProperty("next_url", out var nextUrl)
                    ? $"{nextUrl.GetString()}&apiKey={_apiKey}"
                    : null;

                if (url != null) await Task.Delay(200, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching chunk");
                break;
            }
        }

        return allBars;
    }

    private string BuildUrl(string symbol, int multiplier, string timespan, DateTime from, DateTime to) =>
        $"https://api.polygon.io/v2/aggs/ticker/{symbol}/range/{multiplier}/{timespan}" +
        $"/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}?adjusted=true&sort=asc&limit=50000&apiKey={_apiKey}";
}
