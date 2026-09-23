using AkmlSql.Site.Admin;
using AkmlSql.Site.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T016/T017 (US4): the admin portal shell.
/// <para>
/// This class holds the coverage for the chrome that moved out of the individual pages —
/// persistent section navigation, the shared reporting window, and sign-out. Those assertions used
/// to live in <see cref="AdminPagesTests"/> against each page; they belong here now, because the
/// whole point of the shell is that one implementation serves every section rather than each page
/// repeating its own.
/// </para>
/// </summary>
public sealed class AdminLayoutTests
{
    private static BunitContext NewCtx(string path = "/admin", int? days = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        // The layout names the period ("Today · UTC time"); UTC keeps that text the same on every machine.
        ctx.Services.AddSingleton(new AkmlSql.Site.Analytics.ReportClock(TimeZoneInfo.Utc));

        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        var uri = nav.BaseUri.TrimEnd('/') + path;
        if (days is not null)
        {
            uri += "?days=" + days.Value;
        }

        nav.NavigateTo(uri);
        return ctx;
    }

    [Fact]
    public void Layout_RendersEverySectionInTheNavigation()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<AdminLayout>();

        // FR-031: every section is present on every portal page.
        foreach (var section in AdminNav.Sections)
        {
            Assert.Contains(section.Title, cut.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(cut.FindAll($".admin-nav-list a[href^='{section.Route}?']"));
        }
    }

    [Fact]
    public void Layout_RendersSignOutPostingToTheLogoutEndpoint()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<AdminLayout>();

        // Moved here from AdminPagesTests.Dashboard_RendersStatsChartTablesAndFolderFiles.
        Assert.NotNull(cut.Find("form[action='/admin/logout'][method='post']"));
        Assert.NotNull(cut.Find("form[action='/admin/logout'] button[type='submit']"));
    }

    [Theory]
    [InlineData(7, "7 days")]
    [InlineData(30, "30 days")]
    [InlineData(90, "90 days")]
    [InlineData(365, "12 months")]
    public void Layout_MarksTheSelectedWindowCurrent(int days, string label)
    {
        using var ctx = NewCtx("/admin", days);

        var cut = ctx.Render<AdminLayout>();

        // Moved here from AdminPagesTests.Dashboard_HonoursTheRequestedWindow.
        Assert.NotNull(cut.Find($".admin-ranges a[href='/admin?days={days}'].is-current"));
        Assert.Contains(label, cut.Markup, StringComparison.Ordinal);

        // FR-032, second clause: the active window is stated in words, so a screenshot of any
        // section is never ambiguous about the range it describes. It now also names the actual
        // dates and the timezone -- "today" is only unambiguous once it says whose today.
        var active = cut.Find(".admin-range-active").TextContent;
        Assert.Contains(label, active, StringComparison.Ordinal);
        Assert.Contains("UTC", active, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("today", "Today")]
    [InlineData("yesterday", "Yesterday")]
    public void Layout_OffersTodayAndYesterday_AndNamesTheDay(string key, string label)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        ctx.Services.AddSingleton(new AkmlSql.Site.Analytics.ReportClock(TimeZoneInfo.Utc));
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.BaseUri.TrimEnd('/') + "/admin?days=" + key);

        var cut = ctx.Render<AdminLayout>();

        Assert.NotNull(cut.Find($".admin-ranges a[href='/admin?days={key}'].is-current"));

        // A one-day range names the weekday and date it covers, not just "Today".
        var active = cut.Find(".admin-range-active").TextContent;
        Assert.StartsWith(label + " (", active, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_PropagatesTheWindowToEverySectionLink()
    {
        using var ctx = NewCtx("/admin", 7);

        var cut = ctx.Render<AdminLayout>();

        // FR-032 / SC-010: the range survives navigation because every link carries it. A
        // hard-coded href would silently drop it, which is exactly what this pins.
        foreach (var section in AdminNav.Sections)
        {
            Assert.NotNull(cut.Find($".admin-nav-list a[href='{section.Route}?days=7']"));
        }
    }

    [Fact]
    public void Layout_MarksOnlyTheActiveSection()
    {
        using var ctx = NewCtx("/admin/people");

        var cut = ctx.Render<AdminLayout>();

        var current = cut.FindAll(".admin-nav-list a.is-current");
        var link = Assert.Single(current);
        Assert.StartsWith("/admin/people", link.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal("page", link.GetAttribute("aria-current"));
    }

    [Fact]
    public void Layout_MarksTheOwningSectionForAChildRoute()
    {
        using var ctx = NewCtx("/admin/people/3f2a9c");

        var cut = ctx.Render<AdminLayout>();

        var link = Assert.Single(cut.FindAll(".admin-nav-list a.is-current"));
        Assert.StartsWith("/admin/people", link.GetAttribute("href"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("banana")]
    [InlineData("")]
    public void Layout_FallsBackToTheDefaultWindow_ForAMalformedValue(string days)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        // The layout names the period ("Today · UTC time"); UTC keeps that text the same on every machine.
        ctx.Services.AddSingleton(new AkmlSql.Site.Analytics.ReportClock(TimeZoneInfo.Utc));
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("days", days));

        var cut = ctx.Render<AdminLayout>();

        // Contract A4.4: the query string is user input; a bad value must not break the portal.
        Assert.Contains(AdminDashboardOptions.Label(AdminDashboardOptions.DefaultDays), cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_IsNoIndexed()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<AdminLayout>();

        // Contract A2.5 — the portal must never be indexed. HeadContent is not rendered into the
        // component markup by bUnit, so this asserts the source carries it.
        var source = File.ReadAllText(LayoutSourcePath());
        Assert.Contains("noindex, nofollow", source, StringComparison.Ordinal);
        Assert.NotNull(cut);
    }

    private static string LayoutSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "AkmlSql.Site", "Components", "Layout", "AdminLayout.razor");
    }
}
