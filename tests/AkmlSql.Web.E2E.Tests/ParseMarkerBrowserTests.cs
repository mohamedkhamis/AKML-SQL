using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047 (WEB-005, contracts/analysis-display.md §4, quickstart V13) — with no engine at all, SQL that does not parse is
/// reported: Analyse lists a syntax Error in Problems and underlines it in red, and Parse (Ctrl+F5) checks the script in
/// the browser, says so in Messages and reports "Parse failed: 1 syntax error." in the status bar. Before, Problems said
/// "No problems found." and Parse was disabled without a connection.
///
/// <para><b>Build the web app in DEBUG first</b> (<c>dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug</c>):
/// <see cref="WebAppFixture"/> serves the Debug bundle.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class ParseMarkerBrowserTests
{
    private const string Broken = "SELEC * FROM dbo.Customers;";

    private static async Task<IBrowser?> TryLaunchAsync(IPlaywright pw)
    {
        try { return await pw.Chromium.LaunchForTestsAsync(); }
        catch (PlaywrightException) { return null; }   // browsers not installed -> skip
    }

    private static async Task<WebAppFixture> StartWebOrSkipAsync()
    {
        try { return await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
    }

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    [SkippableFact]
    public async Task Without_an_engine_Analyse_and_Parse_report_the_syntax_error()
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
            Broken);

        // Analyse: an Error row in Problems, and a red underline in the editor.
        await Id(page, "analyse-button").ClickAsync();
        var pane = page.Locator("[data-testid='results-pane']");
        var syntaxRow = pane.Locator("[data-testid='problem-item'].akml-severity-error");
        await Assertions.Expect(syntaxRow).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Assertions.Expect(syntaxRow.Locator(".akml-problems-rule")).ToHaveTextAsync("SYNTAX");
        await Assertions.Expect(pane).Not.ToContainTextAsync("No problems found");
        Assert.True(await page.Locator(".cm-lintRange-error").CountAsync() >= 1, "no red underline in the editor");

        // Parse: available without a connection, checked in the browser, reported in its own words.
        var parse = Id(page, "parse-button");
        await Assertions.Expect(parse).ToBeEnabledAsync();
        await page.Locator(".cm-content").ClickAsync();
        await page.Keyboard.PressAsync("Control+F5");

        await Assertions.Expect(Id(page, "status-outcome")).ToContainTextAsync("Parse failed: 1 syntax error.", new() { Timeout = 30_000 });
        var messages = pane.Locator("[data-testid='messages-list']");
        await Assertions.Expect(messages).ToContainTextAsync("Checked in the browser; the server was not asked.");
        await Assertions.Expect(messages).ToContainTextAsync("Msg 102, Level 15");
    }
}
