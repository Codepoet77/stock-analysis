using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StockAnalysis.Api.Data;
using StockAnalysis.Api.Hubs;
using StockAnalysis.Api.Models;
using StockAnalysis.Api.Models.Backtest;
using StockAnalysis.Api.Services;

namespace StockAnalysis.Api.Analysis;

public class BacktestEngine
{
    private readonly BacktestDataService _data;
    private readonly IHubContext<AnalysisHub> _hub;
    private readonly BacktestJobService _jobs;
    private readonly ILogger<BacktestEngine> _logger;

    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    // Serialises concurrent DB progress writes from parallel WalkForward tasks
    private readonly SemaphoreSlim _progressDbLock = new(1, 1);

    public BacktestEngine(BacktestDataService data, IHubContext<AnalysisHub> hub, BacktestJobService jobs, ILogger<BacktestEngine> logger)
    {
        _data = data;
        _hub = hub;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<BacktestResult> RunAsync(
        BacktestParameters parameters,
        BacktestJob job,
        string userId,
        CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var from = DateTime.UtcNow.AddDays(-parameters.DaysBack).Date;
        var to = DateTime.UtcNow.Date;

        // Determine all unique timeframes needed
        var neededTfs = parameters.Timeframes.ToHashSet();
        foreach (var combo in TimeframeCombination.All)
        {
            if (combo.BiasTf.HasValue) neededTfs.Add(combo.BiasTf.Value);
            if (combo.ConfirmTf.HasValue) neededTfs.Add(combo.ConfirmTf.Value);
            neededTfs.Add(combo.SignalTf);
        }

        // Cache for fetched bar data
        var barCache = new Dictionary<(string symbol, BacktestTimeframe tf), List<Bar>>();

        var jobId = job.Id;

        // Pre-calculate total chunks across all fetches for smooth per-chunk progress
        var totalChunks = parameters.Symbols.Length * neededTfs.Sum(tf =>
            (int)Math.Ceiling((to - from).TotalDays / (TimeframeHelper.FetchChunkMonths(tf) * 30.0)));
        totalChunks = Math.Max(totalChunks, 1);
        int fetchedChunks = 0;

        // Fetch all needed data (sequential — I/O bound)
        foreach (var symbol in parameters.Symbols)
        {
            foreach (var tf in neededTfs)
            {
                if (ct.IsCancellationRequested) break;

                var pct = (int)((double)fetchedChunks / totalChunks * 30);
                await SendProgress(userId, jobId, "Fetching data",
                    $"{symbol} {TimeframeHelper.Label(tf)}", pct, ct);

                var bars = await _data.FetchAsync(symbol, tf, from, to,
                    new Progress<string>(msg =>
                    {
                        fetchedChunks++;
                        var subPct = (int)((double)fetchedChunks / totalChunks * 30);
                        _ = SendProgress(userId, jobId, "Fetching", msg, subPct, ct);
                    }),
                    ct);

                barCache[(symbol, tf)] = bars;
                _logger.LogInformation("Fetched {Count} bars for {Symbol} {Tf}", bars.Count, symbol, TimeframeHelper.Label(tf));
            }
        }

        // ── D: Incremental stats — accumulate per (label, symbol, exitStrategy) as outcomes flow ──
        var statsMap = new ConcurrentDictionary<(string label, string symbol, ExitStrategy exit, bool isCombo), StatsAccumulator>();
        var sampleBag = new ConcurrentBag<BacktestSignalOutcome>();
        int totalOutcomes = 0;
        const int SampleCap = 2000;

        void AddOutcome(string label, string symbol, bool isCombo, BacktestSignalOutcome o)
        {
            statsMap.GetOrAdd(
                (label, symbol, o.ExitStrategy, isCombo),
                _ => new StatsAccumulator(label, symbol, o.ExitStrategy, isCombo)
            ).Add(o);
            var n = Interlocked.Increment(ref totalOutcomes);
            if (n <= SampleCap) sampleBag.Add(o);
        }

        // Run individual timeframe tests and combination tests in parallel
        int totalAnalysis = parameters.Symbols.Length *
            (parameters.Timeframes.Length + TimeframeCombination.All.Count);
        var analysisStep = 0;

        // Build individual work list
        var individualWork = (from symbol in parameters.Symbols
                              from tf in parameters.Timeframes
                              select (symbol, tf)).ToList();

        Parallel.ForEach(individualWork, new ParallelOptions { CancellationToken = ct }, item =>
        {
            var (symbol, tf) = item;
            if (!barCache.TryGetValue((symbol, tf), out var bars) || bars.Count == 0)
            {
                Interlocked.Increment(ref analysisStep);
                return;
            }
            var tfLabel = TimeframeHelper.Label(tf);
            var count = WalkForward(symbol, tfLabel, null, bars, null, null, null, parameters,
                o => AddOutcome(tfLabel, symbol, false, o),
                (barsDone, barsTotal) =>
                {
                    var curPct = 30 + (int)((double)analysisStep / totalAnalysis * 60);
                    _ = SendProgress(userId, jobId, "Testing signals",
                        $"{symbol} · {tfLabel} — {barsDone:N0} / {barsTotal:N0} bars", curPct, ct);
                });
            var step = Interlocked.Increment(ref analysisStep);
            var pct = 30 + (int)((double)step / totalAnalysis * 60);
            _ = SendProgress(userId, jobId, "Testing signals",
                $"{symbol} · {tfLabel} — {count} signals found", pct, ct);
        });

        // Build combination work list
        var comboWork = (from symbol in parameters.Symbols
                         from combo in TimeframeCombination.All
                         select (symbol, combo)).ToList();

        Parallel.ForEach(comboWork, new ParallelOptions { CancellationToken = ct }, item =>
        {
            var (symbol, combo) = item;
            if (!barCache.TryGetValue((symbol, combo.SignalTf), out var signalBars) || signalBars.Count == 0)
            {
                Interlocked.Increment(ref analysisStep);
                return;
            }
            barCache.TryGetValue((symbol, combo.BiasTf ?? combo.SignalTf), out var biasBars);
            barCache.TryGetValue((symbol, combo.ConfirmTf ?? combo.SignalTf), out var confirmBars);
            var count = WalkForward(symbol, TimeframeHelper.Label(combo.SignalTf), combo.Label,
                signalBars, biasBars, confirmBars, null, parameters,
                o => AddOutcome(combo.Label, symbol, true, o),
                (barsDone, barsTotal) =>
                {
                    var curPct = 30 + (int)((double)analysisStep / totalAnalysis * 60);
                    _ = SendProgress(userId, jobId, "Testing signals",
                        $"{symbol} · {combo.Label} — {barsDone:N0} / {barsTotal:N0} bars", curPct, ct);
                });
            var step = Interlocked.Increment(ref analysisStep);
            var pct = 30 + (int)((double)step / totalAnalysis * 60);
            _ = SendProgress(userId, jobId, "Testing signals",
                $"{symbol} · {combo.Label} — {count} signals found", pct, ct);
        });

        // Build stats from accumulators — no LINQ grouping over millions of outcomes
        var individualStats = statsMap
            .Where(kv => !kv.Key.isCombo)
            .Select(kv => kv.Value.ToStats())
            .Where(s => s.TotalSignals > 0)
            .ToList();

        var combinationStats = statsMap
            .Where(kv => kv.Key.isCombo)
            .Select(kv => kv.Value.ToStats())
            .Where(s => s.TotalSignals > 0)
            .ToList();

        await SendProgress(userId, jobId, "Complete", "Backtest finished", 100, ct);

        // Cap sample signals at 500 most recent
        var sample = sampleBag
            .OrderByDescending(o => o.EntryTime)
            .Take(500)
            .ToList();

        return new BacktestResult(
            Parameters: parameters,
            StartedAt: startedAt,
            CompletedAt: DateTime.UtcNow,
            IndividualStats: individualStats,
            CombinationStats: combinationStats,
            SampleSignals: sample,
            TotalSignalsAnalyzed: totalOutcomes
        );
    }

    // ── Walk-forward engine ──────────────────────────────────────────────────

    private int WalkForward(
        string symbol,
        string tfLabel,
        string? comboLabel,
        List<Bar> bars,
        List<Bar>? biasBars,
        List<Bar>? confirmBars,
        List<Bar>? unusedParam,  // kept for signature compatibility
        BacktestParameters parameters,
        Action<BacktestSignalOutcome> onOutcome,
        Action<int, int>? onProgress = null) // (barsDone, barsTotal) heartbeat
    {
        // A: Filter intraday bars to regular trading hours (9:30–16:00 ET) to cut bar count 3–4×.
        //    Extended-hours bars inflate 1M count and don't generate valid ICT session signals.
        if (tfLabel is "1M" or "5M" or "15M")
        {
            bars = bars.Where(b =>
            {
                var tod = TimeZoneInfo.ConvertTimeFromUtc(b.Time, Eastern).TimeOfDay;
                return tod >= new TimeSpan(9, 30, 0) && tod < new TimeSpan(16, 0, 0);
            }).ToList();
        }

        int outcomeCount = 0;
        var lookback = GetLookback(tfLabel);
        var maxFwd = GetMaxForward(tfLabel);
        var totalBars = bars.Count - lookback - maxFwd;

        // Pre-compute previous-day high/low per date to avoid O(n²) scan per bar
        var prevDayCache = BuildPrevDayCache(bars);

        // Pre-compute kill zone flags to avoid per-bar timezone conversion
        var isKillZone = new bool[bars.Count];
        for (int k = 0; k < bars.Count; k++)
            isKillZone[k] = IsKillZone(bars[k].Time);

        // Pre-compute Eastern date for each bar to avoid per-bar timezone conversion
        var barDates = new DateOnly[bars.Count];
        for (int k = 0; k < bars.Count; k++)
            barDates[k] = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(bars[k].Time, Eastern));

        // Pre-compute HTF bias arrays indexed by HTF bar position
        var biasCache = PrecomputeBias(biasBars, 30);
        var confirmCache = PrecomputeBias(confirmBars, 40);

        for (int i = lookback; i < bars.Count - maxFwd; i++)
        {
            // Heartbeat every 20,000 bars so the UI doesn't appear stuck on long timeframes
            if (onProgress != null && (i - lookback) % 20_000 == 0 && i > lookback)
                onProgress(i - lookback, totalBars);

            var currentBar = bars[i];

            // Session filter — skip bars outside the configured session window
            if (parameters.SessionFilter != SessionFilter.Full)
            {
                var et = TimeZoneInfo.ConvertTimeFromUtc(currentBar.Time, Eastern).TimeOfDay;
                if (!parameters.IsInSession(et)) continue;
            }

            // Get HTF bias using pre-computed cache
            StructureBias? biasBias = LookupBias(biasBars, biasCache, currentBar.Time);
            StructureBias? confirmBias = LookupBias(confirmBars, confirmCache, currentBar.Time);

            // PDH/PDL from previous trading day's bars
            var currentDate = barDates[i];
            prevDayCache.TryGetValue(currentDate, out var prevDay);
            var pdh = prevDay.High > 0 ? prevDay.High : currentBar.High * 1.01m;
            var pdl = prevDay.Low > 0 ? prevDay.Low : currentBar.Low * 0.99m;

            // Use GetRange (Array.Copy) instead of Skip/Take to avoid O(n) per iteration
            var context = bars.GetRange(i - lookback, lookback);

            // Run ICT analysis
            var structure = MarketStructureAnalyzer.Analyze(context);
            var fvgs      = FairValueGapDetector.Detect(context);
            var obs       = OrderBlockDetector.Detect(context);
            var liquidity = LiquidityLevelDetector.Detect(context, pdh, pdl);

            // Generate signals with HTF filters applied
            var signals = GenerateSignals(currentBar, structure, fvgs, obs, liquidity,
                biasBias, confirmBias, parameters);

            var fwdCount = Math.Min(maxFwd, bars.Count - i - 1);
            var futureBars = bars.GetRange(i + 1, fwdCount);
            bool duringKz = isKillZone[i];

            foreach (var sig in signals)
            {
                if (parameters.ExitStrategy == ExitStrategy.FixedR || parameters.ExitStrategy == ExitStrategy.All)
                {
                    onOutcome(TrackFixedR(sig, futureBars, parameters.FixedRRatio, symbol, tfLabel, comboLabel, ExitStrategy.FixedR, duringKz));
                    outcomeCount++;
                }
                if (parameters.ExitStrategy == ExitStrategy.BreakevenStop || parameters.ExitStrategy == ExitStrategy.All)
                {
                    onOutcome(TrackBreakevenStop(sig, futureBars, parameters.FixedRRatio, symbol, tfLabel, comboLabel, duringKz));
                    outcomeCount++;
                }
                if (parameters.ExitStrategy == ExitStrategy.TrailingStop || parameters.ExitStrategy == ExitStrategy.All)
                {
                    onOutcome(TrackTrailingStop(sig, futureBars, parameters.FixedRRatio, symbol, tfLabel, comboLabel, duringKz));
                    outcomeCount++;
                }
                if (parameters.ExitStrategy == ExitStrategy.NextLiquidity || parameters.ExitStrategy == ExitStrategy.All)
                {
                    onOutcome(TrackNextLiquidity(sig, futureBars, liquidity, symbol, tfLabel, comboLabel, ExitStrategy.NextLiquidity, duringKz));
                    outcomeCount++;
                }
            }
        }

        return outcomeCount;
    }

