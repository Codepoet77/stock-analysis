using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Analysis;

public static class FairValueGapDetector
{
    /// <summary>
    /// Detects Fair Value Gaps (3-candle imbalances).
    /// Bullish FVG: candle[i+2].Low > candle[i].High  (gap above candle 1)
    /// Bearish FVG: candle[i+2].High < candle[i].Low  (gap below candle 1)
    /// </summary>
    public static List<FairValueGap> Detect(List<Bar> bars)
    {
        var fvgs = new List<FairValueGap>();

        for (int i = 0; i < bars.Count - 2; i++)
        {
            var c1 = bars[i];
            var c3 = bars[i + 2];

            // Bullish FVG: gap between c1 high and c3 low
            if (c3.Low > c1.High)
            {
                fvgs.Add(new FairValueGap(
                    FvgType.Bullish,
                    Top: c3.Low,
                    Bottom: c1.High,
                    FormedAt: c3.Time,
                    IsFilled: false
                ));
            }

            // Bearish FVG: gap between c1 low and c3 high
            if (c3.High < c1.Low)
            {
                fvgs.Add(new FairValueGap(
                    FvgType.Bearish,
                    Top: c1.Low,
                    Bottom: c3.High,
                    FormedAt: c3.Time,
                    IsFilled: false
                ));
            }
        }

        // Mark filled FVGs based on subsequent price action
        var lastBar = bars.LastOrDefault();
        if (lastBar != null)
        {
            return fvgs.Select(fvg =>
            {
                bool filled = fvg.Type == FvgType.Bullish
                    ? lastBar.Low <= fvg.Bottom
                    : lastBar.High >= fvg.Top;

                return fvg with { IsFilled = filled };
            }).ToList();
        }

        return fvgs;
    }
}
