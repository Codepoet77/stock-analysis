namespace StockAnalysis.Api.Models;

public record Bar(
    string Symbol,
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
);
