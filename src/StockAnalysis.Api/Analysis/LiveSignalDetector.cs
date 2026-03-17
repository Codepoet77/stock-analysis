using Microsoft.Extensions.Logging;
using StockAnalysis.Api.Models;
using StockAnalysis.Api.Models.Backtest;

namespace StockAnalysis.Api.Analysis;

/// <summary>
/// Shared signal generation logic used by both BacktestEngine and LiveSignalEngine.
/// </summary>
public static class LiveSignalDetector
{
    public static List<DetectedSignal> GenerateSignals(
        Bar bar,
        MarketStructure structure,
        List<FairValueGap> fvgs,
        List<OrderBlock> obs,
        List<LiquidityLevel> liquidity,
        StructureBias? htfBias,
        StructureBias? confirmBias,
        BacktestParameters parameters,
        ILogger? logger = null)
    {
        var signals = new List<DetectedSignal>();
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

        var canLong = CanLong();
        var canShort = CanShort();
        logger?.LogDebug("  GenerateSignals: price={Price:F2} localBias={Bias} canLong={CanLong} canShort={CanShort}",
            price, localBias, canLong, canShort);

        if (parameters.IsSignalEnabled(EnabledSignalType.OBRetest))
        {
            var validObs = obs.Where(o => o.IsValid).ToList();
            logger?.LogDebug("  OBRetest: checking {Count} valid order blocks", validObs.Count);
            foreach (var ob in validObs)
            {
                var mid = (ob.Top + ob.Bottom) / 2;
                var dist = Math.Abs(price - mid) / mid;
                if (dist > 0.003m)
                {
                    logger?.LogDebug("  OBRetest: {Type} OB {Top:F2}-{Bottom:F2} too far (dist={Dist:P3} > 0.3%)",
                        ob.Type, ob.Top, ob.Bottom, dist);
                    continue;
                }

                logger?.LogDebug("  OBRetest: {Type} OB {Top:F2}-{Bottom:F2} IN RANGE (dist={Dist:P3})",
                    ob.Type, ob.Top, ob.Bottom, dist);

                if (ob.Type == OrderBlockType.Bullish && canLong)
                    signals.Add(new DetectedSignal(SignalType.OBRetest, SignalDirection.Long,
                        ob.Bottom, ob.Bottom * 0.9985m));
                else if (ob.Type == OrderBlockType.Bullish)
                    logger?.LogDebug("  OBRetest: bullish OB in range but canLong=false (bias conflict)");

                if (ob.Type == OrderBlockType.Bearish && canShort)
                    signals.Add(new DetectedSignal(SignalType.OBRetest, SignalDirection.Short,
                        ob.Top, ob.Top * 1.0015m));
                else if (ob.Type == OrderBlockType.Bearish)
                    logger?.LogDebug("  OBRetest: bearish OB in range but canShort=false (bias conflict)");
            }
        }

        if (parameters.IsSignalEnabled(EnabledSignalType.FVGFill))
        {
            var openFvgs = fvgs.Where(f => !f.IsFilled).ToList();
            logger?.LogDebug("  FVGFill: checking {Count} open FVGs", openFvgs.Count);
            foreach (var fvg in openFvgs)
            {
                if (price < fvg.Bottom || price > fvg.Top)
                {
                    logger?.LogDebug("  FVGFill: {Type} FVG {Top:F2}-{Bottom:F2} price outside gap",
                        fvg.Type, fvg.Top, fvg.Bottom);
                    continue;
                }

                logger?.LogDebug("  FVGFill: {Type} FVG {Top:F2}-{Bottom:F2} PRICE INSIDE GAP",
                    fvg.Type, fvg.Top, fvg.Bottom);

                if (fvg.Type == FvgType.Bullish && canLong)
                    signals.Add(new DetectedSignal(SignalType.FVGFill, SignalDirection.Long,
                        fvg.Bottom, fvg.Bottom * 0.9985m));
                else if (fvg.Type == FvgType.Bullish)
                    logger?.LogDebug("  FVGFill: bullish FVG in range but canLong=false (bias conflict)");

                if (fvg.Type == FvgType.Bearish && canShort)
                    signals.Add(new DetectedSignal(SignalType.FVGFill, SignalDirection.Short,
                        fvg.Top, fvg.Top * 1.0015m));
                else if (fvg.Type == FvgType.Bearish)
                    logger?.LogDebug("  FVGFill: bearish FVG in range but canShort=false (bias conflict)");
            }
        }

        if (parameters.IsSignalEnabled(EnabledSignalType.LiquiditySweep))
        {
            var swept = liquidity.Where(l => l.IsSwept).ToList();
            logger?.LogDebug("  LiqSweep: {Count} swept levels", swept.Count);
            foreach (var lvl in swept)
            {
                if (lvl.Type == LiquidityType.SellSide && canLong)
                    signals.Add(new DetectedSignal(SignalType.LiquiditySweep, SignalDirection.Long,
                        lvl.Price * 0.999m, lvl.Price * 0.997m));
                if (lvl.Type == LiquidityType.BuySide && canShort)
                    signals.Add(new DetectedSignal(SignalType.LiquiditySweep, SignalDirection.Short,
                        lvl.Price, lvl.Price * 1.003m));
            }
        }

        if (parameters.IsSignalEnabled(EnabledSignalType.StructureBreak))
        {
            logger?.LogDebug("  StructureBreak: structureBreak={Break}", structure.StructureBreak);
            if (structure.StructureBreak)
            {
                if (localBias == StructureBias.Bullish && canLong)
                    signals.Add(new DetectedSignal(SignalType.StructureBreak, SignalDirection.Long,
                        price * 0.999m, price * 0.997m));
                if (localBias == StructureBias.Bearish && canShort)
                    signals.Add(new DetectedSignal(SignalType.StructureBreak, SignalDirection.Short,
                        price * 1.001m, price * 1.003m));
            }
        }

        logger?.LogDebug("  GenerateSignals: {Count} raw signals before dedup", signals.Count);

        // Cap to 1 per direction — most recently added wins
        var lastLong  = signals.FindLast(s => s.Direction == SignalDirection.Long);
        var lastShort = signals.FindLast(s => s.Direction == SignalDirection.Short);
        signals.Clear();
        if (lastLong  is not null) signals.Add(lastLong);
        if (lastShort is not null) signals.Add(lastShort);

        logger?.LogDebug("  GenerateSignals: {Count} signals after dedup", signals.Count);

        return signals;
    }
}

public record DetectedSignal(
    SignalType Type,
    SignalDirection Direction,
    decimal EntryPrice,
    decimal Stop);
