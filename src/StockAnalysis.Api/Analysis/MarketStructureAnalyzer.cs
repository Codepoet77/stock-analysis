using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Analysis;

public static class MarketStructureAnalyzer
{
    private const int SwingLookback = 3;

    public static MarketStructure Analyze(List<Bar> bars)
    {
        if (bars.Count < SwingLookback * 2 + 1)
            return new MarketStructure(StructureBias.Neutral, [], false, "Insufficient data");

        var swings = DetectSwings(bars);
        if (swings.Count < 2)
            return new MarketStructure(StructureBias.Neutral, swings, false, "Not enough swings detected");

        var bias = DetermineBias(swings, out bool structureBreak, out string breakDesc);
        return new MarketStructure(bias, swings, structureBreak, breakDesc);
    }

    private static List<SwingPoint> DetectSwings(List<Bar> bars)
    {
        var swings = new List<SwingPoint>();

        for (int i = SwingLookback; i < bars.Count - SwingLookback; i++)
        {
            var bar = bars[i];
            bool isSwingHigh = true;
            bool isSwingLow = true;

            for (int j = i - SwingLookback; j <= i + SwingLookback; j++)
            {
                if (j == i) continue;
                if (bars[j].High >= bar.High) isSwingHigh = false;
                if (bars[j].Low <= bar.Low) isSwingLow = false;
            }

            if (isSwingHigh) swings.Add(new SwingPoint(SwingType.High, bar.High, bar.Time));
            if (isSwingLow) swings.Add(new SwingPoint(SwingType.Low, bar.Low, bar.Time));
        }

        return swings.OrderBy(s => s.Time).ToList();
    }

    private static StructureBias DetermineBias(List<SwingPoint> swings, out bool structureBreak, out string breakDesc)
    {
        structureBreak = false;
        breakDesc = string.Empty;

        var highs = swings.Where(s => s.Type == SwingType.High).TakeLast(3).ToList();
        var lows = swings.Where(s => s.Type == SwingType.Low).TakeLast(3).ToList();

        if (highs.Count < 2 || lows.Count < 2) return StructureBias.Neutral;

        bool higherHighs = highs[^1].Price > highs[^2].Price;
        bool higherLows = lows[^1].Price > lows[^2].Price;
        bool lowerHighs = highs[^1].Price < highs[^2].Price;
        bool lowerLows = lows[^1].Price < lows[^2].Price;

        // Structure break detection
        if (higherHighs && lows[^1].Price < lows[^2].Price)
        {
            structureBreak = true;
            breakDesc = "Bearish Structure Break — HL broken";
            return StructureBias.Bearish;
        }

        if (lowerLows && highs[^1].Price > highs[^2].Price)
        {
            structureBreak = true;
            breakDesc = "Bullish Structure Break — LH broken";
            return StructureBias.Bullish;
        }

        if (higherHighs && higherLows) return StructureBias.Bullish;
        if (lowerHighs && lowerLows) return StructureBias.Bearish;

        return StructureBias.Neutral;
    }
}
