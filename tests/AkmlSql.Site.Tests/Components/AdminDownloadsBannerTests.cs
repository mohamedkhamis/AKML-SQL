using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Consent;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// The Downloads page's country banner.
/// <para>
/// Two very different situations produce the same all-Unknown table — no database installed, or a
/// database installed after the downloads were recorded — and the banner claimed the first for
/// hours after the database actually went live. A status message that is confidently wrong is worse
/// than no message, because it sends the reader to fix something that is not broken.
/// </para>
/// </summary>
public sealed class AdminDownloadsBannerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private BunitContext NewCtx(AnalyticsStore store, bool geoInstalled)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);

        // A path that does not exist gives an unavailable GeoLookup; the real installed database
        // gives an available one. No mocking — this is the production type either way.
        var geoPath = geoInstalled
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AKML SQL Site",
                GeoLookup.DefaultFileName)
            : Path.Combine(_dir.Path, "absent.mmdb");

        ctx.Services.AddSingleton(new GeoLookup(geoPath));
        return ctx;
    }

    private static void LogDownloadWithoutCountry(AnalyticsStore store) =>
        store.LogDownload(new DownloadInfo(Now, "AKMLSQLSetup-1.26.0911.2219.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Denied,
            Location = GeoLocation.Unknown,
        });

    [Fact]
    public void WithNoDatabase_ItSaysTheDatabaseIsMissing_AndHowToInstallIt()
    {
        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "a.db"));
        LogDownloadWithoutCountry(store);

        using var ctx = NewCtx(store, geoInstalled: false);
        var cut = ctx.Render<AdminDownloads>();

        Assert.Contains("No country database installed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("update-geoip.ps1", cut.Markup, StringComparison.Ordinal);

        // The instruction must not send the owner chasing a licence key they no longer need.
        Assert.DoesNotContain("MaxMind licence key", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no account or licence key", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithADatabaseButOlderDownloads_ItSaysSoInstead()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AKML SQL Site",
            GeoLookup.DefaultFileName);

        if (!File.Exists(installed))
        {
            // No database on this machine; the other test covers that branch.
            return;
        }

        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "b.db"));
        LogDownloadWithoutCountry(store);

        using var ctx = NewCtx(store, geoInstalled: true);
        var cut = ctx.Render<AdminDownloads>();

        Assert.Contains("No country data yet", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("is</strong> installed", cut.Markup, StringComparison.Ordinal);

        // And it must NOT tell the owner to install something they already have.
        Assert.DoesNotContain("not installed on this server", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithCountriesRecorded_NoBannerAtAll()
    {
        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "c.db"));
        store.LogDownload(new DownloadInfo(Now, "AKMLSQLSetup-1.26.0911.2219.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Denied,
            Location = new GeoLocation("EG", "Egypt"),
        });

        using var ctx = NewCtx(store, geoInstalled: true);
        var cut = ctx.Render<AdminDownloads>();

        Assert.DoesNotContain("No country data", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No country database", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Egypt", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCountriesStat_CountsResolvedCountriesOnly()
    {
        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "d.db"));
        LogDownloadWithoutCountry(store);

        using var ctx = NewCtx(store, geoInstalled: false);
        var cut = ctx.Render<AdminDownloads>();

        // "1 COUNTRIES" when the only bucket is Unknown is a number that looks like data and is not.
        var stats = cut.FindAll(".admin-stat");
        var countries = stats.Last().TextContent;
        Assert.Contains("0", countries, StringComparison.Ordinal);
        Assert.Contains("Countries", countries, StringComparison.OrdinalIgnoreCase);
    }
}
