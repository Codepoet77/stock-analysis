using StockAnalysis.Api.Models.Backtest;

namespace StockAnalysis.Api.Models.Strategy;

public enum LiveSignalStatus { Open, Win, Loss, Scratch, Expired }

public class LiveSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StrategyRuleId { get; set; }
    public StrategyRule Rule { get; set; } = null!;

    public string Symbol { get; set; } = "";
    public string Direction { get; set; } = "";       // "Long" | "Short"
    public string SignalType { get; set; } = "";
    public string TimeframeLabel { get; set; } = "";

    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Target { get; set; }
    public decimal Stop { get; set; }
    public ExitStrategy ExitStrategy { get; set; }

    public LiveSignalStatus Status { get; set; } = LiveSignalStatus.Open;
    public DateTime? OutcomeTime { get; set; }
    public decimal? ActualExitPrice { get; set; }
    public decimal? ActualRR { get; set; }

    // Mutable stop for trailing/breakeven — updated in memory and persisted on resolution
    public decimal CurrentStop { get; set; }
    public bool BreakevenActivated { get; set; }
}
