using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Reports on the owner's calendar, and the analyses built on it: the comparison period,
/// conversion, when people visit, and new versus returning.
/// <para>
/// Every test uses a Cairo store and a fixed "now" of 10:00 Cairo on Tue 22 Sep 2026 (summer time,
/// UTC+3), so each boundary case can be stated in the owner's terms.
/// </para>
/// </summary>
public sealed class LocalTimeAnalyticsTests : IDisposable
{
    private static readonly TimeZoneInfo Cairo = FindCairo();

    /// <summary>10:00 Cairo, Tue 22 Sep 2026.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 7, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();
    private readonly AnalyticsStore _store;

    public LocalTimeAnalyticsTests()
    {
        _store = new AnalyticsStore(Path.Combine(_dir.Path, "a.db"), Cairo);
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
    }

    private static TimeZoneInfo FindCairo()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
    }

    /// <summary>A UTC instant given as Cairo summer-time wall clock (UTC+3).</summary>
    private static DateTimeOffset Cairo3(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 9, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    private void Visit(DateTimeOffset at, string ip = "203.0.113.1", string path = "/",
        string? referrer = null, string? country = null, string? visitorId = null)
    {
        _store.LogVisit(new VisitInfo(at, path, referrer, "Chrome", ip)
        {
            Location = country is null ? GeoLocation.Unknown : new GeoLocation("XX", country),
            Consent = visitorId is null ? ConsentState.Denied : ConsentState.Granted,
            VisitorId = visitorId,
        });
    }

    private void Download(DateTimeOffset at, string ip = "203.0.113.1", string? visitorId = null)
    {
        _store.LogDownload(new DownloadInfo(at, "AKMLSQLSetup-1.0.0.0.exe", null, "Chrome", ip)
        {
            Consent = visitorId is null ? ConsentState.Denied : ConsentState.Granted,
            VisitorId = visitorId,
        });
    }

    private ReportWindow Window(ReportRange range) => _store.ResolveWindow(range, Now);

    // ------------------------------------------------------------------ the reported bug

    [Fact]
    public void AVisitAtOneInTheMorning_CountsToday_NotYesterday()
    {
        // 01:30 on the 22nd in Cairo is 22:30 on the 21st in UTC. UTC-day reporting put it on
        // the 21st -- a download made "today" showed up as yesterday's.
        Visit(Cairo3(22, 1, 30));

        Assert.Equal(1, _store.GetSummary(Window(ReportRange.Today)).Headline.Visits);
        Assert.Equal(0, _store.GetSummary(Window(ReportRange.Yesterday)).Headline.Visits);
    }

    [Fact]
    public void Yesterday_CountsExactlyThatLocalDay()
    {
        Visit(Cairo3(20, 23, 59));   // the day before yesterday
        Visit(Cairo3(21, 0, 0));     // first instant of yesterday
        Visit(Cairo3(21, 23, 59));   // last minute of yesterday
        Visit(Cairo3(22, 0, 0));     // first instant of today

        Assert.Equal(2, _store.GetSummary(Window(ReportRange.Yesterday)).Headline.Visits);
    }

    [Fact]
    public void TheDailyChart_PutsEachVisitOnItsLocalDay()
    {
        Visit(Cairo3(22, 1, 30));   // 21st in UTC, 22nd in Cairo

        var series = _store.GetSummary(Window(ReportRange.Last7)).DailyVisits;

        Assert.Equal(1, series.Single(d => d.Day == new DateOnly(2026, 9, 22)).Count);
        Assert.Equal(0, series.Single(d => d.Day == new DateOnly(2026, 9, 21)).Count);
    }

    [Fact]
    public void AVisitorActiveAcrossUtcMidnight_IsOneUniqueVisitorForTheLocalDay()
    {
        // The per-day hash used to rotate at UTC midnight -- 03:00 Cairo in summer -- so the same
        // person at 02:00 and 04:00 counted as two unique visitors. It now rotates at local midnight.
        Visit(Cairo3(22, 2, 0), ip: "198.51.100.7");
        Visit(Cairo3(22, 4, 0), ip: "198.51.100.7");

        var today = _store.GetSummary(Window(ReportRange.Today));

        Assert.Equal(2, today.Headline.Visits);
        Assert.Equal(1, today.Headline.Visitors);
        Assert.Equal(1, today.UniqueVisitorsToday);
    }

    // ------------------------------------------------------------------ comparison

    [Fact]
    public void Today_IsComparedWithYesterdayUpToTheSameTime()
    {
        Visit(Cairo3(22, 9, 0));                    // today, before 10:00
        Visit(Cairo3(21, 9, 0), ip: "10.0.0.2");     // yesterday, before 10:00 -- compared
        Visit(Cairo3(21, 15, 0), ip: "10.0.0.3");    // yesterday, AFTER 10:00 -- not compared

        var summary = _store.GetSummary(Window(ReportRange.Today));

        Assert.Equal(1, summary.Headline.Visits);
        Assert.Equal(1, summary.PreviousHeadline.Visits);
    }

    // ------------------------------------------------------------------ conversion

    [Fact]
    public void Conversion_IsVisitorDaysThatDownloaded_OverVisitorDays()
    {
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.1");
        Download(Cairo3(22, 8, 5), ip: "10.0.0.1");        // same person, same day: converted
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.2");           // visited, did not download
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.3");
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.4");
        Download(Cairo3(22, 9, 0), ip: "10.0.0.99");       // downloaded with no visit behind it

        var headline = _store.GetSummary(Window(ReportRange.Today)).Headline;

        Assert.Equal(4, headline.Visitors);
        Assert.Equal(2, headline.Downloads);
        Assert.Equal(1, headline.Downloaders);
        Assert.Equal(25.0, headline.ConversionPercent);

        // The unmatched download is reported, so the two numbers reconcile.
        Assert.Equal(1, headline.DownloadsWithoutVisit);
    }

    [Fact]
    public void Conversion_IsAttributedToTheFirstPageAndSourceOfTheDay()
    {
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.1", path: "/features", referrer: "github.com", country: "Egypt");
        Visit(Cairo3(22, 8, 1), ip: "10.0.0.1", path: "/download", referrer: "akml.khamis.work", country: "Egypt");
        Download(Cairo3(22, 8, 2), ip: "10.0.0.1");
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.2", path: "/", country: "Jordan");

        var insights = _store.GetInsights(Window(ReportRange.Today));

        var bySource = insights.ConversionBySource.ToDictionary(r => r.Label);
        Assert.Equal(1, bySource["github.com"].Downloaders);
        Assert.Equal(100.0, bySource["github.com"].ConversionPercent);
        Assert.Equal(0, bySource["(direct)"].Downloaders);

        var byPage = insights.ConversionByLandingPage.ToDictionary(r => r.Label);
        Assert.Equal(1, byPage["/features"].Downloaders);          // landed on /features
        Assert.False(byPage.ContainsKey("/download"));              // not a landing page

        var byCountry = insights.ConversionByCountry.ToDictionary(r => r.Label);
        Assert.Equal(1, byCountry["Egypt"].Downloaders);
        Assert.Equal(0, byCountry["Jordan"].Downloaders);
    }

    // ------------------------------------------------------------------ when people visit

    [Fact]
    public void WhenPeopleVisit_UsesTheLocalClock()
    {
        Visit(Cairo3(22, 1, 30));   // 22:30 UTC on the 21st, a Monday in UTC
        Download(Cairo3(22, 9, 15));

        var insights = _store.GetInsights(Window(ReportRange.Last7));

        Assert.Equal(1, insights.VisitsByHour[1]);          // 01:xx local, not 22:xx
        Assert.Equal(0, insights.VisitsByHour[22]);
        Assert.Equal(1, insights.DownloadsByHour[9]);

        // Tuesday locally (the 22nd), not Monday.
        Assert.Equal(1, insights.VisitHeatmap[(int)DayOfWeek.Tuesday, 1]);
        Assert.Equal(0, insights.VisitHeatmap[(int)DayOfWeek.Monday, 22]);
    }

    // ------------------------------------------------------------------ new vs returning

    [Fact]
    public void NewVersusReturning_UsesTheWholeHistory_AndStatesItsCoverage()
    {
        Visit(Cairo3(10, 12, 0), ip: "10.0.0.1", visitorId: "returning-person");   // weeks before
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.1", visitorId: "returning-person");
        Download(Cairo3(22, 8, 5), ip: "10.0.0.1", visitorId: "returning-person");

        Visit(Cairo3(22, 9, 0), ip: "10.0.0.2", visitorId: "new-person");
        Visit(Cairo3(22, 9, 0), ip: "10.0.0.3");                                   // declined the cookie

        var loyalty = _store.GetInsights(Window(ReportRange.Today)).Loyalty;

        Assert.Equal(1, loyalty.NewVisitors);
        Assert.Equal(0, loyalty.NewDownloaders);
        Assert.Equal(1, loyalty.ReturningVisitors);
        Assert.Equal(1, loyalty.ReturningDownloaders);
        Assert.Equal(100.0, loyalty.ReturningConversionPercent);

        // Two of today's three human page views carry a visitor id.
        Assert.Equal(66.7, loyalty.CoveragePercent);
    }

    [Fact]
    public void ThePeopleList_MarksReturningVisitors_AndShowsWhenTheyWereFirstSeenEver()
    {
        // Both were broken: "first seen" was the first visit INSIDE the window, and "returning"
        // compared that against the window start -- so it could never be true, for anyone.
        Visit(Cairo3(10, 12, 0), ip: "10.0.0.1", visitorId: "returning-person");
        Visit(Cairo3(22, 8, 0), ip: "10.0.0.1", visitorId: "returning-person");
        Visit(Cairo3(22, 9, 0), ip: "10.0.0.2", visitorId: "new-person");

        var rows = _store.GetIndividuals(new IndividualFilter(1, Page: 0, PageSize: 50), Window(ReportRange.Today))
            .ToDictionary(r => r.VisitorId);

        Assert.True(rows["returning-person"].IsReturning);
        Assert.Equal(Cairo3(10, 12, 0), rows["returning-person"].FirstSeen);
        Assert.False(rows["new-person"].IsReturning);
    }
}
