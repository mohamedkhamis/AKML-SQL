using AkmlSql.Site.Components.Layout;
using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Docs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 034 T019 (US2): bUnit tests for the /docs pages — section tree from the catalog,
/// empty-state message, cached-HTML rendering, unknown-slug NotFound behavior, and the
/// no-JS sidebar title filter (contracts/site-routes.md FR-004–FR-007).
/// </summary>
public sealed class DocsPagesTests
{
    private static Document MakeDoc(
        string slug,
        string title,
        string section,
        int order = int.MaxValue,
        string? html = null,
        IReadOnlyList<HeadingAnchor>? toc = null,
        DocBadge badge = DocBadge.None) =>
        new()
        {
            Title = title,
            Slug = slug,
            SourcePath = slug + ".md",
            Section = section,
            Order = order,
            HtmlContent = html ?? $"<p>Body of {title}.</p>",
            PlainText = $"Body of {title}.",
            Headings = [],
            Toc = toc ?? [],
            Badge = badge,
        };

    private static DocsContentService TwoSectionCatalog() =>
        DocsContentService.Create(
            [
                MakeDoc("architecture", "Architecture Overview", "Guides"),
                MakeDoc("formatting", "SQL Formatting", "Guides"),
                MakeDoc("web/m4-iis-installer", "M4 IIS Installer", "Web"),
            ]);

