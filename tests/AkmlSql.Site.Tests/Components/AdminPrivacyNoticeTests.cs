using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T096: the dashboard's visible privacy paragraph must describe what the site actually
/// does.
/// <para>
/// Before this feature it read "full IP addresses are never stored … No cookies are set for
/// visitors". Both halves became false the moment the consent gate shipped. A false privacy claim
/// shown to the owner is how a false one ends up shown to visitors, so the invalidated wording is
/// asserted <b>absent</b>, not merely contradicted elsewhere on the page.
/// </para>
/// <para>
/// These mirror the assertions in <c>AkmlSql.Site.E2E.Tests.AdminPortalTests.Dashboard_StatesWhatIsActuallyStored</c>,
/// which needs the real admin password and is skipped without it — so the same guarantee is pinned
/// here where it always runs.
/// </para>
/// </summary>
public sealed class AdminPrivacyNoticeTests
{
    private static BunitContext NewCtx(TempDirectory dir, int identifiableRetentionDays = 365)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(new AnalyticsStore(Path.Combine(dir.Path, "analytics.db")));
        ctx.Services.Configure<DownloadsOptions>(o => o.Folder = Path.Combine(dir.Path, "downloads"));
        ctx.Services.Configure<AnalyticsOptions>(o => o.RetentionDays = 400);
        ctx.Services.AddSingleton(new GeoLookup(Path.Combine(dir.Path, "no-such-geo.mmdb")));

        var settings = new SiteSettingsStore(Path.Combine(dir.Path, "settings.db"));
        settings.CreateTableIfMissing();
        settings.Load();
        settings.Save(settings.Current with { IdentifiableRetentionDays = identifiableRetentionDays }, "test");
        ctx.Services.AddSingleton(settings);

        return ctx;
    }

    private static string PrivacyText(TempDirectory dir, int retentionDays = 365)
    {
        using var ctx = NewCtx(dir, retentionDays);
        var cut = ctx.Render<AdminDashboard>();
        return cut.Find(".admin-privacy").TextContent;
    }

    [Fact]
    public void ItStatesWhatIsStoredForConsentingVisitors()
    {
        using var dir = new TempDirectory();

        var privacy = PrivacyText(dir);

        Assert.Contains("full IP address", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accept", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cookie", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItStatesWhatRemainsTrueForEveryoneElse()
    {
        using var dir = new TempDirectory();

        var privacy = PrivacyText(dir);

        Assert.Contains("salted hash", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/24", privacy, StringComparison.Ordinal);
        Assert.Contains("/48", privacy, StringComparison.Ordinal);
        Assert.Contains("no city, region or finer location", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheClaimsTheReversalInvalidated_AreGone()
    {
        using var dir = new TempDirectory();

        var privacy = PrivacyText(dir);

        // The exact sentences that shipped before spec 038 and are now false.
        Assert.DoesNotContain("full IP addresses are never stored", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No cookies are set", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void TheStatedRetentionFollowsTheLiveSetting(int days)
    {
        using var dir = new TempDirectory();

        var privacy = PrivacyText(dir, days);

        // Read from SiteSettingsStore, never a literal — the same drift gate as /privacy.
        Assert.Contains($"{days} days", privacy, StringComparison.Ordinal);
    }
}
