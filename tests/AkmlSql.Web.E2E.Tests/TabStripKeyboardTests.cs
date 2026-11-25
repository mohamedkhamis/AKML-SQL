using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 041 (T168, research R58, FR-070, FR-071) — quickstart §5 step 1: every tab strip (results, AI panel, style
/// preview) is the same strip — Left/Right activate the neighbour at once, Home/End the ends, the active tab is
/// <c>aria-selected</c>, and the active marker looks the same in all three.
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class TabStripKeyboardTests
{
    private readonly ITestOutputHelper _output;

    public TabStripKeyboardTests(ITestOutputHelper output) => _output = output;

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static Task<string> MarkerAsync(ILocator tab) => tab.EvaluateAsync<string>(
        "t => { const s = getComputedStyle(t); return [s.borderBottomWidth, s.borderBottomStyle, s.borderBottomColor, s.height].join(' | '); }");

    [SkippableFact]
    public async Task Every_strip_moves_with_the_arrow_keys_and_shows_the_same_marker()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.ExecuteAsync("SELECT 1 AS a;");

        // Results.
        await Id(page, "results-tab-results").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(Id(page, "results-tab-messages")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(Id(page, "results-tab-problems")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(Id(page, "results-tab-results")).ToHaveAttributeAsync("aria-selected", "true");
        var resultsMarker = await MarkerAsync(Id(page, "results-tab-results"));

        // AI panel.
        await Id(page, "ai-button").ClickAsync();
        await Id(page, "ai-tab-actions").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(Id(page, "ai-tab-chat")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(Id(page, "ai-tab-actions")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(Id(page, "ai-tab-chat")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(Id(page, "ai-tab-actions")).ToHaveAttributeAsync("aria-selected", "true");
        var aiMarker = await MarkerAsync(Id(page, "ai-tab-actions"));

        // Style preview.
        await page.ClickAsync("nav >> text=Format styles");
        await page.Locator("[data-testid='style-row']").First.ClickAsync();
        await Id(page, "preview-sample").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(Id(page, "preview-mysql")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(Id(page, "preview-sample")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(Id(page, "preview-mysql")).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(Id(page, "preview-sample")).ToHaveAttributeAsync("aria-selected", "true");
        var previewMarker = await MarkerAsync(Id(page, "preview-sample"));

        _output.WriteLine($"results: {resultsMarker}\nai: {aiMarker}\npreview: {previewMarker}");
        Assert.Equal(resultsMarker, aiMarker);
        Assert.Equal(resultsMarker, previewMarker);
    }

    /// <summary>
    /// Spec 047 (WEB-009, contracts/cli-docs-a11y.md §4) — a query tab's close button is beside its tab, not inside it (a tab
    /// that holds a button is interactive content inside interactive content), Tab reaches it, and every way of closing a tab
    /// still works: the button, Delete, a middle click and Ctrl+Alt+W. No engine is needed.
    /// </summary>
    [SkippableFact]
    public async Task A_query_tabs_close_button_is_beside_it_and_every_way_of_closing_works()
    {
        WebAppFixture web;
        try { web = await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
        await using var _ = web;
        using var playwright = await Playwright.CreateAsync();
        IBrowser? browser;
        try { browser = await playwright.Chromium.LaunchForTestsAsync(); }
        catch (PlaywrightException) { browser = null; }
        Skip.If(browser == null, "Playwright browsers are not installed.");
        await using var __ = browser;
        var page = await (await browser!.NewContextAsync()).NewPageAsync();
        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });

        var tabs = Id(page, "doc-tab");
        for (int i = 0; i < 4; i++) await Id(page, "doc-new").ClickAsync();
        await Assertions.Expect(tabs).ToHaveCountAsync(5);

        // Beside, not inside: no close button is a descendant of a tab, and each tab has one.
        Assert.Equal(0, await page.Locator("[data-testid='doc-tab'] [data-testid='doc-tab-close']").CountAsync());
        Assert.Equal(5, await Id(page, "doc-tab-close").CountAsync());
        var label = await tabs.Last.Locator(".akml-tab-label").InnerTextAsync();
        Assert.Equal($"Close {label}", await Id(page, "doc-tab-close").Last.GetAttributeAsync("aria-label"));

        // Tab from the active tab reaches its close button.
        await tabs.Last.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("doc-tab-close", await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.getAttribute('data-testid')"));

        // The close button, Delete, a middle click and Ctrl+Alt+W each close one tab.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tabs).ToHaveCountAsync(4);
        await tabs.First.FocusAsync();
        await page.Keyboard.PressAsync("Delete");
        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        await tabs.Nth(1).ClickAsync(new() { Button = MouseButton.Middle });
        await Assertions.Expect(tabs).ToHaveCountAsync(2);
        await page.Locator(".cm-content").ClickAsync();
        await page.Keyboard.PressAsync("Control+Alt+w");
        await Assertions.Expect(tabs).ToHaveCountAsync(1);
    }
}
