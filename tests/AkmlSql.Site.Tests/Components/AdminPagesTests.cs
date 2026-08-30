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

        // Stat cards: 3 visits today (also 7d/30d), 1 download.
        var values = cut.FindAll(".admin-stat-value").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["3", "3", "3", "1", "1"], values);
        Assert.Equal(5, cut.FindAll(".admin-stat").Count);

        // Pure-CSS chart: one bar per day of the 30-day window, heights via bucket classes.
        Assert.Equal(30, cut.FindAll(".admin-chart-bar").Count);
        Assert.NotEmpty(cut.FindAll(".admin-chart-bar[class*='bar-h-']"));

        // Top pages + downloads-by-file tables.
        Assert.Contains("/features", cut.Markup);
        Assert.Contains("AKMLSQLSetup-1.0.0.exe", cut.Markup);

        // Referrer table shows host only.
        Assert.Contains("example.com", cut.Markup);

        // Downloads folder listing.
        Assert.Contains(downloadsDir, cut.Markup);

        // Sign-out posts to the logout endpoint.
        Assert.NotNull(cut.Find("form[action='/admin/logout'][method='post']"));
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
        Assert.Equal(30, cut.FindAll(".admin-chart-bar.bar-h-0").Count);
        Assert.Contains("No visits recorded yet.", cut.Markup);
        Assert.Contains("No downloads recorded yet.", cut.Markup);
        Assert.Contains("No installer files present.", cut.Markup);
    }
}
