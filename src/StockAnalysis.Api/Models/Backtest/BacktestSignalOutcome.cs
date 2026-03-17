namespace StockAnalysis.Api.Models.Backtest;

public enum BacktestOutcome { Win, Loss, Scratch }

public record BacktestSignalOutcome(
    string Symbol,
    string TimeframeLabel,
    string? CombinationLabel,
    SignalType SignalType,
    SignalDirection Direction,
    ExitStrategy ExitStrategy,
    DateTime EntryTime,
    decimal EntryPrice,
    decimal Target,
    decimal Invalidation,
    BacktestOutcome Outcome,
    decimal ActualRR,
    int BarsToOutcome,
    bool DuringKillZone
);
