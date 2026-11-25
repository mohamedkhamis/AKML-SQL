using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047 (SN-001, contracts/workspace-ai.md §1, quickstart V14) — a snippet made on the Snippets page completes in the
/// editor: typing its shortcode and pressing Tab expands it, with the caret at <c>$CURSOR$</c>. Deleting it stops that.
/// Before, only the built-in snippets ever completed, so a user snippet stayed the word as typed.
///
/// <para>The pages are switched inside the app (no reload), so the editor's kept snippet list must follow the store's
/// change. <b>Build the web app in DEBUG first</b>: <see cref="WebAppFixture"/> serves the Debug bundle.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class SnippetsInEditorTests(ITestOutputHelper output)
{
    private const string Body = "SELECT $CURSOR$ FROM dbo.Region;";

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

    /// <summary>Change page inside the app, as the navigation links do (a reload would start the app again).</summary>
    private static async Task GoInAppAsync(IPage page, string place, string href)
    {
        var link = Id(page, "nav-" + place);
        if (await link.IsVisibleAsync()) await link.ClickAsync();
        else await page.EvaluateAsync("u => Blazor.navigateTo(u)", href);
    }

    private static async Task<string> EditorTextAsync(IPage page) =>
        string.Join("\n", await page.Locator(".cm-editor .cm-line").AllInnerTextsAsync());

    private static async Task TypeAtStartAsync(IPage page, string text)
    {
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
        await page.EvaluateAsync(
            "async () => { const m = await import('/js/akml-editor.js'); m.setText(document.querySelector('.cm-editor').closest('[id]').id, ''); }");
        await page.Locator(".cm-content").ClickAsync();
        await page.Keyboard.TypeAsync(text, new() { Delay = 60 });
    }

    [SkippableFact]
    public async Task A_user_snippet_expands_on_Tab_and_stops_once_deleted()
    {
        await using var web = await StartWebOrSkipAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(playwright);
        Skip.If(browser == null, "Playwright browsers are not installed.");
        await using var context = await browser!.NewContextAsync();
        var page = await context.NewPageAsync();

        // 1. Make the snippet (a SQL Server one, the default kind).
        await page.GotoAsync(web.Url + "snippets");
        await Id(page, "snippet-new").ClickAsync(new() { Timeout = 60_000 });
        await page.Locator("#akml-snippet-shortcode").FillAsync("qaz");
        await page.Locator("#akml-snippet-body").FillAsync(Body);
        await Id(page, "snippet-save").ClickAsync();
        await Assertions.Expect(Id(page, "snippet-status")).ToContainTextAsync("Saved 'qaz'.");

        // 2. In the editor, qaz + Tab expands it, and the caret is where $CURSOR$ was.
        await GoInAppAsync(page, "editor", "/");
        await TypeAtStartAsync(page, "qaz");
        var option = page.Locator(".cm-tooltip-autocomplete .cm-completionLabel", new() { HasTextString = "qaz" });
        await Assertions.Expect(option.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        // CodeMirror ignores an accept key for a moment after the list changes (interactionDelay), and each keystroke's
        // reply refreshes it: wait until qaz is the selected option of a settled list, as a person would.
        await Assertions.Expect(page.Locator(".cm-tooltip-autocomplete li[aria-selected] .cm-completionLabel"))
            .ToHaveTextAsync("qaz", new() { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(500);
        await page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(page.Locator(".cm-editor .cm-line").First).ToHaveTextAsync("SELECT  FROM dbo.Region;", new() { Timeout = 10_000 });
        await page.Keyboard.TypeAsync("*");
        Assert.Equal("SELECT * FROM dbo.Region;", await EditorTextAsync(page));

        // 3. Delete it; back in the editor, qaz + Tab leaves the word as typed.
        await GoInAppAsync(page, "snippets", "/snippets");
        var row = page.Locator("[data-testid='snippet-row'][data-shortcode='qaz']");
        await row.ClickAsync(new() { Timeout = 30_000 });
        await Id(page, "snippet-delete").ClickAsync();
        await Id(page, "dialog-confirm").ClickAsync();
        await Assertions.Expect(Id(page, "snippet-status")).ToContainTextAsync("Deleted 'qaz'.");

        await GoInAppAsync(page, "editor", "/");
        await TypeAtStartAsync(page, "qaz");
        await page.WaitForTimeoutAsync(1_500);   // give a (wrong) completion the time to show
        Assert.Equal(0, await option.CountAsync());
        await page.Keyboard.PressAsync("Tab");
        var text = await EditorTextAsync(page);
        output.WriteLine($"after delete: [{text}]");
        Assert.Equal("qaz", text.Trim());
    }
}
