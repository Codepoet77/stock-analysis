namespace StockAnalysis.Api.Models.Backtest;

public record SignalTypeStats(
    int Total,
    int Wins,
    int Losses,
    decimal WinRate,
    decimal AverageRR
);

public record BacktestStats(
    string Label,
    string Symbol,
    ExitStrategy ExitStrategy,
    int TotalSignals,
    int Wins,
    int Losses,
    int Scratches,
    decimal WinRate,
    decimal AverageRR,
    decimal ProfitFactor,
    decimal ExpectedValue,
    Dictionary<string, SignalTypeStats> BySignalType,
    Dictionary<string, SignalTypeStats> BySession
)
{
    public static BacktestStats Compute(
        string label,
        string symbol,
        ExitStrategy strategy,
        IEnumerable<BacktestSignalOutcome> outcomes)
    {
        var list = outcomes.ToList();
        if (list.Count == 0)
            return new BacktestStats(label, symbol, strategy, 0, 0, 0, 0, 0, 0, 0, 0,
                new Dictionary<string, SignalTypeStats>(),
                new Dictionary<string, SignalTypeStats>());

        var wins    = list.Count(o => o.Outcome == BacktestOutcome.Win);
        var losses  = list.Count(o => o.Outcome == BacktestOutcome.Loss);
        var scratches = list.Count(o => o.Outcome == BacktestOutcome.Scratch);
        var decided   = wins + losses; // scratches excluded from rate calculations
        var winRate   = decided > 0 ? (decimal)wins / decided : 0m;
        var avgRR     = list.Any() ? list.Average(o => o.ActualRR) : 0m;

        var grossWin  = list.Where(o => o.Outcome == BacktestOutcome.Win).Sum(o => o.ActualRR);
        var grossLoss = list.Where(o => o.Outcome == BacktestOutcome.Loss).Sum(o => Math.Abs(o.ActualRR));
        var pf = grossLoss > 0 ? grossWin / grossLoss : grossWin > 0 ? 999m : 0m;

        var ev = winRate * avgRR - (1 - winRate);

        var bySignalType = list
            .GroupBy(o => o.SignalType.ToString())
            .ToDictionary(g => g.Key, g => ComputeSignalStats(g));

        var bySession = new Dictionary<string, SignalTypeStats>
        {
            ["KillZone"]    = ComputeSignalStats(list.Where(o => o.DuringKillZone)),
            ["NonKillZone"] = ComputeSignalStats(list.Where(o => !o.DuringKillZone))
        };

        return new BacktestStats(label, symbol, strategy, list.Count, wins, losses, scratches,
            Math.Round(winRate, 4), Math.Round(avgRR, 2), Math.Round(pf, 2), Math.Round(ev, 2),
            bySignalType, bySession);
    }

    private static SignalTypeStats ComputeSignalStats(IEnumerable<BacktestSignalOutcome> outcomes)
    {
        var list = outcomes.ToList();
        if (list.Count == 0) return new SignalTypeStats(0, 0, 0, 0, 0);
        var wins = list.Count(o => o.Outcome == BacktestOutcome.Win);
        var losses = list.Count(o => o.Outcome == BacktestOutcome.Loss);
        var decided = wins + losses;
        return new SignalTypeStats(
            list.Count, wins, losses,
            decided > 0 ? Math.Round((decimal)wins / decided, 4) : 0m,
            Math.Round(list.Average(o => o.ActualRR), 2));
    }
}
