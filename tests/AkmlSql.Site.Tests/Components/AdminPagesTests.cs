using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Pages.Admin;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// bUnit render tests for the admin portal: login card states (configured, error, not
/// configured) and the metrics dashboard driven by a real temp-file AnalyticsStore plus a
/// temp downloads folder.
/// </summary>
public sealed class AdminPagesTests
{
    // A real PBKDF2 hash (SEC-001) rather than a literal: the salt is random, so the value cannot
    // be inlined without pinning one salt forever.
    private static readonly string ConfiguredHash = AdminAuth.HashPassword("correct horse battery staple");

    private static BunitContext NewLoginCtx(string passwordHash)
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        ctx.Services.Configure<AdminOptions>(o => o.PasswordHash = passwordHash);
        return ctx;
    }

    [Fact]
    public void Login_Configured_RendersPasswordForm()
    {
        using var ctx = NewLoginCtx(ConfiguredHash);

        var cut = ctx.Render<AdminLogin>();

        var form = cut.Find("form[action='/admin/login'][method='post']");
        Assert.NotNull(form);
        var password = cut.Find("input[type='password'][name='password']");
        Assert.Equal("current-password", password.GetAttribute("autocomplete"));
        Assert.Empty(cut.FindAll(".admin-error"));
        Assert.Empty(cut.FindAll(".notice"));
    }

    [Fact]
    public void Login_WithErrorQuery_ShowsErrorState()
    {
        using var ctx = NewLoginCtx(ConfiguredHash);

        // SupplyParameterFromQuery values come from the (fake) NavigationManager in bUnit.
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("error", "1"));

        var cut = ctx.Render<AdminLogin>();

        Assert.NotNull(cut.Find(".admin-error"));
        Assert.Contains("Invalid password", cut.Markup);
    }

    [Fact]
    public void Login_Throttled_ExplainsTheWaitInsteadOfBlamingThePassword()
    {
        using var ctx = NewLoginCtx(ConfiguredHash);

        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["error"] = "throttled",
            ["retry"] = "45",
        }));

        var cut = ctx.Render<AdminLogin>();

        Assert.Contains("Too many failed attempts", cut.Markup);
        Assert.Contains("45 seconds", cut.Markup);
        // SEC-002: the attempt was never evaluated, so claiming the password was wrong would lie.
        Assert.DoesNotContain("Invalid password", cut.Markup);
    }

    [Fact]
    public void Login_NotConfigured_ShowsNoticeInsteadOfForm()
    {
        using var ctx = NewLoginCtx("");

        var cut = ctx.Render<AdminLogin>();

        Assert.Contains("not configured", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("input[type='password']"));
    }

    [Fact]
    public void Dashboard_RendersStatsChartTablesAndFolderFiles()
    {
        using var dir = new TempDirectory();
        var downloadsDir = Path.Combine(dir.Path, "downloads");
        Directory.CreateDirectory(downloadsDir);
        File.WriteAllText(Path.Combine(downloadsDir, "AKMLSQLSetup-1.0.0.exe"), "payload");

        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var now = DateTimeOffset.UtcNow;
        store.LogVisit(new VisitInfo(now, "/", "https://example.com/", "Chrome", "203.0.113.1"));
        store.LogVisit(new VisitInfo(now, "/", "https://example.com/", "Chrome", "203.0.113.2"));
        store.LogVisit(new VisitInfo(now, "/features", null, "Firefox", "203.0.113.3"));
        store.LogDownload(new DownloadInfo(now, "AKMLSQLSetup-1.0.0.exe", null, "Chrome", "203.0.113.1"));

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);
        ctx.Services.Configure<DownloadsOptions>(o => o.Folder = downloadsDir);

        var cut = ctx.Render<AdminDashboard>();

        // Stat tiles: visits today, unique today, 7d, window, downloads window, downloads total,
        // bot hits. Two distinct IPs visited "/" plus one more for "/features" => 3 unique.
        var values = cut.FindAll(".admin-stat-value").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["3", "3", "3", "3", "1", "1", "0"], values);

        // Two charts now (ADM-005): visits and downloads, one column per day of the window.
        Assert.Equal(2, cut.FindAll(".admin-chart").Count);
        Assert.Equal(60, cut.FindAll(".admin-chart-col").Count); // 30 days x 2 charts
        Assert.NotEmpty(cut.FindAll(".admin-chart-bar[class*='bar-h-']"));

        // Top pages + downloads-by-file tables.
        Assert.Contains("/features", cut.Markup);
        Assert.Contains("AKMLSQLSetup-1.0.0.exe", cut.Markup);

        // ADM-002: browser mix is displayed, from data that was always being recorded.
        Assert.Contains("Chrome", cut.Markup);
        Assert.Contains("Firefox", cut.Markup);

        // Referrer table shows host only.
        Assert.Contains("example.com", cut.Markup);

        // Downloads folder listing.
        Assert.Contains(downloadsDir, cut.Markup);

        // Sign-out posts to the logout endpoint.
        Assert.NotNull(cut.Find("form[action='/admin/logout'][method='post']"));
    }

    [Fact]
    public void Dashboard_ExcludesBotTraffic_ButReportsItSeparately()
    {
        // ADM-001: crawler hits used to be counted as visits, inflating every headline figure and
        // the top-pages table.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var now = DateTimeOffset.UtcNow;
        store.LogVisit(new VisitInfo(now, "/", null, "Chrome", "203.0.113.1"));
        store.LogVisit(new VisitInfo(now, "/crawled", null, "bot", "203.0.113.9"));
        store.LogVisit(new VisitInfo(now, "/crawled", null, "bot", "203.0.113.9"));

        using var ctx = NewDashboardCtx(store, dir);
        var cut = ctx.Render<AdminDashboard>();

        var values = cut.FindAll(".admin-stat-value").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal("1", values[0]); // visits today: the human only
        Assert.Equal("2", values[^1]); // bot hits: reported, not discarded
        Assert.DoesNotContain("/crawled", cut.Markup);
    }

    [Theory]
    [InlineData(7, "7 days")]
    [InlineData(90, "90 days")]
    [InlineData(365, "12 months")]
    public void Dashboard_HonoursTheRequestedWindow(int days, string label)
    {
        // ADM-003: the window was hardcoded to 30 even though GetSummary already took it.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        using var ctx = NewDashboardCtx(store, dir);
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("days", days.ToString()));

        var cut = ctx.Render<AdminDashboard>();

        Assert.Equal(days * 2, cut.FindAll(".admin-chart-col").Count); // both charts follow it
        Assert.Contains(label, cut.Markup);
        Assert.NotNull(cut.Find($"a[href='/admin/metrics.csv?days={days}']")); // export follows too
        Assert.NotNull(cut.Find($"a[href='/admin?days={days}'].is-current"));
    }

    [Theory]
    [InlineData("-5")]        // negative
    [InlineData("0")]         // zero
    [InlineData("banana")]    // unparseable -- this returned HTTP 500 before the parameter was
                              // bound as a string, because Blazor.s query binder throws on it
    [InlineData("")]
    public void Dashboard_FallsBackToTheDefaultWindow_ForAMalformedValue(string days)
    {
        // The query string is user input; a bad value must not break the owner.s dashboard.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        using var ctx = NewDashboardCtx(store, dir);
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("days", days));

        var cut = ctx.Render<AdminDashboard>();

        Assert.Equal(AdminDashboardOptions.DefaultDays * 2, cut.FindAll(".admin-chart-col").Count);
    }

    [Fact]
    public void Dashboard_ChartsCarryAnAccessibleDataTable()
    {
        // A11Y-005: values used to be reachable only through a title tooltip.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        store.LogVisit(new VisitInfo(DateTimeOffset.UtcNow, "/", null, "Chrome", "203.0.113.1"));

        using var ctx = NewDashboardCtx(store, dir);
        var cut = ctx.Render<AdminDashboard>();

        // The graphic is hidden from assistive tech...
        Assert.All(cut.FindAll(".admin-chart"), c => Assert.Equal("true", c.GetAttribute("aria-hidden")));
        // ...and a real table carries the same numbers.
        Assert.Equal(2, cut.FindAll(".admin-chart-data table").Count);
        Assert.NotEmpty(cut.FindAll(".admin-chart-data caption"));
    }

    [Fact]
    public void Dashboard_EmptyStore_RendersZeroesAndEmptyStates()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);
        ctx.Services.Configure<DownloadsOptions>(o => o.Folder = Path.Combine(dir.Path, "downloads"));

        var cut = ctx.Render<AdminDashboard>();

        Assert.All(cut.FindAll(".admin-stat-value"), v => Assert.Equal("0", v.TextContent.Trim()));
        // 30 days x (visits + unique) + 30 days x downloads = 90 zero-height bars.
        Assert.Equal(90, cut.FindAll(".admin-chart-bar.bar-h-0").Count);
        Assert.Contains("No visits recorded yet.", cut.Markup);
        Assert.Contains("No downloads recorded yet.", cut.Markup);
        Assert.Contains("No installer files present.", cut.Markup);
    }

    /// <summary>Dashboard context: a real store plus a downloads folder inside <paramref name="dir"/>.</summary>
    private static BunitContext NewDashboardCtx(AnalyticsStore store, TempDirectory dir)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);
        ctx.Services.Configure<DownloadsOptions>(o => o.Folder = Path.Combine(dir.Path, "downloads"));
        return ctx;
    }
}
