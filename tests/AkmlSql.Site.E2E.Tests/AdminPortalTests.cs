using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// Browser tests for the deployed admin portal. These need the real password, supplied via the
/// <c>AKML_SITE_ADMIN_PASSWORD</c> environment variable; without it the class skips rather than
/// failing, so the suite stays green on a machine that has no credential.
/// <para>
/// Running through a browser matters here: the admin cookie is <c>Secure</c> + <c>HttpOnly</c>
/// with <c>SameSite=Lax</c>, so an http-only or header-forged check would not exercise what a
/// real sign-in does.
/// </para>
/// </summary>
[Collection(SiteCollection.Name)]
public sealed class AdminPortalTests(SiteFixture site)
{
    private void SkipIfUnavailable()
    {
        Skip.If(site.SkipReason is not null, site.SkipReason);
        Skip.If(SiteFixture.AdminPassword is null,
            "Set AKML_SITE_ADMIN_PASSWORD to run the admin portal E2E tests.");
    }

    /// <summary>Signs in and returns the authenticated page.</summary>
    private async Task<IPage> SignInAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin/login");
        await page.FillAsync("#admin-password", SiteFixture.AdminPassword!);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/admin");
        return page;
    }

    [SkippableFact]
    public async Task Admin_RedirectsToLogin_WhenNotSignedIn()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin");

        Assert.EndsWith("/admin/login", page.Url, StringComparison.Ordinal);
        Assert.Equal(1, await page.Locator("input[type='password']").CountAsync());
    }

    [SkippableFact]
    public async Task Login_WithTheRealPassword_ReachesTheDashboard()
    {
        SkipIfUnavailable();
        // SEC-001: proves the deployed PBKDF2 hash verifies through the real sign-in path.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        Assert.Contains("Site metrics", await page.InnerTextAsync("h1"));
        Assert.True(await page.Locator(".admin-stat").CountAsync() >= 7);
    }

    [SkippableFact]
    public async Task Login_WithAWrongPassword_IsRejected()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin/login");
        await page.FillAsync("#admin-password", "definitely-not-the-password");
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/admin/login?error=1");

        Assert.Contains("Invalid password", await page.InnerTextAsync(".admin-error"));
    }

    [SkippableFact]
    public async Task Dashboard_RangeSelector_ChangesTheWindow()
    {
        SkipIfUnavailable();
        // ADM-003: the window was hardcoded to 30 days.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        await page.ClickAsync(".admin-ranges a[href='/admin?days=7']");

        // Auto-retrying assertion rather than a URL wait plus a one-shot read, so the check
        // cannot observe the page mid-update.
        //
        // Casing matters here: .admin-stat-label is text-transform: uppercase, and the two
        // Playwright APIs disagree about it. InnerTextAsync is render-aware and returns
        // "VISITS · 7 DAYS"; Expect(...).ToContainTextAsync compares textContent and sees the
        // source casing. Match the source.
        await Assertions.Expect(page.Locator("section[aria-label='Key metrics']")).ToContainTextAsync("Visits · 7 days");
        await Assertions.Expect(page.Locator(".admin-ranges a[href='/admin?days=7']"))
            .ToHaveAttributeAsync("aria-current", "true");
        // The export follows the selected window.
        await Assertions.Expect(page.Locator(".admin-export"))
            .ToHaveAttributeAsync("href", "/admin/metrics.csv?days=7");
    }

    [SkippableFact]
    public async Task Dashboard_SurvivesAMalformedWindow()
    {
        SkipIfUnavailable();
        // This returned HTTP 500 before the parameter was bound as a string.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        var response = await page.GotoAsync(SiteFixture.BaseUrl + "/admin?days=banana");

        Assert.Equal(200, response!.Status);
        await Assertions.Expect(page.Locator("section[aria-label='Key metrics']")).ToContainTextAsync("Visits · 30 days");
    }

    [SkippableFact]
    public async Task Dashboard_ChartsExposeAnAccessibleDataTable()
    {
        SkipIfUnavailable();
        // A11Y-005: values used to be reachable only through a title tooltip.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        Assert.Equal(2, await page.Locator(".admin-chart[aria-hidden='true']").CountAsync());
        Assert.Equal(2, await page.Locator(".admin-chart-data table").CountAsync());

        // The table is real content, not an empty shell.
        await page.ClickAsync(".admin-chart-data summary");
        Assert.True(await page.Locator(".admin-chart-data tbody tr").CountAsync() > 0);
    }

    [SkippableFact]
    public async Task CsvExport_DownloadsThroughTheBrowser()
    {
        SkipIfUnavailable();
        // ADM-007: metrics were viewable only on screen.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        var download = await page.RunAndWaitForDownloadAsync(async () =>
            await page.ClickAsync(".admin-export"));

        Assert.StartsWith("akml-site-metrics-", download.SuggestedFilename, StringComparison.Ordinal);
        Assert.EndsWith(".csv", download.SuggestedFilename, StringComparison.Ordinal);

        var path = await download.PathAsync();
        var csv = await File.ReadAllTextAsync(path!);
        Assert.StartsWith("section,key,value", csv, StringComparison.Ordinal);
        Assert.Contains("totals,visits_today,", csv, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Dashboard_RecordsA404_AndExcludesItFromVisits()
    {
        SkipIfUnavailable();
        // ADM-008: only 2xx responses were tracked, so broken links were invisible.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        // The panel is ORDER BY COUNT(*) DESC, path LIMIT 10 against a database that persists
        // across runs, so "did my 404 get recorded?" cannot be answered by looking for a
        // one-off path — it competes with every 404 the suite has ever recorded and ties break
        // alphabetically. A fixed path plus enough hits to beat the current leader makes the
        // assertion deterministic instead of alphabetically lucky.
        const string probe = "/docs/e2e-broken-link-probe";
        var hits = Math.Min(await CurrentTopNotFoundCountAsync(page) + 1, 25);

        for (var i = 0; i < hits; i++)
        {
            var response = await page.GotoAsync(SiteFixture.BaseUrl + probe);
            Assert.Equal(404, response!.Status);
        }

        // The write is fire-and-forget through a background consumer, so allow it to land.
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if ((await page.InnerTextAsync("#notfound-heading + *, .admin-tables")).Contains(probe, StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(500);
            await page.ReloadAsync();
        }

        await Assertions.Expect(page.Locator(".admin-tables")).ToContainTextAsync(probe);

        // A 404 is not a page view: the probe must not reach the top-pages table.
        var topPages = await page.Locator("section[aria-labelledby='top-pages-heading']").InnerTextAsync();
        Assert.DoesNotContain(probe, topPages, StringComparison.Ordinal);
    }

    /// <summary>
    /// Highest count currently in the "Broken links" panel (0 when empty), so a test can make its
    /// own probe outrank whatever history the deployed database already holds.
    /// </summary>
    private static async Task<int> CurrentTopNotFoundCountAsync(IPage page)
    {
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin");
        var counts = await page
            .Locator("section[aria-labelledby='notfound-heading'] tbody tr td.admin-num")
            .AllInnerTextsAsync();

        return counts
            .Select(text => int.TryParse(text.Trim(), out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    [SkippableFact]
    public async Task Dashboard_ShowsTheEnrichmentDimensions()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        // Arrive on a campaign-tagged link from an external referrer, then browse — so the
        // request carries everything the enrichment is supposed to capture.
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["Accept-Language"] = "ar-EG,ar;q=0.9,en;q=0.8",
        });
        await page.GotoAsync($"{SiteFixture.BaseUrl}/?utm_source=e2e&utm_medium=test&utm_campaign=enrichment-probe");
        await page.GotoAsync(SiteFixture.BaseUrl + "/features");

        var admin = await SignInAsync(context);

        // Every dimension panel is present, whether or not it has rows yet.
        foreach (var id in (string[])
                 ["countries", "cities", "devices", "os", "languages",
                  "campaigns", "referrer-urls", "entry-pages", "exit-pages", "slow-pages"])
        {
            Assert.Equal(1, await admin.Locator($"#{id}-heading").CountAsync());
        }

        // Engagement tiles are computed from sessions, not page views.
        var engagement = await admin.Locator("section[aria-label='Engagement']").InnerTextAsync();
        Assert.Contains("Sessions", engagement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bounce rate", engagement, StringComparison.OrdinalIgnoreCase);

        // Assert on dimensions with few distinct values, which are therefore always inside the
        // LIMIT 10. A specific campaign name is NOT asserted: every panel is
        // ORDER BY COUNT(*) DESC over a database that persists across runs, so a one-off value
        // competes with all history and its presence would be a matter of luck, not correctness
        // (the same trap the 404 test fell into).
        var tables = await admin.Locator(".admin-tables").InnerTextAsync();
        Assert.Contains("ar-eg", tables, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("desktop", tables, StringComparison.OrdinalIgnoreCase);

        // Campaign capture is proven by the panel having rows at all — it is empty until some
        // visit arrives with UTM parameters, and this test just made one.
        await Assertions.Expect(
            admin.Locator("section[aria-labelledby='campaigns-heading'] tbody tr").First).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Dashboard_StatesWhatIsActuallyStored()
    {
        SkipIfUnavailable();
        // The privacy note is a claim made to visitors; it must match the schema. Storing a
        // truncated prefix and a location means the old "hashes only" wording would be false.
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        var privacy = await page.Locator(".admin-privacy").InnerTextAsync();

        Assert.Contains("never stored", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/24", privacy, StringComparison.Ordinal);
        Assert.Contains("/48", privacy, StringComparison.Ordinal);
        Assert.Contains("No cookies", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task SignOut_ClearsTheSession()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        await page.ClickAsync(".admin-signout button");
        await page.WaitForURLAsync("**/admin/login");

        await page.GotoAsync(SiteFixture.BaseUrl + "/admin");
        Assert.EndsWith("/admin/login", page.Url, StringComparison.Ordinal);
    }
}
