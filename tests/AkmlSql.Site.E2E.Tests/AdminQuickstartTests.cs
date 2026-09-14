using System.Diagnostics;
using System.Globalization;
using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// The six spec-038 quickstart scenarios that were marked <c>[MANUAL]</c> because they needed the
/// real admin password: S3.1, S3.3, S4.1, S4.4, S4.5 and S4.6.
/// <para>
/// They were manual only for want of a credential, not because a browser cannot check them. With
/// <c>AKML_SITE_ADMIN_PASSWORD</c> set they run here against the deployed site, and the two
/// timing-based ones (SC-006, SC-007) are measured rather than judged by eye. Without the password
/// the class skips, so a machine with no credential still gets a green suite.
/// </para>
/// <para>
/// After the initial sign-in, navigation is deliberately by clicking and never by
/// <c>GotoAsync</c>: S4.1's whole claim is that every section is reachable without typing a URL,
/// and a test that navigated directly would assert nothing about that.
/// </para>
/// </summary>
[Collection(SiteCollection.Name)]
public sealed class AdminQuickstartTests(SiteFixture site)
{
    private void SkipIfUnavailable()
    {
        Skip.If(site.SkipReason is not null, site.SkipReason);
        Skip.If(SiteFixture.AdminPassword is null,
            "Set AKML_SITE_ADMIN_PASSWORD to run the admin quickstart E2E tests.");
    }