    private static BunitContext NewCtx(DocsContentService docs)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(docs);
        return ctx;
    }

    [Fact]
    public void DocsIndex_RendersSectionTreeWithLinks()
    {
        using var ctx = NewCtx(TwoSectionCatalog());

        var cut = ctx.Render<DocsIndex>();

        Assert.Contains("Guides", cut.Markup);
        Assert.Contains("Web", cut.Markup);
        Assert.Contains("Architecture Overview", cut.Markup);
        Assert.Contains("SQL Formatting", cut.Markup);
        Assert.NotNull(cut.Find("a[href='/docs/architecture']"));
        Assert.NotNull(cut.Find("a[href='/docs/web/m4-iis-installer']"));
    }

    [Fact]
    public void DocsIndex_EmptyCatalog_RendersEmptyState_NotError()
    {
        using var ctx = NewCtx(DocsContentService.Create([]));

        var cut = ctx.Render<DocsIndex>();

        Assert.Contains("no documentation", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocsIndex_FilterQueryParam_FiltersDocumentsByTitle()
    {
        using var ctx = NewCtx(TwoSectionCatalog());
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("filter", "arch"));

        var cut = ctx.Render<DocsIndex>();

        Assert.Contains("Architecture Overview", cut.Markup);
        Assert.DoesNotContain("SQL Formatting", cut.Markup);
        Assert.DoesNotContain("M4 IIS Installer", cut.Markup);
    }

    [Fact]
    public void DocsIndex_NoFilter_ShowsEveryDocument()
    {
        using var ctx = NewCtx(TwoSectionCatalog());

        var cut = ctx.Render<DocsIndex>();

        Assert.Contains("Architecture Overview", cut.Markup);
        Assert.Contains("SQL Formatting", cut.Markup);
        Assert.Contains("M4 IIS Installer", cut.Markup);
    }

    [Fact]
    public void DocsLayout_RendersSidebarWithNoJsFilterForm_AndSectionTree()
    {
        using var ctx = NewCtx(TwoSectionCatalog());

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>page body</p>"));

        // FR-007 baseline: plain GET form so the title filter works with JS disabled.
        var form = cut.Find("form.docs-filter");
        Assert.Equal("get", form.GetAttribute("method"));
        Assert.Equal("/docs", form.GetAttribute("action"));
        Assert.NotNull(form.QuerySelector("input[type='search'][name='filter']"));

        Assert.Contains("Guides", cut.Markup);
        Assert.Contains("Web", cut.Markup);
        Assert.Contains("page body", cut.Markup);

        // Progressive-enhancement search box is present but hidden until JS enables it.
        Assert.NotNull(cut.Find("#docs-search-input"));
    }

    [Fact]
    public void DocsLayout_MarksCurrentDocumentInSidebar()
    {
        using var ctx = NewCtx(TwoSectionCatalog());
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/docs/architecture");

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var current = cut.Find("a.docs-current");
        Assert.Equal("page", current.GetAttribute("aria-current"));
        Assert.Equal("/docs/architecture", current.GetAttribute("href"));
    }

    [Fact]
    public void DocPage_RendersCachedHtml_AndTitle_ForKnownSlug()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("architecture", "Architecture Overview", "Guides", html: "<h2 id=\"internals\">Internals</h2><p>Cached body.</p>")]));

        var cut = ctx.Render<DocPage>(ps => ps.Add(p => p.Slug, "architecture"));

        Assert.Contains("<h1", cut.Markup);
        Assert.Contains("Architecture Overview", cut.Markup);
        Assert.Contains("Internals", cut.Markup);
        Assert.Contains("Cached body.", cut.Markup);
    }

    [Fact]
    public void DocPage_UnknownSlug_InvokesNotFound()
    {
        using var ctx = NewCtx(TwoSectionCatalog());
        var nav = new RecordingNavigationManager();
        ctx.Services.AddSingleton<NavigationManager>(nav);

        ctx.Render<DocPage>(ps => ps.Add(p => p.Slug, "bogus-slug"));

        Assert.True(nav.NotFoundCalled);
    }

    [Fact]
    public void DocPage_RendersOnThisPageNav_WhenAtLeastTwoH2Anchors()
    {
        // U15: the TOC links carry the full route + fragment (a bare "#id" would resolve
        // against <base href="/"> and navigate home).
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("architecture", "Architecture Overview", "Guides",
                toc: [new HeadingAnchor("Internals", "internals"), new HeadingAnchor("Deployment", "deployment")])]));

        var cut = ctx.Render<DocPage>(ps => ps.Add(p => p.Slug, "architecture"));

        var nav = cut.Find("nav.on-this-page");
        Assert.Contains("On this page", nav.TextContent);
        Assert.NotNull(nav.QuerySelector("a[href='/docs/architecture#internals']"));
        Assert.NotNull(nav.QuerySelector("a[href='/docs/architecture#deployment']"));
    }

    [Fact]
    public void DocPage_OmitsOnThisPageNav_WhenFewerThanTwoH2Anchors()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("architecture", "Architecture Overview", "Guides",
                toc: [new HeadingAnchor("Internals", "internals")])]));

        var cut = ctx.Render<DocPage>(ps => ps.Add(p => p.Slug, "architecture"));

        Assert.Empty(cut.FindAll("nav.on-this-page"));
    }

    [Fact]
    public void DocsLayout_StripsAkmlSqlPrefix_FromSidebarNavTitles()
    {
        // U18: the "AKML SQL — " corpus prefix is display-only noise in nav — the full
        // title stays in the document H1 and the browser tab.
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("formatting", "AKML SQL — SQL Formatting", "Guides")]));

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var link = cut.Find("a[href='/docs/formatting']");
        Assert.Equal("SQL Formatting", link.TextContent.Trim());
        Assert.DoesNotContain("AKML SQL — SQL Formatting", cut.Markup);
    }

    [Fact]
    public void DocsLayout_NoMatchFilter_RendersNothingExtra_InSidebar()
    {
        // U20: the no-match state is reported once, by the /docs content area — the
        // sidebar just shows an empty tree.
        using var ctx = NewCtx(TwoSectionCatalog());
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("filter", "zzzz"));

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        Assert.DoesNotContain("No documents match", cut.Markup);
    }

    [Fact]
    public void DocsLayout_SearchResults_HaveLiveRegionWiring()
    {
        // U21: dynamic search results must be announced to screen readers.
        // A11Y-003: role="status" used to sit on the <ul> itself, which overrode the list role —
        // results were announced as one blob instead of "list, N items". The live region is now a
        // separate node announcing the count, and the list keeps listbox semantics.
        using var ctx = NewCtx(TwoSectionCatalog());

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var status = cut.Find("#docs-search-status");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal("polite", status.GetAttribute("aria-live"));

        var results = cut.Find("ul#docs-search-results");
        Assert.Equal("listbox", results.GetAttribute("role"));
        Assert.NotNull(results.GetAttribute("aria-label"));

        // The input drives the listbox, so it must advertise the relationship.
        var input = cut.Find("#docs-search-input");
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("docs-search-results", input.GetAttribute("aria-controls"));
    }

    [Fact]
    public void DocsLayout_FilterForm_KeepsDocsFilterClass_AsCssHideHook()
    {
        // U16: site.css hides the GET filter form via `html.js .docs-filter` once the
        // JS full-text search is live — the class hook must stay on the form.
        using var ctx = NewCtx(TwoSectionCatalog());

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        Assert.NotNull(cut.Find("form.docs-filter"));
    }

    [Fact]
    public void DocsIndex_StripsAkmlSqlPrefix_FromIndexLinks()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("formatting", "AKML SQL — SQL Formatting", "Guides")]));

        var cut = ctx.Render<DocsIndex>();

        var link = cut.Find("a[href='/docs/formatting']");
        Assert.Equal("SQL Formatting", link.TextContent.Trim());
        Assert.DoesNotContain("AKML SQL — SQL Formatting", cut.Markup);
    }

    [Fact]
    public void DocsIndex_NoMatchFilter_RendersSingleNoMatchMessage()
    {
        // U20: exactly one "No documents match" rendering across the whole page.
        using var ctx = NewCtx(TwoSectionCatalog());
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("filter", "zzzz"));

        var cut = ctx.Render<DocsIndex>();

        Assert.Contains("No documents match", cut.Markup);
        var first = cut.Markup.IndexOf("No documents match", StringComparison.Ordinal);
        Assert.Equal(-1, cut.Markup.IndexOf("No documents match", first + 1, StringComparison.Ordinal));
    }

    [Fact]
    public void DocsLayout_RendersNewBadge_InSidebar_ForFreshDoc()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("topics/getting-started", "Getting Started", "User Guides", badge: DocBadge.New)]));

        var cut = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var badge = cut.Find("span.doc-badge.doc-badge-new");
        Assert.Equal("New", badge.TextContent.Trim());
        // The pill rides inside the sidebar link, right after the title text.
        Assert.Equal("/docs/topics/getting-started", badge.Closest("a")!.GetAttribute("href"));
    }

    [Fact]
    public void DocsIndex_RendersUpdatedBadge_AndLegendWithDefaultWindow()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("topics/formatting", "Formatting SQL", "User Guides", badge: DocBadge.Updated)]));

        var cut = ctx.Render<DocsIndex>();

        var badge = cut.Find("span.doc-badge.doc-badge-updated");
        Assert.Equal("Updated", badge.TextContent.Trim());
        var legend = cut.Find("p.docs-legend");
        Assert.Contains("added in the last 30 days", legend.TextContent);
        Assert.Contains("changed in the last 30 days", legend.TextContent);
    }

    [Fact]
    public void DocsIndex_Legend_UsesConfiguredBadgeWindow()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("architecture", "Architecture Overview", "Guides")],
            badgeWindowDays: 14));

        var cut = ctx.Render<DocsIndex>();

        Assert.Contains("added in the last 14 days", cut.Find("p.docs-legend").TextContent);
    }

    [Fact]
    public void DocPage_RendersNewBadge_NextToH1()
    {
        using var ctx = NewCtx(DocsContentService.Create(
            [MakeDoc("topics/getting-started", "Getting Started", "User Guides", badge: DocBadge.New)]));

        var cut = ctx.Render<DocPage>(ps => ps.Add(p => p.Slug, "topics/getting-started"));

        Assert.NotNull(cut.Find(".doc-header h1 span.doc-badge.doc-badge-new"));
    }

    [Fact]
    public void DocsPages_NoBadges_ForDocsWithoutFreshMetadata()
    {
        using var ctx = NewCtx(TwoSectionCatalog());

        var layout = ctx.Render<DocsLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));
        var index = ctx.Render<DocsIndex>();
        var page = ctx.Render<DocPage>(ps => ps.Add(p => p.Slug, "architecture"));

        Assert.Empty(layout.FindAll(".doc-badge"));
        Assert.Empty(index.FindAll(".doc-badge"));
        Assert.Empty(page.FindAll(".doc-badge"));
    }

    /// <summary>
    /// Records <see cref="NavigationManager.NotFound"/> calls. In .NET 10 NotFound() raises the
    /// <c>OnNotFound</c> event (the static-SSR host turns that into the 404 response), so this
    /// test double subscribes to its own event — bUnit's fake navigation manager does not model it.
    /// </summary>
    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/docs");
            OnNotFound += (_, _) => NotFoundCalled = true;
        }

        public bool NotFoundCalled { get; private set; }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}
