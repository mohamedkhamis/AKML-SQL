using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// Browser tests against the deployed public site. These exercise the things unit tests cannot:
/// that stylesheets actually apply, that the progressive-enhancement scripts run, and that the
/// layout holds at a real mobile viewport.
/// </summary>
[Collection(SiteCollection.Name)]
public sealed class PublicSiteTests(SiteFixture site)
{
    private void SkipIfUnavailable() => Skip.If(site.SkipReason is not null, site.SkipReason);

    // --- The whole-page failure mode: CSS not applying ----------------------

    [SkippableFact]
    public async Task HomePage_IsActuallyStyled()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl);

        // A page whose stylesheets failed to load renders on the default white ground with the
        // skip link visible. Assert on computed style rather than markup, so a 200-but-empty
        // stylesheet (the exact failure seen under `dotnet run`) is caught.
        var background = await page.EvaluateAsync<string>(
            "getComputedStyle(document.body).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", background);
        Assert.NotEqual("rgb(255, 255, 255)", background);

        // The skip link is positioned off-screen by CSS until focused.
        var skipLinkTop = await page.EvaluateAsync<double>(
            "document.querySelector('.skip-link').getBoundingClientRect().bottom");
        Assert.True(skipLinkTop < 0, $"Skip link is visible at {skipLinkTop}px — site.css did not apply.");

        // The hero headline is laid out at display size, which only the stylesheet provides.
        var fontSize = await page.EvaluateAsync<double>(
            "parseFloat(getComputedStyle(document.querySelector('#hero-heading')).fontSize)");
        Assert.True(fontSize > 30, $"Hero headline is {fontSize}px — type scale did not apply.");
    }

    [SkippableFact]
    public async Task StaticAssets_AreServedCompressed_ToARealBrowser()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        var encodings = new Dictionary<string, string>();
        page.Response += (_, response) =>
        {
            if (response.Url.Contains("/css/", StringComparison.Ordinal)
                || response.Url.Contains("/js/", StringComparison.Ordinal))
            {
                encodings[response.Url] = response.Headers.GetValueOrDefault("content-encoding", "");
            }
        };

        await page.GotoAsync(SiteFixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotEmpty(encodings);
        // Every stylesheet/script the browser fetched came back compressed and non-empty.
        Assert.All(encodings, kv =>
            Assert.True(kv.Value is "br" or "gzip", $"{kv.Key} served with encoding '{kv.Value}'"));
    }

    // --- UI-004 / A11Y-006: the three-option theme control ------------------

    [SkippableFact]
    public async Task ThemePicker_SwitchesTheme_AndPersistsAcrossReload()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl);

        // The control is revealed only once its script has run (no dead button without JS).
        await page.WaitForSelectorAsync("html.js-theme-toggle #theme-toggle");
        Assert.Equal("false", await page.GetAttributeAsync("#theme-toggle", "aria-expanded"));

        await page.ClickAsync("#theme-toggle");
        Assert.Equal("true", await page.GetAttributeAsync("#theme-toggle", "aria-expanded"));

        // UI-004: high contrast shipped from day one but nothing could select it.
        await page.ClickAsync("[data-theme-value='high-contrast']");
        Assert.Equal("high-contrast", await page.GetAttributeAsync("html", "data-akml-theme"));
        Assert.Contains("high-contrast.css", await page.GetAttributeAsync("#akml-theme-css", "href"));

        // A11Y-006: state is exposed, not just implied by a label.
        await page.ClickAsync("#theme-toggle");
        Assert.Equal("true", await page.GetAttributeAsync("[data-theme-value='high-contrast']", "aria-checked"));
        Assert.Equal("false", await page.GetAttributeAsync("[data-theme-value='dark']", "aria-checked"));

        await page.ReloadAsync();
        Assert.Equal("high-contrast", await page.GetAttributeAsync("html", "data-akml-theme"));
    }

    [SkippableFact]
    public async Task LightTheme_KeepsCodeBlocksDistinctFromTheirPanel()
    {
        SkipIfUnavailable();
        // UI-002: light surface-elevated used to equal surface-panel, so code blocks, badges and
        // buttons all flattened to a border.
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/docs/topics/connecting");
        await page.WaitForSelectorAsync("html.js-theme-toggle #theme-toggle");

        await page.ClickAsync("#theme-toggle");
        await page.ClickAsync("[data-theme-value='light']");
        await page.WaitForFunctionAsync("document.documentElement.getAttribute('data-akml-theme') === 'light'");

        var (codeBackground, pageBackground) = (
            await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.doc-body pre')).backgroundColor"),
            await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor"));

        Assert.NotEqual(pageBackground, codeBackground);
    }

    // --- DOC-003/004: the docs reading experience ---------------------------

    [SkippableFact]
    public async Task DocPage_ShowsFreshness_Permalinks_TocRail_AndPager()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/docs/topics/connecting");

        // DOC-003: the git date was loaded for the badge and then discarded.
        Assert.Contains("Last updated", await page.InnerTextAsync(".doc-meta"));

        // DOC-004: permalinks exist and resolve to the heading they name.
        var anchorHref = await page.GetAttributeAsync(".doc-body .doc-anchor", "href");
        Assert.StartsWith("#", anchorHref);
        Assert.Equal(1, await page.Locator($"[id='{anchorHref![1..]}']").CountAsync());

        // DOC-004: the TOC is a sticky rail at desktop width, not a card above the article.
        Assert.Equal("sticky", await page.EvaluateAsync<string>(
            "getComputedStyle(document.querySelector('.on-this-page')).position"));

        // DOC-004: prev/next follow the sidebar order and actually navigate.
        var nextHref = await page.GetAttributeAsync(".doc-pager-next", "href");
        Assert.NotNull(nextHref);
        await page.ClickAsync(".doc-pager-next");
        await page.WaitForURLAsync($"**{nextHref}");

        // DOC-004: edit link points at the document's own source file.
        Assert.Contains("/doc/topics/", await page.GetAttributeAsync(".doc-source a", "href"));
    }

    [SkippableFact]
    public async Task CodeBlocks_HaveAWorkingCopyButton()
    {
        SkipIfUnavailable();
        // DOC-005: the copy affordance existed for one hash on /download and nowhere in the docs.
        await using var context = await site.NewContextAsync();
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/docs/topics/connecting");

        var button = page.Locator(".code-block .copy-code-btn").First;
        await button.WaitForAsync();
        await button.ClickAsync();

        await Assertions.Expect(button).ToHaveTextAsync("Copied!");
        var clipboard = await page.EvaluateAsync<string>("navigator.clipboard.readText()");
        Assert.False(string.IsNullOrWhiteSpace(clipboard));
    }

    [SkippableFact]
    public async Task DocsSearch_ReturnsExcerpts_AndSupportsKeyboardNavigation()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/docs");

        // PERF-002: the index is fetched on first contact with the box, not on page load.
        var indexRequested = false;
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/search-index.json", StringComparison.Ordinal))
            {
                indexRequested = true;
            }
        };

        await page.WaitForSelectorAsync("html.js .docs-search");
        Assert.False(indexRequested, "search-index.json was fetched before the box was touched.");

        await page.ClickAsync("#docs-search-input");
        await page.FillAsync("#docs-search-input", "format");
        await page.WaitForSelectorAsync("#docs-search-results li[role='option']");
        Assert.True(indexRequested);

        // DOC-006: results carry a match excerpt and a count, not just a title.
        Assert.NotEmpty(await page.InnerTextAsync(".docs-search-excerpt"));
        Assert.Contains("result", await page.InnerTextAsync("#docs-search-status"));

        // A11Y-003: the list keeps listbox semantics (role=status used to sit on the <ul>).
        Assert.Equal("listbox", await page.GetAttributeAsync("#docs-search-results", "role"));

        // A11Y-007: arrow keys move a selection; Enter follows it.
        await page.Keyboard.PressAsync("ArrowDown");
        var active = page.Locator("#docs-search-results li[aria-selected='true']");
        Assert.Equal(1, await active.CountAsync());
        var target = await active.Locator("a").GetAttributeAsync("href");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync($"**{target}");
    }

    // --- DL-003 / SEO-001 / SEO-002 -----------------------------------------

    [SkippableFact]
    public async Task DownloadPage_ShowsSizeAndACopyableDigest()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        // DL-003: read from the file on disk, so it cannot be stale.
        Assert.Contains("Download size", await page.InnerTextAsync(".release-facts"));

        var digest = await page.InnerTextAsync("#latest-sha256");
        Assert.Equal(64, digest.Trim().Length);

        await page.ClickAsync(".copy-hash-btn");
        Assert.Equal(digest.Trim(), (await page.EvaluateAsync<string>("navigator.clipboard.readText()")).Trim());
    }

    [SkippableFact]
    public async Task Head_CarriesCanonicalAndShareTags()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/features");

        // SEO-001: every share used to render a blank card.
        Assert.Equal($"{SiteFixture.BaseUrl}/features",
            await page.GetAttributeAsync("link[rel='canonical']", "href"));
        Assert.Equal("summary_large_image",
            await page.GetAttributeAsync("meta[name='twitter:card']", "content"));

        var ogImage = await page.GetAttributeAsync("meta[property='og:image']", "content");
        Assert.NotNull(ogImage);

        // The share card is really there, not just referenced.
        var response = await page.APIRequest.GetAsync(ogImage!);
        Assert.True(response.Ok, $"og:image returned {response.Status}");
        Assert.Contains("image/", response.Headers.GetValueOrDefault("content-type", ""));
    }

    [SkippableFact]
    public async Task RobotsTxt_PointsAtThisHost_AndDisallowsAdmin()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.APIRequest.GetAsync(SiteFixture.BaseUrl + "/robots.txt");
        var body = await response.TextAsync();

        // SEO-002: the checked-in file advertised a sitemap on a host the site does not serve.
        Assert.Contains($"Sitemap: {SiteFixture.BaseUrl}/sitemap.xml", body, StringComparison.Ordinal);
        Assert.Contains("Disallow: /admin", body, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task ContributorDocs_AreNotPublished()
    {
        SkipIfUnavailable();
        // DOC-001: IPC internals and deploy procedure were public product documentation.
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        foreach (var slug in (string[])["ipc-api", "architecture", "deployment", "m3-security", "use-cases"])
        {
            var response = await page.APIRequest.GetAsync($"{SiteFixture.BaseUrl}/docs/{slug}");
            Assert.Equal(404, response.Status);
        }
    }

    // --- SC-003: the mobile viewport --------------------------------------

    [SkippableFact]
    public async Task MobileViewport_HasNoHorizontalOverflow_AndAWorkingMenu()
    {
        SkipIfUnavailable();
        // A real 390x844 viewport — the spec's SC-003 floor is 360px.
        await using var context = await site.NewContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl);

        var (scrollWidth, clientWidth) = (
            await page.EvaluateAsync<int>("document.documentElement.scrollWidth"),
            await page.EvaluateAsync<int>("document.documentElement.clientWidth"));
        Assert.True(scrollWidth <= clientWidth + 1,
            $"Page scrolls horizontally at 390px: scrollWidth {scrollWidth} vs viewport {clientWidth}.");

        // UI-003: the hero visual is cropped on phones, not removed.
        Assert.True(await page.Locator(".hero-visual").IsVisibleAsync(),
            "The hero visual is hidden at phone width.");

        // A11Y-004: the menu reports open/closed state.
        var toggle = page.Locator(".nav-toggle-btn");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        await page.CheckAsync(".nav-toggle");
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await page.Locator("#site-nav-links").IsVisibleAsync());
    }

    [SkippableTheory]
    [InlineData(360)]
    [InlineData(768)]
    [InlineData(1024)]
    [InlineData(1920)]
    public async Task NoHorizontalOverflow_AcrossTheSupportedWidthRange(int width)
    {
        SkipIfUnavailable();
        // SC-003: "renders without layout breakage at viewport widths from 360px to 1920px".
        await using var context = await site.NewContextAsync(width, 900);
        var page = await context.NewPageAsync();

        foreach (var path in (string[])["/", "/features", "/download", "/docs", "/docs/topics/connecting"])
        {
            await page.GotoAsync(SiteFixture.BaseUrl + path);
            var scrollWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
            var clientWidth = await page.EvaluateAsync<int>("document.documentElement.clientWidth");
            Assert.True(scrollWidth <= clientWidth + 1,
                $"{path} scrolls horizontally at {width}px: scrollWidth {scrollWidth} vs {clientWidth}.");
        }
    }
}
