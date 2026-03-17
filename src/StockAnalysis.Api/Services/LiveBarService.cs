using Microsoft.AspNetCore.SignalR;
using StockAnalysis.Api.Analysis;
using StockAnalysis.Api.Hubs;
using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Services;

/// <summary>
/// Background service that manages the Polygon WebSocket connection during market hours
/// and pushes fresh analysis to connected clients on each new bar.
/// </summary>
public class LiveBarService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHubContext<AnalysisHub> _hub;
    private readonly MarketStateService _marketState;
    private readonly LiveSignalEngine _signalEngine;
    private readonly ILogger<LiveBarService> _logger;
    private readonly Dictionary<string, List<Bar>> _barCache = new();
    private PolygonWebSocketClient? _wsClient;

    private static readonly string[] Symbols = ["SPY", "QQQ"];

    // Previous day OHLC per symbol — fetched once when streaming starts, valid all session
    private readonly Dictionary<string, (decimal Pdh, decimal Pdl, decimal Pdc)> _prevDay = new();

    public LiveBarService(
        IServiceProvider services,
        IHubContext<AnalysisHub> hub,
        MarketStateService marketState,
        LiveSignalEngine signalEngine,
        ILogger<LiveBarService> logger)
    {
        _services = services;
        _hub = hub;
        _marketState = marketState;
        _signalEngine = signalEngine;
        _logger = logger;

        foreach (var sym in Symbols)
            _barCache[sym] = [];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var session = _marketState.GetCurrentSession();
            bool shouldStream = session is MarketSession.PreMarket or MarketSession.RegularHours or MarketSession.AfterHours;

            if (shouldStream && _wsClient == null)
            {
                await StartStreamingAsync();
            }
            else if (!shouldStream && _wsClient != null)
            {
                await StopStreamingAsync();
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task StartStreamingAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PolygonWebSocketClient>>();
            var polygon = scope.ServiceProvider.GetRequiredService<PolygonRestClient>();

            // Cache previous day data — valid for the entire session, no need to re-fetch per bar
            foreach (var sym in Symbols)
            {
                var prev = await polygon.GetPreviousDayBarAsync(sym);
                _prevDay[sym] = (prev?.High ?? 0m, prev?.Low ?? 0m, prev?.Close ?? 0m);
            }

            _wsClient = new PolygonWebSocketClient(config, logger);
            _wsClient.OnBar += OnBarReceived;
            _wsClient.OnDisconnected += OnWebSocketDisconnected;
            await _wsClient.ConnectAsync(Symbols);
            _logger.LogInformation("Live streaming started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start WebSocket streaming");
            _wsClient = null;
        }
    }

    private async Task StopStreamingAsync()
    {
        if (_wsClient != null)
        {
            await _wsClient.DisconnectAsync();
            await _wsClient.DisposeAsync();
            _wsClient = null;
            _logger.LogInformation("Live streaming stopped");
        }
    }

    private void OnBarReceived(Bar bar)
    {
        if (!_barCache.ContainsKey(bar.Symbol)) return;

        _barCache[bar.Symbol].Add(bar);

        // Keep last 200 bars in memory
        if (_barCache[bar.Symbol].Count > 200)
            _barCache[bar.Symbol].RemoveAt(0);

        // Push the new bar to all connected clients
        _ = _hub.Clients.All.SendAsync("ReceiveBar", bar);

        // Forward to live signal engine for open signal resolution
        _signalEngine.OnBarReceived(bar);

        // Re-run ICT analysis and push updated dashboard during regular hours
        if (_marketState.GetCurrentSession() == MarketSession.RegularHours)
            _ = PushAnalysisAsync(bar);

        _logger.LogDebug("Bar received: {Symbol} @ {Close}", bar.Symbol, bar.Close);
    }

    private Task PushAnalysisAsync(Bar bar)
    {
        var bars = _barCache[bar.Symbol];
        if (bars.Count < 5) return Task.CompletedTask;

        _prevDay.TryGetValue(bar.Symbol, out var prev);

        var structure     = MarketStructureAnalyzer.Analyze(bars);
        var fvgs          = FairValueGapDetector.Detect(bars);
        var orderBlocks   = OrderBlockDetector.Detect(bars);
        var liquidity     = LiquidityLevelDetector.Detect(bars, prev.Pdh, prev.Pdl);

        var result = new AnalysisResult(
            Symbol:           bar.Symbol,
            AnalyzedAt:       DateTime.UtcNow,
            Session:          _marketState.GetCurrentSession(),
            CurrentPrice:     bar.Close,
            PreviousDayHigh:  prev.Pdh,
            PreviousDayLow:   prev.Pdl,
            PreviousDayClose: prev.Pdc,
            MarketStructure:  structure,
            FairValueGaps:    fvgs.Where(f => !f.IsFilled).TakeLast(5).ToList(),
            OrderBlocks:      orderBlocks.Where(ob => ob.IsValid).TakeLast(5).ToList(),
            LiquidityLevels:  liquidity,
            IsNyKillZone:     _marketState.IsNyKillZone(),
            RecentBars:       bars.TakeLast(50).ToList()
        );

        return _hub.Clients.All.SendAsync("ReceiveAnalysis", result);
    }

    private void OnWebSocketDisconnected()
    {
        _logger.LogWarning("Polygon WebSocket disconnected — will reconnect on next cycle");
        var dead = _wsClient;
        _wsClient = null;
        _ = dead?.DisposeAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopStreamingAsync();
        await base.StopAsync(cancellationToken);
    }
}
