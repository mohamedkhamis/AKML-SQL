using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047 (WEB-012, contracts/workspace-ai.md §3, quickstart V16) — the Refactor menu and its preview work from the
/// keyboard: the menu opens with focus inside it, Escape closes it and focus is back on the Refactor button, an item is
/// reached with the keyboard and chosen with Enter, the preview opens with focus on Apply, and Escape closes that too, back
/// on the Refactor button. Before, the items could not be reached by keyboard and Escape did nothing.
///
/// <para><b>Build the web app in DEBUG first</b>: <see cref="WebAppFixture"/> serves the Debug bundle. No engine is needed.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class RefactorMenuKeyboardTests(ITestOutputHelper output)
{
    private static async Task<IBrowser?> TryLaunchAsync(IPlaywright pw)
    {
        try { return await pw.Chromium.LaunchForTestsAsync(); }
        catch (PlaywrightException) { return null; }
    }

    private static async Task<WebAppFixture> StartWebOrSkipAsync()
    {
        try { return await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
    }

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static Task<string?> FocusedTestIdAsync(IPage page) =>
        page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.getAttribute('data-testid')");

    private static Task<string> FocusedTextAsync(IPage page) =>
        page.EvaluateAsync<string>("() => (document.activeElement && document.activeElement.textContent || '').trim()");

    [SkippableFact]
    public async Task The_menu_and_the_preview_open_close_and_give_focus_back_from_the_keyboard()
    {
        await using var web = await StartWebOrSkipAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(playwright);
        Skip.If(browser == null, "Playwright browsers are not installed.");
        await using var context = await browser!.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
        await page.EvaluateAsync(
            "async text => { const m = await import('/js/akml-editor.js'); m.setText(document.querySelector('.cm-editor').closest('[id]').id, text); }",
            "SELECT 1;");

        // Open from the toolbar with the keyboard: focus is in the menu, on an item that can run.
        var refactor = Id(page, "refactor-button");
        await refactor.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var menu = Id(page, "refactor-menu");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'refactor-item'");

        // Escape closes it; focus is on the Refactor button again.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToHaveCountAsync(0);
        Assert.Equal("refactor-button", await FocusedTestIdAsync(page));

        // Open it again and reach "Remove semicolons" with the keyboard.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'refactor-item'");
        for (int i = 0; i < 12 && await FocusedTextAsync(page) != "Remove semicolons"; i++)
            await page.Keyboard.PressAsync("ArrowDown");
        Assert.Equal("Remove semicolons", await FocusedTextAsync(page));
        await page.Keyboard.PressAsync("Enter");

        // The preview opens with focus on Apply; Escape closes it, back on the Refactor button.
        var preview = Id(page, "refactor-preview");
        await Assertions.Expect(preview).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'refactor-apply'");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(preview).ToHaveCountAsync(0);
        Assert.Equal("refactor-button", await FocusedTestIdAsync(page));

        // Tab stays inside the menu while it is open (once focus is in it: the menu moves it there after its first render).
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'refactor-item'");
        for (int i = 0; i < 20; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var inside = await page.EvaluateAsync<bool>("() => !!document.activeElement.closest(\"[data-testid='refactor-menu']\")");
            Assert.True(inside, $"Tab {i + 1} left the menu");
        }
        output.WriteLine("Tab stayed inside the menu for 20 presses.");
    }
}
