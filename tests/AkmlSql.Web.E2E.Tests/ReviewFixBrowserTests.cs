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
    /// <summary>Re-review REV-08: with more snippets than the list shows, End and Home keep the active option inside the
    /// list's visible area (the list scrolls, the page does not), and Enter selects it.</summary>
    [SkippableFact]
    public async Task The_active_snippet_stays_visible_while_the_keys_move_it()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output, contextOptions: new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        var page = s.Page;
        await page.ClickAsync("nav >> text=Snippets");
        await Assertions.Expect(Id(page, "snippet-row").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await page.SelectOptionAsync("[data-testid='snippet-kind-filter']", "-1");   // every snippet, as in the review
        var list = Id(page, "snippet-list");
        // The review's case: more options than the list shows (22 built-ins at 1440 x 900).
        Assert.True(await list.EvaluateAsync<bool>("l => l.scrollHeight > l.clientHeight"), "The snippet list does not overflow; the test needs it to.");

        const string Visible = """
            l => { const o = document.getElementById(l.getAttribute('aria-activedescendant'));
                   const a = l.getBoundingClientRect(), b = o.getBoundingClientRect();
                   return b.top >= a.top - 1 && b.bottom <= a.bottom + 1; }
            """;
        await list.FocusAsync();
        await page.Keyboard.PressAsync("End");
        Assert.True(await list.EvaluateAsync<bool>(Visible), "End: the last option is out of view");
        Assert.True(await list.EvaluateAsync<int>("l => l.scrollTop") > 0);
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.scrollY"));

        await page.Keyboard.PressAsync("Home");
        Assert.True(await list.EvaluateAsync<bool>(Visible), "Home: the first option is out of view");
        for (int i = 0; i < 12; i++) await page.Keyboard.PressAsync("ArrowDown");
        Assert.True(await list.EvaluateAsync<bool>(Visible), "ArrowDown: the active option is out of view");

        var active = await list.GetAttributeAsync("aria-activedescendant");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#" + active)).ToHaveAttributeAsync("aria-selected", "true");
    }

    /// <summary>Re-review REV-06 through the real grid: a result from a multi-statement batch is not re-run after Apply, so the
    /// inserted row must show what the server stored — identity, NOT NULL and nullable defaults, an explicit NULL — and be
    /// editable at once by its key.</summary>
    [SkippableFact]
    public async Task An_inserted_row_shows_what_the_server_stored_and_can_be_edited_without_a_rerun()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_insert_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await s.ExecuteAsync($"CREATE TABLE dbo.{table} (id int IDENTITY PRIMARY KEY, name nvarchar(50) NOT NULL, " +
                             "tag nvarchar(50) NOT NULL DEFAULT N'tagged', note nvarchar(50) NULL DEFAULT N'noted'); " +
                             $"INSERT dbo.{table} (name) VALUES (N'alpha');");
        try
        {
            // Two statements: the page will not re-run them after Apply.
            await s.ExecuteAsync($"SELECT id, name, tag, note FROM dbo.{table}; SELECT 1 AS other;");
            var first = s.Page.Locator("[data-testid='results-set-0']");
            await first.Locator("[data-testid='results-add-row']").ClickAsync();
            await first.Locator("[data-testid='results-insert-cell-0-1']").ClickAsync();
            await s.Page.Keyboard.TypeAsync("bravo");
            await s.Page.Keyboard.PressAsync("Enter");
            await first.Locator("[data-testid='results-apply-0']").ClickAsync();
            await Assertions.Expect(first.Locator("[data-testid='results-applybar']")).ToHaveCountAsync(0, new() { Timeout = 30_000 });

            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-0']")).ToHaveTextAsync("2");
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-2']")).ToHaveTextAsync("tagged");
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-3']")).ToHaveTextAsync("noted");

            // The new row has its key: edit it without running anything again.
            await first.Locator("[data-testid='results-cell-1-1']").ClickAsync();
            await s.Page.Keyboard.TypeAsync("charlie");
            await s.Page.Keyboard.PressAsync("Enter");
            await first.Locator("[data-testid='results-apply-0']").ClickAsync();
            await Assertions.Expect(first.Locator("[data-testid='results-applybar']")).ToHaveCountAsync(0, new() { Timeout = 30_000 });

            await s.ExecuteAsync($"SELECT name FROM dbo.{table} WHERE id = 2;");
            await Assertions.Expect(Cell(s.Page, 0, 0)).ToHaveTextAsync("charlie");
        }
        finally
        {
            await DropAsync(s, table);
        }
    }

    /// <summary>Applies the pending edits of the set and waits for the apply bar to close. (The Apply button's test id has a
    /// "-n" suffix only when result sets are stacked.)</summary>
    private static async Task ApplyFirstSetAsync(ILocator set)
    {
        await set.Locator("button[data-testid^='results-apply']").ClickAsync();
        await Assertions.Expect(set.Locator("[data-testid='results-applybar']")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
    }

    private static async Task TypeIntoAsync(IPage page, ILocator cell, string value)
    {
        await cell.ClickAsync();
        await page.Keyboard.TypeAsync(value);
        await page.Keyboard.PressAsync("Enter");
    }

    /// <summary>Review round 3 (REV-06) through the real grid: a table with an AFTER INSERT trigger that changes the row, loaded
    /// by a batch with a side effect of its own. After Apply the new row shows its identity, its default and the trigger's value,
    /// the batch is not run again, and the next edit and delete find the rows by their keys.</summary>
    [SkippableFact]
    public async Task A_triggered_insert_from_a_batch_shows_the_stored_row_and_the_next_edit_and_delete_find_it()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string table = "akml_e2e_trg_" + suffix, log = "akml_e2e_trglog_" + suffix;
        await s.ExecuteAsync(
            $"CREATE TABLE dbo.{log} (n int NOT NULL); " +
            $"CREATE TABLE dbo.{table} (id int IDENTITY PRIMARY KEY, name nvarchar(50) NOT NULL, " +
            "tag nvarchar(50) NOT NULL DEFAULT N'tagged', stamp nvarchar(80) NULL); " +
            $"INSERT dbo.{table} (name) VALUES (N'alpha');");
        await s.ExecuteAsync(
            $"CREATE TRIGGER dbo.trg_{table} ON dbo.{table} AFTER INSERT AS BEGIN SET NOCOUNT ON; " +
            $"UPDATE x SET stamp = N'by trigger: ' + x.name FROM dbo.{table} AS x JOIN inserted AS i ON i.id = x.id; END;");
        try
        {
            // A batch that writes before it reads: running it again after Apply would log a second row.
            await s.ExecuteAsync($"INSERT dbo.{log} (n) VALUES (1); SELECT id, name, tag, stamp FROM dbo.{table};");
            var first = s.Page.Locator("[data-testid='results-set-0']");
            await first.Locator("[data-testid='results-add-row']").ClickAsync();
            await TypeIntoAsync(s.Page, first.Locator("[data-testid='results-insert-cell-0-1']"), "bravo");
            await ApplyFirstSetAsync(first);

            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-0']")).ToHaveTextAsync("2");
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-1']")).ToHaveTextAsync("bravo");
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-2']")).ToHaveTextAsync("tagged");
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-3']")).ToHaveTextAsync("by trigger: bravo");
            await Assertions.Expect(first.Locator("[data-testid^='results-unconfirmed-']")).ToHaveCountAsync(0);

            await TypeIntoAsync(s.Page, first.Locator("[data-testid='results-cell-1-1']"), "charlie");
            await ApplyFirstSetAsync(first);
            await first.Locator("[data-testid='results-delete-0']").ClickAsync();
            await ApplyFirstSetAsync(first);

            await s.ExecuteAsync($"SELECT (SELECT COUNT(*) FROM dbo.{log}) AS runs, id, name, stamp FROM dbo.{table};");
            await Assertions.Expect(Cell(s.Page, 0, 0)).ToHaveTextAsync("1");                    // the batch ran once
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("2");
            await Assertions.Expect(Cell(s.Page, 0, 2)).ToHaveTextAsync("charlie");
            await Assertions.Expect(Cell(s.Page, 0, 3)).ToHaveTextAsync("by trigger: bravo");
            await Assertions.Expect(Cell(s.Page, 1, 0)).ToHaveCountAsync(0);                      // alpha is gone
        }
        finally
        {
            await DropAsync(s, table);
            await s.ExecuteAsync($"DROP TABLE IF EXISTS dbo.{log};");
        }
    }

    /// <summary>Review round 3 (REV-09) through the real grid: the session is moved to master, the table is in tempdb by its three-part
    /// name, has an enabled trigger and a server-generated uniqueidentifier key. The insert applies, shows the generated key and
    /// the default, and the next edit and delete reach the right rows.</summary>
    [SkippableFact]
    public async Task A_three_part_name_insert_into_a_triggered_table_with_a_generated_key_applies_from_master()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_xdb_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await s.ExecuteAsync(
            $"CREATE TABLE tempdb.dbo.{table} (id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY, " +
            "name nvarchar(50) NOT NULL, tag nvarchar(50) NOT NULL DEFAULT N'tagged'); " +
            $"INSERT tempdb.dbo.{table} (name) VALUES (N'alpha'); " +
            $"EXEC tempdb.sys.sp_executesql N'CREATE TRIGGER dbo.trg_{table} ON dbo.{table} AFTER INSERT AS SET NOCOUNT ON;';");
        try
        {
            // USE moves the session to master; the result's table stays tempdb's, by its three-part name.
            await s.ExecuteAsync($"USE master; SET NOCOUNT ON; SELECT id, name, tag FROM tempdb.dbo.{table};");
            var first = s.Page.Locator("[data-testid='results-set-0']");
            await first.Locator("[data-testid='results-add-row']").ClickAsync();
            await TypeIntoAsync(s.Page, first.Locator("[data-testid='results-insert-cell-0-1']"), "bravo");
            await ApplyFirstSetAsync(first);

            await Assertions.Expect(first.Locator("[data-testid='results-apply-message']")).Not.ToContainTextAsync("failed");
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-0']"))
                .ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$"));
            await Assertions.Expect(first.Locator("[data-testid='results-cell-1-2']")).ToHaveTextAsync("tagged");
            await Assertions.Expect(first.Locator("[data-testid^='results-unconfirmed-']")).ToHaveCountAsync(0);

            await TypeIntoAsync(s.Page, first.Locator("[data-testid='results-cell-1-1']"), "charlie");
            await ApplyFirstSetAsync(first);
            await first.Locator("[data-testid='results-delete-0']").ClickAsync();
            await ApplyFirstSetAsync(first);

            await s.ExecuteAsync($"SELECT DB_NAME() AS db, name, tag FROM tempdb.dbo.{table};");
            await Assertions.Expect(Cell(s.Page, 0, 0)).ToHaveTextAsync("master");
            await Assertions.Expect(Cell(s.Page, 0, 1)).ToHaveTextAsync("charlie");
            await Assertions.Expect(Cell(s.Page, 0, 2)).ToHaveTextAsync("tagged");
            await Assertions.Expect(Cell(s.Page, 1, 0)).ToHaveCountAsync(0);
        }
        finally
        {
            try
            {
                if (await Id(s.Page, "dialog-cancel").CountAsync() > 0) await Id(s.Page, "dialog-cancel").ClickAsync();
                await s.ExecuteAsync($"DROP TABLE IF EXISTS tempdb.dbo.{table};");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"cleanup of {table} failed: {ex.Message}");
            }
        }
    }
}
