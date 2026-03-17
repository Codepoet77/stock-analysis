using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Api.Analysis;
using StockAnalysis.Api.Services;

namespace StockAnalysis.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    private readonly IctAnalysisEngine _engine;
    private readonly MarketStateService _marketState;

    public AnalysisController(IctAnalysisEngine engine, MarketStateService marketState)
    {
        _engine = engine;
        _marketState = marketState;
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetAnalysis(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("Symbol is required");

        var result = await _engine.AnalyzeAsync(symbol.ToUpper());
        return Ok(result);
    }

    [HttpGet("status")]
    public IActionResult GetMarketStatus()
    {
        var session = _marketState.GetCurrentSession();
        var isKillZone = _marketState.IsNyKillZone();
        return Ok(new { session = session.ToString(), isNyKillZone = isKillZone });
    }
}
