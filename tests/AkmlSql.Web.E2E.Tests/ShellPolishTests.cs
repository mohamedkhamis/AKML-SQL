using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

[Trait("Category", "BridgeE2E")]
public sealed class ShellPolishTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public async Task Toolbar_database_status_and_analysis_follow_the_live_session(string theme)
    {
        var requests = new List<string>();
        var dialogs = new List<string>();
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: new() { ReducedMotion = ReducedMotion.Reduce }, configurePage: p =>
        {
            p.Request += (_, request) => requests.Add(request.Url);
            InAppDialogs.FailOnBrowserDialogs(p, dialogs);
        });
        var page = s.Page;
        await NavigateAsync(page, "settings/general");
        await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
        await NavigateAsync(page, "editor");

        foreach (var id in new[] { "execute-button", "parse-button", "format-button", "analyse-button", "doc-new", "doc-save", "doc-saveas", "view-menu" })
        {
            var button = page.GetByTestId(id);
            Assert.False(string.IsNullOrWhiteSpace(await button.GetAttributeAsync("title")));
            Assert.InRange(await button.Locator("svg").CountAsync(), 1, 2);
        }
        Assert.True(await page.Locator(".akml-toolbar-sep").CountAsync() >= 4);

        await s.ExecuteAsync("CREATE TABLE #shell_check (n int); INSERT #shell_check VALUES (41); SELECT n FROM #shell_check;");
        await page.GetByTestId("db-selector").ClickAsync();
        await page.GetByTestId("db-selector-list").GetByText("master", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/master");
        await s.ExecuteAsync("SELECT n FROM #shell_check;");
        await Assertions.Expect(page.GetByTestId("results-cell-0-0")).ToContainTextAsync("41");
        await s.ExecuteAsync("USE tempdb; SELECT 42 AS n;");
        await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync("tempdb");
        await s.ExecuteAsync("USE master; SELECT 43 AS n;");
        await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync("master");
        await Assertions.Expect(page.GetByTestId("schema-header")).ToContainTextAsync("master");
        await Assertions.Expect(page.GetByTestId("status-outcome")).ToContainTextAsync("successfully");
        await Assertions.Expect(page.GetByTestId("status-rows")).ToContainTextAsync("1 row");
        await Assertions.Expect(page.GetByTestId("status-caret")).ToContainTextAsync("Ln");
        await Assertions.Expect(page.GetByTestId("status-version-web")).ToContainTextAsync("Web");
        await Assertions.Expect(page.GetByTestId("status-version-engine")).ToContainTextAsync("Engine");

        await s.SetEditorTextAsync("CREATE PROCEDURE dbo.shell_check AS\nBEGIN\n-- 3\n-- 4\n-- 5\n-- 6\nSELECT * FROM sys.objects;\nEND;");
        await page.GetByTestId("analyse-button").ClickAsync();
        await Assertions.Expect(page.Locator(".cm-line").Nth(6).Locator("[class*='cm-lintRange']").First).ToBeVisibleAsync();
        await page.Locator(".cm-lint-marker").First.FocusAsync();
        await Assertions.Expect(page.Locator(".cm-tooltip-lint")).ToBeVisibleAsync();
        await AssertSurfaceAsync(page.Locator(".cm-tooltip-lint"), "--akml-surface-panel");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".cm-tooltip-lint")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("error-banner")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("engine-issue")).ToHaveCountAsync(0);
        Assert.Empty(dialogs);
        AssertLocalRequests(requests, s);
    }

    [SkippableTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public async Task Destructive_actions_use_in_app_confirmation_and_headers_share_styles(string theme)
    {
        var requests = new List<string>();
        var dialogs = new List<string>();
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { ReducedMotion = ReducedMotion.Reduce }, configurePage: p =>
            {
                p.Request += (_, request) => requests.Add(request.Url);
                InAppDialogs.FailOnBrowserDialogs(p, dialogs);
            });
        var page = s.Page;
        await NavigateAsync(page, "settings/general");
        await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
        string? headerStyle = null;
        foreach (var route in new[] { "history", "snippets", "styles", "settings/schema-cache", "settings/diagnostics" })
        {
            await NavigateAsync(page, route);
            var header = page.GetByTestId("page-header").First;
            await Assertions.Expect(header).ToBeVisibleAsync();
            var style = await header.EvaluateAsync<string>("el => { const s = getComputedStyle(el); return [s.display, s.alignItems, s.gap].join('|'); }");
            headerStyle ??= style;
            Assert.Equal(headerStyle, style);
        }
        await page.Keyboard.PressAsync("Control+p");
        await page.GetByTestId("command-palette-input").FillAsync("Clear schema cache");
        await page.GetByTestId("command-palette-row").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await NavigateAsync(page, "settings/connections");
        await page.Locator("[data-testid^='engine-remove-']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-pill")).ToContainTextAsync("Live");
        await NavigateAsync(page, "settings/diagnostics");
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear…", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-title")).ToHaveTextAsync("Clear diagnostics");
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await NavigateAsync(page, "settings/editor");
        await page.Locator("#setting-editor-reset-session-control").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await NavigateAsync(page, "snippets");
        await page.GetByTestId("snippet-new").ClickAsync();
        await page.Locator("#akml-snippet-shortcode").FillAsync("shell-check");
        await page.Locator("#akml-snippet-title").FillAsync("Shell confirmation check");
        await page.Locator("#akml-snippet-body").FillAsync("SELECT 1;");
        await page.GetByTestId("snippet-save").ClickAsync();
        await page.GetByTestId("snippet-delete").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await NavigateAsync(page, "settings/ai-assistance");
        await page.GetByLabel("Provider").SelectOptionAsync("ollama");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Model", Exact = true }).FillAsync("shell-test");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint", Exact = true }).FillAsync("http://localhost:11434");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-title")).ToHaveTextAsync("Remove provider");
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await page.GetByTestId("status-connection").ClickAsync();
        await page.GetByTestId("conn-name-input").FillAsync("Shell test connection");
        await page.GetByTestId("conn-save-btn").ClickAsync();
        await page.GetByTestId("conn-delete-btn").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        Assert.Empty(dialogs);
        AssertLocalRequests(requests, s);
    }

    // CodeMirror's base theme paints its tooltips and panels light (#f5f5f5, white fields) whatever the
    // AKML theme; the schema filter once carried the label wrapper class and so kept the browser's white.
    [SkippableTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public async Task Editor_popups_find_panel_and_schema_filter_use_the_theme_surfaces(string theme)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, contextOptions: new() { ReducedMotion = ReducedMotion.Reduce });
        var page = s.Page;
        await NavigateAsync(page, "settings/general");
        await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
        await NavigateAsync(page, "editor");

        var filter = page.GetByTestId("schema-filter");
        await Assertions.Expect(filter).ToBeVisibleAsync();
        await AssertSurfaceAsync(filter, "--akml-surface-input");

        await s.SetEditorTextAsync("SELECT 1\nWHERE 1 = 1;");
        var where = page.Locator(".cm-line").Nth(1);
        var box = (await where.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 12, box.Y + box.Height / 2);
        var hover = page.Locator(".cm-tooltip-hover");
        await Assertions.Expect(hover).ToBeVisibleAsync();
        await AssertSurfaceAsync(hover, "--akml-surface-panel");
        await page.Mouse.MoveAsync(0, 0);

        await page.Locator(".cm-content").ClickAsync();
        await page.Keyboard.PressAsync("Control+f");
        var panel = page.Locator(".cm-panels");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await AssertSurfaceAsync(panel, "--akml-surface-panel");
        await AssertSurfaceAsync(panel.Locator(".cm-textfield").First, "--akml-surface-input");
        var button = panel.Locator(".cm-button").First;
        await AssertSurfaceAsync(button, "--akml-surface-panel");
        Assert.Equal("none", await button.EvaluateAsync<string>("el => getComputedStyle(el).backgroundImage"));
    }

    /// <summary>The element's background is the theme token's colour (resolved through a probe, so system colours compare too).</summary>
    private static async Task AssertSurfaceAsync(ILocator element, string token)
    {
        var colours = await element.EvaluateAsync<string[]>(@"(el, token) => {
            const probe = document.createElement('div');
            probe.style.backgroundColor = `var(${token})`;
            document.body.appendChild(probe);
            const want = getComputedStyle(probe).backgroundColor;
            probe.remove();
            return [want, getComputedStyle(el).backgroundColor];
        }", token);
        Assert.Equal(colours[0], colours[1]);
    }

    [SkippableFact]
    public async Task Startup_screen_is_styled_before_Blazor_starts()
    {
        var requests = new List<string>();
        var dialogs = new List<string>();
        void Configure(IPage p)
        {
            p.Request += (_, request) => requests.Add(request.Url);
            InAppDialogs.FailOnBrowserDialogs(p, dialogs);
        }
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, configurePage: Configure);
        var page = await s.Context.NewPageAsync();
        Configure(page);
        await page.RouteAsync("**/blazor.webassembly*.js", route => route.AbortAsync());
        await page.GotoAsync(s.Web.Url);
        var boot = page.Locator(".akml-boot");
        await Assertions.Expect(boot).ToBeVisibleAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await boot.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
        Assert.Equal(0, await boot.Locator("[style]").CountAsync());
        Assert.Empty(dialogs);
        AssertLocalRequests(requests, s);
        await page.CloseAsync();
    }

    private static void AssertLocalRequests(IEnumerable<string> requests, SqlSession s) =>
        Assert.All(requests, url => Assert.True(new Uri(url).Authority == new Uri(s.Web.Url).Authority
            || new Uri(url).Authority == $"127.0.0.1:{s.Engine.Port}", url));

    internal static async Task NavigateAsync(IPage page, string route)
    {
        var parts = route.Split('/');
        await page.GetByTestId("nav-" + parts[0]).ClickAsync();
        if (parts.Length > 1) await page.GetByTestId("settings-nav-" + parts[1]).ClickAsync();
    }
}
