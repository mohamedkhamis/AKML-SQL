using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Review findings checked in a real browser (the 2026-10-17 web review):
/// <list type="bullet">
///   <item>WEB-09 — pending grid edits make the browser ask before the tab closes or reloads; with none, it does not.</item>
///   <item>WEB-08 — while a query runs, the previous grid takes no edit (it used to accept them and the new result then
///   dropped them without asking).</item>
///   <item>WEB-13 / WEB-14 — Snippets works from the keyboard: the list is a tab stop moved by the arrows, Enter selects,
///   and Import is reached with Tab.</item>
/// </list>
/// <c>BridgeE2E</c>: needs a local SQL Server, DEBUG builds of AkmlSql.Web and AkmlSql.Engine, and Playwright Chromium; run
/// serially. Tables are created in tempdb under unique names and dropped afterwards.
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class ReviewFixBrowserTests
{
    private readonly ITestOutputHelper _output;

    public ReviewFixBrowserTests(ITestOutputHelper output) => _output = output;

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static ILocator Cell(IPage page, int r, int c) => page.Locator($"[data-testid='results-cell-{r}-{c}']");

    private static async Task DropAsync(SqlSession s, string table)
    {
        try
        {
            if (await Id(s.Page, "dialog-cancel").CountAsync() > 0) await Id(s.Page, "dialog-cancel").ClickAsync();
            if (await Id(s.Page, "results-discard").CountAsync() > 0)
            {
                await Id(s.Page, "results-discard").ClickAsync();
                await Id(s.Page, "dialog-confirm").ClickAsync();
            }
            await s.ExecuteAsync($"DROP TABLE IF EXISTS dbo.{table};");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"cleanup of {table} failed: {ex.Message}");
        }
    }

    private static async Task EditFirstNameAsync(IPage page, string value)
    {
        await Cell(page, 0, 1).ClickAsync();
        await page.Keyboard.TypeAsync(value);
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(Id(page, "results-applybar")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Pending_grid_edits_make_the_browser_ask_before_the_tab_closes()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_unload_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await s.ExecuteAsync($"CREATE TABLE dbo.{table} (id int IDENTITY PRIMARY KEY, name nvarchar(50) NULL); INSERT dbo.{table} (name) VALUES (N'alpha');");
        try
        {
            await s.ExecuteAsync($"SELECT id, name FROM dbo.{table};");
            var prompts = new List<string>();
            s.Page.Dialog += async (_, d) => { prompts.Add(d.Type); await d.DismissAsync(); };

            await EditFirstNameAsync(s.Page, "changed");
            await s.Page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true });
            await Task.Delay(1000);

            Assert.Contains("beforeunload", prompts);
            Assert.False(s.Page.IsClosed);                                   // dismissed: the tab and its edit stay
            await Assertions.Expect(Id(s.Page, "results-applybar")).ToBeVisibleAsync();

            // Discarded: nothing to lose, no prompt.
            await Id(s.Page, "results-discard").ClickAsync();
            await Id(s.Page, "dialog-confirm").ClickAsync();
            await Assertions.Expect(Id(s.Page, "results-applybar")).ToHaveCountAsync(0);
            prompts.Clear();
            await s.Page.ReloadAsync();
            await s.Page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 60_000 });
            Assert.Empty(prompts);
        }
        finally
        {
            if (!s.Page.IsClosed) await DropAsync(s, table);
        }
    }

    [SkippableFact]
    public async Task While_a_query_runs_the_previous_grid_takes_no_edit()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_running_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await s.ExecuteAsync($"CREATE TABLE dbo.{table} (id int IDENTITY PRIMARY KEY, name nvarchar(50) NULL); INSERT dbo.{table} (name) VALUES (N'alpha');");
        try
        {
            await s.ExecuteAsync($"SELECT id, name FROM dbo.{table};");

            await s.SetEditorTextAsync($"WAITFOR DELAY '00:00:04'; SELECT id, name FROM dbo.{table};");
            var before = await s.ExecuteVersionAsync();
            await Id(s.Page, "execute-button").ClickAsync();
            await Assertions.Expect(Id(s.Page, "results-frozen-reason")).ToBeVisibleAsync();

            await Cell(s.Page, 0, 1).ClickAsync();
            await s.Page.Keyboard.TypeAsync("lost");
            await s.Page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(Id(s.Page, "results-applybar")).ToHaveCountAsync(0);   // no edit was taken
            await Assertions.Expect(Id(s.Page, "results-cell-input")).ToHaveCountAsync(0);

            await s.WaitForExecuteAfterAsync(before);
            await Assertions.Expect(Id(s.Page, "results-frozen-reason")).ToHaveCountAsync(0);
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("alpha");
        }
        finally
        {
            await DropAsync(s, table);
        }
    }

    [SkippableFact]
    public async Task Snippets_select_and_import_from_the_keyboard()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await page.ClickAsync("nav >> text=Snippets");
        await Assertions.Expect(Id(page, "snippet-row").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var list = Id(page, "snippet-list");
        await list.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        var active = await list.GetAttributeAsync("aria-activedescendant");
        Assert.False(string.IsNullOrEmpty(active));
        await page.Keyboard.PressAsync("Enter");
        var option = page.Locator("#" + active);
        await Assertions.Expect(option).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("#akml-snippet-shortcode")).ToHaveValueAsync(await option.GetAttributeAsync("data-shortcode") ?? "");

        // Import is in the tab order: from New, Tab reaches the file input inside the Import label.
        await Id(page, "snippet-new").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("snippet-import", await page.EvaluateAsync<string>("() => document.activeElement?.dataset?.testid ?? ''"));
    }
}
