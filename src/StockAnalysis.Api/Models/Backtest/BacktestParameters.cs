namespace StockAnalysis.Api.Models.Backtest;

public enum BacktestTimeframe { OneMinute, FiveMinute, FifteenMinute, OneHour, FourHour }
public enum ExitStrategy { FixedR, BreakevenStop, TrailingStop, NextLiquidity, All, Both = All }
public enum EnabledSignalType { OBRetest, FVGFill, LiquiditySweep, StructureBreak }

/// <summary>Which part of the regular session to test signals in.</summary>
public enum SessionFilter
{
    Full,     // 9:30–16:00 ET
    OpenKZ,   // 9:30–11:00 ET (NY open kill zone)
    CloseKZ,  // 14:00–16:00 ET (PM power hour)
    BothKZ,   // 9:30–11:00 OR 14:00–16:00
}

public record BacktestParameters(
    string[] Symbols,
    int DaysBack,
    BacktestTimeframe[] Timeframes,
    ExitStrategy ExitStrategy,
    decimal FixedRRatio,
    SessionFilter SessionFilter = SessionFilter.Full,
    EnabledSignalType[]? SignalTypes = null  // null = all signals enabled
)
{
    public bool IsSignalEnabled(EnabledSignalType t) =>
        SignalTypes == null || SignalTypes.Length == 0 || SignalTypes.Contains(t);

    public bool IsInSession(TimeSpan etTimeOfDay) => SessionFilter switch
    {
        SessionFilter.OpenKZ  => etTimeOfDay >= new TimeSpan(9,  30, 0) && etTimeOfDay <  new TimeSpan(11, 0, 0),
        SessionFilter.CloseKZ => etTimeOfDay >= new TimeSpan(14,  0, 0) && etTimeOfDay <  new TimeSpan(16, 0, 0),
        SessionFilter.BothKZ  => (etTimeOfDay >= new TimeSpan(9,  30, 0) && etTimeOfDay <  new TimeSpan(11, 0, 0))
                              || (etTimeOfDay >= new TimeSpan(14,  0, 0) && etTimeOfDay <  new TimeSpan(16, 0, 0)),
        _                     => true, // Full — no additional restriction beyond trading hours filter
    };

    public static BacktestParameters Default => new(
        Symbols: ["SPY", "QQQ"],
        DaysBack: 730,
        Timeframes: [
            BacktestTimeframe.OneMinute,
            BacktestTimeframe.FiveMinute,
            BacktestTimeframe.FifteenMinute,
            BacktestTimeframe.OneHour,
            BacktestTimeframe.FourHour
        ],
        ExitStrategy: ExitStrategy.All,
        FixedRRatio: 2.0m,
        SessionFilter: SessionFilter.Full,
        SignalTypes: null
    );
}

public static class TimeframeHelper
{
    public static (int Multiplier, string Timespan) ToPolygon(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute    => (1,  "minute"),
        BacktestTimeframe.FiveMinute   => (5,  "minute"),
        BacktestTimeframe.FifteenMinute => (15, "minute"),
        BacktestTimeframe.OneHour      => (1,  "hour"),
        BacktestTimeframe.FourHour     => (4,  "hour"),
        _ => (1, "minute")
    };

    public static string Label(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute    => "1M",
        BacktestTimeframe.FiveMinute   => "5M",
        BacktestTimeframe.FifteenMinute => "15M",
        BacktestTimeframe.OneHour      => "1H",
        BacktestTimeframe.FourHour     => "4H",
        _ => "?"
    };

    public static int Lookback(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute    => 100,
        BacktestTimeframe.FiveMinute   => 80,
        BacktestTimeframe.FifteenMinute => 60,
        BacktestTimeframe.OneHour      => 50,
        BacktestTimeframe.FourHour     => 30,
        _ => 50
    };

    public static int MaxForwardBars(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute    => 60,
        BacktestTimeframe.FiveMinute   => 48,
        BacktestTimeframe.FifteenMinute => 32,
        BacktestTimeframe.OneHour      => 24,
        BacktestTimeframe.FourHour     => 10,
        _ => 24
    };

    // Months per chunk when fetching from Polygon
    public static int FetchChunkMonths(BacktestTimeframe tf) => tf switch
    {
        BacktestTimeframe.OneMinute    => 2,
        BacktestTimeframe.FiveMinute   => 3,
        BacktestTimeframe.FifteenMinute => 6,
        BacktestTimeframe.OneHour      => 12,
        BacktestTimeframe.FourHour     => 24,
        _ => 6
    };
}

public record TimeframeCombination(
    string Label,
    BacktestTimeframe? BiasTf,
    BacktestTimeframe? ConfirmTf,
    BacktestTimeframe SignalTf
)
{
    public static IReadOnlyList<TimeframeCombination> All =>
    [
        new("4H+1H",      BacktestTimeframe.FourHour,     null,                          BacktestTimeframe.OneHour),
        new("4H+15M",     BacktestTimeframe.FourHour,     null,                          BacktestTimeframe.FifteenMinute),
        new("4H+5M",      BacktestTimeframe.FourHour,     null,                          BacktestTimeframe.FiveMinute),
        new("1H+15M",     BacktestTimeframe.OneHour,      null,                          BacktestTimeframe.FifteenMinute),
        new("1H+5M",      BacktestTimeframe.OneHour,      null,                          BacktestTimeframe.FiveMinute),
        new("1H+1M",      BacktestTimeframe.OneHour,      null,                          BacktestTimeframe.OneMinute),
        new("15M+5M",     BacktestTimeframe.FifteenMinute,null,                          BacktestTimeframe.FiveMinute),
        new("15M+1M",     BacktestTimeframe.FifteenMinute,null,                          BacktestTimeframe.OneMinute),
        new("4H+1H+15M",  BacktestTimeframe.FourHour,     BacktestTimeframe.OneHour,     BacktestTimeframe.FifteenMinute),
        new("4H+1H+5M",   BacktestTimeframe.FourHour,     BacktestTimeframe.OneHour,     BacktestTimeframe.FiveMinute),
        new("4H+1H+1M",   BacktestTimeframe.FourHour,     BacktestTimeframe.OneHour,     BacktestTimeframe.OneMinute),
        new("1H+15M+1M",  BacktestTimeframe.OneHour,      BacktestTimeframe.FifteenMinute, BacktestTimeframe.OneMinute),
    ];
}
