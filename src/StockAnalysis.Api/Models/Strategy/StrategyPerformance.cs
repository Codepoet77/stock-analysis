namespace StockAnalysis.Api.Models.Strategy;

public record StrategyPerformance(
    int TotalSignals,
    int OpenSignals,
    int Wins,
    int Losses,
    int Scratches,
    decimal WinRate,       // wins / (wins + losses)
    decimal AverageRR,
    decimal ExpectedValue,
    decimal ProfitFactor,
    DateTime? LastSignalAt
);
