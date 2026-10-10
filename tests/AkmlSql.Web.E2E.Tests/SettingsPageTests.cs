using System.Text.RegularExpressions;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 041 (T138, research R43–R57, R70) — quickstart §4, "Settings", in a real browser: the five places and the
/// old routes, the nine sections, the row limit default against the toolbar's session value, analysis switched off
/// and back on, a suppressed rule undone, the AI reset confirmation, an export with no secret imported into another
/// browser, the editor options live and after a reload, the filter, no theme flash, and two tabs.
///
/// <para><c>BridgeE2E</c>, <see cref="SkippableFactAttribute"/>: skipped without a local SQL Server, DEBUG builds of
/// AkmlSql.Web and AkmlSql.Engine, or Playwright Chromium. Run serially
/// (<c>-- xUnit.ParallelizeTestCollections=false</c>).</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class SettingsPageTests
{
    private readonly ITestOutputHelper _output;

    public SettingsPageTests(ITestOutputHelper output) => _output = output;

    private const string SelectStarInProc = "CREATE PROCEDURE dbo.akml_e2e_041 AS\nBEGIN\n    SELECT * FROM sys.objects;\nEND;";

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static async Task OpenAsync(IPage page, string section)
    {
        await page.ClickAsync("nav >> text=Settings");
        await Id(page, $"settings-nav-{section}").ClickAsync();
        await Assertions.Expect(Id(page, $"settings-section-{section}")).ToBeVisibleAsync();
    }

    private static async Task SetNumberAsync(IPage page, string selector, string value)
    {
        await page.FillAsync(selector, value);
        await page.Locator(selector).DispatchEventAsync("change");
    }

    private static async Task GoToEditorAsync(IPage page)
    {
        await page.ClickAsync("nav >> text=Editor");
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
    }

    private static async Task<int> AnalyseVersionAsync(IPage page)
    {
        var marker = Id(page, "analyse-complete");
        return await marker.CountAsync() == 0 ? 0 : int.Parse(await marker.GetAttributeAsync("data-version") ?? "0");
    }

    private static async Task AnalyseAsync(IPage page)
    {
        int before = await AnalyseVersionAsync(page);
        await Id(page, "analyse-button").ClickAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await AnalyseVersionAsync(page) <= before && DateTime.UtcNow < deadline) await Task.Delay(100);
    }

    [SkippableFact]
    public async Task Five_places_in_the_navigation_and_the_old_routes_open_their_sections()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        Assert.Equal(new[] { "Editor", "Snippets", "Format styles", "History", "Settings" },
            (await page.Locator("nav.akml-nav a.akml-nav-link").AllInnerTextsAsync()).Select(t => t.Trim()));

        await page.ClickAsync("nav >> text=Settings");
        await Assertions.Expect(page.Locator("nav.akml-nav a.akml-nav-link.active")).ToHaveTextAsync("Settings");
        Assert.Equal(new[] { "General", "Editor", "Format", "Queries", "Code analysis", "AI assistance", "Connections", "Schema cache", "Diagnostics" },
            (await page.Locator("[data-testid^='settings-nav-']").AllInnerTextsAsync()).Select(t => t.Trim()));

        foreach (var (route, section) in new[] { ("settings/ai", "ai-assistance"), ("settings/schema-cache", "schema-cache"), ("diagnostics", "diagnostics") })
        {
            await page.GotoAsync(s.Web.Url + route);
            await Assertions.Expect(Id(page, $"settings-section-{section}")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }
    }

    [SkippableFact]
    public async Task The_row_limit_default_against_the_toolbar_session_value()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await OpenAsync(page, "queries");
        await SetNumberAsync(page, "#setting-queries-max-rows-control", "500");
        await GoToEditorAsync(page);
        await Assertions.Expect(Id(page, "caps-maxrows")).ToHaveValueAsync("500");

        await s.ExecuteAsync("SELECT TOP (2000) a.object_id FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b;");
        await Assertions.Expect(Id(page, "results-set-header-0")).ToContainTextAsync("500");

        await SetNumberAsync(page, "[data-testid='caps-maxrows']", "50");
        await OpenAsync(page, "queries");
        await Assertions.Expect(page.Locator("#setting-queries-max-rows-control")).ToHaveValueAsync("500");

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await GoToEditorAsync(page);
        await Assertions.Expect(Id(page, "caps-maxrows")).ToHaveValueAsync("500");
    }

    [SkippableFact]
    public async Task With_analysis_off_Analyse_says_so_and_Turn_on_brings_it_back()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await OpenAsync(page, "code-analysis");
        await page.UncheckAsync("#setting-analysis-enabled-control");
        await GoToEditorAsync(page);
        await s.SetEditorTextAsync(SelectStarInProc);
        await AnalyseAsync(page);

        await Assertions.Expect(Id(page, "results-tab-problems")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(Id(page, "problems-analysis-off")).ToContainTextAsync("Code analysis is off.");
        await Assertions.Expect(Id(page, "results-problems-badge")).ToHaveCountAsync(0);

        await Id(page, "problems-enable-analysis").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='problem-item']").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [SkippableFact]
    public async Task A_rule_suppressed_everywhere_is_listed_and_Undo_brings_its_finding_back()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await s.SetEditorTextAsync(SelectStarInProc);
        await AnalyseAsync(page);
        var pe001 = page.Locator("[data-testid='problem-item']", new() { HasTextString = "PE001" });
        await pe001.Locator("[data-testid='suppress-global']").ClickAsync();
        await Assertions.Expect(pe001).ToHaveCountAsync(0, new() { Timeout = 30_000 });

        await OpenAsync(page, "code-analysis");
        await Assertions.Expect(Id(page, "suppressed-rule-PE001")).ToBeVisibleAsync();
        await Id(page, "suppressed-rule-undo-PE001").ClickAsync();
        await Assertions.Expect(Id(page, "suppressed-rule-PE001")).ToHaveCountAsync(0);

        await GoToEditorAsync(page);
        await s.SetEditorTextAsync(SelectStarInProc);
        await AnalyseAsync(page);
        await Assertions.Expect(page.Locator("[data-testid='problem-item']", new() { HasTextString = "PE001" })).ToHaveCountAsync(1, new() { Timeout = 30_000 });
    }

    private static async Task AddProviderAsync(IPage page, string providerId, string model, string? endpoint, string? key)
    {
        await page.GetByLabel("Provider").SelectOptionAsync(providerId);
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Model" }).FillAsync(model);
        if (endpoint != null) await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint" }).FillAsync(endpoint);
        if (key != null) await page.GetByLabel("API key").FillAsync(key);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    [SkippableFact]
    public async Task Restoring_AI_defaults_names_the_three_providers_and_leaves_General_and_Connections()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await OpenAsync(page, "general");
        await page.SelectOptionAsync("#setting-theme-control", "Dark");
        await OpenAsync(page, "ai-assistance");
        await AddProviderAsync(page, "ollama", "llama3", "http://localhost:11434", null);
        await AddProviderAsync(page, "lmstudio", "local-model", "http://localhost:1234/v1", null);
        await AddProviderAsync(page, "openai", "gpt-5", null, "sk-e2e-not-a-real-key");
        await Assertions.Expect(page.Locator("input[type='radio'][name='active']")).ToHaveCountAsync(3);

        await Id(page, "settings-restore-ai-assistance").ClickAsync();
        await Assertions.Expect(Id(page, "dialog-details")).ToContainTextAsync("This removes your 3 providers and their keys.");
        await Id(page, "dialog-confirm").ClickAsync();

        await Assertions.Expect(page.Locator("input[type='radio'][name='active']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-akml-theme", "dark");
        await Assertions.Expect(Id(page, "status-pill")).ToContainTextAsync("Live");
        await OpenAsync(page, "connections");
        await Assertions.Expect(page.Locator(".akml-connections-row")).ToHaveCountAsync(1);
    }

    [SkippableFact]
    public async Task An_export_holds_no_secret_and_imports_into_another_browser()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        const string secret = "sk-e2e-SECRET-0041";

        await OpenAsync(page, "general");
        await page.SelectOptionAsync("#setting-theme-control", "Dark");
        await OpenAsync(page, "ai-assistance");
        await AddProviderAsync(page, "openai", "gpt-5", null, secret);

        var download = await page.RunAndWaitForDownloadAsync(() => Id(page, "settings-export").ClickAsync());
        Assert.Matches(@"^akmlsql-web-settings-\d{8}\.json$", download.SuggestedFilename);
        var path = await download.PathAsync();
        var json = await File.ReadAllTextAsync(path!);
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("\"ciphertext\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pairing", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"theme\": \"dark\"", json);

        // Another browser (a fresh context: its own IndexedDB), with one property this build does not know.
        var edited = json.Replace("\"fontSize\":", "\"zoomLevel\": 3,\n      \"fontSize\":");
        var file = Path.Combine(Path.GetTempPath(), $"akml-e2e-settings-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, edited);
        try
        {
            var other = await s.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
            var page2 = await other.NewPageAsync();
            await page2.GotoAsync(s.Web.Url + "settings", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await Assertions.Expect(Id(page2, "settings-page")).ToBeVisibleAsync(new() { Timeout = 60_000 });

            await page2.Locator("[data-testid='settings-import-input']").SetInputFilesAsync(file);

            await Assertions.Expect(page2.Locator("[data-testid='toast']").Last).ToContainTextAsync("1 setting skipped", new() { Timeout = 10_000 });
            await Assertions.Expect(page2.Locator("html")).ToHaveAttributeAsync("data-akml-theme", "dark");
            await Id(page2, "settings-nav-ai-assistance").ClickAsync();
            await Assertions.Expect(Id(page2, "provider-needs-key-openai")).ToBeVisibleAsync();
            await Id(page2, "settings-nav-connections").ClickAsync();
            await Assertions.Expect(page2.Locator(".akml-connections-row")).ToHaveCountAsync(0);   // its engines untouched
            await other.CloseAsync();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [SkippableFact]
    public async Task Editor_options_change_the_editor_live_and_survive_a_reload()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await OpenAsync(page, "editor");
        await SetNumberAsync(page, "#setting-editor-font-size-control", "18");
        await page.CheckAsync("#setting-editor-word-wrap-control");
        await GoToEditorAsync(page);

        async Task<string> FontSizeAsync() => await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector(\"[data-testid='sql-editor']\")).fontSize");
        Assert.Equal("18px", await FontSizeAsync());
        await Assertions.Expect(page.Locator("[data-testid='sql-editor'] .cm-content")).ToHaveClassAsync(new Regex("cm-lineWrapping"));

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
        Assert.Equal("18px", await FontSizeAsync());
    }

    [SkippableFact]
    public async Task The_filter_keeps_matching_settings_under_their_section_and_greys_the_rest()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await OpenAsync(page, "general");

        await page.FillAsync("[data-testid='settings-filter']", "timeout");

        Assert.Equal(new[] { "queries-timeout" }, await page.Locator("[data-setting]").EvaluateAllAsync<string[]>("els => els.map(e => e.dataset.setting)"));
        await Assertions.Expect(page.Locator(".akml-settings-section-title")).ToHaveTextAsync("Queries");
        await Assertions.Expect(Id(page, "settings-nav-general")).ToHaveAttributeAsync("aria-disabled", "true");

        await page.FillAsync("[data-testid='settings-filter']", "");
        await Assertions.Expect(Id(page, "setting-theme")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "settings-section-general")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task A_dark_theme_is_applied_before_the_first_paint()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await OpenAsync(page, "general");
        await page.SelectOptionAsync("#setting-theme-control", "Dark");

        // Record every href the theme link has, with the time, from before any page script runs.
        await s.Context.AddInitScriptAsync(@"
            window.__akmlThemeHrefs = [];
            new MutationObserver(() => {
                const link = document.getElementById('akml-theme-css');
                if (!link) return;
                const href = link.getAttribute('href');
                const last = window.__akmlThemeHrefs[window.__akmlThemeHrefs.length - 1];
                if (!last || last.href !== href) window.__akmlThemeHrefs.push({ href, t: performance.now() });
            }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['href'] });");
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });

        var times = await page.EvaluateAsync<double[]>(@"() => {
            const dark = window.__akmlThemeHrefs.find(h => h.href.endsWith('dark.css'));
            const paint = performance.getEntriesByType('paint').find(p => p.name === 'first-paint');
            return [dark ? dark.t : -1, paint ? paint.startTime : 1e12];
        }");
        double darkAt = times[0], firstPaint = times[1];
        _output.WriteLine($"dark.css at {darkAt} ms, first paint at {firstPaint} ms");
        Assert.True(darkAt >= 0, "The theme link never pointed at dark.css.");
        Assert.True(darkAt <= firstPaint, $"dark.css was set at {darkAt} ms, after the first paint at {firstPaint} ms.");
    }

    [SkippableFact]
    public async Task A_second_tab_uses_the_new_value_and_never_writes_the_old_one_back()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var a = s.Page;
        var b = await s.Context.NewPageAsync();
        await b.GotoAsync(s.Web.Url + "settings", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Assertions.Expect(Id(b, "settings-page")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await OpenAsync(a, "general");
        await a.SelectOptionAsync("#setting-theme-control", "Dark");
        await a.WaitForTimeoutAsync(500);

        await b.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(b.Locator("html")).ToHaveAttributeAsync("data-akml-theme", "dark", new() { Timeout = 30_000 });
        await Assertions.Expect(b.Locator("#setting-theme-control")).ToHaveValueAsync("Dark");

        await a.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(a.Locator("html")).ToHaveAttributeAsync("data-akml-theme", "dark", new() { Timeout = 30_000 });
    }
}
