using System.Text.Json;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047, installed-product run of 2026-11-22 (baseline.md › Installed-product run): the defects the run found on the
/// installed 1.26.1122.1400 and that the unit suites could not see, held in a real browser on the Debug bundle.
/// <list type="bullet">
/// <item>F1 (UX-001): an advisory finding in marker-only mode was still drawn as a wavy underline — the editor page's own
/// underline rule won a specificity tie against the marker-only rule.</item>
/// <item>F2 (AI-002): Fix could never be used on a SQL Server tab — the editor read the last run's error only for PostgreSQL.</item>
/// <item>F3 (WEB-013): the explorer's "cannot see every object" note was drawn over the rows below it (a fixed 24px row).</item>
/// </list>
/// <para><b>Build the web app in DEBUG first</b>; F2 also needs a Debug engine and a local SQL Server.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class InstalledRunFixesTests(ITestOutputHelper output)
{
    private const string ThreeStatements =
        "SELECT CustomerID FROM dbo.Customers WHERE Country = N'Germany'\n" +
        "SELECT COUNT(*) AS OrderCount FROM dbo.Orders\n" +
        "SELECT ProductName FROM dbo.Products WHERE Discontinued = 1\n";

    private static async Task<WebAppFixture> StartWebOrSkipAsync()
    {
        try { return await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
    }

    private static async Task<IBrowser?> TryLaunchAsync(IPlaywright pw)
    {
        try { return await pw.Chromium.LaunchForTestsAsync(); }
        catch (PlaywrightException) { return null; }
    }

    private static Task SetTextAsync(IPage page, string text) => page.EvaluateAsync(
        "async t => { const m = await import('/js/akml-editor.js'); m.setText(document.querySelector('.cm-editor').closest('[id]').id, t); }", text);

    // ── F1 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Advisory_findings_are_margin_markers_only_and_a_syntax_error_stays_underlined()
    {
        await using var web = await StartWebOrSkipAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(playwright);
        Skip.If(browser == null, "Playwright browsers are not installed.");
        await using var context = await browser!.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });

        await SetTextAsync(page, ThreeStatements);
        await page.GetByTestId("analyse-button").ClickAsync();
        var markers = page.Locator(".cm-gutter-lint .cm-lint-marker-info");
        await Assertions.Expect(markers).ToHaveCountAsync(3, new() { Timeout = 30_000 });

        var advisory = await page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('.cm-lintRange-info')).map(e => { const s = getComputedStyle(e); " +
            "return s.textDecorationLine + '|' + s.backgroundImage; })");
        output.WriteLine("advisory ranges: " + string.Join(", ", advisory));
        Assert.NotEmpty(advisory);
        Assert.All(advisory, look => Assert.Equal("none|none", look));

        await SetTextAsync(page, ThreeStatements + "SELEC * FROM dbo.Customers\n");
        await page.GetByTestId("analyse-button").ClickAsync();
        await Assertions.Expect(page.Locator(".cm-lintRange-error").First).ToBeAttachedAsync(new() { Timeout = 30_000 });
        var error = await page.EvaluateAsync<string>(
            "() => { const s = getComputedStyle(document.querySelector('.cm-lintRange-error')); return s.textDecorationLine + '|' + s.textDecorationStyle; }");
        output.WriteLine("error range: " + error);
        Assert.Equal("underline|wavy", error);
    }

    // ── F3 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task The_explorer_note_grows_with_its_text_instead_of_covering_the_next_row()
    {
        await using var web = await StartWebOrSkipAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(playwright);
        Skip.If(browser == null, "Playwright browsers are not installed.");
        await using var context = await browser!.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
        // The explorer's styles are on the page with the panel (connected or not); lay the note out in a panel-wide list.
        await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('style')).some(s => s.textContent.includes('.akml-schema-tree-note'))",
            null, new() { Timeout = 30_000 });

        var geometry = await page.EvaluateAsync<JsonElement>(@"() => {
            const ul = document.createElement('ul');
            ul.className = 'akml-schema-tree';
            ul.style.width = '240px';
            ul.innerHTML = '<li class=""akml-schema-tree-node akml-schema-tree-note"" data-probe=""note""><span class=""akml-schema-tree-note-text"">The engine signs in as NT AUTHORITY\\SYSTEM, which cannot see every object in Northwind. Grant it VIEW DEFINITION on the database, or connect with a login that can.</span></li>'
                         + '<li class=""akml-schema-tree-node"" data-probe=""next""><span class=""akml-schema-tree-label"">Schema dbo</span></li>';
            document.body.appendChild(ul);
            const note = ul.querySelector('[data-probe=note]'), text = note.querySelector('span'), next = ul.querySelector('[data-probe=next]');
            const r = { row: note.getBoundingClientRect().height, textBottom: text.getBoundingClientRect().bottom, nextTop: next.getBoundingClientRect().top };
            ul.remove();
            return r;
        }");
        var row = geometry.GetProperty("row").GetDouble();
        var textBottom = geometry.GetProperty("textBottom").GetDouble();
        var nextTop = geometry.GetProperty("nextTop").GetDouble();
        output.WriteLine($"note row {row}px, text bottom {textBottom}, next row top {nextTop}");
        Assert.True(row > 30, $"the note row stayed {row}px high for several lines of text");
        Assert.True(textBottom <= nextTop + 0.5, $"the note text ends at {textBottom}, below the next row's top {nextTop}");
    }

    // ── F2 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task On_SQL_Server_Fix_is_enabled_after_a_failed_run_and_sends_the_servers_error()
    {
        using var mock = MockAiProvider.StartOllama();
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, "tempdb");
        var page = s.Page;
        await InAppDialogs.AcceptAllAsync(page, "");

        await page.ClickAsync("nav >> text=Settings");
        await page.ClickAsync("[data-testid='settings-nav-ai-assistance']");
        if (await page.GetByTestId("ai-agent-form").CountAsync() == 0) await page.GetByTestId("ai-agent-add").ClickAsync();
        await page.GetByLabel("Provider").SelectOptionAsync("ollama");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Model" }).FillAsync("mock");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint" }).FillAsync(mock.ChatCompletionsUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("ai-agent-form")).ToHaveCountAsync(0);
        var setActive = page.GetByTestId("ai-agent-set-active");
        if (await setActive.IsEnabledAsync()) await setActive.ClickAsync();
        await page.WaitForTimeoutAsync(750);
        await page.ClickAsync("nav >> text=Editor");
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });

        await page.GetByTestId("ai-button").ClickAsync();
        var fix = page.GetByTestId("ai-fix");
        await Assertions.Expect(fix).ToBeDisabledAsync();   // no run yet: nothing to fix

        await s.ExecuteAsync("SELECT * FROM dbo.NoSuchTable_akml047;");
        await Assertions.Expect(page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("errors", new() { Timeout = 30_000 });
        await Assertions.Expect(fix).ToBeEnabledAsync(new() { Timeout = 15_000 });

        mock.ResetCaptures();
        await fix.ClickAsync();
        await Assertions.Expect(page.Locator(".akml-ai-result pre")).ToContainTextAsync("MOCK-STREAM", new() { Timeout = 30_000 });
        var sent = string.Join("\n", mock.Captures.SelectMany(c => c.GetProperty("messages").EnumerateArray())
            .Select(m => m.GetProperty("content").GetString()));
        output.WriteLine("Fix request: " + sent.Replace("\n", " ⏎ "));
        Assert.Contains("Invalid object name 'dbo.NoSuchTable_akml047'.", sent);
        Assert.DoesNotContain("(no error)", sent);
    }
}
