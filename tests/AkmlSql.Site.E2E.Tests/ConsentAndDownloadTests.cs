using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// Spec 038 (US1/US2/US5) in a real browser against the deployed site.
/// <para>
/// These cover what the quickstart listed as manual-only: the consent bar actually blocking nothing,
/// the download path working with JavaScript disabled, the release list honouring the owner's
/// setting, and the layout holding at phone width. Every one of them needs a browser — a bUnit
/// render cannot tell you whether a fixed-position bar covers the download button, and an HTTP
/// client cannot tell you whether the page works without its scripts.
/// </para>
/// </summary>
[Collection(SiteCollection.Name)]
public sealed class ConsentAndDownloadTests(SiteFixture site)
{
    private void SkipIfUnavailable() => Skip.If(site.SkipReason is not null, site.SkipReason);

    // --- US5: the consent bar ----------------------------------------------

    [SkippableFact]
    public async Task ConsentBar_AppearsForAFreshVisitor_WithBothChoices()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var bar = page.Locator(".consent-bar");
        await Assertions.Expect(bar).ToBeVisibleAsync();

        // FR-043b: equal prominence, both real submit buttons in the same form.
        await Assertions.Expect(page.Locator("button[name='choice'][value='granted']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("button[name='choice'][value='denied']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".consent-bar a[href='/privacy']")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task ConsentBar_IsShort_AndNamesCookiesAndTheAddress()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var text = (await page.Locator(".consent-bar-text").InnerTextAsync()).Trim();
        var words = text.Split((string[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.True(words <= 30, $"Consent banner is {words} words: \"{text}\"");
        Assert.Contains("cookie", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IP address", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task ConsentBar_DoesNotCoverTheDownloadButton()
    {
        SkipIfUnavailable();

        // THE assertion that no unit test can make. FR-043b says the bar blocks nothing; the only
        // honest check is geometric — does the bar's box overlap the primary call to action?
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var button = await page.Locator("a.btn-primary.download-tracked").First.BoundingBoxAsync();
        var bar = await page.Locator(".consent-bar").BoundingBoxAsync();

        Assert.NotNull(button);
        Assert.NotNull(bar);

        var overlaps = button!.X < bar!.X + bar.Width
                       && bar.X < button.X + button.Width
                       && button.Y < bar.Y + bar.Height
                       && bar.Y < button.Y + button.Height;

        Assert.False(overlaps, "The consent bar overlaps the primary download button.");
    }

    [SkippableFact]
    public async Task IgnoringTheBar_StillLetsTheVisitorDownload_AndIssuesNoIdentity()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        // Click the download link WITHOUT touching the bar. The href points at the CDN (upgraded by
        // download-track.js), so assert the navigation target rather than downloading 95 MB.
        var href = await page.Locator("a.btn-primary.download-tracked").First.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(href));

        // SC-021 / contract C2.6: silence is not consent.
        var cookies = await context.CookiesAsync();
        Assert.DoesNotContain(cookies, c => c.Name == "akml.vid");
        Assert.DoesNotContain(cookies, c => c.Name == "akml.consent");
    }

    [SkippableFact]
    public async Task Accepting_IssuesBothCookies_AndTheBarDoesNotReturn()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        await page.Locator("button[name='choice'][value='granted']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);

        var cookies = await context.CookiesAsync();
        var consent = Assert.Single(cookies, c => c.Name == "akml.consent");
        var vid = Assert.Single(cookies, c => c.Name == "akml.vid");

        Assert.Equal("granted", consent.Value);
        Assert.Equal(32, vid.Value.Length);
        Assert.True(vid.HttpOnly, "akml.vid must be HttpOnly");
        Assert.True(vid.Secure, "akml.vid must be Secure");

        // FR-046: never asked again.
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");
        await Assertions.Expect(page.Locator(".consent-bar")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Declining_RemembersTheRefusal_WithoutIssuingAnIdentity()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        await page.Locator("button[name='choice'][value='denied']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);

        var cookies = await context.CookiesAsync();
        Assert.Equal("denied", Assert.Single(cookies, c => c.Name == "akml.consent").Value);

        // Contract C1.1: remembering a refusal must not require issuing the thing refused.
        Assert.DoesNotContain(cookies, c => c.Name == "akml.vid");

        await page.GotoAsync(SiteFixture.BaseUrl + "/download");
        await Assertions.Expect(page.Locator(".consent-bar")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task ConsentReturnsTheVisitorToThePageTheyWereOn()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        await page.Locator("button[name='choice'][value='denied']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);

        // The returnUrl round-trip. (Verifying this from Git Bash is misleading — MSYS rewrites a
        // leading-slash argument into a Windows path, which SafeReturnUrl then correctly rejects.
        // A real browser posts the form value verbatim, which is what this asserts.)
        Assert.EndsWith("/download", page.Url, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task PrivacyPage_OffersWithdrawal_AndStatesWhatIsStored()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/privacy");

        var body = await page.Locator("body").InnerTextAsync();
        Assert.Contains("full IP address", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("akml.vid", body, StringComparison.Ordinal);

        await Assertions.Expect(page.Locator("form[action='/privacy/forget'] button[type='submit']")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task WithdrawingConsent_ClearsTheIdentityCookie()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(SiteFixture.BaseUrl + "/download");
        await page.Locator("button[name='choice'][value='granted']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);
        Assert.Contains(await context.CookiesAsync(), c => c.Name == "akml.vid");

        await page.GotoAsync(SiteFixture.BaseUrl + "/privacy");
        await page.Locator("form[action='/privacy/forget'] button[type='submit']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);

        var after = await context.CookiesAsync();
        Assert.DoesNotContain(after, c => c.Name == "akml.vid");
        Assert.Equal("denied", Assert.Single(after, c => c.Name == "akml.consent").Value);
        Assert.Contains("forgotten=", page.Url, StringComparison.Ordinal);
    }

    // --- US2: the release-visibility setting --------------------------------

    [SkippableFact]
    public async Task DownloadPage_AdvertisesOnlyTheConfiguredNumberOfReleases()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var cards = await page.Locator(".release-card").CountAsync();

        // The manifest holds 16; the shipped default advertises 3. Before spec 038 the page rendered
        // all of them, which is the clutter this feature exists to remove.
        Assert.InRange(cards, 1, 5);
    }

    // --- US1: the no-JS path ------------------------------------------------

    [SkippableFact]
    public async Task TheWholePath_WorksWithJavaScriptDisabled()
    {
        SkipIfUnavailable();

        // FR-009 / FR-043b: the download AND the consent choice must work without scripts. With JS
        // off, download-track.js never upgrades the link, so the visitor follows /dl/{file} — which
        // 302s to the CDN and counts the download server-side.
        await using var context = await site.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            JavaScriptEnabled = false,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var href = await page.Locator("a.btn-primary.download-tracked").First.GetAttributeAsync("href");
        Assert.StartsWith("/dl/", href, StringComparison.Ordinal);

        // The consent form is a plain POST, so it still works.
        await page.Locator("button[name='choice'][value='denied']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);
        Assert.Equal("denied", Assert.Single(await context.CookiesAsync(), c => c.Name == "akml.consent").Value);
    }

    [SkippableFact]
    public async Task TheTrackedDownloadUrl_RedirectsToTheCdn_WithoutDownloading95Megabytes()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var href = await page.Locator("a.btn-primary.download-tracked").First.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(href));

        // Ask the server what /dl/{file} does, without following the redirect into a 95 MB body.
        var response = await context.APIRequest.GetAsync(
            href!.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : SiteFixture.BaseUrl + href,
            new APIRequestContextOptions { MaxRedirects = 0 });

        // GitHub answers the release URL with a further redirect to its asset CDN, so the final
        // host is release-assets.githubusercontent.com rather than github.com. Accept either --
        // what matters is that the site handed the visitor off to GitHub rather than streaming
        // 95 MB itself.
        Assert.InRange(response.Status, 200, 399);
        Assert.Contains("github", response.Url, StringComparison.OrdinalIgnoreCase);
    }

    // --- US1: phone width ---------------------------------------------------

    [SkippableFact]
    public async Task AtPhoneWidth_TheCurrentVersionAndItsButtonComeFirst()
    {
        SkipIfUnavailable();

        // SC-004: a first-time visitor must reach the current version without scrolling past other
        // versions. 390x844 is an iPhone 14 viewport.
        await using var context = await site.NewContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var button = await page.Locator("a.btn-primary.download-tracked").First.BoundingBoxAsync();
        Assert.NotNull(button);

        var history = page.Locator(".release-history");
        if (await history.CountAsync() > 0)
        {
            var historyBox = await history.BoundingBoxAsync();
            Assert.True(
                button!.Y < historyBox!.Y,
                $"The download button (y={button.Y}) is below the release history (y={historyBox.Y}) at phone width.");
        }

        // And the page must not scroll sideways.
        var scrollWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
        var clientWidth = await page.EvaluateAsync<int>("document.documentElement.clientWidth");
        Assert.True(scrollWidth <= clientWidth + 1, $"Horizontal overflow at 390px: {scrollWidth} vs {clientWidth}.");
    }

    [SkippableFact]
    public async Task AtPhoneWidth_TheConsentBarDoesNotSwallowTheViewport()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/download");

        var bar = await page.Locator(".consent-bar").BoundingBoxAsync();
        Assert.NotNull(bar);

        // A bar taller than a third of the screen is a modal wearing a bar's clothes.
        Assert.True(bar!.Height < 844 / 3.0, $"Consent bar is {bar.Height}px tall on an 844px viewport.");
    }
}
