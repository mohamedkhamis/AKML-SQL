using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Telemetry;
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
        ctx.Services.Configure<AnalyticsOptions>(o => o.RetentionDays = 400);
        ctx.Services.AddSingleton(new GeoLookup(Path.Combine(dir.Path, "no-such-geo.mmdb")));

        // Spec 038 T096: the dashboard's privacy paragraph reads the configured identifiable
        // retention, so the page needs the settings store. (This test builds its own context rather
        // than using NewDashboardCtx because it needs a populated downloads folder.)
        var dashboardSettings = new AkmlSql.Site.Settings.SiteSettingsStore(Path.Combine(dir.Path, "settings.db"));
        dashboardSettings.CreateTableIfMissing();
        dashboardSettings.Load();
        ctx.Services.AddSingleton(dashboardSettings);

        var cut = ctx.Render<AdminDashboard>();

        // Stat tiles: visits today, unique today, 7d, window, downloads window, downloads total,
        // bot hits. Two distinct IPs visited "/" plus one more for "/features" => 3 unique.
        var values = cut.FindAll("section[aria-label='Key metrics'] .admin-stat-value")
            .Select(e => e.TextContent.Trim()).ToList();
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

        // Spec 038 T017: sign-out moved to AdminLayout, which every portal page now shares.
        // AdminLayoutTests.Layout_RendersSignOutPostingToTheLogoutEndpoint covers it there.
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

        var values = cut.FindAll("section[aria-label='Key metrics'] .admin-stat-value")
            .Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal("1", values[0]); // visits today: the human only
        // Selected by class, not by position: an index from the end broke as soon as a second
        // stats row was added below this one.
        Assert.Equal("2", cut.Find(".admin-stat-muted .admin-stat-value").TextContent.Trim());
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
        // Spec 038 T017: the range selector itself moved to AdminLayout, shared by every section
        // (FR-032). AdminLayoutTests asserts the selected range is marked current there.
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

        using var ctx = NewDashboardCtx(store, dir);

        var cut = ctx.Render<AdminDashboard>();

        // Only the headline counters are all-zero; the engagement row carries rates and a
        // duration, which render as "0.00", "0%" and an em dash rather than "0".
        Assert.All(
            cut.FindAll("section[aria-label='Key metrics'] .admin-stat-value"),
            v => Assert.Equal("0", v.TextContent.Trim()));
        // 30 days x (visits + unique) + 30 days x downloads = 90 zero-height bars.
        Assert.Equal(90, cut.FindAll(".admin-chart-bar.bar-h-0").Count);
        Assert.Contains("No visits recorded yet.", cut.Markup);
        Assert.Contains("No downloads recorded yet.", cut.Markup);
        Assert.Contains("No installer files present.", cut.Markup);
    }

    [Fact]
    public void Errors_RendersStoredErrorsStatsAndLevelBreakdown()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var now = DateTimeOffset.UtcNow;
        store.LogClientErrors(new ClientErrorBatch(
        [
            new ClientErrorInfo(now, now, "Error", "Parser blew up", "System.Exception: boom", "parser", "1.4.0", "ssms", "abcdef1234567890"),
            new ClientErrorInfo(now, now, "Error", "Formatter crashed", null, "formatter", "1.4.0", "ssms", "abcdef1234567890"),
            new ClientErrorInfo(now, now, "Warning", "Slow completion", null, "intellisense", "1.4.0", "ssms", "abcdef1234567890"),
        ]));

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);

        var cut = ctx.Render<AdminErrors>();

        // Stat tiles: errors in window, distinct installs, top level.
        var values = cut.FindAll(".admin-stat-value").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["3", "1", "Error"], values);

        // The recent table shows the stored rows; the install id is truncated to 8 characters.
        Assert.Contains("Parser blew up", cut.Markup);
        Assert.Contains("Slow completion", cut.Markup);
        Assert.Contains("abcdef12", cut.Markup);
        Assert.DoesNotContain("abcdef1234567890", cut.Markup);

        // The stack is tucked behind <details>, and the severity breakdown table rendered.
        Assert.NotNull(cut.Find("details summary"));
        Assert.Contains("By severity level", cut.Markup);

        // Spec 038 T017: the hand-rolled "back to dashboard" link and the window selector both
        // moved to AdminLayout's persistent navigation (FR-031/FR-032).
    }

    [Fact]
    public void Errors_LevelQuery_FiltersRecentRowsAndPreservesItselfInLinks()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var now = DateTimeOffset.UtcNow;
        store.LogClientErrors(new ClientErrorBatch(
        [
            new ClientErrorInfo(now, now, "Error", "Parser blew up", null, "parser", "1.4.0", "ssms", "install-a"),
            new ClientErrorInfo(now, now, "Warning", "Slow completion", null, "intellisense", "1.4.0", "ssms", "install-a"),
        ]));

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        // Lowercase on purpose: the page normalizes the query value to the canonical casing.
        nav.NavigateTo(nav.GetUriWithQueryParameter("level", "warning"));

        var cut = ctx.Render<AdminErrors>();

        Assert.Contains("Slow completion", cut.Markup);
        Assert.DoesNotContain("Parser blew up", cut.Markup);
        // The selected level survives in the filter links (canonical casing).
        Assert.Contains("level=Warning", cut.Markup);
    }

    [Fact]
    public void Errors_EmptyStore_RendersEmptyStates()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);

        var cut = ctx.Render<AdminErrors>();

        Assert.Contains("No client errors recorded in this window.", cut.Markup);
        Assert.All(
            cut.FindAll(".admin-stat-value").Take(2),
            v => Assert.Equal("0", v.TextContent.Trim()));
    }

    /// <summary>Dashboard context: a real store plus a downloads folder inside <paramref name="dir"/>.</summary>
    private static BunitContext NewDashboardCtx(AnalyticsStore store, TempDirectory dir)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);
        ctx.Services.Configure<DownloadsOptions>(o => o.Folder = Path.Combine(dir.Path, "downloads"));
        ctx.Services.Configure<AnalyticsOptions>(o => o.RetentionDays = 400);
        // No .mmdb path: GeoLookup resolves to "unavailable", which is the state the dashboard
        // must render correctly on any machine without a MaxMind licence key.
        ctx.Services.AddSingleton(new GeoLookup(Path.Combine(dir.Path, "no-such-geo.mmdb")));

        // Spec 038 T096: the dashboard's privacy paragraph now quotes the CONFIGURED identifiable
        // retention rather than a literal, so it cannot drift from what the store enforces.
        var settings = new AkmlSql.Site.Settings.SiteSettingsStore(Path.Combine(dir.Path, "settings.db"));
        settings.CreateTableIfMissing();
        settings.Load();
        ctx.Services.AddSingleton(settings);

        return ctx;
    }
}
