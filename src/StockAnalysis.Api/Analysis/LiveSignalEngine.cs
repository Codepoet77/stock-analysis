using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StockAnalysis.Api.Hubs;
using StockAnalysis.Api.Models;
using StockAnalysis.Api.Models.Backtest;
using StockAnalysis.Api.Models.Strategy;
using StockAnalysis.Api.Services;

namespace StockAnalysis.Api.Analysis;

/// <summary>
/// Singleton background service that:
///  1. Polls Polygon REST every minute for fresh bars per active strategy rule.
///  2. Runs the same ICT signal detection as the backtest engine.
///  3. Tracks open signals and resolves them as 1M bars arrive from the WebSocket.
/// </summary>
public class LiveSignalEngine : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AnalysisHub> _hub;
    private readonly ILogger<LiveSignalEngine> _logger;

    // In-memory open signal store for fast bar-by-bar resolution
    private readonly ConcurrentDictionary<Guid, LiveSignal> _openSignals = new();

    // Cache of recent bars per (symbol, tfLabel) — refreshed on schedule
    private readonly ConcurrentDictionary<string, (DateTime FetchedAt, List<Bar> Bars)> _barCache = new();

    // Deduplicate: suppress a new signal if the same (ruleId, direction) fired recently
    private readonly ConcurrentDictionary<string, DateTime> _lastFired = new();
    private static readonly TimeSpan SignalCooldown = TimeSpan.FromMinutes(30);

    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public LiveSignalEngine(
        IServiceScopeFactory scopeFactory,
        IHubContext<AnalysisHub> hub,
        ILogger<LiveSignalEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Load open signals from DB on startup
        await LoadOpenSignalsAsync();

        // Reconcile: replay bar history against any signals that were open during downtime
        await ReconcileOpenSignalsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LiveSignalEngine cycle error");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private async Task LoadOpenSignalsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        var open = await svc.GetOpenSignalsAsync();
        foreach (var s in open)
            _openSignals[s.Id] = s;
        _logger.LogInformation("LiveSignalEngine loaded {Count} open signals", open.Count);
    }

    private async Task ReconcileOpenSignalsAsync(CancellationToken ct)
    {
        if (_openSignals.IsEmpty) return;

        _logger.LogInformation("Reconciling {Count} open signal(s) against bar history", _openSignals.Count);

        using var scope = _scopeFactory.CreateScope();
        var dataService = scope.ServiceProvider.GetRequiredService<BacktestDataService>();

        foreach (var signal in _openSignals.Values.ToList())
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await ReconcileSignalAsync(signal, dataService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reconciling signal {SignalId}", signal.Id);
            }
        }
    }

    private async Task ReconcileSignalAsync(LiveSignal signal, BacktestDataService dataService, CancellationToken ct)
    {
        var bars = await dataService.FetchAsync(
            signal.Symbol, BacktestTimeframe.OneMinute,
            signal.EntryTime, DateTime.UtcNow, ct: ct);

        // Only bars that arrived after the signal entry
        var barsAfterEntry = bars.Where(b => b.Time > signal.EntryTime).ToList();

        if (barsAfterEntry.Count == 0)
        {
            _logger.LogInformation(
                "Reconcile: no bars found after entry for signal {SignalId} ({Symbol}), resuming live tracking",
                signal.Id, signal.Symbol);
            return;
        }

        foreach (var bar in barsAfterEntry)
        {
            var resolved = CheckResolution(signal, bar);
            if (resolved.HasValue)
            {
                var (status, exitPrice, actualRR) = resolved.Value;
                _logger.LogInformation(
                    "Reconcile: {Symbol} {Direction} → {Status} @ {RR:F2}R (resolved during downtime at {BarTime:u})",
                    signal.Symbol, signal.Direction, status, actualRR, bar.Time);
                await ResolveAndNotifyAsync(signal, status, exitPrice, actualRR);
                return;
            }

            // Advance dynamic stop in-memory without spamming DB on every bar
            AdvanceStopInMemory(signal, bar);
        }

        // Persist the final stop position after replaying all bars
        _ = PersistStopAsync(signal);

        _logger.LogInformation(
            "Reconcile: {Symbol} {Direction} signal still open after replay, resuming live tracking (stop now {Stop:F2})",
            signal.Symbol, signal.Direction, signal.CurrentStop);
    }



    /// <summary>Same logic as <see cref="UpdateDynamicStop"/> but without the DB write per bar.</summary>
    private static void AdvanceStopInMemory(LiveSignal signal, Bar bar)
    {
        var risk = Math.Abs(signal.EntryPrice - signal.Stop);
        if (risk == 0) return;

        if (signal.ExitStrategy == ExitStrategy.TrailingStop)
        {
            var newStop = signal.Direction == "Long"
                ? bar.High - risk
                : bar.Low + risk;
            if (signal.Direction == "Long"  && newStop > signal.CurrentStop) signal.CurrentStop = newStop;
            if (signal.Direction == "Short" && newStop < signal.CurrentStop) signal.CurrentStop = newStop;
        }
        else if (signal.ExitStrategy == ExitStrategy.BreakevenStop && !signal.BreakevenActivated)
        {
            var trigger = signal.Direction == "Long"
                ? signal.EntryPrice + risk
                : signal.EntryPrice - risk;
            if ((signal.Direction == "Long"  && bar.High >= trigger) ||
                (signal.Direction == "Short" && bar.Low  <= trigger))
            {
                signal.BreakevenActivated = true;
                signal.CurrentStop = signal.EntryPrice;
            }
        }
    }

    // ── Called by LiveBarService on every incoming 1M WebSocket bar ──────────

    public void OnBarReceived(Bar bar)
    {
        // Update bar cache for 1M
        var cacheKey = $"{bar.Symbol}|1M";
        _barCache.AddOrUpdate(cacheKey,
            _ => (DateTime.UtcNow, [bar]),
            (_, existing) =>
            {
                var list = new List<Bar>(existing.Bars) { bar };
                if (list.Count > 500) list.RemoveAt(0);
                return (DateTime.UtcNow, list);
            });

        // Resolve open signals for this symbol using the fresh bar
        ResolveSignalsWithBar(bar);
    }

    // ── Main polling cycle ────────────────────────────────────────────────────

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var strategyService = scope.ServiceProvider.GetRequiredService<StrategyService>();
        var dataService     = scope.ServiceProvider.GetRequiredService<BacktestDataService>();

        var strategies = await strategyService.GetStrategiesAsync_AllUsers();
        var activeRules = strategies
            .Where(s => s.IsActive)
            .SelectMany(s => s.Rules.Where(r => r.IsActive))
            .ToList();

        if (activeRules.Count == 0)
        {
            _logger.LogDebug("Cycle: no active rules found, skipping");
            return;
        }

        _logger.LogDebug("Cycle: evaluating {Count} active rules", activeRules.Count);

        foreach (var rule in activeRules)
        {
            if (ct.IsCancellationRequested) break;
            await DetectSignalsForRuleAsync(rule, strategyService, dataService, ct);
        }
    }

    private async Task DetectSignalsForRuleAsync(
        StrategyRule rule,
        StrategyService strategyService,
        BacktestDataService dataService,
        CancellationToken ct)
    {
        try
        {
            var ruleLabel = $"{rule.Symbol} {rule.SignalTimeframe} [{rule.Label}]";

            // Trading window check (Eastern time)
            if (rule.TradingWindowStart.HasValue && rule.TradingWindowEnd.HasValue)
            {
                var etNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Eastern).TimeOfDay;
                if (etNow < rule.TradingWindowStart.Value || etNow >= rule.TradingWindowEnd.Value)
                {
                    _logger.LogDebug("Rule {Rule}: outside trading window ({Start}-{End}), current ET={Now}",
                        ruleLabel, rule.TradingWindowStart.Value, rule.TradingWindowEnd.Value, etNow);
                    return;
                }
            }

            var signalBars  = await GetCachedBarsAsync(rule.Symbol, rule.SignalTimeframe, dataService, ct);
            var biasBars    = rule.BiasTimeframe.HasValue
                ? await GetCachedBarsAsync(rule.Symbol, rule.BiasTimeframe.Value, dataService, ct) : null;
            var confirmBars = rule.ConfirmTimeframe.HasValue
                ? await GetCachedBarsAsync(rule.Symbol, rule.ConfirmTimeframe.Value, dataService, ct) : null;

            if (signalBars.Count < 10)
            {
                _logger.LogDebug("Rule {Rule}: insufficient signal bars ({Count} < 10)", ruleLabel, signalBars.Count);
                return;
            }

            var lookback = GetLookback(rule.SignalTimeframe);
            if (signalBars.Count < lookback + 2)
            {
                _logger.LogDebug("Rule {Rule}: insufficient bars for lookback ({Count} < {Needed})",
                    ruleLabel, signalBars.Count, lookback + 2);
                return;
            }

            var i = signalBars.Count - 1;       // latest bar
            var currentBar = signalBars[i];

            // Override the close price with the live 1M WebSocket price so signal detection
            // uses the actual current price, not a potentially stale REST bar close.
            // Guard: only apply the override if the 1M cache was updated within the last 2 minutes
            // — if the WebSocket has been down longer, the snapshot is too stale to be useful.
            var liveKey = $"{rule.Symbol}|1M";
            bool liveOverride = false;
            if (_barCache.TryGetValue(liveKey, out var live1M) && live1M.Bars.Count > 0
                && DateTime.UtcNow - live1M.FetchedAt < TimeSpan.FromMinutes(2))
            {
                var oldClose = currentBar.Close;
                currentBar = currentBar with { Close = live1M.Bars[^1].Close };
                liveOverride = true;
                _logger.LogDebug("Rule {Rule}: live price override {Old:F2} → {New:F2}", ruleLabel, oldClose, currentBar.Close);
            }
            else
            {
                _logger.LogDebug("Rule {Rule}: no live price override (WS stale or missing), using REST close {Price:F2}",
                    ruleLabel, currentBar.Close);
            }

            // Kill zone filter
            if (rule.KillZoneOnly && !IsKillZone(currentBar.Time))
            {
                _logger.LogDebug("Rule {Rule}: outside kill zone, skipping", ruleLabel);
                return;
            }

            var context = signalBars.GetRange(i - lookback, lookback);
            var pdh = context.Max(b => b.High);
            var pdl = context.Min(b => b.Low);

            var structure = MarketStructureAnalyzer.Analyze(context);
            var fvgs      = FairValueGapDetector.Detect(context);
            var obs       = OrderBlockDetector.Detect(context);
            var liquidity = LiquidityLevelDetector.Detect(context, pdh, pdl);

            var validObs  = obs.Where(o => o.IsValid).ToList();
            var openFvgs  = fvgs.Where(f => !f.IsFilled).ToList();

            _logger.LogInformation(
                "Rule {Rule}: price={Price:F2} bias={Bias} OBs={OBCount} FVGs={FVGCount} livePrice={Live} bar={BarTime:u}",
                ruleLabel, currentBar.Close, structure.Bias, validObs.Count, openFvgs.Count, liveOverride, currentBar.Time);

            // Log proximity to order blocks
            foreach (var ob in validObs)
            {
                var mid = (ob.Top + ob.Bottom) / 2;
                var dist = Math.Abs(currentBar.Close - mid) / mid;
                _logger.LogDebug("Rule {Rule}:   OB {Type} {Top:F2}-{Bottom:F2} mid={Mid:F2} dist={Dist:P3} (need <0.3%)",
                    ruleLabel, ob.Type, ob.Top, ob.Bottom, mid, dist);
            }

            // Log proximity to FVGs
            foreach (var fvg in openFvgs)
            {
                var inside = currentBar.Close >= fvg.Bottom && currentBar.Close <= fvg.Top;
                _logger.LogDebug("Rule {Rule}:   FVG {Type} {Top:F2}-{Bottom:F2} priceInside={Inside}",
                    ruleLabel, fvg.Type, fvg.Top, fvg.Bottom, inside);
            }

            // Build a BacktestParameters proxy to reuse GenerateSignals logic
            var signalTypes = strategyService.DeserializeSignalTypes(rule.SignalTypesJson);
            var fakeParams  = new BacktestParameters(
                Symbols: [rule.Symbol],
                DaysBack: 30,
                Timeframes: [rule.SignalTimeframe],
                ExitStrategy: rule.ExitStrategy,
                FixedRRatio: rule.FixedRRatio,
                SessionFilter: rule.KillZoneOnly ? SessionFilter.OpenKZ : SessionFilter.Full,
                SignalTypes: signalTypes.Count > 0 ? [.. signalTypes] : null
            );

            var biasBias    = biasBars != null ? GetLatestBias(biasBars, currentBar.Time, 30) : null;
            var confirmBias = confirmBars != null ? GetLatestBias(confirmBars, currentBar.Time, 40) : null;

            _logger.LogDebug("Rule {Rule}: htfBias={HtfBias} confirmBias={ConfirmBias} signalTypes={Types}",
                ruleLabel, biasBias?.ToString() ?? "none", confirmBias?.ToString() ?? "none", rule.SignalTypesJson);

            var signals = LiveSignalDetector.GenerateSignals(
                currentBar, structure, fvgs, obs, liquidity, biasBias, confirmBias, fakeParams, _logger);

            if (signals.Count == 0)
            {
                _logger.LogDebug("Rule {Rule}: no signals generated this cycle", ruleLabel);
            }

            foreach (var sig in signals)
            {
                _logger.LogInformation("Rule {Rule}: candidate signal {Type} {Direction} entry={Entry:F2} stop={Stop:F2}",
                    ruleLabel, sig.Type, sig.Direction, sig.EntryPrice, sig.Stop);

                // Direction filter
                if (rule.Direction == "Long"  && sig.Direction != SignalDirection.Long)
                {
                    _logger.LogDebug("Rule {Rule}: skipped {Direction} — rule direction filter is Long only", ruleLabel, sig.Direction);
                    continue;
                }
                if (rule.Direction == "Short" && sig.Direction != SignalDirection.Short)
                {
                    _logger.LogDebug("Rule {Rule}: skipped {Direction} — rule direction filter is Short only", ruleLabel, sig.Direction);
                    continue;
                }

                var cooldownKey = $"{rule.Id}|{sig.Direction}";
                if (_lastFired.TryGetValue(cooldownKey, out var lastFire) &&
                    DateTime.UtcNow - lastFire < SignalCooldown)
                {
                    _logger.LogDebug("Rule {Rule}: skipped {Direction} — cooldown active (last fired {Ago:F0}s ago)",
                        ruleLabel, sig.Direction, (DateTime.UtcNow - lastFire).TotalSeconds);
                    continue;
                }

                // Check for existing open signal for this rule+direction
                if (_openSignals.Values.Any(s =>
                    s.StrategyRuleId == rule.Id &&
                    s.Direction == sig.Direction.ToString() &&
                    s.Status == LiveSignalStatus.Open))
                {
                    _logger.LogDebug("Rule {Rule}: skipped {Direction} — already has open signal", ruleLabel, sig.Direction);
                    continue;
                }

                await FireSignalAsync(rule, sig, strategyService);
                _lastFired[cooldownKey] = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting signals for rule {RuleId}", rule.Id);
        }
    }

    private async Task FireSignalAsync(StrategyRule rule, DetectedSignal sig, StrategyService strategyService)
    {
        var risk = Math.Abs(sig.EntryPrice - sig.Stop);
        if (risk == 0) return;

        var target = sig.Direction == SignalDirection.Long
            ? sig.EntryPrice + risk * rule.FixedRRatio
            : sig.EntryPrice - risk * rule.FixedRRatio;

        var liveSignal = new LiveSignal
        {
            StrategyRuleId = rule.Id,
            Symbol         = rule.Symbol,
            Direction      = sig.Direction.ToString(),
            SignalType     = sig.Type.ToString(),
            TimeframeLabel = rule.Label,
            EntryTime      = DateTime.UtcNow,
            EntryPrice     = sig.EntryPrice,
            Target         = target,
            Stop           = sig.Stop,
            CurrentStop    = sig.Stop,
            ExitStrategy   = rule.ExitStrategy,
        };

        liveSignal.Rule = rule;
        await strategyService.CreateSignalAsync(liveSignal);
        _openSignals[liveSignal.Id] = liveSignal;

        _logger.LogInformation(
            "Live signal fired: {Symbol} {Direction} {Type} @ {Price:F2}",
            liveSignal.Symbol, liveSignal.Direction, liveSignal.SignalType, liveSignal.EntryPrice);

        // Notify all clients subscribed to this strategy
        await _hub.Clients.Group($"strategy-{rule.StrategyId}")
            .SendAsync("LiveSignalFired", MapToDto(liveSignal, rule));
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    private void ResolveSignalsWithBar(Bar bar)
    {
        var toResolve = _openSignals.Values
            .Where(s => s.Symbol == bar.Symbol)
            .ToList();

        foreach (var signal in toResolve)
        {
            var resolved = CheckResolution(signal, bar);
            if (resolved.HasValue)
            {
                var (status, exitPrice, actualRR) = resolved.Value;
                _ = ResolveAndNotifyAsync(signal, status, exitPrice, actualRR);
            }
            else
            {
                // Update trailing/breakeven stop in memory
                UpdateDynamicStop(signal, bar);
            }
        }
    }

    private (LiveSignalStatus status, decimal exitPrice, decimal actualRR)?
        CheckResolution(LiveSignal signal, Bar bar)
    {
        var risk = Math.Abs(signal.EntryPrice - signal.Stop);
        if (risk == 0) return null;

        var stop   = signal.CurrentStop;
        var target = signal.Target;
        bool isLong = signal.Direction == "Long";

        if (isLong)
        {
            if (bar.High >= target)
            {
                var rr = (target - signal.EntryPrice) / risk;
                return (LiveSignalStatus.Win, target, Math.Round(rr, 2));
            }
            if (bar.Low <= stop)
            {
                if (signal.ExitStrategy == ExitStrategy.BreakevenStop && signal.BreakevenActivated)
                    return (LiveSignalStatus.Scratch, stop, 0m);
                var rr = (stop - signal.EntryPrice) / risk;
                var status = rr >= 0 ? LiveSignalStatus.Scratch : LiveSignalStatus.Loss;
                return (status, stop, Math.Round(rr, 2));
            }
        }
        else
        {
            if (bar.Low <= target)
            {
                var rr = (signal.EntryPrice - target) / risk;
                return (LiveSignalStatus.Win, target, Math.Round(rr, 2));
            }
            if (bar.High >= stop)
            {
                if (signal.ExitStrategy == ExitStrategy.BreakevenStop && signal.BreakevenActivated)
                    return (LiveSignalStatus.Scratch, stop, 0m);
                var rr = (signal.EntryPrice - stop) / risk;
                var status = rr >= 0 ? LiveSignalStatus.Scratch : LiveSignalStatus.Loss;
                return (status, stop, Math.Round(rr, 2));
            }
        }
        return null;
    }

    private void UpdateDynamicStop(LiveSignal signal, Bar bar)
    {
        var risk = Math.Abs(signal.EntryPrice - signal.Stop);
        if (risk == 0) return;
        bool changed = false;

        if (signal.ExitStrategy == ExitStrategy.TrailingStop)
        {
            var newStop = signal.Direction == "Long"
                ? bar.High - risk
                : bar.Low + risk;
            if (signal.Direction == "Long"  && newStop > signal.CurrentStop) { signal.CurrentStop = newStop; changed = true; }
            if (signal.Direction == "Short" && newStop < signal.CurrentStop) { signal.CurrentStop = newStop; changed = true; }
        }
        else if (signal.ExitStrategy == ExitStrategy.BreakevenStop && !signal.BreakevenActivated)
        {
            var trigger = signal.Direction == "Long"
                ? signal.EntryPrice + risk
                : signal.EntryPrice - risk;
            if ((signal.Direction == "Long"  && bar.High >= trigger) ||
                (signal.Direction == "Short" && bar.Low  <= trigger))
            {
                signal.BreakevenActivated = true;
                signal.CurrentStop = signal.EntryPrice;
                changed = true;
            }
        }

        if (changed)
            _ = PersistStopAsync(signal);
    }

    private async Task PersistStopAsync(LiveSignal signal)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        await svc.UpdateStopAsync(signal.Id, signal.CurrentStop, signal.BreakevenActivated);
    }

    private async Task ResolveAndNotifyAsync(LiveSignal signal,
        LiveSignalStatus status, decimal exitPrice, decimal actualRR)
    {
        _openSignals.TryRemove(signal.Id, out _);
        signal.Status       = status;
        signal.ActualRR     = actualRR;
        signal.ActualExitPrice = exitPrice;
        signal.OutcomeTime  = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StrategyService>();
        await svc.ResolveSignalAsync(signal.Id, status, exitPrice, actualRR);

        _logger.LogInformation(
            "Live signal resolved: {Symbol} {Direction} → {Status} @ {RR:F2}R",
            signal.Symbol, signal.Direction, status, actualRR);

        // Notify strategy group
        var rule = signal.Rule;
        if (rule != null)
        {
            await _hub.Clients.Group($"strategy-{rule.StrategyId}")
                .SendAsync("LiveSignalResolved", MapToDto(signal, rule));
        }
    }

    // ── Bar cache ─────────────────────────────────────────────────────────────

    private async Task<List<Bar>> GetCachedBarsAsync(
        string symbol, BacktestTimeframe tf,
        BacktestDataService dataService, CancellationToken ct)
    {
        var label = TimeframeHelper.Label(tf);
        var key   = $"{symbol}|{label}";
        var ttl   = GetCacheTtl(tf);

        if (_barCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < ttl)
            return cached.Bars;

        var from = DateTime.UtcNow.AddDays(-GetFetchDays(tf));
        var bars = await dataService.FetchAsync(symbol, tf, from, DateTime.UtcNow, ct: ct);
        _barCache[key] = (DateTime.UtcNow, bars);
        return bars;
    }

    private static TimeSpan GetCacheTtl(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute     => TimeSpan.FromMinutes(1),
        BacktestTimeframe.FiveMinute    => TimeSpan.FromMinutes(5),
        BacktestTimeframe.FifteenMinute => TimeSpan.FromMinutes(15),
        BacktestTimeframe.OneHour       => TimeSpan.FromHours(1),
        BacktestTimeframe.FourHour      => TimeSpan.FromHours(4),
        _                               => TimeSpan.FromMinutes(5),
    };

    private static int GetFetchDays(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute     => 3,
        BacktestTimeframe.FiveMinute    => 7,
        BacktestTimeframe.FifteenMinute => 14,
        BacktestTimeframe.OneHour       => 30,
        BacktestTimeframe.FourHour      => 60,
        _                               => 14,
    };

    private static int GetLookback(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute     => 30,
        BacktestTimeframe.FiveMinute    => 80,
        BacktestTimeframe.FifteenMinute => 60,
        BacktestTimeframe.OneHour       => 50,
        BacktestTimeframe.FourHour      => 30,
        _                               => 50,
    };

    private static bool IsKillZone(DateTime utcTime)
    {
        var et  = TimeZoneInfo.ConvertTimeFromUtc(utcTime, Eastern);
        var tod = et.TimeOfDay;
        return tod >= new TimeSpan(9, 30, 0) && tod <= new TimeSpan(11, 0, 0);
    }

    private static StructureBias? GetLatestBias(List<Bar> bars, DateTime before, int lookback)
    {
        var idx = bars.FindLastIndex(b => b.Time <= before);
        if (idx < lookback) return null;
        var slice = bars.GetRange(idx - lookback, lookback);
        return slice.Count >= 6 ? MarketStructureAnalyzer.Analyze(slice).Bias : null;
    }

    // ── DTO mapping ───────────────────────────────────────────────────────────

    private static object MapToDto(LiveSignal s, StrategyRule rule) => new
    {
        s.Id,
        s.Symbol,
        s.Direction,
        s.SignalType,
        s.TimeframeLabel,
        s.EntryTime,
        s.EntryPrice,
        s.Target,
        s.Stop,
        CurrentStop = s.CurrentStop,
        s.ExitStrategy,
        s.Status,
        s.OutcomeTime,
        s.ActualExitPrice,
        s.ActualRR,
        s.BreakevenActivated,
        StrategyId = rule.StrategyId,
        RuleId = rule.Id,
        RuleLabel = rule.Label,
    };
}
