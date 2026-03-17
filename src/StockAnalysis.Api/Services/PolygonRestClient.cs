using System.Text.Json;
using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Services;

public class PolygonRestClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<PolygonRestClient> _logger;

    public PolygonRestClient(HttpClient http, IConfiguration config, ILogger<PolygonRestClient> logger)
    {
        _http = http;
        _apiKey = config["Polygon:ApiKey"] ?? throw new InvalidOperationException("Polygon:ApiKey not configured");
        _logger = logger;
    }

    public async Task<List<Bar>> GetIntradayBarsAsync(string symbol, DateTime date, int minuteMultiplier = 1)
    {
        var from = date.ToString("yyyy-MM-dd");
        var to = date.ToString("yyyy-MM-dd");
        var url = $"https://api.polygon.io/v2/aggs/ticker/{symbol}/range/{minuteMultiplier}/minute/{from}/{to}?adjusted=true&sort=asc&limit=500&apiKey={_apiKey}";

        try
        {
            var response = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("results", out var results))
                return [];

            return results.EnumerateArray().Select(r => new Bar(
                symbol,
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetProperty("t").GetInt64()).UtcDateTime,
                r.GetProperty("o").GetDecimal(),
                r.GetProperty("h").GetDecimal(),
                r.GetProperty("l").GetDecimal(),
                r.GetProperty("c").GetDecimal(),
                (long)r.GetProperty("v").GetDouble()
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch intraday bars for {Symbol}", symbol);
            return [];
        }
    }

    public async Task<Bar?> GetPreviousDayBarAsync(string symbol)
    {
        var url = $"https://api.polygon.io/v2/aggs/ticker/{symbol}/prev?adjusted=true&apiKey={_apiKey}";

        try
        {
            var response = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("results", out var results))
                return null;

            var r = results.EnumerateArray().FirstOrDefault();
            if (r.ValueKind == JsonValueKind.Undefined) return null;

            return new Bar(
                symbol,
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetProperty("t").GetInt64()).UtcDateTime,
                r.GetProperty("o").GetDecimal(),
                r.GetProperty("h").GetDecimal(),
                r.GetProperty("l").GetDecimal(),
                r.GetProperty("c").GetDecimal(),
                (long)r.GetProperty("v").GetDouble()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch previous day bar for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<decimal?> GetCurrentPriceAsync(string symbol)
    {
        var url = $"https://api.polygon.io/v2/snapshot/locale/us/markets/stocks/tickers/{symbol}?apiKey={_apiKey}";

        try
        {
            var response = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("ticker", out var ticker)) return null;
            if (!ticker.TryGetProperty("lastTrade", out var lastTrade)) return null;

            return lastTrade.GetProperty("p").GetDecimal();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch current price for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<MarketStatusResult> GetMarketStatusAsync()
    {
        var url = $"https://api.polygon.io/v1/marketstatus/now?apiKey={_apiKey}";

        try
        {
            var response = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(response);
            var market = doc.RootElement.GetProperty("market").GetString() ?? "closed";
            return new MarketStatusResult(market == "open", market);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch market status");
            return new MarketStatusResult(false, "unknown");
        }
    }
}

public record MarketStatusResult(bool IsOpen, string Status);
