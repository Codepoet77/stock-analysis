using StockAnalysis.Api.Models.Backtest;

namespace StockAnalysis.Api.Models.Strategy;

public class StrategyRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StrategyId { get; set; }
    public Strategy Strategy { get; set; } = null!;

    public string Symbol { get; set; } = "";
    public string Label { get; set; } = "";            // e.g. "4H+1H" or "1H"

    // Timeframes resolved from Label
    public BacktestTimeframe SignalTimeframe { get; set; }
    public BacktestTimeframe? BiasTimeframe { get; set; }
    public BacktestTimeframe? ConfirmTimeframe { get; set; }

    // Signal filter — serialised as JSON array, e.g. ["OBRetest","FVGFill"]
    public string SignalTypesJson { get; set; } = "[]";

    // Trade parameters
    public ExitStrategy ExitStrategy { get; set; } = ExitStrategy.TrailingStop;
    public decimal FixedRRatio { get; set; } = 2.0m;
    public bool KillZoneOnly { get; set; }
    public string Direction { get; set; } = "Both";   // "Long" | "Short" | "Both"

    // Trading window in Eastern time — null on either end means no restriction
    public TimeSpan? TradingWindowStart { get; set; } = new TimeSpan(9, 30, 0);
    public TimeSpan? TradingWindowEnd   { get; set; } = new TimeSpan(16, 0, 0);

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<LiveSignal> Signals { get; set; } = [];
}