    private async Task<IPage> SignInAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin/login");
        await page.FillAsync("#admin-password", SiteFixture.AdminPassword!);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/admin");
        return page;
    }

    /// <summary>Clicks a portal section in the nav by its exact title.</summary>
    private static Task ClickSectionAsync(IPage page, string title) =>
        page.ClickAsync($".admin-nav a:text-is('{title}')");

    private static string PathOf(IPage page) => new Uri(page.Url).AbsolutePath.TrimEnd('/');

    // ---------------------------------------------------------------- S4.1

    [SkippableFact]
    public async Task S4_1_EverySectionIsReachableFromTheNav_WithoutTypingAUrl()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        // Route, nav title, and a heading only that section renders. The heading is the part that
        // matters: a nav link landing on the right URL but rendering an error page would still
        // satisfy a URL-only assertion.
        (string Route, string Title, string Heading)[] sections =
        [
            ("/admin", "Overview", "Site metrics"),
            ("/admin/downloads", "Downloads", "Downloads"),
            ("/admin/people", "People", "People"),
            ("/admin/pages", "Pages", "Pages"),
            ("/admin/errors", "Errors", "Client error logs"),
            ("/admin/releases", "Releases", "Releases"),
            ("/admin/settings", "Settings", "Settings"),
        ];

        foreach (var (route, title, heading) in sections)
        {
            await ClickSectionAsync(page, title);

            // Wait for the NEW document, not merely for the click to return. WaitForLoadState is a
            // no-op while the previous document is still current, so the assertion below happily
            // read the old page's heading and reported "Downloads not found in Site metrics" --
            // a failure that looks like a broken nav link and is really a race in the test.
            await page.Locator("h1", new PageLocatorOptions { HasTextString = heading }).WaitForAsync();

            Assert.Equal(route.TrimEnd('/'), PathOf(page));
            Assert.Contains(heading, await page.InnerTextAsync("h1"), StringComparison.OrdinalIgnoreCase);

            // SC-010: and the section you are on says which one it is, so the nav is never
            // ambiguous about where you have arrived.
            Assert.Equal(1, await page.Locator(".admin-nav a[aria-current='page']").CountAsync());
        }
    }

    // ---------------------------------------------------------------- S3.1

    [SkippableFact]
    public async Task S3_1_DownloadsByCountryIsRankedWithCountsAndShares_Within15Seconds()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        // SC-006 measures the owner's question, not the server's response time: starting from the
        // dashboard, how long until "which countries downloaded this" is on screen and readable.
        var stopwatch = Stopwatch.StartNew();
        await ClickSectionAsync(page, "Downloads");
        await page.WaitForSelectorAsync("#country-heading");
        var rows = page.Locator("section[aria-labelledby='country-heading'] tbody tr");
        await rows.First.WaitForAsync();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"SC-006: country grouping took {stopwatch.Elapsed.TotalSeconds:F1}s from the dashboard (budget 15s).");

        var count = await rows.CountAsync();
        Assert.True(count > 0, "No country rows rendered; record at least one download with a resolved country.");

        // Every row carries a country, a count and a share — the three things the ranking claim
        // rests on. The numbers are parsed rather than pattern-matched, so a cell rendering a bare
        // "%" or an empty count fails instead of passing on the presence of a <td>.
        var previous = long.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var cells = rows.Nth(i).Locator("td");
            Assert.Equal(3, await cells.CountAsync());

            Assert.False(string.IsNullOrWhiteSpace(await cells.Nth(0).InnerTextAsync()));

            var downloads = long.Parse(
                (await cells.Nth(1).InnerTextAsync()).Replace(",", string.Empty, StringComparison.Ordinal),
                CultureInfo.InvariantCulture);

            var share = double.Parse(
                (await cells.Nth(2).InnerTextAsync()).Trim().TrimEnd('%'),
                CultureInfo.InvariantCulture);
            Assert.InRange(share, 0d, 100d);

            // Ranked, not merely listed.
            Assert.True(downloads <= previous, "Country rows are not in descending download order.");
            previous = downloads;
        }
    }

    // ---------------------------------------------------------------- S3.3

    [SkippableFact]
    public async Task S3_3_OneIndividualsFullHistoryIsReachable_Within30Seconds()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        var stopwatch = Stopwatch.StartNew();
        await ClickSectionAsync(page, "People");
        await page.WaitForSelectorAsync("#people-heading");

        var individuals = page.Locator("section[aria-labelledby='people-heading'] tbody tr td.admin-key a");
        Skip.If(await individuals.CountAsync() == 0,
            "No identified individuals recorded on this deployment; nothing to open.");

        await individuals.First.ClickAsync();
        await page.WaitForSelectorAsync("#who-heading");
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"SC-007: an individual's history took {stopwatch.Elapsed.TotalSeconds:F1}s to reach (budget 30s).");

        // The scenario names six facts. Asserting the labels rather than the values keeps the test
        // honest on a deployment where a value is legitimately absent — an address erased at the
        // retention boundary still has to be shown AS erased, not silently dropped from the list.
        var who = await page.InnerTextAsync("section[aria-labelledby='who-heading']");
        foreach (var label in (string[])["First seen", "Last seen", "Country", "IP address", "Network", "Device"])
        {
            Assert.Contains(label, who, StringComparison.Ordinal);
        }

        // And the interleaved stream, which is what makes this a history rather than a card.
        Assert.Equal(1, await page.Locator("section[aria-labelledby='activity-heading']").CountAsync());
    }

    // ---------------------------------------------------------------- S4.4

    [SkippableFact]
    public async Task S4_4_ReleasesFlagsBothAdvertisedButMissing_AndPresentButUnadvertised()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        await ClickSectionAsync(page, "Releases");
        await page.WaitForSelectorAsync("#advertised-heading");

        // A6.2: every manifest row states whether the public page offers it, and why not when it
        // does not. Silence on a row is the failure mode this guards — an owner cannot tell a
        // deliberately hidden release from a broken one by looking at a blank cell.
        var offered = page.Locator("section[aria-labelledby='advertised-heading'] tbody tr td:last-child");
        var rowCount = await offered.CountAsync();
        Assert.True(rowCount > 0, "No releases in the manifest.");

        for (var i = 0; i < rowCount; i++)
        {
            var text = (await offered.Nth(i).InnerTextAsync()).Trim();
            Assert.True(
                text.StartsWith("Yes", StringComparison.Ordinal) || text.StartsWith("No", StringComparison.Ordinal),
                $"Release row {i} says '{text}', which does not state whether it is offered or why not.");
        }

        // A6.3: the other direction — files on disk that no release references. The panel must be
        // present and must say something either way; "nothing to clean up" is a real answer, an
        // absent panel is not.
        var orphans = page.Locator("section[aria-labelledby='orphans-heading']");
        Assert.Equal(1, await orphans.CountAsync());
        Assert.False(string.IsNullOrWhiteSpace(await orphans.InnerTextAsync()));
    }

    // ---------------------------------------------------------------- S4.5

    [SkippableFact]
    public async Task S4_5_SavingASettingConfirmsWhatChangedAndWhenItTakesEffect()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync();
        var page = await SignInAsync(context);

        await ClickSectionAsync(page, "Settings");
        await page.WaitForSelectorAsync("#visibility-heading");

        // This writes to the DEPLOYED site's settings, so the original value is captured first and
        // restored in a finally. A no-op save would avoid the write, but it would also prove
        // nothing: FR-035 is about confirming WHAT changed, which only a real change demonstrates.
        // If this test dies mid-flight the site is left advertising a different number of releases
        // — visible and harmless, and the next run puts it back.
        var original = await page.InputValueAsync("#visibility-count");
        var changed = (int.Parse(original, CultureInfo.InvariantCulture) == 4 ? 3 : 4)
            .ToString(CultureInfo.InvariantCulture);

        try
        {
            await SaveVisibilityCountAsync(page, changed);

            var notice = await page.InnerTextAsync(".notice[role='status']");
            Assert.Contains("Settings saved", notice, StringComparison.Ordinal);

            // What changed: the confirmation names the resulting state, not a generic "saved".
            Assert.Contains($"Latest {changed} releases", notice, StringComparison.Ordinal);

            // When it takes effect: the reader should not have to guess whether a restart is needed.
            Assert.Contains("live on the next public request", notice, StringComparison.Ordinal);
        }
        finally
        {
            await SaveVisibilityCountAsync(page, original);
        }

        // Restored — and the confirmation tracks the restore rather than echoing the previous save.
        Assert.Contains(
            $"Latest {original} releases",
            await page.InnerTextAsync(".notice[role='status']"),
            StringComparison.Ordinal);
        Assert.Equal(original, await page.InputValueAsync("#visibility-count"));
    }

    private static async Task SaveVisibilityCountAsync(IPage page, string count)
    {
        await page.CheckAsync("input[name='visibility'][value='LatestN']");
        await page.FillAsync("#visibility-count", count);

        // Scope the submit to the settings form. Every admin page also carries the layout's "Sign
        // out" form, and its button comes FIRST in the DOM -- a bare button[type='submit'] clicks
        // that one, signs the session out, and every later assertion then fails against the login
        // page for reasons that look nothing like the real cause.
        await page.ClickAsync("form[action='/admin/settings'] button[type='submit']");
        await page.WaitForSelectorAsync(".notice[role='status']");
    }

    // ---------------------------------------------------------------- S4.6

    [SkippableTheory]
    [InlineData("Overview", "/admin")]
    [InlineData("Downloads", "/admin/downloads")]
    public async Task S4_6_AtPhoneWidth_ThePageDoesNotScrollSideways_ButWideTablesDo(string section, string route)
    {
        SkipIfUnavailable();

        // 400px is the width FR-036 names.
        await using var context = await site.NewContextAsync(400, 800);
        var page = await SignInAsync(context);

        if (route != "/admin")
        {
            await ClickSectionAsync(page, section);
            await page.WaitForSelectorAsync("#country-heading");
        }

        Assert.Equal(route.TrimEnd('/'), PathOf(page));

        // No horizontal page scroll. Read from the document rather than a screenshot, because a
        // few pixels of overflow is invisible in an image and is exactly the bug this guards.
        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"{section} overflows the 400px viewport by {overflow}px.");

        // The escape valve: a table too wide to fit scrolls inside its own container instead of
        // pushing the page. Any .table-scroll whose content exceeds it must really be scrollable.
        var containers = page.Locator(".table-scroll");
        var containerCount = await containers.CountAsync();
        for (var i = 0; i < containerCount; i++)
        {
            var contained = await containers.Nth(i).EvaluateAsync<bool>(
                "el => el.scrollWidth <= el.clientWidth || getComputedStyle(el).overflowX !== 'visible'");
            Assert.True(contained, $"A wide table on {section} is not scrollable inside its own container.");
        }
    }
}
