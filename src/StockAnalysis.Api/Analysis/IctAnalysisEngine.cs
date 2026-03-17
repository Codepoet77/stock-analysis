using StockAnalysis.Api.Models;
using StockAnalysis.Api.Services;

namespace StockAnalysis.Api.Analysis;

public class IctAnalysisEngine
{
    private readonly PolygonRestClient _polygon;
    private readonly MarketStateService _marketState;

    public IctAnalysisEngine(PolygonRestClient polygon, MarketStateService marketState)
    {
        _polygon = polygon;
        _marketState = marketState;
    }

    public async Task<AnalysisResult> AnalyzeAsync(string symbol)
    {
        var session = _marketState.GetCurrentSession();
        var easternNow = _marketState.GetEasternNow();
        var isKillZone = _marketState.IsNyKillZone();

        // Fetch previous day data
        var prevDay = await _polygon.GetPreviousDayBarAsync(symbol);
        var pdh = prevDay?.High ?? 0m;
        var pdl = prevDay?.Low ?? 0m;
        var pdc = prevDay?.Close ?? 0m;

        // Fetch intraday bars — use today if market active, otherwise last trading day
        var targetDate = session == MarketSession.RegularHours || session == MarketSession.PreMarket
            ? easternNow.Date
            : GetLastTradingDay(easternNow.Date);

        var bars = await _polygon.GetIntradayBarsAsync(symbol, targetDate);

        // Get current price — snapshot for live, fallback to last bar close, then prev day close
        var currentPrice = await _polygon.GetCurrentPriceAsync(symbol)
            ?? bars.LastOrDefault()?.Close
            ?? pdc;

        // Run ICT analysis
        var structure = MarketStructureAnalyzer.Analyze(bars);
        var fvgs = FairValueGapDetector.Detect(bars);
        var orderBlocks = OrderBlockDetector.Detect(bars);
        var liquidityLevels = LiquidityLevelDetector.Detect(bars, pdh, pdl);

        // Only return recent unfilled FVGs (last 20 bars worth)
        var recentFvgs = fvgs
            .Where(f => !f.IsFilled)
            .TakeLast(5)
            .ToList();

        // Only return valid order blocks (last 5)
        var validObs = orderBlocks
            .Where(ob => ob.IsValid)
            .TakeLast(5)
            .ToList();

        return new AnalysisResult(
            Symbol: symbol,
            AnalyzedAt: DateTime.UtcNow,
            Session: session,
            CurrentPrice: currentPrice,
            PreviousDayHigh: pdh,
            PreviousDayLow: pdl,
            PreviousDayClose: pdc,
            MarketStructure: structure,
            FairValueGaps: recentFvgs,
            OrderBlocks: validObs,
            LiquidityLevels: liquidityLevels,
            IsNyKillZone: isKillZone,
            RecentBars: bars.TakeLast(50).ToList()
        );
    }

    private static DateTime GetLastTradingDay(DateTime date)
    {
        var d = date;
        // Step back until we land on a weekday
        do { d = d.AddDays(-1); }
        while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday);
        return d;
    }
}
