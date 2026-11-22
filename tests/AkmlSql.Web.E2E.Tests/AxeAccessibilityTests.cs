using AkmlSql.Web.E2E.Tests.Harness;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047 (WEB-007…009, contracts/cli-docs-a11y.md §4, quickstart V22) — the accessibility gate. axe (WCAG 2 A and AA plus
/// its best-practice rules) finds nothing on the editor, Snippets, Format styles, History, Settings and Settings ›
/// Connections, in the light, dark and high-contrast themes. Before, the audit found an unnamed editor, badges below 4.5:1,
/// a button inside a tab, unlabelled selects, an empty table header, no h1 and no landmarks.
///
/// <para><b>Build the web app and the engine in DEBUG first</b>: <see cref="WebAppFixture"/> serves the Debug bundle and
/// <see cref="SandboxEngine"/> runs the Debug engine (Settings › Connections shows its Active badge only when paired).</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class AxeAccessibilityTests(ITestOutputHelper output)
{
    private static readonly string[] Routes = { "editor", "snippets", "styles", "history", "settings", "settings/connections" };
    private static readonly string[] Themes = { "Light", "Dark", "HighContrast" };

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

    [SkippableFact]
    public async Task Every_page_has_no_axe_violations_in_any_theme()
    {
        await using var web = await StartWebOrSkipAsync();
        await using var engine = await SandboxEngine.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(playwright);
        Skip.If(browser == null, "Playwright browsers are not installed.");
        await using var context = await browser!.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();

        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
        await WebAppFixture.PairAsync(page, engine.Port);

        var options = new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = new List<string> { "wcag2a", "wcag2aa", "best-practice" } },
        };
        var failures = new List<string>();
        foreach (var theme in Themes)
        {
            await ShellPolishTests.NavigateAsync(page, "settings/general");
            await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
            foreach (var route in Routes)
            {
                await ShellPolishTests.NavigateAsync(page, route);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.WaitForTimeoutAsync(600);   // let the page's own loading rows settle

                var result = await page.RunAxe(options);
                output.WriteLine($"{theme} /{route}: {result.Violations.Length} violation(s), {result.Passes.Length} rule(s) passed");
                foreach (var violation in result.Violations)
                {
                    var where = string.Join(" ; ", violation.Nodes.Select(n =>
                        $"{n.Target} {{{n.Any?.FirstOrDefault()?.Message}}}"));
                    var line = $"{theme} /{route}: {violation.Id} [{violation.Impact}] {violation.Help} -> {where}";
                    output.WriteLine("  " + line);
                    failures.Add(line);
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} axe violation(s):\n" + string.Join("\n", failures));
    }
}
