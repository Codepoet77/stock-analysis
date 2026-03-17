namespace StockAnalysis.Api.Models;

public enum MarketSession { PreMarket, RegularHours, AfterHours, Closed }

public record AnalysisResult(
    string Symbol,
    DateTime AnalyzedAt,
    MarketSession Session,
    decimal CurrentPrice,
    decimal PreviousDayHigh,
    decimal PreviousDayLow,
    decimal PreviousDayClose,
    MarketStructure MarketStructure,
    List<FairValueGap> FairValueGaps,
    List<OrderBlock> OrderBlocks,
    List<LiquidityLevel> LiquidityLevels,
    bool IsNyKillZone,
    List<Bar> RecentBars
);
