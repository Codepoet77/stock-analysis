namespace StockAnalysis.Api.Models;

public enum LiquidityType { BuySide, SellSide }

public record LiquidityLevel(
    LiquidityType Type,
    decimal Price,
    string Label,
    bool IsSwept,
    DateTime? SweptAt
);
