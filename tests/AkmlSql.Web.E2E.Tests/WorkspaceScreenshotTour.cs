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

    /// <summary>
    /// Spec 043 (T085, US4, FR-031 to FR-033): the dark walk — every page plus the dialogs, menus and popups the 041 tour
    /// never opened, in the dark theme at 1280x720 and 1920x1080, into <c>specs/043-web-query-tabs-polish/baseline/dark/</c>
    /// for review. A surface that cannot be reached in this environment is logged and skipped, not failed: this is a
    /// capture tour, and the review of the images is the check.
    /// </summary>
    [SkippableFact]
    public async Task Capture_the_dark_walk_of_pages_dialogs_menus_and_popups()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { ViewportSize = new() { Width = 1280, Height = 720 }, ReducedMotion = ReducedMotion.Reduce });
        var page = s.Page;
        var folder = Path.Combine(SandboxEngine.RepoRoot(), "specs", "043-web-query-tabs-polish", "baseline", "dark");
        Directory.CreateDirectory(folder);
        await ShellPolishTests.NavigateAsync(page, "settings/general");
        await page.Locator("#setting-theme-control").SelectOptionAsync("Dark");

        foreach (var (w, h) in new[] { (1280, 720), (1920, 1080) })
        {
            await page.SetViewportSizeAsync(w, h);
            async Task Capture(string name) =>
                await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"dark-{w}x{h}-{name}.png"), Animations = ScreenshotAnimations.Disabled });
            async Task Try(string name, Func<Task> open)
            {
                try { await open(); await Capture(name); }
                catch (Exception ex) { output.WriteLine($"{w}x{h} {name}: not captured ({ex.GetType().Name}: {ex.Message.Split('\n')[0]})"); }
                finally { await page.Keyboard.PressAsync("Escape"); await page.Keyboard.PressAsync("Escape"); }
            }

            await ShellPolishTests.NavigateAsync(page, "editor");
            await s.ExecuteAsync("SELECT name, database_id, create_date FROM sys.databases; PRINT 'Dark walk';");
            await Capture("editor-results");
            await Try("grid-context-menu", () => page.GetByTestId("results-cell-0-0").ClickAsync(new() { Button = MouseButton.Right }));
            await Try("connect-sqlserver", async () =>
            {
                await page.ClickAsync("[data-testid='status-connection']");
                await page.GetByTestId("conn-kind-sqlserver").ClickAsync();
            });
            await Try("connect-postgresql", async () =>
            {
                await page.ClickAsync("[data-testid='status-connection']");
                await page.GetByTestId("conn-kind-postgresql").ClickAsync(new() { Timeout = 3_000 });
            });
            await Try("command-palette", async () =>
            {
                await page.Keyboard.PressAsync("Control+Shift+P");
                await page.GetByTestId("command-palette-input").FillAsync("format");
            });
            await Try("completion", async () =>
            {
                // Keywords complete without a loaded schema; retried while the session settles (as SqlServerAliasWebTests).
                var popup = page.Locator(".cm-tooltip-autocomplete");
                for (int attempt = 0; attempt < 10 && await popup.CountAsync() == 0; attempt++)
                {
                    await s.SetEditorTextAsync(string.Empty);
                    await page.Keyboard.TypeAsync("SEL");
                    await page.Keyboard.PressAsync("Control+Space");
                    await page.WaitForTimeoutAsync(1_500);
                }
                await Assertions.Expect(popup).ToBeVisibleAsync();
            });
            await Try("find-panel", async () =>
            {
                await page.Locator("[data-testid='sql-editor'] .cm-content").ClickAsync();
                await page.Keyboard.PressAsync("Control+F");
                await page.Locator(".cm-search input[name='search']").FillAsync("sys");
            });
            await Try("schema-context-menu", async () =>
            {
                // tempdb has no user object until one is made; refresh, open folders until an object row shows.
                await s.ExecuteAsync("IF OBJECT_ID('dbo.dark_walk_043') IS NULL CREATE TABLE dbo.dark_walk_043 (n int); SELECT 1;");
                await page.GetByTestId("schema-refresh").ClickAsync();
                var obj = page.GetByTestId("schema-tree-object").First;
                for (int attempt = 0; attempt < 10 && await obj.CountAsync() == 0; attempt++)
                {
                    var closed = page.Locator("[data-testid='schema-tree'] [aria-expanded='false']").First;
                    if (await closed.CountAsync() > 0) await closed.ClickAsync();
                    await page.WaitForTimeoutAsync(1_000);
                }
                await obj.ClickAsync(new() { Button = MouseButton.Right });
                await Assertions.Expect(page.GetByTestId("schema-context-menu")).ToBeVisibleAsync();
            });
            await Try("notification", async () =>
            {
                await ShellPolishTests.NavigateAsync(page, "settings/editor");
                await page.Locator("#setting-editor-word-wrap-control").ClickAsync();
                await page.Locator("#setting-editor-word-wrap-control").ClickAsync();
            });
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
            await Try("snippet-editor", async () =>
            {
                await ShellPolishTests.NavigateAsync(page, "snippets");
                await page.GetByTestId("snippet-new").ClickAsync();
            });
        }
        output.WriteLine($"Screenshots: {folder}");
    }
}
