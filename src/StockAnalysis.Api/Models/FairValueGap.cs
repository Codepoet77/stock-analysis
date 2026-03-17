namespace StockAnalysis.Api.Models;

public enum FvgType { Bullish, Bearish }

public record FairValueGap(
    FvgType Type,
    decimal Top,
    decimal Bottom,
    DateTime FormedAt,
    bool IsFilled
);
