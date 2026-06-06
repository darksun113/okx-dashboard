using System;
using OKXMonitor.Utils;
using Xunit;

public class PeriodCalculatorTests
{
    static readonly TimeZoneInfo Cn = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    static DateTime CnOf(double ms) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime, Cn);

    [Fact]
    public void Today_start_is_cn_midnight_of_now()
    {
        var now = new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc); // 09:00 CN
        var (today, _, _) = PeriodCalculator.PeriodStartsMs(now);
        var t = CnOf(today);
        Assert.Equal(new DateTime(2026, 6, 6), t.Date);
        Assert.Equal(0, t.Hour);
        Assert.Equal(0, t.Minute);
    }

    [Fact]
    public void Week_start_is_a_cn_monday_not_after_now()
    {
        var now = new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc); // Sat 6 Jun, 09:00 CN
        var (_, week, _) = PeriodCalculator.PeriodStartsMs(now);
        var w = CnOf(week);
        Assert.Equal(DayOfWeek.Monday, w.DayOfWeek);
        Assert.Equal(new DateTime(2026, 6, 1), w.Date); // Mon 1 Jun
    }

    [Fact]
    public void Month_start_is_first_of_cn_month()
    {
        var now = new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc);
        var (_, _, month) = PeriodCalculator.PeriodStartsMs(now);
        var m = CnOf(month);
        Assert.Equal(1, m.Day);
        Assert.Equal(6, m.Month);
        Assert.Equal(0, m.Hour);
    }
}
