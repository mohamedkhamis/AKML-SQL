using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Consent;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T082/T091/T092/T093 (US3/US4): the new portal pages.
/// <para>
/// The People page carries an assertion that matters more than the table itself — the coverage
/// caveat. The owner chose to ask every visitor and block none, so most traffic is unattributed by
/// design; a list of individuals presented without that number reads as the whole audience, and
/// every conclusion drawn from it would be wrong.
/// </para>
/// </summary>
public sealed class AdminPortalPageTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private AnalyticsStore NewStore() => new(Path.Combine(_dir.Path, "analytics.db"));

    private SiteSettingsStore NewSettings(ReleaseVisibilityMode mode = ReleaseVisibilityMode.LatestN, int count = 3)
    {
        var store = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        store.CreateTableIfMissing();
        store.Load();
        store.Save(store.Current with { Visibility = mode, VisibilityCount = count }, "test");
        return store;
    }

    private static Release MakeRelease(int index, bool withCdn = true) => new()
    {
        Version = $"1.26.0910.{1000 + index}",
        ReleasedAt = new DateOnly(2026, 9, 10).AddDays(-index),
        SupportedHosts = ["SSMS 22"],
        DownloadUrl = $"downloads/AKMLSQLSetup-{index}.exe",
        Sha256Hash = new string('a', 64),
        CdnUrl = withCdn ? $"https://github.com/mohamedkhamis/AKML-SQL/releases/download/v{index}/s.exe" : null,
    };

    // --- T082: the People page ---------------------------------------------

    private BunitContext NewPeopleCtx(AnalyticsStore store)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);
        return ctx;
    }

    private static VisitInfo Visit(string? visitorId, string? country = "Egypt") =>
        new(Now, "/download", null, "Chrome", "203.0.113.7")
        {
            Consent = visitorId is null ? ConsentState.Denied : ConsentState.Granted,
            VisitorId = visitorId,
            Location = country is null ? GeoLocation.Unknown : new GeoLocation(country[..2].ToUpperInvariant(), country),
        };

    [Fact]
    public void PeoplePage_ListsIndividuals()
    {
        using var store = NewStore();
        var id = Guid.NewGuid().ToString("N");
        store.LogVisit(Visit(id));

        using var ctx = NewPeopleCtx(store);
        var cut = ctx.Render<AdminPeople>();

        Assert.Contains(id[..8], cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Egypt", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PeoplePage_LeadsWithWhatTheListCannotSee()
    {
        using var store = NewStore();
        store.LogVisit(Visit(Guid.NewGuid().ToString("N")));
        store.LogVisit(Visit(visitorId: null));
        store.LogVisit(Visit(visitorId: null));
        store.LogVisit(Visit(visitorId: null));

        using var ctx = NewPeopleCtx(store);
        var cut = ctx.Render<AdminPeople>();

        // FR-047 / contract M3.1: the unattributed share is stated, not buried.
        Assert.Contains("What this list can and cannot see", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("75%", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("cannot be tied to anyone", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PeoplePage_StatesThatOneRowIsOneBrowser_NotOnePerson()
    {
        using var store = NewStore();

        using var ctx = NewPeopleCtx(store);
        var cut = ctx.Render<AdminPeople>();

        // Contract C5.1 — the caveat has to be on the page, because the number invites the wrong
        // reading: two devices are two rows, and clearing cookies creates a third.
        Assert.Contains("browser", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clearing cookies creates a new one", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PeoplePage_FiltersTravelInTheQueryString_SoTheViewIsBookmarkable()
    {
        using var store = NewStore();
        store.LogVisit(Visit(Guid.NewGuid().ToString("N")));

        using var ctx = NewPeopleCtx(store);
        var cut = ctx.Render<AdminPeople>();

        // Contract A4.5: a plain GET form, so filters end up in the URL and the page stays static SSR.
        var form = cut.Find("form[method='get'][action='/admin/people']");
        Assert.NotNull(form);
        Assert.NotNull(cut.Find("select[name='country']"));
        Assert.NotNull(cut.Find("select[name='downloaded']"));
        Assert.Empty(cut.FindAll("script"));
    }

    [Fact]
    public void PeoplePage_WithNobodyTracked_ExplainsWhyRatherThanRenderingAnEmptyTable()
    {
        using var store = NewStore();

        using var ctx = NewPeopleCtx(store);
        var cut = ctx.Render<AdminPeople>();

        Assert.Contains("No individuals match", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("nobody has accepted tracking", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PeoplePage_ExportLinkCarriesTheWindowAndFilters()
    {
        using var store = NewStore();

        using var ctx = NewPeopleCtx(store);
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("days", "7"));

        var cut = ctx.Render<AdminPeople>();

        var export = cut.Find("a.admin-export");
        Assert.Contains("days=7", export.GetAttribute("href"), StringComparison.Ordinal);
    }

    // --- T092: the Releases page -------------------------------------------

    private BunitContext NewReleasesCtx(ReleasesManifest manifest, string downloadsFolder)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(manifest);
        ctx.Services.AddSingleton(new ReleaseAvailability(downloadsFolder));
        ctx.Services.AddSingleton(NewSettings());
        ctx.Services.Configure<DownloadsOptions>(o => o.Folder = downloadsFolder);
        return ctx;
    }

    [Fact]
    public void ReleasesPage_ShowsCdnBackedReleasesAsAvailableWithoutALocalFile()
    {
        var downloads = Path.Combine(_dir.Path, "downloads");
        Directory.CreateDirectory(downloads);

        using var ctx = NewReleasesCtx(ReleasesManifest.Create([MakeRelease(0)]), downloads);
        var cut = ctx.Render<AdminReleases>();

        // The defect this feature fixed: a CDN-backed release with no local file is still offered.
        Assert.Contains("1.26.0910.1000", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("not downloadable", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleasesPage_FlagsAdvertisedButMissing()
    {
        var downloads = Path.Combine(_dir.Path, "downloads-empty");
        Directory.CreateDirectory(downloads);

        // No CDN mirror AND no local file: the case that turns the primary CTA into a dead link.
        using var ctx = NewReleasesCtx(ReleasesManifest.Create([MakeRelease(0, withCdn: false)]), downloads);
        var cut = ctx.Render<AdminReleases>();

        Assert.Contains("cannot be downloaded", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not downloadable", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleasesPage_FlagsPresentButUnadvertised()
    {
        var downloads = Path.Combine(_dir.Path, "downloads-orphan");
        Directory.CreateDirectory(downloads);
        File.WriteAllBytes(Path.Combine(downloads, "AKMLSQLSetup-orphan.exe"), new byte[2048]);

        using var ctx = NewReleasesCtx(ReleasesManifest.Create([MakeRelease(0)]), downloads);
        var cut = ctx.Render<AdminReleases>();

        // 1.38 GB of these accumulated on the live server before anything could show them.
        Assert.Contains("On disk but not in the manifest", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("AKMLSQLSetup-orphan.exe", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2 KB", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasesPage_SaysHiddenReleasesStayReachable()
    {
        var downloads = Path.Combine(_dir.Path, "downloads-hidden");
        Directory.CreateDirectory(downloads);

        using var ctx = NewReleasesCtx(
            ReleasesManifest.Create(Enumerable.Range(0, 10).Select(i => MakeRelease(i)).ToList()), downloads);
        var cut = ctx.Render<AdminReleases>();

        // FR-015 is a promise the owner needs to see, or hiding a release looks like deleting it.
        Assert.Contains("Hidden releases stay reachable", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasesPage_IsReadOnly()
    {
        var downloads = Path.Combine(_dir.Path, "downloads-ro");
        Directory.CreateDirectory(downloads);

        using var ctx = NewReleasesCtx(ReleasesManifest.Create([MakeRelease(0)]), downloads);
        var cut = ctx.Render<AdminReleases>();

        // Contract A6.4: editing metadata and uploading installers are out of scope, so the page
        // must offer no way to do either.
        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("input[type='file']"));
    }

    // --- T093: the sign-in page stays outside the portal shell --------------

    [Fact]
    public void LoginPage_DoesNotUseThePortalShell()
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        ctx.Services.Configure<AdminOptions>(o => o.PasswordHash = AdminAuth.HashPassword("correct horse battery staple"));

        using (ctx)
        {
            var cut = ctx.Render<AdminLogin>();

            // Contract A3.5: a sign-in page must not display navigation to sections the visitor
            // cannot reach. Asserted on the rendered markup AND on the source, because bUnit renders
            // a component without its layout either way.
            Assert.Empty(cut.FindAll(".admin-nav-list"));
            Assert.Empty(cut.FindAll("form[action='/admin/logout']"));
        }

        var source = File.ReadAllText(LoginSourcePath());
        Assert.DoesNotContain("@layout AdminLayout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOtherPortalPage_DoesUseTheShell()
    {
        foreach (var page in (string[])
                 ["AdminDashboard.razor", "AdminDownloads.razor", "AdminPeople.razor",
                  "AdminPerson.razor", "AdminPages.razor", "AdminReleases.razor",
                  "AdminSettings.razor", "AdminErrors.razor"])
        {
            var source = File.ReadAllText(Path.Combine(PortalPagesDir(), page));
            Assert.Contains("@layout AdminLayout", source, StringComparison.Ordinal);
        }
    }

    private static string PortalPagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "AkmlSql.Site", "Components", "Pages", "Admin");
    }

    private static string LoginSourcePath() => Path.Combine(PortalPagesDir(), "AdminLogin.razor");
}
