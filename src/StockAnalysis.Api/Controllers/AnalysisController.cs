using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Api.Analysis;
using StockAnalysis.Api.Models.Backtest;
using StockAnalysis.Api.Services;

namespace StockAnalysis.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    private readonly IctAnalysisEngine _engine;
    private readonly MarketStateService _marketState;
    private readonly BacktestDataService _dataService;

    public AnalysisController(IctAnalysisEngine engine, MarketStateService marketState, BacktestDataService dataService)
    {
        _engine = engine;
        _marketState = marketState;
        _dataService = dataService;
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetAnalysis(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("Symbol is required");

        var result = await _engine.AnalyzeAsync(symbol.ToUpper());
        return Ok(result);
    }

    [HttpGet("{symbol}/bars")]
    public async Task<IActionResult> GetBars(string symbol, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.Date.AddHours(4); // default: 4 AM UTC (~premarket start ET)
        var bars = await _dataService.FetchAsync(symbol.ToUpper(), BacktestTimeframe.OneMinute, start, end);
        return Ok(bars);
    }

    [HttpGet("status")]
    public IActionResult GetMarketStatus()
    {
        var session = _marketState.GetCurrentSession();
        var isKillZone = _marketState.IsNyKillZone();
        return Ok(new { session = session.ToString(), isNyKillZone = isKillZone });
    }
}
