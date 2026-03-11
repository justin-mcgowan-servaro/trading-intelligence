using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Infrastructure.Helpers;

public static class MarketSessionHelper
{
    private static readonly TimeZoneInfo SastZone =
        TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");

    private static readonly TimeZoneInfo EstZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static MarketSession CurrentSession()
    {
        var estNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EstZone);

        if (estNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return MarketSession.WeekendClosed;

        var time = estNow.TimeOfDay;

        return time switch
        {
            var t when t >= new TimeSpan(4, 0, 0) && t < new TimeSpan(9, 30, 0) => MarketSession.PreMarket,
            var t when t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(10, 30, 0) => MarketSession.MarketOpen,
            var t when t >= new TimeSpan(10, 30, 0) && t < new TimeSpan(14, 0, 0) => MarketSession.MidSession,
            var t when t >= new TimeSpan(14, 0, 0) && t < new TimeSpan(16, 0, 0) => MarketSession.MarketClose,
            var t when t >= new TimeSpan(16, 0, 0) && t < new TimeSpan(20, 0, 0) => MarketSession.AfterHours,
            _ => MarketSession.AfterHours
        };
    }

    public static string ToSast(DateTime utcDateTime)
    {
        var sast = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, SastZone);
        return sast.ToString("HH:mm SAST");
    }

    public static string ToEst(DateTime utcDateTime)
    {
        var est = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, EstZone);
        return est.ToString("HH:mm EST");
    }

    public static string SessionDisplayName(MarketSession session) => session switch
    {
        MarketSession.PreMarket => "Pre-Market",
        MarketSession.MarketOpen => "Market Open",
        MarketSession.MidSession => "Mid-Session",
        MarketSession.MarketClose => "Market Close",
        MarketSession.AfterHours => "After-Hours",
        MarketSession.WeekendClosed => "Weekend / Closed",
        _ => "Unknown"
    };
}
