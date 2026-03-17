using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StockAnalysis.Api.Analysis;
using StockAnalysis.Api.Models.Backtest;
using StockAnalysis.Api.Models.Strategy;
using StockAnalysis.Api.Services;

namespace StockAnalysis.Api.Hubs;

[Authorize]
public class AnalysisHub : Hub
{
    private readonly IctAnalysisEngine _engine;
    private readonly BacktestJobService _jobs;
    private readonly BacktestSessionService _sessions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AnalysisHub> _hubContext;
    private readonly ILogger<AnalysisHub> _logger;

    public AnalysisHub(
        IctAnalysisEngine engine,
        BacktestJobService jobs,
        BacktestSessionService sessions,
        IServiceScopeFactory scopeFactory,
        IHubContext<AnalysisHub> hubContext,
        ILogger<AnalysisHub> logger)
    {
        _engine = engine;
        _jobs = jobs;
        _sessions = sessions;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? Context.User?.FindFirstValue(ClaimTypes.Email)
                          ?? Context.ConnectionId;

    private string UserGroup => $"backtest-{UserId}";

    // ── Connection lifecycle ──────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup);
        await base.OnConnectedAsync();

        // Replay current job state to this connection
        var job = await _jobs.GetLatestJobAsync(UserId);
        if (job is null) return;

        if (job.Status == Data.BacktestJobStatus.Running)
        {
            var progress = _jobs.DeserializeProgress(job.ProgressJson)
                ?? new BacktestProgressUpdate("Running", "Backtest in progress…", 0);
            await Clients.Caller.SendAsync("BacktestProgress", progress);
        }
        else if (job.Status == Data.BacktestJobStatus.Completed && job.ResultJson is not null)
        {
            var result = _jobs.DeserializeResult(job.ResultJson);
            if (result is not null)
                await Clients.Caller.SendAsync("BacktestComplete", result);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup);
        await base.OnDisconnectedAsync(exception);
    }

    // ── Analysis ─────────────────────────────────────────────────────────────

    public async Task RequestAnalysis(string symbol)
    {
        _logger.LogInformation("Analysis requested for {Symbol} by {UserId}", symbol, UserId);
        var result = await _engine.AnalyzeAsync(symbol.ToUpper());
        await Clients.Caller.SendAsync("ReceiveAnalysis", result);
    }

    public async Task RequestAnalysisBoth()
    {
        var spy = await _engine.AnalyzeAsync("SPY");
        var qqq = await _engine.AnalyzeAsync("QQQ");
        await Clients.Caller.SendAsync("ReceiveAnalysis", spy);
        await Clients.Caller.SendAsync("ReceiveAnalysis", qqq);
    }

    // ── Backtest ──────────────────────────────────────────────────────────────

    public async Task StartBacktest(BacktestParameters parameters)
    {
        var existing = await _jobs.GetRunningJobAsync(UserId);
        if (existing is not null)
        {
            var progress = _jobs.DeserializeProgress(existing.ProgressJson)
                ?? new BacktestProgressUpdate("Running", "Backtest in progress…", 0);
            await Clients.Caller.SendAsync("BacktestProgress", progress);
            return;
        }

        var userEmail = Context.User?.FindFirstValue(ClaimTypes.Email) ?? "";
        var userId = UserId;
        var userGroup = UserGroup;
        var job = await _jobs.CreateJobAsync(userId, userEmail, parameters);
        var ct = _sessions.CreateSession(userId);

        _logger.LogInformation("Backtest {JobId} started by {UserId}", job.Id, userId);

        var scope = _scopeFactory.CreateScope();
        _ = Task.Run(async () =>
        {
            using (scope)
            {
                var jobs = scope.ServiceProvider.GetRequiredService<BacktestJobService>();
                var engine = scope.ServiceProvider.GetRequiredService<BacktestEngine>();
                try
                {
                    var result = await engine.RunAsync(parameters, job, userId, ct);
                    await jobs.CompleteJobAsync(job.Id, result);
                    _sessions.RemoveSession(userId);
                    await _hubContext.Clients.Group(userGroup).SendAsync("BacktestComplete", result);
                }
                catch (OperationCanceledException)
                {
                    await jobs.CancelJobAsync(job.Id);
                    _sessions.RemoveSession(userId);
                    await _hubContext.Clients.Group(userGroup).SendAsync("BacktestCancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backtest {JobId} failed", job.Id);
                    await jobs.FailJobAsync(job.Id, ex.Message);
                    _sessions.RemoveSession(userId);
                    await _hubContext.Clients.Group(userGroup).SendAsync("BacktestError", ex.Message);
                }
            }
        });
    }

    public async Task CancelBacktest()
    {
        _sessions.CancelSession(UserId);
        var job = await _jobs.GetRunningJobAsync(UserId);
        if (job is not null)
            await _jobs.CancelJobAsync(job.Id);
        _logger.LogInformation("Backtest cancelled by {UserId}", UserId);
    }

