using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockAnalysis.Api.Data;
using StockAnalysis.Api.Models.Backtest;
using StockAnalysis.Api.Models.Strategy;

namespace StockAnalysis.Api.Services;

public class StrategyService(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ── Strategies ────────────────────────────────────────────────────────────

    public async Task<List<Strategy>> GetStrategiesAsync(string userId)
        => await db.Strategies
            .Where(s => s.UserId == userId)
            .Include(s => s.Rules)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

    /// <summary>Used by LiveSignalEngine to evaluate all users' active rules.</summary>
    public async Task<List<Strategy>> GetStrategiesAsync_AllUsers()
        => await db.Strategies
            .Where(s => s.IsActive)
            .Include(s => s.Rules.Where(r => r.IsActive))
            .ToListAsync();

    public async Task<Strategy> CreateStrategyAsync(string userId, string name)
    {
        var strategy = new Strategy { UserId = userId, Name = name };
        db.Strategies.Add(strategy);
        await db.SaveChangesAsync();
        return strategy;
    }

    public async Task<bool> DeleteStrategyAsync(string userId, Guid strategyId)
    {
        var rows = await db.Strategies
            .Where(s => s.UserId == userId && s.Id == strategyId)
            .ExecuteDeleteAsync();
        return rows > 0;
    }

    public async Task<bool> ToggleStrategyAsync(string userId, Guid strategyId, bool isActive)
    {
        var rows = await db.Strategies
            .Where(s => s.UserId == userId && s.Id == strategyId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, isActive));
        return rows > 0;
    }

    // ── Rules ─────────────────────────────────────────────────────────────────

    public async Task<StrategyRule?> AddRuleAsync(Guid strategyId, AddRuleRequest req)
    {
        // Prevent duplicate: same symbol + label + exitStrategy + overlapping direction + same trading window
        bool exists = await db.StrategyRules.AnyAsync(r =>
            r.StrategyId == strategyId &&
            r.Symbol == req.Symbol &&
            r.Label == req.Label &&
            r.ExitStrategy == req.ExitStrategy &&
            (r.Direction == req.Direction || r.Direction == "Both" || req.Direction == "Both") &&
            r.TradingWindowStart == req.TradingWindowStart &&
            r.TradingWindowEnd   == req.TradingWindowEnd);
        if (exists) return null;

        var (signal, bias, confirm) = ResolveTimeframes(req.Label);
        var rule = new StrategyRule
        {
            StrategyId      = strategyId,
            Symbol          = req.Symbol,
            Label           = req.Label,
            SignalTimeframe = signal,
            BiasTimeframe   = bias,
            ConfirmTimeframe = confirm,
            SignalTypesJson = JsonSerializer.Serialize(req.SignalTypes ?? [], JsonOpts),
            ExitStrategy    = req.ExitStrategy,
            FixedRRatio     = req.FixedRRatio,
            KillZoneOnly        = req.KillZoneOnly,
            Direction           = req.Direction,
            TradingWindowStart  = req.TradingWindowStart,
            TradingWindowEnd    = req.TradingWindowEnd,
        };
        db.StrategyRules.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    public async Task<bool> DeleteRuleAsync(Guid ruleId)
    {
        var rows = await db.StrategyRules.Where(r => r.Id == ruleId).ExecuteDeleteAsync();
        return rows > 0;
    }

    public async Task<bool> ToggleRuleAsync(Guid ruleId, bool isActive)
    {
        var rows = await db.StrategyRules
            .Where(r => r.Id == ruleId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, isActive));
        return rows > 0;
    }

    // ── Live signals ──────────────────────────────────────────────────────────

    public async Task<LiveSignal> CreateSignalAsync(LiveSignal signal)
    {
        db.LiveSignals.Add(signal);
        await db.SaveChangesAsync();
        return signal;
    }

    public async Task ResolveSignalAsync(Guid signalId, LiveSignalStatus status,
        decimal exitPrice, decimal actualRR)
    {
        await db.LiveSignals
            .Where(s => s.Id == signalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.OutcomeTime, DateTime.UtcNow)
                .SetProperty(x => x.ActualExitPrice, exitPrice)
                .SetProperty(x => x.ActualRR, actualRR)
                .SetProperty(x => x.CurrentStop, exitPrice));
    }

    public async Task UpdateStopAsync(Guid signalId, decimal currentStop, bool breakevenActivated)
    {
        await db.LiveSignals
            .Where(s => s.Id == signalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentStop, currentStop)
                .SetProperty(x => x.BreakevenActivated, breakevenActivated));
    }

    public async Task<List<LiveSignal>> GetOpenSignalsAsync()
        => await db.LiveSignals
            .Include(s => s.Rule)
            .Where(s => s.Status == LiveSignalStatus.Open)
            .ToListAsync();

    public async Task<List<LiveSignal>> GetRecentSignalsAsync(Guid strategyId, int limit = 50)
        => await db.LiveSignals
            .Include(s => s.Rule)
            .Where(s => s.Rule.StrategyId == strategyId)
            .OrderByDescending(s => s.EntryTime)
            .Take(limit)
            .ToListAsync();

    /// <summary>Mark open signals older than the cutoff as expired (handles server restarts).</summary>
    public async Task ExpireStaleSignalsAsync(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        await db.LiveSignals
            .Where(s => s.Status == LiveSignalStatus.Open && s.EntryTime < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, LiveSignalStatus.Expired)
                .SetProperty(x => x.OutcomeTime, DateTime.UtcNow));
    }

    // ── Performance ───────────────────────────────────────────────────────────

    public async Task<StrategyPerformance> GetPerformanceAsync(Guid strategyId)
    {
        var signals = await db.LiveSignals
            .Include(s => s.Rule)
            .Where(s => s.Rule.StrategyId == strategyId)
            .ToListAsync();

        var resolved = signals.Where(s => s.Status != LiveSignalStatus.Open && s.Status != LiveSignalStatus.Expired).ToList();
        var wins     = resolved.Count(s => s.Status == LiveSignalStatus.Win);
        var losses   = resolved.Count(s => s.Status == LiveSignalStatus.Loss);
        var scratches = resolved.Count(s => s.Status == LiveSignalStatus.Scratch);
        var decided  = wins + losses;
        var winRate  = decided > 0 ? (decimal)wins / decided : 0m;
        var avgRR    = resolved.Count > 0 ? resolved.Average(s => s.ActualRR ?? 0m) : 0m;
        var grossWin  = resolved.Where(s => s.Status == LiveSignalStatus.Win).Sum(s => s.ActualRR ?? 0m);
        var grossLoss = resolved.Where(s => s.Status == LiveSignalStatus.Loss).Sum(s => Math.Abs(s.ActualRR ?? 0m));
        var pf = grossLoss > 0 ? grossWin / grossLoss : grossWin > 0 ? 999m : 0m;
        var ev = winRate * avgRR - (1m - winRate);

        return new StrategyPerformance(
            TotalSignals: signals.Count,
            OpenSignals: signals.Count(s => s.Status == LiveSignalStatus.Open),
            Wins: wins,
            Losses: losses,
            Scratches: scratches,
            WinRate: Math.Round(winRate, 4),
            AverageRR: Math.Round(avgRR, 2),
            ExpectedValue: Math.Round(ev, 2),
            ProfitFactor: Math.Round(pf, 2),
            LastSignalAt: signals.MaxBy(s => s.EntryTime)?.EntryTime
        );
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public List<EnabledSignalType> DeserializeSignalTypes(string json)
    {
        try { return JsonSerializer.Deserialize<List<EnabledSignalType>>(json, JsonOpts) ?? []; }
        catch { return []; }
    }

    public static (BacktestTimeframe Signal, BacktestTimeframe? Bias, BacktestTimeframe? Confirm)
        ResolveTimeframes(string label)
    {
        var combo = TimeframeCombination.All.FirstOrDefault(c => c.Label == label);
        if (combo != null) return (combo.SignalTf, combo.BiasTf, combo.ConfirmTf);

        var tf = label switch
        {
            "1M"  => BacktestTimeframe.OneMinute,
            "5M"  => BacktestTimeframe.FiveMinute,
            "15M" => BacktestTimeframe.FifteenMinute,
            "1H"  => BacktestTimeframe.OneHour,
            "4H"  => BacktestTimeframe.FourHour,
            _     => BacktestTimeframe.OneHour
        };
        return (tf, null, null);
    }
}

public record AddRuleRequest(
    string Symbol,
    string Label,
    ExitStrategy ExitStrategy,
    decimal FixedRRatio,
    bool KillZoneOnly,
    string Direction,
    List<EnabledSignalType>? SignalTypes,
    TimeSpan? TradingWindowStart,
    TimeSpan? TradingWindowEnd
);
