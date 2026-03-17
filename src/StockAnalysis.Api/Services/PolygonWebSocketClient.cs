using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Services;

public class PolygonWebSocketClient : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private readonly string _apiKey;
    private readonly ILogger<PolygonWebSocketClient> _logger;
    private CancellationTokenSource? _cts;

    public event Action<Bar>? OnBar;
    public event Action? OnDisconnected;

    public PolygonWebSocketClient(IConfiguration config, ILogger<PolygonWebSocketClient> logger)
    {
        _apiKey = config["Polygon:ApiKey"] ?? throw new InvalidOperationException("Polygon:ApiKey not configured");
        _logger = logger;
    }

    public async Task ConnectAsync(IEnumerable<string> symbols)
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        await _ws.ConnectAsync(new Uri("wss://socket.polygon.io/stocks"), _cts.Token);
        _logger.LogInformation("Connected to Polygon WebSocket");

        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        // Wait for connected message then auth
        await Task.Delay(500);
        await SendAsync(JsonSerializer.Serialize(new { action = "auth", @params = _apiKey }));
        await Task.Delay(500);

        // Subscribe to per-minute aggregates
        var subs = string.Join(",", symbols.Select(s => $"AM.{s}"));
        await SendAsync(JsonSerializer.Serialize(new { action = "subscribe", @params = subs }));
        _logger.LogInformation("Subscribed to: {Subscriptions}", subs);
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(json);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket receive error");
                break;
            }
        }

        OnDisconnected?.Invoke();
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var messages = JsonDocument.Parse(json);
            foreach (var msg in messages.RootElement.EnumerateArray())
            {
                if (!msg.TryGetProperty("ev", out var ev)) continue;
                if (ev.GetString() != "AM") continue;

                var symbol = msg.GetProperty("sym").GetString() ?? "";
                var bar = new Bar(
                    symbol,
                    DateTimeOffset.FromUnixTimeMilliseconds(msg.GetProperty("s").GetInt64()).UtcDateTime,
                    msg.GetProperty("o").GetDecimal(),
                    msg.GetProperty("h").GetDecimal(),
                    msg.GetProperty("l").GetDecimal(),
                    msg.GetProperty("c").GetDecimal(),
                    msg.GetProperty("v").GetInt64()
                );

                OnBar?.Invoke(bar);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process WebSocket message");
        }
    }

    private async Task SendAsync(string message)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
