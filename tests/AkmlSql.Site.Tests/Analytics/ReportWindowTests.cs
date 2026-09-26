using AkmlSql.Site.Analytics;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Reporting windows in the owner's timezone.
/// <para>
/// Uses the real Cairo zone from the operating system rather than a hand-built one, because the
/// point is to agree with the calendar the owner actually lives on — including Egypt's summer-time
/// switch, which in 2026 happens at MIDNIGHT (Fri 24 Apr: 00:00 does not exist) and back again at
/// the end of Thu 29 Oct (23:00–23:59 happens twice). Those dates were read from the timezone
/// database, not assumed.
/// </para>
/// </summary>
public sealed class ReportWindowTests
{
    private static readonly TimeZoneInfo Cairo = FindCairo();

    private static TimeZoneInfo FindCairo()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
    }

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi = 0) =>
        new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ the reported bug

    [Fact]
    public void Today_IsTheOwnersDay_NotTheUtcDay()
    {
        // 01:00 on 22 Sep in Cairo (summer time, UTC+3) is still 21 Sep in UTC. The old windows
        // called this moment the 21st; the owner's calendar says the 22nd.
        var now = Utc(2026, 9, 21, 22, 0);

        var today = ReportWindow.Resolve(ReportRange.Today, now, Cairo);

        Assert.Equal(new DateOnly(2026, 9, 22), today.FirstDay);
        Assert.Equal(new DateOnly(2026, 9, 22), today.LastDay);
        Assert.Equal(Utc(2026, 9, 21, 21, 0), today.FromUtc);   // Cairo midnight
        Assert.Equal(Utc(2026, 9, 22, 21, 0), today.ToUtc);     // the next Cairo midnight
    }

    [Fact]
    public void Yesterday_IsOneWholeLocalDay_WithBothEndsClosed()
    {
        var now = Utc(2026, 9, 22, 10, 0);   // 13:00 Cairo, 22 Sep

        var yesterday = ReportWindow.Resolve(ReportRange.Yesterday, now, Cairo);

        Assert.Equal(new DateOnly(2026, 9, 21), yesterday.FirstDay);
        Assert.Equal(yesterday.FirstDay, yesterday.LastDay);
        Assert.Equal(Utc(2026, 9, 20, 21, 0), yesterday.FromUtc);
        Assert.Equal(Utc(2026, 9, 21, 21, 0), yesterday.ToUtc);   // closed: ends at today's midnight
    }

    [Fact]
    public void LastSevenDays_CoversSevenCalendarDaysIncludingToday()
    {
        var now = Utc(2026, 9, 22, 10, 0);

        var week = ReportWindow.Resolve(ReportRange.Last7, now, Cairo);

        Assert.Equal(7, week.Days.Count());
        Assert.Equal(new DateOnly(2026, 9, 16), week.FirstDay);
        Assert.Equal(new DateOnly(2026, 9, 22), week.LastDay);
        Assert.Equal(Utc(2026, 9, 22, 21, 0), week.ToUtc);   // end of today, so an event at "now" counts
    }

    // ------------------------------------------------------------------ comparison periods

    [Fact]
    public void TodaySoFar_IsComparedWithYesterdayUpToTheSameTime()
    {
        // Comparing four hours of today with all twenty-four of yesterday would show every
        // morning as a collapse. The comparison stops at the same local time.
        var now = Utc(2026, 9, 22, 7, 0);   // 10:00 Cairo

        var previous = ReportWindow.Resolve(ReportRange.Today, now, Cairo).Previous();

        Assert.Equal(new DateOnly(2026, 9, 21), previous.FirstDay);
        Assert.Equal(Utc(2026, 9, 20, 21, 0), previous.FromUtc);   // yesterday's midnight
        Assert.Equal(Utc(2026, 9, 21, 7, 0), previous.ToUtc);       // yesterday 10:00
    }

    [Fact]
    public void Yesterday_IsComparedWithTheWholeDayBefore()
    {
        var now = Utc(2026, 9, 22, 7, 0);

        var previous = ReportWindow.Resolve(ReportRange.Yesterday, now, Cairo).Previous();

        Assert.Equal(new DateOnly(2026, 9, 20), previous.FirstDay);
        Assert.Equal(Utc(2026, 9, 19, 21, 0), previous.FromUtc);
        Assert.Equal(Utc(2026, 9, 20, 21, 0), previous.ToUtc);
    }

    [Fact]
    public void LastSevenDays_IsComparedWithTheSevenBefore_UpToTheSameMoment()
    {
        var now = Utc(2026, 9, 22, 7, 0);

        var previous = ReportWindow.Resolve(ReportRange.Last7, now, Cairo).Previous();

        Assert.Equal(new DateOnly(2026, 9, 9), previous.FirstDay);
        Assert.Equal(new DateOnly(2026, 9, 15), previous.LastDay);
        Assert.Equal(Utc(2026, 9, 15, 7, 0), previous.ToUtc);
    }

    // ------------------------------------------------------------------ daylight saving

    [Fact]
    public void TheDaySummerTimeStarts_BeginsAtItsFirstRealMinute()
    {
        // Fri 24 Apr 2026: Cairo goes from 23:59:59 straight to 01:00. Local midnight does not
        // exist, and converting it throws — the day starts at 01:00 summer time (UTC+3).
        var start = ReportWindow.StartOfDayUtc(new DateOnly(2026, 4, 24), Cairo);

        Assert.Equal(Utc(2026, 4, 23, 22, 0), start);

        // So Friday itself is the short day -- 23 hours, missing its first. (Not Thursday: Thursday
        // runs a full 00:00-24:00 in winter time and hands over to Friday's 01:00.)
        var saturday = ReportWindow.StartOfDayUtc(new DateOnly(2026, 4, 25), Cairo);
        var thursday = ReportWindow.StartOfDayUtc(new DateOnly(2026, 4, 23), Cairo);
        Assert.Equal(TimeSpan.FromHours(23), saturday - start);
        Assert.Equal(TimeSpan.FromHours(24), start - thursday);
    }

    [Fact]
    public void TheDaySummerTimeEnds_IsTwentyFiveHoursLong()
    {
        // Thu 29 Oct 2026: 23:00-23:59 happens twice. The day still starts and ends at a midnight,
        // but it is 25 hours long — a fixed "+3 hours" would misplace one of them.
        var start = ReportWindow.StartOfDayUtc(new DateOnly(2026, 10, 29), Cairo);
        var next = ReportWindow.StartOfDayUtc(new DateOnly(2026, 10, 30), Cairo);

        Assert.Equal(Utc(2026, 10, 28, 21, 0), start);   // midnight, summer time
        Assert.Equal(Utc(2026, 10, 29, 22, 0), next);    // midnight, winter time
        Assert.Equal(TimeSpan.FromHours(25), next - start);
    }

    [Fact]
    public void AnAmbiguousLocalTime_ResolvesToItsFirstOccurrence()
    {
        // 23:30 on 29 Oct happens at 20:30Z (summer) and again at 21:30Z (winter). A window that
        // starts there must start at the earlier one, or it silently drops an hour of data.
        var utc = ReportWindow.LocalToUtc(new DateTime(2026, 10, 29, 23, 30, 0), Cairo);

        Assert.Equal(Utc(2026, 10, 29, 20, 30), utc);
    }

    [Fact]
    public void TheComparisonPeriod_LinesUpWallClockTimes_AcrossASummerTimeChange()
    {
        // 10:00 Cairo on 24 Apr (the first summer-time day, UTC+3) compared with 10:00 on 23 Apr
        // (still winter time, UTC+2). Shifting by 24 hours would compare 10:00 with 11:00.
        var now = Utc(2026, 4, 24, 7, 0);   // 10:00 local, UTC+3

        var previous = ReportWindow.Resolve(ReportRange.Today, now, Cairo).Previous();

        Assert.Equal(Utc(2026, 4, 23, 8, 0), previous.ToUtc);   // 10:00 local, UTC+2
    }

    // ------------------------------------------------------------------ parsing and naming

    [Theory]
    [InlineData("today", "today")]
    [InlineData("TODAY", "today")]
    [InlineData("yesterday", "yesterday")]
    [InlineData("7", "7")]
    [InlineData("365", "365")]
    [InlineData(null, "30")]
    [InlineData("", "30")]
    [InlineData("tomorrow", "30")]
    [InlineData("<script>", "30")]
    public void Parse_AcceptsKnownRanges_AndFallsBackForAnythingElse(string? key, string expected)
    {
        Assert.Equal(expected, ReportRange.Parse(key).Key);
    }

    [Fact]
    public void Describe_NamesTheTimezone_SoTheFigureIsNeverAmbiguous()
    {
        var window = ReportWindow.Resolve(ReportRange.Today, Utc(2026, 9, 22, 7, 0), Cairo);

        var text = window.Describe();

        Assert.Contains("Cairo", text, StringComparison.Ordinal);
        Assert.Contains("UTC+03:00", text, StringComparison.Ordinal);
        Assert.Contains("22 Sep", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InUtc_TheWindowsMatchTheOldBehaviour()
    {
        // A store with no timezone configured reports in UTC, exactly as before, so every existing
        // caller and test keeps meaning what it meant.
        var now = Utc(2026, 9, 22, 7, 0);

        var week = ReportWindow.Resolve(ReportRange.LastDays(7), now, TimeZoneInfo.Utc);

        Assert.Equal(Utc(2026, 9, 16, 0, 0), week.FromUtc);
        Assert.Equal(new DateOnly(2026, 9, 16), week.FirstDay);
    }
}
