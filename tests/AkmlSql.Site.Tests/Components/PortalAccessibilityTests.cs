using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components;
using AkmlSql.Site.Components.Layout;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Consent;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T103/T106: accessibility and terminology for the surfaces this feature added.
/// <para>
/// The consent bar is now the most-seen new UI on the site — it renders for every visitor who has
/// not answered — so its semantics matter more than any admin page's. These assert the markup-level
/// properties: it is announced as a landmark, both choices are real focusable buttons in DOM order,
/// and nothing steals or traps focus.
/// </para>
/// </summary>
public sealed class PortalAccessibilityTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private SiteSettingsStore NewSettings()
    {
        var store = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        store.CreateTableIfMissing();
        store.Load();
        return store;
    }

    private BunitContext NewConsentCtx()
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        ctx.Services.AddSingleton(NewSettings());

        var http = new DefaultHttpContext();
        http.Request.Path = "/download";
        http.Items[ConsentMiddleware.StateKey] = ConsentState.Unknown;
        ctx.Services.AddSingleton<HttpContext>(http);
        return ctx;
    }

    private static IRenderedComponent<ConsentBar> RenderBar(BunitContext ctx) =>
        ctx.Render<ConsentBar>(ps => ps.AddCascadingValue(ctx.Services.GetRequiredService<HttpContext>()));

    // --- consent bar --------------------------------------------------------

    [Fact]
    public void ConsentBar_IsAnAnnouncedLandmark()
    {
        using var ctx = NewConsentCtx();

        var bar = RenderBar(ctx).Find(".consent-bar");

        Assert.Equal("region", bar.GetAttribute("role"));
        Assert.False(
            string.IsNullOrWhiteSpace(bar.GetAttribute("aria-label")),
            "A region landmark without an accessible name is announced as an unlabelled region.");
    }

    [Fact]
    public void ConsentBar_ChoicesAreRealKeyboardOperableButtons()
    {
        using var ctx = NewConsentCtx();
        var cut = RenderBar(ctx);

        foreach (var value in (string[])["granted", "denied"])
        {
            var button = cut.Find($"button[name='choice'][value='{value}']");

            // A <button type=submit> is focusable and activates on Enter AND Space for free.
            // A styled <div onclick> would do neither — which is how most cookie banners fail.
            Assert.Equal("BUTTON", button.TagName);
            Assert.Equal("submit", button.GetAttribute("type"));
            Assert.Null(button.GetAttribute("disabled"));
            Assert.Null(button.GetAttribute("tabindex")); // natural tab order, not a managed one
        }
    }

    [Fact]
    public void ConsentBar_DoesNotStealOrTrapFocus()
    {
        using var ctx = NewConsentCtx();
        var markup = RenderBar(ctx).Markup;

        // FR-043b: non-blocking. Each of these is a way to capture the keyboard.
        Assert.DoesNotContain("autofocus", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aria-modal", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inert", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tabindex=\"-1\"", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsentBar_LinksToTheNoticeWithDescriptiveText()
    {
        using var ctx = NewConsentCtx();

        var link = RenderBar(ctx).Find("a[href='/privacy']");

        // "Privacy notice" says where it goes; "click here" or a bare "more" would not.
        Assert.False(string.IsNullOrWhiteSpace(link.TextContent));
        Assert.DoesNotContain("click here", link.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.True(link.TextContent.Trim().Length > 4);
    }

    // --- portal shell -------------------------------------------------------

    private static BunitContext NewLayoutCtx(string path = "/admin/people")
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();

        // The layout marks a section active from the CURRENT path, so the test has to be on one.
        // bUnit's default URL is the base URI, where correctly nothing is active.
        var nav = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo(nav.BaseUri.TrimEnd('/') + path);

        return ctx;
    }

    [Fact]
    public void PortalNavigation_IsALabelledLandmarkWithAnActiveMarker()
    {
        using var ctx = NewLayoutCtx();
        var cut = ctx.Render<AdminLayout>();

        var nav = cut.Find("nav.admin-nav");
        Assert.False(string.IsNullOrWhiteSpace(nav.GetAttribute("aria-label")));

        // aria-current is what tells a screen-reader user which section they are in; a colour
        // change alone conveys it to nobody using one.
        Assert.Single(cut.FindAll(".admin-nav-list a[aria-current='page']"));
    }

    [Fact]
    public void PortalRangeSelector_IsALabelledLandmark()
    {
        using var ctx = NewLayoutCtx();
        var cut = ctx.Render<AdminLayout>();

        var toolbar = cut.Find("nav.admin-toolbar");
        Assert.False(string.IsNullOrWhiteSpace(toolbar.GetAttribute("aria-label")));
        Assert.NotNull(cut.Find(".admin-ranges[aria-labelledby]"));
    }

    // --- tables and forms ---------------------------------------------------

    [Fact]
    public void PeopleTable_UsesScopedHeaders()
    {
        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "analytics.db"));
        store.LogVisit(new VisitInfo(DateTimeOffset.UtcNow, "/download", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = Guid.NewGuid().ToString("N"),
        });

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);

        using (ctx)
        {
            var cut = ctx.Render<AdminPeople>();

            var headers = cut.FindAll("table.admin-table thead th");
            Assert.NotEmpty(headers);

            // Without scope, a screen reader cannot associate a cell with its column.
            Assert.All(headers, th => Assert.Equal("col", th.GetAttribute("scope")));
        }
    }

    [Fact]
    public void PeopleFilters_HaveAssociatedLabels()
    {
        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "analytics.db"));
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);

        using (ctx)
        {
            var cut = ctx.Render<AdminPeople>();

            foreach (var id in (string[])["filter-country", "filter-downloaded"])
            {
                Assert.NotNull(cut.Find($"select#{id}"));
                Assert.NotNull(cut.Find($"label[for='{id}']"));
            }
        }
    }

    // --- T106: terminology --------------------------------------------------

    [Fact]
    public void TheCanonicalTermIsIndividual_NotUser()
    {
        using var store = new AnalyticsStore(Path.Combine(_dir.Path, "analytics.db"));
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(store);

        using (ctx)
        {
            var markup = ctx.Render<AdminPeople>().Markup;

            // "individual" is the spec's canonical term. "user" is avoided deliberately: these are
            // browsers that accepted a cookie, not accounts, and calling them users invites exactly
            // the over-reading the coverage caveat exists to prevent.
            Assert.Contains("individual", markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" users", markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unique users", markup, StringComparison.OrdinalIgnoreCase);
        }
    }
}
