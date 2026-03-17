namespace StockAnalysis.Api.Models;

public enum SignalDirection { Long, Short }
public enum SignalType { OBRetest, FVGFill, LiquiditySweep, StructureBreak, KillZoneConfluence }
public enum SignalStrength { Low, Medium, High }

public record ConfluenceFactor(string Description, bool Positive);

public record TradeSignal(
    string Symbol,
    SignalType Type,
    SignalDirection Direction,
    decimal EntryZoneTop,
    decimal EntryZoneBottom,
    decimal Target,
    decimal Invalidation,
    SignalStrength Strength,
    int ConfluenceScore,
    List<ConfluenceFactor> ConfluenceFactors,
    string Description,
    DateTime GeneratedAt
);
