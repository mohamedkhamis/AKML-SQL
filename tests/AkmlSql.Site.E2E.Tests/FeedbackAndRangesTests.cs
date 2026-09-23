using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// Against the deployed site: a message sent from the public feedback form reaches the admin
/// inbox and can be handled and deleted; and the Today / Yesterday ranges are selectable and say
/// which day they mean.
/// <para>
/// The feedback test writes one real message to the live database and deletes it again at the
/// end. Its text carries a unique marker so it can never be confused with a real visitor's
/// message, and so a run that fails half-way leaves something obviously identifiable behind.
/// </para>
/// <para>
/// Every check after a link click is an auto-retrying <c>Expect</c>: Blazor's enhanced navigation
/// updates the URL before it patches the page, so a URL wait followed by a one-shot read can see
/// the previous page.
/// </para>
/// </summary>
[Collection(SiteCollection.Name)]
public sealed class FeedbackAndRangesTests(SiteFixture site)
{
    private void SkipIfUnavailable()
    {
        Skip.If(site.SkipReason is not null, site.SkipReason);
        Skip.If(SiteFixture.AdminPassword is null,
            "Set AKML_SITE_ADMIN_PASSWORD to run the admin E2E tests.");
    }

    private static async Task SignInAsync(IPage page)
    {
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin/login");
        await page.FillAsync("#admin-password", SiteFixture.AdminPassword!);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/admin");
    }