    public async Task GetBacktestHistory()
    {
        var history = await _jobs.GetHistoryAsync(UserId);
        await Clients.Caller.SendAsync("BacktestHistory", history.Select(j =>
        {
            var result = _jobs.DeserializeResult(j.ResultJson);
            return new
            {
                j.Id,
                j.CreatedAt,
                j.CompletedAt,
                Parameters = _jobs.DeserializeParameters(j.ParametersJson),
                TotalSignalsAnalyzed = result?.TotalSignalsAnalyzed ?? 0,
                BestIndividual = result?.BestIndividual,
            };
        }));
    }

    public async Task GetBacktestResult(Guid jobId)
    {
        var job = await _jobs.GetJobAsync(UserId, jobId);
        if (job?.ResultJson is null) return;
        var result = _jobs.DeserializeResult(job.ResultJson);
        if (result is not null)
            await Clients.Caller.SendAsync("BacktestResultLoaded", result);
    }

    // ── Strategies ────────────────────────────────────────────────────────────

    public async Task GetStrategies()
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        var strategies = await svc.GetStrategiesAsync(UserId);

        var dtos = new List<object>();
        foreach (var s in strategies)
        {
            var perf    = await svc.GetPerformanceAsync(s.Id);
            var signals = await svc.GetRecentSignalsAsync(s.Id, 50);
            dtos.Add(MapStrategyDto(s, perf, signals, svc));
        }
        await Clients.Caller.SendAsync("StrategiesLoaded", dtos);
    }

    public async Task CreateStrategy(string name)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        var strategy = await svc.CreateStrategyAsync(UserId, name);
        var perf = await svc.GetPerformanceAsync(strategy.Id);
        await Clients.Caller.SendAsync("StrategyCreated", MapStrategyDto(strategy, perf, [], svc));
    }

    public async Task AddRule(Guid strategyId, AddRuleRequest request)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();

        // Verify ownership
        var strategies = await svc.GetStrategiesAsync(UserId);
        if (!strategies.Any(s => s.Id == strategyId)) return;

        var rule = await svc.AddRuleAsync(strategyId, request);
        if (rule is null)
        {
            await Clients.Caller.SendAsync("RuleAddError", "A rule with the same symbol, timeframe, exit strategy, and direction already exists in this strategy.");
            return;
        }

        // Subscribe this connection to strategy group for live signal events
        await Groups.AddToGroupAsync(Context.ConnectionId, $"strategy-{strategyId}");

        await Clients.Caller.SendAsync("RuleAdded", new
        {
            strategyId,
            rule.Id,
            rule.Symbol,
            rule.Label,
            rule.Direction,
            rule.ExitStrategy,
            rule.FixedRRatio,
            rule.KillZoneOnly,
            rule.SignalTypesJson,
            rule.TradingWindowStart,
            rule.TradingWindowEnd,
            rule.IsActive,
            rule.CreatedAt,
        });
    }

    public async Task RemoveRule(Guid ruleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        await svc.DeleteRuleAsync(ruleId);
        await Clients.Caller.SendAsync("RuleRemoved", ruleId);
    }

    public async Task ToggleStrategy(Guid strategyId, bool isActive)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        await svc.ToggleStrategyAsync(UserId, strategyId, isActive);
        await Clients.Caller.SendAsync("StrategyToggled", new { strategyId, isActive });
    }

    public async Task DeleteStrategy(Guid strategyId)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        await svc.DeleteStrategyAsync(UserId, strategyId);
        await Clients.Caller.SendAsync("StrategyDeleted", strategyId);
    }

    public async Task SubscribeToStrategy(Guid strategyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"strategy-{strategyId}");
    }

    private static object MapStrategyDto(
        Models.Strategy.Strategy s,
        StrategyPerformance perf,
        List<LiveSignal> signals,
        StrategyService svc) => new
    {
        s.Id,
        s.Name,
        s.IsActive,
        s.CreatedAt,
        Rules = s.Rules.Select(r => new
        {
            r.Id,
            r.Symbol,
            r.Label,
            r.Direction,
            r.ExitStrategy,
            r.FixedRRatio,
            r.KillZoneOnly,
            SignalTypes = svc.DeserializeSignalTypes(r.SignalTypesJson),
            r.TradingWindowStart,
            r.TradingWindowEnd,
            r.IsActive,
            r.CreatedAt,
        }),
        Performance = perf,
        RecentSignals = signals.Select(sig => new
        {
            sig.Id,
            sig.Symbol,
            sig.Direction,
            sig.SignalType,
            sig.TimeframeLabel,
            sig.EntryTime,
            sig.EntryPrice,
            sig.Target,
            sig.Stop,
            sig.CurrentStop,
            sig.ExitStrategy,
            sig.Status,
            sig.OutcomeTime,
            sig.ActualExitPrice,
            sig.ActualRR,
            sig.BreakevenActivated,
        }),
    };
}
