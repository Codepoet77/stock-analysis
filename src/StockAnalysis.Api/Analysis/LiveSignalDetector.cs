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
        BacktestParameters parameters)
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

        if (parameters.IsSignalEnabled(EnabledSignalType.OBRetest))
        {
            foreach (var ob in obs.Where(o => o.IsValid))
            {
                var mid = (ob.Top + ob.Bottom) / 2;
                if (Math.Abs(price - mid) / mid > 0.003m) continue;

                if (ob.Type == OrderBlockType.Bullish && CanLong())
                    signals.Add(new DetectedSignal(SignalType.OBRetest, SignalDirection.Long,
                        ob.Bottom, ob.Bottom * 0.9985m));
                if (ob.Type == OrderBlockType.Bearish && CanShort())
                    signals.Add(new DetectedSignal(SignalType.OBRetest, SignalDirection.Short,
                        ob.Top, ob.Top * 1.0015m));
            }
        }

        if (parameters.IsSignalEnabled(EnabledSignalType.FVGFill))
        {
            foreach (var fvg in fvgs.Where(f => !f.IsFilled))
            {
                if (price < fvg.Bottom || price > fvg.Top) continue;

                if (fvg.Type == FvgType.Bullish && CanLong())
                    signals.Add(new DetectedSignal(SignalType.FVGFill, SignalDirection.Long,
                        fvg.Bottom, fvg.Bottom * 0.9985m));
                if (fvg.Type == FvgType.Bearish && CanShort())
                    signals.Add(new DetectedSignal(SignalType.FVGFill, SignalDirection.Short,
                        fvg.Top, fvg.Top * 1.0015m));
            }
        }

        if (parameters.IsSignalEnabled(EnabledSignalType.LiquiditySweep))
        {
            foreach (var lvl in liquidity.Where(l => l.IsSwept))
            {
                if (lvl.Type == LiquidityType.SellSide && CanLong())
                    signals.Add(new DetectedSignal(SignalType.LiquiditySweep, SignalDirection.Long,
                        lvl.Price * 0.999m, lvl.Price * 0.997m));
                if (lvl.Type == LiquidityType.BuySide && CanShort())
                    signals.Add(new DetectedSignal(SignalType.LiquiditySweep, SignalDirection.Short,
                        lvl.Price, lvl.Price * 1.003m));
            }
        }

        if (parameters.IsSignalEnabled(EnabledSignalType.StructureBreak) && structure.StructureBreak)
        {
            if (localBias == StructureBias.Bullish && CanLong())
                signals.Add(new DetectedSignal(SignalType.StructureBreak, SignalDirection.Long,
                    price * 0.999m, price * 0.997m));
            if (localBias == StructureBias.Bearish && CanShort())
                signals.Add(new DetectedSignal(SignalType.StructureBreak, SignalDirection.Short,
                    price * 1.001m, price * 1.003m));
        }

        // Cap to 1 per direction — most recently added wins
        var lastLong  = signals.FindLast(s => s.Direction == SignalDirection.Long);
        var lastShort = signals.FindLast(s => s.Direction == SignalDirection.Short);
        signals.Clear();
        if (lastLong  is not null) signals.Add(lastLong);
        if (lastShort is not null) signals.Add(lastShort);

        return signals;
    }
}

public record DetectedSignal(
    SignalType Type,
    SignalDirection Direction,
    decimal EntryPrice,
    decimal Stop);
