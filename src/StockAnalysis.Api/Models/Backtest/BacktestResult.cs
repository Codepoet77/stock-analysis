namespace StockAnalysis.Api.Models.Backtest;

public record BacktestProgressUpdate(
    string Stage,
    string Detail,
    int PercentComplete
);

public record BacktestResult(
    BacktestParameters Parameters,
    DateTime StartedAt,
    DateTime CompletedAt,
    List<BacktestStats> IndividualStats,
    List<BacktestStats> CombinationStats,
    List<BacktestSignalOutcome> SampleSignals,
    int TotalSignalsAnalyzed
)
{
    public BacktestStats? BestIndividual =>
        IndividualStats.OrderByDescending(s => s.ExpectedValue).FirstOrDefault();

    public BacktestStats? BestCombination =>
        CombinationStats.OrderByDescending(s => s.ExpectedValue).FirstOrDefault();
}
