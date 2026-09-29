using System;
using AkmlSql.Core.Models.History;
using Xunit;

namespace AkmlSql.Core.Tests.History;

/// <summary>
/// Spec 040 (T112, HIS-08) — SQL History groups its rows like SQL Prompt: Today, Yesterday, This
/// week, Last week, This month, Older. A time that fits several groups goes in the first one that
/// matches, in that order.
/// </summary>
public class HistoryDateGroupsTests
{
    // Wednesday 2026-09-30 10:00 local; the week starts on Monday in these cases.
    private static readonly DateTime Now = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Local);

    private static HistoryDateGroup Group(DateTime when, DayOfWeek firstDay = DayOfWeek.Monday) =>
        HistoryDateGroups.For(Now, when, firstDay);

    [Theory]
    [InlineData(2026, 9, 30, 0, 0, HistoryDateGroup.Today)]        // midnight today
    [InlineData(2026, 9, 30, 23, 59, HistoryDateGroup.Today)]
    [InlineData(2026, 9, 29, 23, 59, HistoryDateGroup.Yesterday)]   // one minute before today
    [InlineData(2026, 9, 29, 0, 0, HistoryDateGroup.Yesterday)]
    [InlineData(2026, 9, 28, 23, 59, HistoryDateGroup.ThisWeek)]    // Monday, the week's first day
    [InlineData(2026, 9, 28, 0, 0, HistoryDateGroup.ThisWeek)]
    [InlineData(2026, 9, 27, 23, 59, HistoryDateGroup.LastWeek)]    // Sunday before the week start
    [InlineData(2026, 9, 21, 0, 0, HistoryDateGroup.LastWeek)]      // last week's Monday
    [InlineData(2026, 9, 20, 23, 59, HistoryDateGroup.ThisMonth)]
    [InlineData(2026, 9, 1, 0, 0, HistoryDateGroup.ThisMonth)]      // the month's first day
    [InlineData(2026, 8, 31, 23, 59, HistoryDateGroup.Older)]
    [InlineData(2025, 9, 30, 10, 0, HistoryDateGroup.Older)]        // same day last year
    public void Each_boundary_falls_in_the_right_group(int y, int mo, int d, int h, int mi, HistoryDateGroup expected)
        => Assert.Equal(expected, Group(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Local)));

    [Fact]
    public void Yesterday_wins_over_this_week_when_both_fit()
        => Assert.Equal(HistoryDateGroup.Yesterday, Group(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void Yesterday_can_be_last_week_territory_and_still_says_yesterday()
    {
        // Monday now, week starting Monday: yesterday (Sunday) is also in last week.
        var monday = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Local);
        Assert.Equal(HistoryDateGroup.Yesterday,
            HistoryDateGroups.For(monday, monday.AddDays(-1), DayOfWeek.Monday));
    }

    [Fact]
    public void Last_week_wins_over_this_month_when_both_fit()
        => Assert.Equal(HistoryDateGroup.LastWeek, Group(new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void The_week_start_follows_the_culture()
    {
        // With Sunday as the first day, Sunday the 27th is this week.
        var sunday = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(HistoryDateGroup.ThisWeek, Group(sunday, DayOfWeek.Sunday));
        Assert.Equal(HistoryDateGroup.LastWeek, Group(sunday, DayOfWeek.Monday));
    }

    [Fact]
    public void Utc_times_are_compared_in_local_time()
    {
        var localMidnight = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Local);
        Assert.Equal(HistoryDateGroup.Today, HistoryDateGroups.For(Now, localMidnight.ToUniversalTime(), DayOfWeek.Monday));
        Assert.Equal(HistoryDateGroup.Yesterday, HistoryDateGroups.For(Now, localMidnight.AddSeconds(-1).ToUniversalTime(), DayOfWeek.Monday));
        Assert.Equal(HistoryDateGroup.Today, HistoryDateGroups.For(Now.ToUniversalTime(), localMidnight, DayOfWeek.Monday));
    }

    [Fact]
    public void A_future_time_counts_as_today()
        => Assert.Equal(HistoryDateGroup.Today, Group(Now.AddDays(2)));

    [Fact]
    public void The_culture_overload_uses_the_current_culture_week_start()
    {
        var first = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var when = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(HistoryDateGroups.For(Now, when, first), HistoryDateGroups.For(Now, when));
    }

    [Theory]
    [InlineData(HistoryDateGroup.Today, "Today")]
    [InlineData(HistoryDateGroup.Yesterday, "Yesterday")]
    [InlineData(HistoryDateGroup.ThisWeek, "This week")]
    [InlineData(HistoryDateGroup.LastWeek, "Last week")]
    [InlineData(HistoryDateGroup.ThisMonth, "This month")]
    [InlineData(HistoryDateGroup.Older, "Older")]
    public void Labels(HistoryDateGroup group, string label) => Assert.Equal(label, HistoryDateGroups.Label(group));
}
