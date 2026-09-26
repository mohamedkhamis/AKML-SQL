using AkmlSql.Site.Admin;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T038/T040 (US2): the settings page.
/// </summary>
public sealed class AdminSettingsPageTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static Release MakeRelease(int index) => new()
    {
        Version = $"1.26.0910.{1000 + index}",
        ReleasedAt = new DateOnly(2026, 9, 10).AddDays(-index),
        SupportedHosts = ["SSMS 22"],
        DownloadUrl = $"downloads/AKMLSQLSetup-{index}.exe",
        Sha256Hash = new string('a', 64),
        CdnUrl = $"https://github.com/mohamedkhamis/AKML-SQL/releases/download/v{index}/s.exe",
    };

    private SiteSettingsStore NewSettings(ReleaseVisibilityMode? mode = null, int count = 3, bool createTable = true)
    {
        var store = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        if (createTable)
        {
            store.CreateTableIfMissing();
        }

        store.Load();
        if (mode is not null)
        {
            store.Save(store.Current with { Visibility = mode.Value, VisibilityCount = count }, "owner");
        }

        return store;
    }

    private BunitContext NewCtx(SiteSettingsStore settings, int releaseCount = 16)
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        ctx.Services.AddSingleton(settings);
        ctx.Services.AddSingleton(ReleasesManifest.Create(
            Enumerable.Range(0, releaseCount).Select(MakeRelease).ToList(), product: "AKML SQL"));
        ctx.Services.AddSingleton(new ReleaseAvailability(_dir.Path));
        return ctx;
    }

    [Fact]
    public void Page_RendersEveryVisibilityOptionWithItsDescription()
    {
        using var ctx = NewCtx(NewSettings());

        var cut = ctx.Render<AdminSettings>();

        // FR-011 / A5.1: each choice explains in plain language what it publishes.
        Assert.Contains("Latest release only", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("All releases", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No history section is shown", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(3, cut.FindAll("input[type='radio'][name='visibility']").Count);
    }

    [Fact]
    public void Page_MarksTheSavedChoiceAsSelected()
    {
        using var ctx = NewCtx(NewSettings(ReleaseVisibilityMode.LatestOnly));

        var cut = ctx.Render<AdminSettings>();

        var selected = cut.Find("input[type='radio'][name='visibility'][checked]");
        Assert.Equal("LatestOnly", selected.GetAttribute("value"));
    }

    [Fact]
    public void Preview_ListsExactlyWhatThePublicPageWouldShow()
    {
        var settings = NewSettings(ReleaseVisibilityMode.LatestN, 3);
        using var ctx = NewCtx(settings, releaseCount: 16);

        var cut = ctx.Render<AdminSettings>();

        // FR-017 / A5.2: computed with the same helper the public page uses, so the preview cannot
        // disagree with what visitors actually see.
        Assert.Equal(3, cut.FindAll(".release-preview li").Count);
        Assert.Contains("1.26.0910.1002", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("1.26.0910.1003", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("13 older releases are hidden", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_SaysHiddenReleasesRemainReachable()
    {
        using var ctx = NewCtx(NewSettings(ReleaseVisibilityMode.LatestOnly), releaseCount: 5);

        var cut = ctx.Render<AdminSettings>();

        // FR-015 is a promise to the owner as much as a behaviour: hiding does not break links.
        Assert.Contains("Direct links to them keep working", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationErrors_RenderInlineAndNameTheBound()
    {
        var settings = NewSettings();
        using var ctx = NewCtx(settings);
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter(
            "error", "Number of releases to show must be between 1 and 50. You entered 999."));

        var cut = ctx.Render<AdminSettings>();

        Assert.Contains("Nothing was saved", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("between 1 and 50", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedFlag_ConfirmsWhatChangedAndWhenItTakesEffect()
    {
        using var ctx = NewCtx(NewSettings(ReleaseVisibilityMode.LatestOnly));
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("saved", "1"));

        var cut = ctx.Render<AdminSettings>();

        // FR-035 / A5.4.
        Assert.Contains("Settings saved", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Latest release only", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("live on the next public request", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTheStoreCouldNotBeRead_TheOwnerIsToldRatherThanLeftGuessing()
    {
        var broken = NewSettings(createTable: false);
        Assert.True(broken.LoadFailed);

        using var ctx = NewCtx(broken);

        var cut = ctx.Render<AdminSettings>();

        // FR-016a: never fatal to the public page, so the portal is where it must be visible.
        Assert.Contains("Settings could not be read", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("documented defaults", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Form_PostsWithoutJavaScript()
    {
        using var ctx = NewCtx(NewSettings());

        var cut = ctx.Render<AdminSettings>();

        // Contract A5.6 — plain form POST, consistent with the rest of the site.
        var form = cut.Find("form[method='post'][action='/admin/settings']");
        Assert.NotNull(form);
        Assert.NotNull(cut.Find("button[type='submit']"));
        Assert.Empty(cut.FindAll("script"));
    }

    [Fact]
    public void SettingsRoutes_RequireAuthentication()
    {
        // T040 / FR-033. Both the page and its POST live under /admin, so the existing branch guard
        // covers them with no second authorization path.
        Assert.True(RequiresChallenge("/admin/settings"));
        Assert.True(RequiresChallenge("/admin/settings", "POST"));
        Assert.False(RequiresChallenge("/admin/login"));
    }

    private static bool RequiresChallenge(string path, string method = "GET")
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.Method = method;
        return AdminBranchMiddleware.RequiresChallenge(http);
    }
}
