using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

[Trait("Category", "BridgeE2E")]
public sealed class BrowserKeyCaptureTests(ITestOutputHelper output)
{
    // Records DOM cancellation in each browser. Native OS/browser accelerator and keyboard-layout
    // checks remain a separate interactive acceptance step; synthetic presses cannot prove those.
    [SkippableTheory]
    [InlineData("chromium")]
    [InlineData("edge")]
    [InlineData("firefox")]
    public async Task Workspace_and_grid_keys_cancel_their_DOM_default(string browser)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, browserName: browser);
        var page = s.Page;
        page.FileChooser += async (_, chooser) => await chooser.SetFilesAsync(Array.Empty<string>());
        await s.SetEditorTextAsync("SELECT 1 AS n;");
        foreach (var key in new[] { "F5", "Control+F5", "Control+r", "F8", "F6", "Shift+F6", "Control+s", "Control+Shift+s", "Control+Alt+n", "Control+Alt+o" })
        {
            var before = await s.ExecuteVersionAsync();
            await Keys.AssertClaimedAsync(page, key);
            output.WriteLine($"{browser} {key}: DOM default prevented");
            if (key is "F5" or "Control+F5") await s.WaitForExecuteAfterAsync(before);
            if (await page.GetByTestId("dialog-cancel").CountAsync() > 0) await page.GetByTestId("dialog-cancel").ClickAsync();
            await page.Keyboard.PressAsync("Escape");
        }
        await page.GetByTestId("view-menu").ClickAsync();
        await Assertions.Expect(page.GetByTestId("view-saveas")).ToContainTextAsync("Ctrl+K, Ctrl+A");
        await page.GetByTestId("view-saveas").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await page.Keyboard.PressAsync("Control+p");
        await page.Locator(".akml-cmd-input").FillAsync("Save as");
        var saveAs = page.GetByTestId("command-palette-row").Filter(new() { HasTextString = "Save as" });
        await Assertions.Expect(saveAs.Locator(".akml-cmd-shortcut")).ToContainTextAsync("Ctrl+K, Ctrl+A");
        await saveAs.ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
        foreach (var key in new[] { "Control+f", "Control+l", "Control+s", "Control+a" })
        {
            await Keys.AssertClaimedAsync(page, "Control+k");
            await Keys.AssertClaimedAsync(page, key);
            output.WriteLine($"{browser} Ctrl+K {key}: DOM default prevented");
            if (key == "Control+s")
            {
                await Assertions.Expect(page.GetByTestId("surround-picker")).ToBeVisibleAsync();
                await page.GetByTestId("surround-picker").GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            }
            if (key == "Control+a")
            {
                await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();
                await page.GetByTestId("dialog-cancel").ClickAsync();
            }
            await page.Keyboard.PressAsync("Escape");
        }
        await s.ExecuteAsync("SELECT 1 AS n;");
        await page.GetByTestId("results-cell-0-0").ClickAsync();
        foreach (var key in new[] { "Control+Shift+c", "Control+0", "Shift+Alt+ArrowLeft", "Shift+Alt+ArrowRight" })
        {
            await Keys.AssertClaimedAsync(page, key);
            output.WriteLine($"{browser} {key}: DOM default prevented");
        }
        var altGr = await page.GetByTestId("sql-editor").Locator(".cm-content").EvaluateAsync<bool>("""
            el => ['ó', 'ń'].every((key, i) => el.dispatchEvent(new KeyboardEvent('keydown', {
                key, code: i === 0 ? 'KeyO' : 'KeyN', ctrlKey: true, altKey: true, bubbles: true, cancelable: true
            })))
            """);
        Assert.True(altGr);
        await ShellPolishTests.NavigateAsync(page, "settings/general");
        await Keys.AssertNotClaimedAsync(page, "F5");
    }
}
