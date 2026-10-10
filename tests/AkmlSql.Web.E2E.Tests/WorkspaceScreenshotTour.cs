using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

// Deliberate local captures. Exclude this tour, like SiteScreenshotTour, from SC-008.
public sealed class WorkspaceScreenshotTour(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Capture_workspace_in_three_themes_at_both_supported_sizes()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        var page = s.Page;
        var folder = Path.Combine(SandboxEngine.RepoRoot(), "specs", "041-web-ssms-ux-parity", "baseline", "after");
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark", "HighContrast" })
        foreach (var size in new[] { (1440, 900), (1100, 700) })
        {
            await page.SetViewportSizeAsync(size.Item1, size.Item2);
            await ShellPolishTests.NavigateAsync(page, "settings/general");
            await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
            await ShellPolishTests.NavigateAsync(page, "editor");
            await s.ExecuteAsync("SELECT name, database_id, create_date FROM sys.databases; PRINT 'Workspace screenshot verification';");
            async Task Capture(string name)
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"{theme.ToLowerInvariant()}-{size.Item1}x{size.Item2}-{name}.png"), Animations = ScreenshotAnimations.Disabled });
            }
            await Capture("editor-results");
            await page.GetByTestId("results-tab-messages").ClickAsync();
            await Capture("messages");
            await s.SetEditorTextAsync("CREATE PROCEDURE dbo.sample AS\nBEGIN\n    SELECT * FROM sys.objects;\nEND;");
            await page.GetByTestId("analyse-button").ClickAsync();
            await Assertions.Expect(page.GetByTestId("results-tab-problems")).ToHaveAttributeAsync("aria-selected", "true");
            await Capture("problems");
            foreach (var section in new[] { "general", "editor", "format", "queries", "code-analysis", "ai-assistance", "connections", "schema-cache", "diagnostics" })
            {
                await ShellPolishTests.NavigateAsync(page, "settings/" + section);
                await Capture("settings-" + section);
            }
            foreach (var route in new[] { "history", "snippets", "styles" })
            {
                await ShellPolishTests.NavigateAsync(page, route);
                await Capture(route);
            }
        }
        output.WriteLine($"Screenshots: {folder}");
    }
}
