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
}
