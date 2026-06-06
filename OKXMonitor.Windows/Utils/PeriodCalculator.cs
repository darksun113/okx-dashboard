using System;

namespace OKXMonitor.Utils;

/// Start-of-period timestamps (Unix ms) in GMT+8 (Asia/Shanghai, no DST):
/// today 00:00, this week's Monday 00:00, this month's 1st 00:00.
/// Port of Store.swift's periodStartsMs; takes `utcNow` for deterministic tests.
public static class PeriodCalculator
{
    static readonly TimeZoneInfo Cn = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    public static (double today, double week, double month) PeriodStartsMs(DateTime utcNow)
    {
        var nowCn = TimeZoneInfo.ConvertTimeFromUtc(utcNow.ToUniversalTime(), Cn);

        var todayCn = nowCn.Date;                              // 00:00 CN today
        int dow = ((int)nowCn.DayOfWeek + 6) % 7;             // 0 = Monday
        var weekCn = todayCn.AddDays(-dow);                   // Monday 00:00 CN
        var monthCn = new DateTime(nowCn.Year, nowCn.Month, 1); // 1st 00:00 CN

        return (Ms(todayCn), Ms(weekCn), Ms(monthCn));
    }

    static double Ms(DateTime cnUnspecified)
    {
        var unspec = DateTime.SpecifyKind(cnUnspecified, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspec, Cn);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }
}
