using System.Text;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// The Format styles page (SQL Prompt style editor) in a real browser: change options and watch
/// the preview change, save and find the style still there after a reload, format in the editor
/// with it, and move SQL Prompt .json styles in and out unchanged.
///
/// <para><b>Build the web app in DEBUG first</b> (<c>dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug</c>):
/// <see cref="WebAppFixture"/> serves the Debug bundle with <c>--no-build</c>.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class FormatStylesTests
{
    private static async Task<WebAppFixture> StartWebOrSkipAsync()
    {
        try { return await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
    }

    private static async Task<(IPlaywright, IBrowser, IPage)> OpenAsync(WebAppFixture web, string answerPromptsWith = "E2E Style")
    {
        var pw = await Playwright.CreateAsync();
        IBrowser browser;
        try { browser = await pw.Chromium.LaunchAsync(); }
        catch (PlaywrightException) { pw.Dispose(); throw new SkipException("Playwright Chromium not installed (run playwright.ps1 install chromium)."); }
        var context = await browser.NewContextAsync(new() { AcceptDownloads = true });
        var page = await context.NewPageAsync();
        // New / Save as / Rename ask for a name; confirmations are accepted.
        page.Dialog += async (_, dialog) =>
        {
            if (dialog.Type == DialogType.Prompt) await dialog.AcceptAsync(answerPromptsWith);
            else await dialog.AcceptAsync();
        };
        await page.GotoAsync(web.Url + "styles");
        await page.Locator("[data-testid=style-row]").First.WaitForAsync(new() { Timeout = 60_000 });
        return (pw, browser, page);
    }

    private static ILocator Preview(IPage page) => page.Locator("[data-testid=preview]");

    private static Task<string> PreviewTextAsync(IPage page) => Preview(page).InnerTextAsync();

    private static async Task CreateStyleAsync(IPage page, string name)
    {
        await page.Locator("[data-testid=style-new]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=style-name]")).ToHaveTextAsync(name);
    }

    [SkippableFact]
    public async Task Changing_options_changes_the_preview_and_a_saved_style_survives_a_reload()
    {
        await using var web = await StartWebOrSkipAsync();
        var (pw, browser, page) = await OpenAsync(web);
        using var _ = pw;
        await using var __ = browser;

        await CreateStyleAsync(page, "E2E Style");

        // Lists page: commas before items.
        await page.Locator("[data-testid=page-lists]").ClickAsync();
        var before = await PreviewTextAsync(page);
        await page.Locator("[data-testid='option-lists.placeCommasBeforeItems']").CheckAsync();
        await Assertions.Expect(Preview(page)).Not.ToHaveTextAsync(before);
        var after = await PreviewTextAsync(page);
        Assert.Contains(after.Split('\n'), line => line.TrimStart().StartsWith(", c.CompanyName"));
        await Assertions.Expect(page.Locator("[data-testid=preview-status]")).ToContainTextAsync("changed");
        await Assertions.Expect(page.Locator("[data-testid=preview] .moved").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=style-dirty]")).ToBeVisibleAsync();

        // Casing page: lowercase keywords.
        await page.Locator("[data-testid=page-casing]").ClickAsync();
        await page.Locator("[data-testid='option-casing.reservedKeywords']").SelectOptionAsync("lowercase");
        await Assertions.Expect(Preview(page)).ToContainTextAsync("select top");

        await page.Locator("[data-testid=style-save]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=styles-status]")).ToContainTextAsync("Saved");
        await Assertions.Expect(page.Locator("[data-testid=style-dirty]")).ToHaveCountAsync(0);

        // A reload reads the style back from storage.
        await page.ReloadAsync();
        var row = page.Locator("[data-testid=style-row][data-style='E2E Style']");
        await row.WaitForAsync(new() { Timeout = 60_000 });
        await row.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=style-name]")).ToHaveTextAsync("E2E Style");
        await page.Locator("[data-testid=page-lists]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='option-lists.placeCommasBeforeItems']")).ToBeCheckedAsync();
        await page.Locator("[data-testid=page-casing]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='option-casing.reservedKeywords']")).ToHaveValueAsync("lowercase");
    }

    [SkippableFact]
    public async Task The_editor_formats_with_the_style_chosen_on_the_styles_page()
    {
        await using var web = await StartWebOrSkipAsync();
        var (pw, browser, page) = await OpenAsync(web, "Editor Style");
        using var _ = pw;
        await using var __ = browser;

        await CreateStyleAsync(page, "Editor Style");
        await page.Locator("[data-testid=page-lists]").ClickAsync();
        await page.Locator("[data-testid='option-lists.placeCommasBeforeItems']").CheckAsync();
        await page.Locator("[data-testid=page-casing]").ClickAsync();
        await page.Locator("[data-testid='option-casing.reservedKeywords']").SelectOptionAsync("uppercase");
        await page.Locator("[data-testid=style-save]").ClickAsync();
        await page.Locator("[data-testid=style-set-active]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=styles-status]")).ToContainTextAsync("now uses");

        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-content", new() { Timeout = 60_000 });
        await SetEditorTextAsync(page, "select a, b from t where x = 1;");
        await page.GetByRole(AriaRole.Button, new() { Name = "Format", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='format-complete']", new() { State = WaitForSelectorState.Attached });

        var formatted = (await GetEditorTextAsync(page)).Replace("\r\n", "\n");
        Assert.Contains("SELECT a\n", formatted);
        Assert.Contains(formatted.Split('\n'), line => line.TrimStart() == ", b");
        Assert.Contains("FROM", formatted);
    }

    [SkippableFact]
    public async Task A_sql_prompt_style_imports_formats_and_exports_unchanged()
    {
        await using var web = await StartWebOrSkipAsync();
        var (pw, browser, page) = await OpenAsync(web);
        using var _ = pw;
        await using var __ = browser;

        var fixture = Directory.GetFiles(Path.Combine(RepoRoot(), "specs", "031-redgate-style-import", "reference"), "MohamedKhamis-*.json").Single();
        await page.Locator("[data-testid=style-import]").SetInputFilesAsync(fixture);
        await Assertions.Expect(page.Locator("[data-testid=style-name]")).ToHaveTextAsync("MohamedKhamis", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("[data-testid=styles-status]")).ToContainTextAsync("Created");

        // The imported settings are the ones the editor shows…
        await page.Locator("[data-testid=page-whitespace]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='option-whitespace.spacesOrTabs']")).ToHaveValueAsync("tabsIfPossible");
        await Assertions.Expect(page.Locator("[data-testid='option-whitespace.numberOfSpacesInTabs']")).ToHaveValueAsync("2");
        // …and the preview formats with them (tabs, uppercase, space before the semicolon).
        await Assertions.Expect(Preview(page)).ToContainTextAsync("SELECT");
        Assert.Contains("\t", await PreviewTextAsync(page));
        Assert.Contains(" ;", await PreviewTextAsync(page));

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-testid=style-export]").ClickAsync());
        Assert.Equal("MohamedKhamis.json", download.SuggestedFilename);
        var exported = await File.ReadAllTextAsync((await download.PathAsync())!, Encoding.UTF8);
        var original = AkmlSql.Formatting.SqlPrompt.SqlPromptStyleDocument.Parse(await File.ReadAllTextAsync(fixture));
        var roundTripped = AkmlSql.Formatting.SqlPrompt.SqlPromptStyleDocument.Parse(exported);
        Assert.True(original.FormatsLike(roundTripped), "The exported style differs from the imported SQL Prompt file.");
        Assert.Equal(original.Id, roundTripped.Id);
    }

    [SkippableFact]
    public async Task Every_page_shows_a_preview_that_reacts_to_its_options()
    {
        await using var web = await StartWebOrSkipAsync();
        var (pw, browser, page) = await OpenAsync(web, "Pages Style");
        using var _ = pw;
        await using var __ = browser;
        await CreateStyleAsync(page, "Pages Style");

        foreach (var section in AkmlSql.Formatting.SqlPrompt.SqlPromptOptionCatalog.Sections)
        {
            await page.Locator($"[data-testid=page-{section.Id}]").ClickAsync();
            // The first option on the page whose effect does not depend on anything else.
            var option = section.Options.First(o => o.EnabledWhen is null && o.Note is null);
            var before = await PreviewTextAsync(page);
            var control = page.Locator($"[data-testid='option-{option.Path}']");
            switch (option.Kind)
            {
                case AkmlSql.Formatting.SqlPrompt.SqlPromptOptionKind.Boolean:
                    await control.SetCheckedAsync(option.Default != "true");
                    break;
                case AkmlSql.Formatting.SqlPrompt.SqlPromptOptionKind.Integer:
                    await control.FillAsync(option.Path == "whitespace.numberOfSpacesInTabs" ? "8" : "2");
                    await control.PressAsync("Tab");
                    break;
                default:
                    await control.SelectOptionAsync(option.Choices.First(c => c.Value != option.Default).Value);
                    break;
            }
            // Exact text: Playwright's text assertions collapse whitespace, and a tabs-for-spaces
            // change is all whitespace.
            await WaitForPreviewChangeAsync(page, before, option.Path);
        }
    }

    private static async Task WaitForPreviewChangeAsync(IPage page, string before, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await PreviewTextAsync(page) != before) return;
            await page.WaitForTimeoutAsync(100);
        }
        Assert.Fail($"The preview did not change after changing {because}.");
    }

    [SkippableFact]
    public async Task A_number_box_shows_the_value_the_style_keeps()
    {
        // Blazor re-renders a value only when it changes: typing 0 into "Spaces per tab" (1-16)
        // clamped the style to 1 while the box kept showing 0, and a cleared box stayed blank.
        await using var web = await StartWebOrSkipAsync();
        var (pw, browser, page) = await OpenAsync(web);
        using var _ = pw;
        await using var __ = browser;

        await CreateStyleAsync(page, "E2E Style");
        await page.Locator("[data-testid=page-whitespace]").ClickAsync();
        var box = page.Locator("[data-testid='option-whitespace.numberOfSpacesInTabs']");
        await Assertions.Expect(box).ToHaveValueAsync("4");

        await box.FillAsync("0");
        await box.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-testid='option-whitespace.numberOfSpacesInTabs']")).ToHaveValueAsync("1");
        // Corrected in place: the box keeps keyboard focus (re-creating it used to drop focus).
        Assert.Equal("option-whitespace.numberOfSpacesInTabs",
            await page.EvaluateAsync<string>("() => document.activeElement && document.activeElement.dataset.testid"));

        box = page.Locator("[data-testid='option-whitespace.numberOfSpacesInTabs']");
        await box.FillAsync("");
        await box.PressAsync("Tab");
        await Assertions.Expect(page.Locator("[data-testid='option-whitespace.numberOfSpacesInTabs']")).ToHaveValueAsync("1");
        await Assertions.Expect(page.Locator("[data-testid=styles-status]")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task The_page_does_not_scroll_sideways_on_a_phone()
    {
        await using var web = await StartWebOrSkipAsync();
        using var pw = await Playwright.CreateAsync();
        IBrowser browser;
        try { browser = await pw.Chromium.LaunchAsync(); }
        catch (PlaywrightException) { throw new SkipException("Playwright Chromium not installed (run playwright.ps1 install chromium)."); }
        await using var __ = browser;
        var page = await (await browser.NewContextAsync(new() { ViewportSize = new ViewportSize { Width = 390, Height = 844 } })).NewPageAsync();

        await page.GotoAsync(web.Url + "styles");
        await page.Locator("[data-testid=style-row]").First.WaitForAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator("[data-testid=style-name]")).Not.ToBeEmptyAsync();

        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        // Name what sticks out, so a failure says where to look.
        var offenders = await page.EvaluateAsync<string>(@"() => [...document.querySelectorAll('body *')]
            .filter(e => e.getBoundingClientRect().right > document.documentElement.clientWidth + 1)
            .filter(e => ![...e.children].some(c => c.getBoundingClientRect().right > document.documentElement.clientWidth + 1))
            .slice(0, 12)
            .map(e => e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : '') + ' w=' + Math.round(e.getBoundingClientRect().width))
            .join(' | ')");
        Assert.True(overflow <= 0, $"The styles page is {overflow}px wider than a 390px screen: {offenders}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task SetEditorTextAsync(IPage page, string sql) =>
        page.EvaluateAsync(
            @"async (sql) => {
                const host = document.querySelector('[data-testid=""sql-editor""]');
                const mod = await import('/js/akml-editor.js');
                mod.setText(host.id, sql);
              }", sql);

    private static Task<string> GetEditorTextAsync(IPage page) =>
        page.EvaluateAsync<string>(
            @"async () => {
                const host = document.querySelector('[data-testid=""sql-editor""]');
                const mod = await import('/js/akml-editor.js');
                return mod.getText(host.id);
              }");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
