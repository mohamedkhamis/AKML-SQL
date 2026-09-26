using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Consent;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// The Insights page, the change indicators on the overview, and the hourly chart used for
/// single-day ranges. The store's figures are pinned in LocalTimeAnalyticsTests; these pin what
/// the owner is shown.
/// </summary>
public sealed class AdminInsightsPageTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly AnalyticsStore _store;

    public AdminInsightsPageTests()
    {
        _store = new AnalyticsStore(Path.Combine(_dir.Path, "analytics.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
    }

    /// <summary>Rendered text with its layout whitespace collapsed.</summary>
    private static string Text(string raw) => System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ");

    private BunitContext NewCtx(string days)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(_store);
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.BaseUri.TrimEnd('/') + "/admin/insights?days=" + days);
        return ctx;
    }

    private void Traffic()
    {
        // Two visitors now; one downloads. The UTC store makes "now" today in the report.
        var now = DateTimeOffset.UtcNow;
        _store.LogVisit(new VisitInfo(now, "/", "www.google.com", "Chrome", "203.0.113.1"));
        _store.LogVisit(new VisitInfo(now, "/download", null, "Chrome", "203.0.113.2"));
        _store.LogDownload(new DownloadInfo(now, "AKMLSQLSetup-1.0.0.exe", null, "Chrome", "203.0.113.2"));
    }

    [Fact]
    public void Conversion_IsStatedWithItsUnit_AndBrokenDown()
    {
        Traffic();
        using var ctx = NewCtx("today");

        var cut = ctx.Render<AdminInsights>();

        Assert.Equal("50%", cut.Find(".insights-kpi-value").TextContent.Trim());
        Assert.Contains("1 of 2 visitors", Text(cut.Find(".insights-kpi-text").TextContent), StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#by-source-heading"));
        Assert.NotNull(cut.Find("#by-page-heading"));
        Assert.NotNull(cut.Find("#by-country-heading"));
        Assert.Contains("/download", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OverSeveralDays_TheUnitIsVisitorDays_AndTheWeekGridIsShown()
    {
        Traffic();
        using var ctx = NewCtx("7");

        var cut = ctx.Render<AdminInsights>();

        Assert.Contains("visitor-days", cut.Find(".insights-kpi-text").TextContent, StringComparison.Ordinal);
        // 7 weekdays x 24 hours; the grid is decoration, the table beside it carries the data.
        Assert.Equal(7 * 24, cut.FindAll(".insights-heatmap .insights-heat").Count);
        Assert.Equal("true", cut.Find(".insights-heatmap").GetAttribute("aria-hidden"));
        Assert.Equal(7, cut.FindAll(".insights-heat-table tbody tr").Count);
        Assert.NotEmpty(cut.FindAll(".insights-heatmap .heat-5"));
    }

    [Fact]
    public void ForOneDay_ThereIsNoWeekGrid_ButThereAreHourlyCharts()
    {
        Traffic();
        using var ctx = NewCtx("today");

        var cut = ctx.Render<AdminInsights>();

        Assert.Empty(cut.FindAll(".insights-heatmap"));
        Assert.Equal(2, cut.FindAll(".admin-chart-hourly").Count);
    }

    [Fact]
    public void WithNoTraffic_ItSaysSoRatherThanDrawingEmptyCharts()
    {
        using var ctx = NewCtx("yesterday");

        var cut = ctx.Render<AdminInsights>();

        Assert.Equal("0%", cut.Find(".insights-kpi-value").TextContent.Trim());
        Assert.Contains("No visits for yesterday", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".admin-chart-hourly"));
    }

    [Fact]
    public void NewVersusReturning_StatesItsSampleSize()
    {
        var now = DateTimeOffset.UtcNow;
        _store.LogVisit(new VisitInfo(now.AddDays(-20), "/", null, "Chrome", "203.0.113.9")
        {
            Consent = ConsentState.Granted,
            VisitorId = "returning-visitor",
        });
        _store.LogVisit(new VisitInfo(now, "/", null, "Chrome", "203.0.113.9")
        {
            Consent = ConsentState.Granted,
            VisitorId = "returning-visitor",
        });
        _store.LogVisit(new VisitInfo(now, "/", null, "Chrome", "203.0.113.10"));
        using var ctx = NewCtx("7");

        var cut = ctx.Render<AdminInsights>();

        var values = cut.FindAll(".insights-loyalty-value").Select(v => v.TextContent.Trim()).ToList();
        Assert.Equal(["0", "1"], values); // new, returning
        Assert.Contains("1 person who accepted the cookie", Text(cut.Find(".insights-coverage").TextContent), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutConsentedVisitors_TheSplitExplainsWhyItIsEmpty()
    {
        Traffic();
        using var ctx = NewCtx("today");

        var cut = ctx.Render<AdminInsights>();

        Assert.Empty(cut.FindAll(".insights-loyalty"));
        Assert.Contains("No visitors who accepted the cookie", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMeterWidth_ComesFromAClass_NotAnInlineStyle()
    {
        // Strict CSP: style="" attributes are blocked, so a width must be a class.
        Traffic();
        using var ctx = NewCtx("today");

        var cut = ctx.Render<AdminInsights>();

        Assert.NotEmpty(cut.FindAll(".insights-meter-fill[class*='bar-w-']"));
        Assert.DoesNotContain("style=", cut.Markup, StringComparison.Ordinal);
    }

    // --- HourlyChart ----------------------------------------------------------------------

    [Fact]
    public void TheHourlyChart_HasTwentyFourBars_AndATableWithTheSameData()
    {
        using var ctx = new BunitContext();
        var values = new long[24];
        values[9] = 4;
        values[14] = 8;

        var cut = ctx.Render<HourlyChart>(p => p
            .Add(c => c.Title, "Visits")
            .Add(c => c.Caption, "today, UTC time")
            .Add(c => c.Values, values)
            .Add(c => c.IdPrefix, "test"));

        Assert.Equal(24, cut.FindAll(".admin-chart-col").Count);
        Assert.Contains("bar-h-100", cut.FindAll(".admin-chart-bar")[14].ClassName, StringComparison.Ordinal);
        Assert.Contains("bar-h-50", cut.FindAll(".admin-chart-bar")[9].ClassName, StringComparison.Ordinal);
        Assert.Equal("12 total", cut.Find(".admin-chart-total").TextContent.Trim());
        var rows = cut.FindAll("table tbody tr");
        Assert.Equal(24, rows.Count);
        Assert.Equal("8", rows[14].QuerySelector("td")!.TextContent.Trim());
    }

    // --- ChangeIndicator ------------------------------------------------------------------

    [Theory]
    [InlineData(112, 100, "up", "▲ 12%")]
    [InlineData(88, 100, "down", "▼ 12%")]
    [InlineData(105, 100, "up", "▲ 5%")]
    [InlineData(1001, 1000, "up", "▲ 0.1%")]
    [InlineData(100, 100, "flat", "● 0%")]
    [InlineData(5, 0, "new", "▲ new")]
    [InlineData(0, 0, "flat", "—")]
    [InlineData(0, 7, "down", "▼ 100%")]
    public void ChangeInACount_ReadsTheWayItShould(long current, long previous, string direction, string text)
    {
        var change = ChangeIndicator.ForCount(current, previous, "yesterday");

        Assert.Equal(direction, change.Direction);
        Assert.Equal(text, change.Text);
        Assert.Contains("yesterday", change.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3.0, 2.0, "up", "▲ 1 pts")]
    [InlineData(2.0, 3.5, "down", "▼ 1.5 pts")]
    [InlineData(2.0, 2.0, "flat", "● 0 pts")]
    public void ChangeInARate_IsInPercentagePoints(double current, double previous, string direction, string text)
    {
        var change = ChangeIndicator.ForPoints(current, previous, "the previous 7 days");

        Assert.Equal(direction, change.Direction);
        Assert.Equal(text, change.Text);
        Assert.Contains(direction == "flat" ? "Unchanged" : "percentage points", change.Description, StringComparison.Ordinal);
    }
}
