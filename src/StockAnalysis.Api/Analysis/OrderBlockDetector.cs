using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Analysis;

public static class OrderBlockDetector
{
    private const int ImpulseLookforward = 3;

    /// <summary>
    /// Bullish OB: Last bearish candle before a bullish impulse move.
    /// Bearish OB: Last bullish candle before a bearish impulse move.
    /// </summary>
    public static List<OrderBlock> Detect(List<Bar> bars)
    {
        var orderBlocks = new List<OrderBlock>();

        for (int i = 1; i < bars.Count - ImpulseLookforward; i++)
        {
            var bar = bars[i];
            bool isBearishCandle = bar.Close < bar.Open;
            bool isBullishCandle = bar.Close > bar.Open;

            // Check for bullish impulse after a bearish candle
            if (isBearishCandle && IsBullishImpulse(bars, i + 1, ImpulseLookforward))
            {
                orderBlocks.Add(new OrderBlock(
                    OrderBlockType.Bullish,
                    Top: bar.Open,
                    Bottom: bar.Low,
                    FormedAt: bar.Time,
                    IsValid: true
                ));
            }

            // Check for bearish impulse after a bullish candle
            if (isBullishCandle && IsBearishImpulse(bars, i + 1, ImpulseLookforward))
            {
                orderBlocks.Add(new OrderBlock(
                    OrderBlockType.Bearish,
                    Top: bar.High,
                    Bottom: bar.Open,
                    FormedAt: bar.Time,
                    IsValid: true
                ));
            }
        }

        // Invalidate OBs that have been violated by price
        var lastBar = bars.LastOrDefault();
        if (lastBar != null)
        {
            return orderBlocks.Select(ob =>
            {
                bool violated = ob.Type == OrderBlockType.Bullish
                    ? lastBar.Close < ob.Bottom
                    : lastBar.Close > ob.Top;

                return ob with { IsValid = !violated };
            }).ToList();
        }

        return orderBlocks;
    }

    private static bool IsBullishImpulse(List<Bar> bars, int startIdx, int length)
    {
        if (startIdx + length > bars.Count) return false;
        var startPrice = bars[startIdx].Open;
        var endPrice = bars[startIdx + length - 1].Close;
        var impulseSize = endPrice - startPrice;
        return impulseSize > 0 && bars.Skip(startIdx).Take(length).All(b => b.Close > b.Open);
    }

    private static bool IsBearishImpulse(List<Bar> bars, int startIdx, int length)
    {
        if (startIdx + length > bars.Count) return false;
        var startPrice = bars[startIdx].Open;
        var endPrice = bars[startIdx + length - 1].Close;
        var impulseSize = startPrice - endPrice;
        return impulseSize > 0 && bars.Skip(startIdx).Take(length).All(b => b.Close < b.Open);
    }
}
