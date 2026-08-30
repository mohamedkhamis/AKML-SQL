using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Site.E2E.Tests;

/// <summary>
/// Captures full-page screenshots of the deployed site into the folder named by
/// <c>AKML_SITE_SCREENSHOT_DIR</c>. Skipped unless that variable is set, so it never runs as part
/// of a normal suite — it is a review aid, not an assertion.
/// <para>
/// It lives here rather than in a script because the admin pages need a real sign-in: the admin
/// cookie is <c>Secure</c>, so a plain-HTTP capture tool cannot reach the dashboard at all.
/// </para>
/// </summary>
[Collection(SiteCollection.Name)]
public sealed class ScreenshotCapture(SiteFixture site)
{
    [SkippableFact]
    public async Task CaptureDeployedPages()
    {
        Skip.If(site.SkipReason is not null, site.SkipReason);
        var outputDir = Environment.GetEnvironmentVariable("AKML_SITE_SCREENSHOT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(outputDir), "Set AKML_SITE_SCREENSHOT_DIR to capture screenshots.");
        Directory.CreateDirectory(outputDir!);

        await using var context = await site.NewContextAsync(1440, 1000);
        var page = await context.NewPageAsync();

        foreach (var (name, path) in ((string Name, string Path)[])
                 [("home", "/"), ("features", "/features"), ("download", "/download"),
                  ("docs", "/docs/topics/connecting")])
        {
            await page.GotoAsync(SiteFixture.BaseUrl + path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(outputDir!, $"{name}.png"),
                FullPage = false,
            });
        }

        // Mobile, at a real viewport this time.
        await using var mobile = await site.NewContextAsync(390, 844);
        var mobilePage = await mobile.NewPageAsync();
        await mobilePage.GotoAsync(SiteFixture.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await mobilePage.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(outputDir!, "home-mobile.png"),
        });

        // The admin dashboard needs a signed-in session.
        if (SiteFixture.AdminPassword is not null)
        {
            await page.GotoAsync(SiteFixture.BaseUrl + "/admin/login");
            await page.FillAsync("#admin-password", SiteFixture.AdminPassword);
            await page.ClickAsync("button[type='submit']");
            await page.WaitForURLAsync("**/admin");
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(outputDir!, "admin.png"),
                FullPage = true,
            });
        }
    }
}
