using StockAnalysis.Api.Models;

namespace StockAnalysis.Api.Services;

public class MarketStateService
{
    private static readonly TimeZoneInfo EasternTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public MarketSession GetCurrentSession()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz);
        return GetSessionForTime(now);
    }

    public bool IsNyKillZone()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz);
        var killZoneStart = now.Date.AddHours(9).AddMinutes(30);
        var killZoneEnd = now.Date.AddHours(11);
        return now >= killZoneStart && now <= killZoneEnd;
    }

    public DateTime GetEasternNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz);

    private static MarketSession GetSessionForTime(DateTime easternTime)
    {
        var tod = easternTime.TimeOfDay;
        var preMarketStart = new TimeSpan(4, 0, 0);
        var regularOpen = new TimeSpan(9, 30, 0);
        var regularClose = new TimeSpan(16, 0, 0);
        var afterHoursEnd = new TimeSpan(20, 0, 0);

        // Check if it's a weekday
        if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
            return MarketSession.Closed;

        if (tod >= preMarketStart && tod < regularOpen) return MarketSession.PreMarket;
        if (tod >= regularOpen && tod < regularClose) return MarketSession.RegularHours;
        if (tod >= regularClose && tod < afterHoursEnd) return MarketSession.AfterHours;

        return MarketSession.Closed;
    }
}
