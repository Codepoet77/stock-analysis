namespace StockAnalysis.Api.Models;

public enum OrderBlockType { Bullish, Bearish }

public record OrderBlock(
    OrderBlockType Type,
    decimal Top,
    decimal Bottom,
    DateTime FormedAt,
    bool IsValid
);