    private static Task<int> OverflowAsync(IPage page) => page.EvaluateAsync<int>(
        "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

    [SkippableFact]
    public async Task AMessageSentFromTheSite_ArrivesInTheInbox_AndCanBeHandledAndDeleted()
    {
        SkipIfUnavailable();
        var marker = $"E2E feedback round trip {Guid.NewGuid():N}";

        // --- the visitor: from the download page's help card, at phone width ---------------------
        await using (var visitor = await site.NewContextAsync(390, 844))
        {
            var page = await visitor.NewPageAsync();
            await page.GotoAsync(SiteFixture.BaseUrl + "/download");
            await page.ClickAsync(".download-help a");

            // The link preselected the category.
            await Assertions.Expect(page.Locator("input[name='Form.Category'][value='download']")).ToBeCheckedAsync();
            var overflow = await OverflowAsync(page);
            Assert.True(overflow <= 0, $"The feedback form overflows a 390px viewport by {overflow}px.");

            // A mistake first: the page comes back with the problem named and the text kept.
            await page.FillAsync("#feedback-message", marker);
            await page.FillAsync("#feedback-email", "not-an-address");
            await page.ClickAsync("button[type='submit']");
            await Assertions.Expect(page.Locator(".feedback-errors[role='alert']")).ToContainTextAsync("email address");
            await Assertions.Expect(page.Locator("#feedback-message")).ToHaveValueAsync(marker);

            await page.FillAsync("#feedback-email", "e2e@example.com");
            await page.ClickAsync("button[type='submit']");
            await Assertions.Expect(page.Locator(".feedback-sent")).ToContainTextAsync("your message was sent");
            Assert.EndsWith("/feedback?sent=1", page.Url, StringComparison.Ordinal);
        }

        // --- the owner ----------------------------------------------------------------------------
        await using var owner = await site.NewContextAsync();
        var admin = await owner.NewPageAsync();
        await SignInAsync(admin);

        // Visible from every portal page, not only from the inbox.
        await Assertions.Expect(admin.Locator(".admin-nav a[href^='/admin/feedback'] .admin-badge")).ToHaveCountAsync(1);

        await admin.ClickAsync(".admin-nav a[href^='/admin/feedback']");
        var item = admin.Locator(".feedback-item").Filter(new LocatorFilterOptions { HasTextString = marker });
        await Assertions.Expect(item).ToHaveCountAsync(1);
        await Assertions.Expect(item).ToContainTextAsync("Download or installation problem");
        await Assertions.Expect(item).ToContainTextAsync("/download");
        await Assertions.Expect(item.Locator("a[href^='mailto:e2e@example.com']")).ToHaveCountAsync(1);

        await item.Locator("button:has-text('Mark handled')").ClickAsync();
        await Assertions.Expect(admin.Locator(".notice[role='status']")).ToContainTextAsync("Marked handled.");
        await Assertions.Expect(item).ToHaveCountAsync(0);

        await admin.ClickAsync(".admin-ranges a[href='/admin/feedback?show=handled']");
        var handled = admin.Locator(".feedback-item.is-handled").Filter(new LocatorFilterOptions { HasTextString = marker });
        await Assertions.Expect(handled).ToHaveCountAsync(1);

        await handled.Locator("button:has-text('Delete')").ClickAsync();
        await Assertions.Expect(admin.Locator(".notice[role='status']")).ToContainTextAsync("Deleted");
        await Assertions.Expect(admin.Locator(".feedback-item").Filter(new LocatorFilterOptions { HasTextString = marker }))
            .ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Insights_AtPhoneWidth_TheConversionTablesFitWithoutScrolling()
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await SignInAsync(page);
        await page.GotoAsync(SiteFixture.BaseUrl + "/admin/insights?days=30");
        await Assertions.Expect(page.Locator("#by-country-heading")).ToBeVisibleAsync();

        // The rate is the rightmost column; a table that scrolls sideways hides exactly it.
        var tables = page.Locator(".insights-table-block .table-scroll");
        for (var i = 0; i < await tables.CountAsync(); i++)
        {
            var excess = await tables.Nth(i).EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth");
            var columns = excess <= 0 ? "" : await tables.Nth(i).EvaluateAsync<string>(
                "el => el.clientWidth + 'px available; columns ' + [...el.querySelectorAll('th')].map(th => th.textContent.trim() + '=' + Math.round(th.getBoundingClientRect().width)).join(', ')");
            Assert.True(excess <= 0, $"Conversion table {i + 1} is {excess}px wider than a 390px phone ({columns}).");
        }

        var overflow = await OverflowAsync(page);
        Assert.True(overflow <= 0, $"Insights overflows a 390px viewport by {overflow}px.");
    }

    [SkippableTheory]
    [InlineData("today", "Today")]
    [InlineData("yesterday", "Yesterday")]
    public async Task ASingleDayRange_IsSelectable_NamesTheDay_AndShowsHours(string key, string label)
    {
        SkipIfUnavailable();
        await using var context = await site.NewContextAsync(400, 800);
        var page = await context.NewPageAsync();
        await SignInAsync(page);

        await page.ClickAsync($".admin-ranges a[href='/admin?days={key}']");

        await Assertions.Expect(page.Locator($".admin-ranges a[href='/admin?days={key}']"))
            .ToHaveAttributeAsync("aria-current", "true");
        // "Today (Tue 22 Sep) · Cairo time (UTC+03:00)": the day, and whose clock.
        await Assertions.Expect(page.Locator(".admin-range-active")).ToHaveTextAsync(
            new Regex($"^{label} \\(\\w{{3}} \\d{{1,2}} \\w{{3}}\\) · .+ time \\(UTC[+-]\\d\\d:\\d\\d\\)$"));

        // Four headline figures, each compared with the matching earlier period.
        await Assertions.Expect(page.Locator(".admin-headline .admin-delta")).ToHaveCountAsync(4);
        // One day is shown hour by hour rather than as a single daily bar.
        await Assertions.Expect(page.Locator(".admin-chart-hourly")).ToHaveCountAsync(2);

        var overflow = await OverflowAsync(page);
        Assert.True(overflow <= 0, $"The overview for {label} overflows a 400px viewport by {overflow}px.");

        // The range carries into the other sections.
        await Assertions.Expect(page.Locator(".admin-nav a[href^='/admin/insights']"))
            .ToHaveAttributeAsync("href", $"/admin/insights?days={key}");
    }
}
