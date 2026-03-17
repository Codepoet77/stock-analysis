namespace StockAnalysis.Api.Models;

public enum StructureBias { Bullish, Bearish, Neutral }
public enum SwingType { High, Low }

public record SwingPoint(SwingType Type, decimal Price, DateTime Time);

public record MarketStructure(
    StructureBias Bias,
    List<SwingPoint> SwingPoints,
    bool StructureBreak,
    string StructureBreakDescription
);
