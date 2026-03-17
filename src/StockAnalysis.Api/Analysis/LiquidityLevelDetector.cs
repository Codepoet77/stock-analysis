using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Analysis;

public static class LiquidityLevelDetector
{
    public static List<LiquidityLevel> Detect(List<Bar> bars, decimal previousDayHigh, decimal previousDayLow)
    {
        var levels = new List<LiquidityLevel>();
        var lastBar = bars.LastOrDefault();

        // Previous Day High — buy-side liquidity sits above it (stop losses of shorts)
        var pdh = new LiquidityLevel(
            LiquidityType.BuySide,
            previousDayHigh,
            "PDH",
            IsSwept: lastBar != null && lastBar.High > previousDayHigh,
            SweptAt: null
        );
        levels.Add(pdh);

        // Previous Day Low — sell-side liquidity sits below it (stop losses of longs)
        var pdl = new LiquidityLevel(
            LiquidityType.SellSide,
            previousDayLow,
            "PDL",
            IsSwept: lastBar != null && lastBar.Low < previousDayLow,
            SweptAt: null
        );
        levels.Add(pdl);

        // Detect equal highs / lows from intraday bars (pools of liquidity)
        if (bars.Count >= 10)
        {
            var swingHighs = DetectSwingHighs(bars);
            var swingLows = DetectSwingLows(bars);

            // Equal highs — clusters within 0.05% of each other
            var equalHighGroups = GroupNearLevels(swingHighs, 0.0005m);
            foreach (var group in equalHighGroups.Where(g => g.Count >= 2))
            {
                levels.Add(new LiquidityLevel(
                    LiquidityType.BuySide,
                    group.Average(),
                    $"Equal Highs ({group.Count})",
                    IsSwept: lastBar != null && lastBar.High > group.Average(),
                    SweptAt: null
                ));
            }

            // Equal lows
            var equalLowGroups = GroupNearLevels(swingLows, 0.0005m);
            foreach (var group in equalLowGroups.Where(g => g.Count >= 2))
            {
                levels.Add(new LiquidityLevel(
                    LiquidityType.SellSide,
                    group.Average(),
                    $"Equal Lows ({group.Count})",
                    IsSwept: lastBar != null && lastBar.Low < group.Average(),
                    SweptAt: null
                ));
            }
        }

        return levels;
    }

    private static List<decimal> DetectSwingHighs(List<Bar> bars)
    {
        var highs = new List<decimal>();
        for (int i = 2; i < bars.Count - 2; i++)
        {
            if (bars[i].High > bars[i - 1].High && bars[i].High > bars[i - 2].High &&
                bars[i].High > bars[i + 1].High && bars[i].High > bars[i + 2].High)
                highs.Add(bars[i].High);
        }
        return highs;
    }

    private static List<decimal> DetectSwingLows(List<Bar> bars)
    {
        var lows = new List<decimal>();
        for (int i = 2; i < bars.Count - 2; i++)
        {
            if (bars[i].Low < bars[i - 1].Low && bars[i].Low < bars[i - 2].Low &&
                bars[i].Low < bars[i + 1].Low && bars[i].Low < bars[i + 2].Low)
                lows.Add(bars[i].Low);
        }
        return lows;
    }

    private static List<List<decimal>> GroupNearLevels(List<decimal> levels, decimal tolerancePct)
    {
        var groups = new List<List<decimal>>();
        foreach (var level in levels.OrderBy(l => l))
        {
            var matched = groups.FirstOrDefault(g =>
                Math.Abs(g.Average() - level) / level <= tolerancePct);

            if (matched != null) matched.Add(level);
            else groups.Add([level]);
        }
        return groups;
    }
}