    // ── Bias pre-computation helpers ─────────────────────────────────────────

    private static StructureBias?[] PrecomputeBias(List<Bar>? htfBars, int lookback)
    {
        if (htfBars == null) return [];
        var result = new StructureBias?[htfBars.Count];
        for (int k = lookback; k < htfBars.Count; k++)
        {
            var slice = htfBars.GetRange(k - lookback, lookback);
            result[k] = slice.Count >= 6 ? MarketStructureAnalyzer.Analyze(slice).Bias : null;
        }
        return result;
    }

    private static StructureBias? LookupBias(List<Bar>? bars, StructureBias?[] cache, DateTime time)
    {
        if (bars == null || cache.Length == 0) return null;
        int lo = 0, hi = bars.Count - 1, idx = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (bars[mid].Time <= time) { idx = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return idx >= 0 ? cache[idx] : null;
    }

    // ── Signal generation ────────────────────────────────────────────────────

    private static List<RawSignal> GenerateSignals(
        Bar bar,
        MarketStructure structure,
        List<FairValueGap> fvgs,
        List<OrderBlock> obs,
        List<LiquidityLevel> liquidity,
        StructureBias? htfBias,
        StructureBias? confirmBias,
        BacktestParameters parameters)
    {
        var signals = new List<RawSignal>();
        var price = bar.Close;
        var localBias = structure.Bias;

        bool CanLong() =>
            localBias != StructureBias.Bearish &&
            (htfBias == null || htfBias != StructureBias.Bearish) &&
            (confirmBias == null || confirmBias != StructureBias.Bearish);

        bool CanShort() =>
            localBias != StructureBias.Bullish &&
            (htfBias == null || htfBias != StructureBias.Bullish) &&
            (confirmBias == null || confirmBias != StructureBias.Bullish);

        // OB Retest signals
        if (parameters.IsSignalEnabled(EnabledSignalType.OBRetest))
        {
            foreach (var ob in obs.Where(o => o.IsValid))
            {
                var mid = (ob.Top + ob.Bottom) / 2;
                var proximity = Math.Abs(price - mid) / mid;
                if (proximity > 0.003m) continue; // must be within 0.3% of OB

                if (ob.Type == OrderBlockType.Bullish && CanLong())
                    signals.Add(new RawSignal(SignalType.OBRetest, SignalDirection.Long,
                        ob.Bottom, ob.Top, ob.Bottom * 0.9985m));

                if (ob.Type == OrderBlockType.Bearish && CanShort())
                    signals.Add(new RawSignal(SignalType.OBRetest, SignalDirection.Short,
                        ob.Top, ob.Bottom, ob.Top * 1.0015m));
            }
        }

        // FVG Fill signals
        if (parameters.IsSignalEnabled(EnabledSignalType.FVGFill))
        {
            foreach (var fvg in fvgs.Where(f => !f.IsFilled))
            {
                if (price < fvg.Bottom || price > fvg.Top) continue; // price must be inside FVG

                if (fvg.Type == FvgType.Bullish && CanLong())
                    signals.Add(new RawSignal(SignalType.FVGFill, SignalDirection.Long,
                        fvg.Bottom, fvg.Top, fvg.Bottom * 0.9985m));

                if (fvg.Type == FvgType.Bearish && CanShort())
                    signals.Add(new RawSignal(SignalType.FVGFill, SignalDirection.Short,
                        fvg.Top, fvg.Bottom, fvg.Top * 1.0015m));
            }
        }

        // Liquidity sweep + reversal signals
        if (parameters.IsSignalEnabled(EnabledSignalType.LiquiditySweep))
        {
            foreach (var lvl in liquidity.Where(l => l.IsSwept))
            {
                if (lvl.Type == LiquidityType.SellSide && CanLong())
                    signals.Add(new RawSignal(SignalType.LiquiditySweep, SignalDirection.Long,
                        lvl.Price * 0.999m, lvl.Price, lvl.Price * 0.997m));

                if (lvl.Type == LiquidityType.BuySide && CanShort())
                    signals.Add(new RawSignal(SignalType.LiquiditySweep, SignalDirection.Short,
                        lvl.Price, lvl.Price * 1.001m, lvl.Price * 1.003m));
            }
        }

        // Structure break signals
        if (parameters.IsSignalEnabled(EnabledSignalType.StructureBreak) && structure.StructureBreak)
        {
            if (localBias == StructureBias.Bullish && CanLong())
                signals.Add(new RawSignal(SignalType.StructureBreak, SignalDirection.Long,
                    price * 0.999m, price * 1.001m, price * 0.997m));

            if (localBias == StructureBias.Bearish && CanShort())
                signals.Add(new RawSignal(SignalType.StructureBreak, SignalDirection.Short,
                    price * 1.001m, price * 0.999m, price * 1.003m));
        }

        // C: Cap to 1 signal per direction — most recently added (closest to current bar) wins.
        //    Prevents signal explosion where 20-30 stacked FVGs each fire a separate outcome.
        var lastLong  = signals.FindLast(s => s.Direction == SignalDirection.Long);
        var lastShort = signals.FindLast(s => s.Direction == SignalDirection.Short);
        signals.Clear();
        if (lastLong  is not null) signals.Add(lastLong);
        if (lastShort is not null) signals.Add(lastShort);

        return signals;
    }

    // ── Outcome tracking ─────────────────────────────────────────────────────

    private static BacktestSignalOutcome TrackFixedR(
        RawSignal sig, List<Bar> future, decimal rRatio,
        string symbol, string tfLabel, string? comboLabel,
        ExitStrategy strategy, bool kz)
    {
        var risk = Math.Abs(sig.EntryPrice - sig.Invalidation);
        var target = sig.Direction == SignalDirection.Long
            ? sig.EntryPrice + risk * rRatio
            : sig.EntryPrice - risk * rRatio;

        return TrackOutcome(sig, future, target, sig.Invalidation,
            symbol, tfLabel, comboLabel, strategy, kz);
    }

    private static BacktestSignalOutcome TrackNextLiquidity(
        RawSignal sig, List<Bar> future, List<LiquidityLevel> liquidity,
        string symbol, string tfLabel, string? comboLabel,
        ExitStrategy strategy, bool kz)
    {
        // Find nearest unswept liquidity level in signal direction
        decimal target;
        if (sig.Direction == SignalDirection.Long)
        {
            var lvl = liquidity
                .Where(l => l.Type == LiquidityType.BuySide && l.Price > sig.EntryPrice && !l.IsSwept)
                .OrderBy(l => l.Price)
                .FirstOrDefault();
            target = lvl?.Price ?? sig.EntryPrice * 1.01m; // fallback 1% target
        }
        else
        {
            var lvl = liquidity
                .Where(l => l.Type == LiquidityType.SellSide && l.Price < sig.EntryPrice && !l.IsSwept)
                .OrderByDescending(l => l.Price)
                .FirstOrDefault();
            target = lvl?.Price ?? sig.EntryPrice * 0.99m;
        }

        return TrackOutcome(sig, future, target, sig.Invalidation,
            symbol, tfLabel, comboLabel, strategy, kz);
    }

    private static BacktestSignalOutcome TrackBreakevenStop(
        RawSignal sig, List<Bar> future, decimal rRatio,
        string symbol, string tfLabel, string? comboLabel, bool kz)
    {
        var risk = Math.Abs(sig.EntryPrice - sig.Invalidation);
        if (risk == 0) return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.BreakevenStop,
            sig.EntryPrice, sig.Invalidation, BacktestOutcome.Scratch, 0m, 0, kz);

        var target = sig.Direction == SignalDirection.Long
            ? sig.EntryPrice + risk * rRatio
            : sig.EntryPrice - risk * rRatio;

        var stop = sig.Invalidation;
        var breakevenTrigger = sig.Direction == SignalDirection.Long
            ? sig.EntryPrice + risk
            : sig.EntryPrice - risk;
        bool breakevenActive = false;

        for (int i = 0; i < future.Count; i++)
        {
            var bar = future[i];

            if (sig.Direction == SignalDirection.Long)
            {
                if (!breakevenActive && bar.High >= breakevenTrigger)
                {
                    breakevenActive = true;
                    stop = sig.EntryPrice;
                }
                if (bar.High >= target)
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.BreakevenStop,
                        target, stop, BacktestOutcome.Win, rRatio, i + 1, kz);
                if (bar.Low <= stop)
                {
                    var outcome = breakevenActive ? BacktestOutcome.Scratch : BacktestOutcome.Loss;
                    var rr = breakevenActive ? 0m : -1m;
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.BreakevenStop,
                        target, stop, outcome, rr, i + 1, kz);
                }
            }
            else
            {
                if (!breakevenActive && bar.Low <= breakevenTrigger)
                {
                    breakevenActive = true;
                    stop = sig.EntryPrice;
                }
                if (bar.Low <= target)
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.BreakevenStop,
                        target, stop, BacktestOutcome.Win, rRatio, i + 1, kz);
                if (bar.High >= stop)
                {
                    var outcome = breakevenActive ? BacktestOutcome.Scratch : BacktestOutcome.Loss;
                    var rr = breakevenActive ? 0m : -1m;
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.BreakevenStop,
                        target, stop, outcome, rr, i + 1, kz);
                }
            }
        }

        return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.BreakevenStop,
            target, stop, BacktestOutcome.Scratch, 0m, future.Count, kz);
    }

    private static BacktestSignalOutcome TrackTrailingStop(
        RawSignal sig, List<Bar> future, decimal rRatio,
        string symbol, string tfLabel, string? comboLabel, bool kz)
    {
        var risk = Math.Abs(sig.EntryPrice - sig.Invalidation);
        if (risk == 0) return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.TrailingStop,
            sig.EntryPrice, sig.Invalidation, BacktestOutcome.Scratch, 0m, 0, kz);

        var target = sig.Direction == SignalDirection.Long
            ? sig.EntryPrice + risk * rRatio
            : sig.EntryPrice - risk * rRatio;

        var trailingStop = sig.Invalidation;

        for (int i = 0; i < future.Count; i++)
        {
            var bar = future[i];

            if (sig.Direction == SignalDirection.Long)
            {
                // Trail stop up behind bar high
                var newStop = bar.High - risk;
                if (newStop > trailingStop) trailingStop = newStop;

                // Check target first (optimistic)
                if (bar.High >= target)
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.TrailingStop,
                        target, trailingStop, BacktestOutcome.Win, rRatio, i + 1, kz);

                if (bar.Low <= trailingStop)
                {
                    var actualRR = (trailingStop - sig.EntryPrice) / risk;
                    var outcome = actualRR > 0 ? BacktestOutcome.Win
                                : actualRR == 0 ? BacktestOutcome.Scratch
                                : BacktestOutcome.Loss;
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.TrailingStop,
                        target, trailingStop, outcome, Math.Round(actualRR, 2), i + 1, kz);
                }
            }
            else
            {
                // Trail stop down behind bar low
                var newStop = bar.Low + risk;
                if (newStop < trailingStop) trailingStop = newStop;

                if (bar.Low <= target)
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.TrailingStop,
                        target, trailingStop, BacktestOutcome.Win, rRatio, i + 1, kz);

                if (bar.High >= trailingStop)
                {
                    var actualRR = (sig.EntryPrice - trailingStop) / risk;
                    var outcome = actualRR > 0 ? BacktestOutcome.Win
                                : actualRR == 0 ? BacktestOutcome.Scratch
                                : BacktestOutcome.Loss;
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.TrailingStop,
                        target, trailingStop, outcome, Math.Round(actualRR, 2), i + 1, kz);
                }
            }
        }

        // Timed out — scratch
        return MakeOutcome(sig, symbol, tfLabel, comboLabel, ExitStrategy.TrailingStop,
            target, trailingStop, BacktestOutcome.Scratch, 0m, future.Count, kz);
    }

    private static BacktestSignalOutcome TrackOutcome(
        RawSignal sig, List<Bar> future, decimal target, decimal invalidation,
        string symbol, string tfLabel, string? comboLabel,
        ExitStrategy strategy, bool kz)
    {
        var risk = Math.Abs(sig.EntryPrice - invalidation);

        for (int i = 0; i < future.Count; i++)
        {
            var bar = future[i];

            if (sig.Direction == SignalDirection.Long)
            {
                if (bar.High >= target)
                {
                    var rr = risk > 0 ? (target - sig.EntryPrice) / risk : 0;
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, strategy,
                        target, invalidation, BacktestOutcome.Win, rr, i + 1, kz);
                }
                if (bar.Low <= invalidation)
                {
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, strategy,
                        target, invalidation, BacktestOutcome.Loss, -1m, i + 1, kz);
                }
            }
            else
            {
                if (bar.Low <= target)
                {
                    var rr = risk > 0 ? (sig.EntryPrice - target) / risk : 0;
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, strategy,
                        target, invalidation, BacktestOutcome.Win, rr, i + 1, kz);
                }
                if (bar.High >= invalidation)
                {
                    return MakeOutcome(sig, symbol, tfLabel, comboLabel, strategy,
                        target, invalidation, BacktestOutcome.Loss, -1m, i + 1, kz);
                }
            }
        }

        // Timed out — scratch
        return MakeOutcome(sig, symbol, tfLabel, comboLabel, strategy,
            target, invalidation, BacktestOutcome.Scratch, 0m, future.Count, kz);
    }

    private static BacktestSignalOutcome MakeOutcome(
        RawSignal sig, string symbol, string tfLabel, string? comboLabel,
        ExitStrategy strategy, decimal target, decimal invalidation,
        BacktestOutcome outcome, decimal rr, int bars, bool kz) =>
        new(symbol, tfLabel, comboLabel, sig.Type, sig.Direction, strategy,
            sig.Time, sig.EntryPrice, target, invalidation,
            outcome, rr, bars, kz);

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Build a dictionary mapping each trading date to the previous day's high/low.
    // Single O(n) pass so WalkForward doesn't scan from index 0 for every bar.
    private static Dictionary<DateOnly, (decimal High, decimal Low)> BuildPrevDayCache(List<Bar> bars)
    {
        // Group bars by Eastern date
        var byDate = new SortedDictionary<DateOnly, (decimal High, decimal Low)>();
        foreach (var b in bars)
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(b.Time, Eastern));
            if (!byDate.TryGetValue(date, out var hl))
                byDate[date] = (b.High, b.Low);
            else
                byDate[date] = (Math.Max(hl.High, b.High), Math.Min(hl.Low, b.Low));
        }

        // For each date, map it to the previous date's high/low
        var result = new Dictionary<DateOnly, (decimal High, decimal Low)>();
        DateOnly? prev = null;
        foreach (var (date, hl) in byDate)
        {
            if (prev.HasValue)
                result[date] = byDate[prev.Value];
            prev = date;
        }
        return result;
    }

    private static bool IsKillZone(DateTime utcTime)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(utcTime, Eastern);
        var tod = et.TimeOfDay;
        return tod >= new TimeSpan(9, 30, 0) && tod <= new TimeSpan(11, 0, 0);
    }

    // B: Reduced 1M lookback 100 → 30 to cut FVG/OB count per window by ~70%.
    //    30 bars is sufficient for ICT pattern detection on the signal timeframe.
    private static int GetLookback(string tfLabel) => tfLabel switch
    {
        "1M" => 30, "5M" => 80, "15M" => 60, "1H" => 50, "4H" => 30, _ => 60
    };

    private static int GetMaxForward(string tfLabel) => tfLabel switch
    {
        "1M" => 60, "5M" => 48, "15M" => 32, "1H" => 24, "4H" => 10, _ => 24
    };

    private async Task SendProgress(string userId, Guid jobId, string stage, string detail, int pct, CancellationToken ct)
    {
        var update = new BacktestProgressUpdate(stage, detail, pct);
        await _hub.Clients.Group($"backtest-{userId}").SendAsync("BacktestProgress", update, ct);
        // Throttle DB writes — only persist every 5% increment
        // SemaphoreSlim ensures parallel WalkForward tasks don't concurrently access the DbContext
        if (pct % 5 == 0)
        {
            await _progressDbLock.WaitAsync(ct);
            try { await _jobs.UpdateProgressAsync(jobId, update); }
            finally { _progressDbLock.Release(); }
        }
    }

    // ── D: Incremental per-group stats accumulator ───────────────────────────
    // One instance per (label, symbol, exitStrategy). Since each WalkForward task
    // owns exactly one set of accumulators, Add() is single-threaded per instance.

    private sealed class StatsAccumulator
    {
        public readonly string Label;
        public readonly string Symbol;
        public readonly ExitStrategy ExitStrategy;
        public readonly bool IsCombo;

        private int _total, _wins, _losses, _scratches;
        private decimal _sumAllRR, _sumWinRR, _sumLossAbsRR;
        private readonly Dictionary<string, (int T, int W, int L, decimal SumRR)> _byType = new();

        private struct Bucket { public int T, W, L; public decimal SumRR; }
        private Bucket _kz, _nkz;

        public StatsAccumulator(string label, string symbol, ExitStrategy exit, bool isCombo)
        {
            Label = label; Symbol = symbol; ExitStrategy = exit; IsCombo = isCombo;
        }

        public void Add(BacktestSignalOutcome o)
        {
            _total++;
            _sumAllRR += o.ActualRR;

            switch (o.Outcome)
            {
                case BacktestOutcome.Win:
                    _wins++; _sumWinRR += o.ActualRR; break;
                case BacktestOutcome.Loss:
                    _losses++; _sumLossAbsRR += Math.Abs(o.ActualRR); break;
                default:
                    _scratches++; break;
            }

            var tk = o.SignalType.ToString();
            int tw = o.Outcome == BacktestOutcome.Win ? 1 : 0;
            int tl = o.Outcome == BacktestOutcome.Loss ? 1 : 0;
            if (_byType.TryGetValue(tk, out var tv))
                _byType[tk] = (tv.T + 1, tv.W + tw, tv.L + tl, tv.SumRR + o.ActualRR);
            else
                _byType[tk] = (1, tw, tl, o.ActualRR);

            ref var sess = ref o.DuringKillZone ? ref _kz : ref _nkz;
            sess.T++;
            sess.SumRR += o.ActualRR;
            if (o.Outcome == BacktestOutcome.Win) sess.W++;
            else if (o.Outcome == BacktestOutcome.Loss) sess.L++;
        }

        public BacktestStats ToStats()
        {
            if (_total == 0)
                return new BacktestStats(Label, Symbol, ExitStrategy, 0, 0, 0, 0, 0, 0, 0, 0, new(), new());

            var decided = _wins + _losses;
            var winRate = decided > 0 ? (decimal)_wins / decided : 0m;
            var avgRR   = _total > 0 ? _sumAllRR / _total : 0m;
            var pf      = _sumLossAbsRR > 0 ? _sumWinRR / _sumLossAbsRR
                        : _sumWinRR > 0 ? 999m : 0m;
            var ev      = winRate * avgRR - (1m - winRate);

            static SignalTypeStats MakeSess(Bucket b)
            {
                var d = b.W + b.L;
                return new SignalTypeStats(b.T, b.W, b.L,
                    d > 0 ? Math.Round((decimal)b.W / d, 4) : 0m,
                    b.T > 0 ? Math.Round(b.SumRR / b.T, 2) : 0m);
            }

            var byType = _byType.ToDictionary(kv => kv.Key, kv =>
            {
                var (t, w, l, sum) = kv.Value;
                var d = w + l;
                return new SignalTypeStats(t, w, l,
                    d > 0 ? Math.Round((decimal)w / d, 4) : 0m,
                    t > 0 ? Math.Round(sum / t, 2) : 0m);
            });

            var bySess = new Dictionary<string, SignalTypeStats>
            {
                ["KillZone"]    = MakeSess(_kz),
                ["NonKillZone"] = MakeSess(_nkz)
            };

            return new BacktestStats(Label, Symbol, ExitStrategy,
                _total, _wins, _losses, _scratches,
                Math.Round(winRate, 4), Math.Round(avgRR, 2),
                Math.Round(pf, 2), Math.Round(ev, 2),
                byType, bySess);
        }
    }

    // Internal signal record used only during walk-forward
    private record RawSignal(
        SignalType Type,
        SignalDirection Direction,
        decimal EntryPrice,
        decimal ZoneOpposite,
        decimal Invalidation,
        DateTime Time)
    {
        public RawSignal(SignalType type, SignalDirection dir, decimal entry, decimal zoneOpp, decimal inv)
            : this(type, dir, entry, zoneOpp, inv, DateTime.UtcNow) { }
    }
}
