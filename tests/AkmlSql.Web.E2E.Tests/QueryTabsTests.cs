using System.Diagnostics;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 043 (T023, US1, quickstart §1 "Browser: tabs" and "Browser: overlap") — query tabs as SSMS has them: each tab its own
/// connection, text and results; a switch shows the tab's connection and results at once; temporary tables stay in their tab;
/// runs in two tabs overlap; a reload brings every tab back; a modified tab asks before it closes; the tab keys; a tab with no
/// connection asks for one and then runs. Needs Debug builds of AkmlSql.Web and AkmlSql.Engine and a local SQL Server.
/// The review of US1 added: Delete on the focused tab, Save from the close question, the tab commands and their keys,
/// History per tab, closing a tab while it runs, and — with a PostgreSQL test server (AKML_PG_TEST_CONNECTION, see
/// scripts/pg-test-server.ps1) — a PostgreSQL tab beside a SQL Server tab and a restored tab whose password is asked once.
/// Not automated here (manual, quickstart §2): the SQL-auth password of M4 (needs a SQL login), the engine restart.
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class QueryTabsTests
{
    private readonly ITestOutputHelper _output;

    public QueryTabsTests(ITestOutputHelper output) => _output = output;

    private static ILocator Tabs(IPage page) => page.GetByTestId("doc-tab");
    private static ILocator ActiveTab(IPage page) => page.Locator("[data-testid='doc-tab'][data-active='true']");
    private static ILocator Pane(IPage page) => page.GetByTestId("results-pane");
    private static ILocator Cell(IPage page, int row, int column) => Pane(page).GetByTestId($"results-cell-{row}-{column}");
    private static ILocator Editor(IPage page) => page.Locator("[data-testid='sql-editor'] .cm-content");

    /// <summary>New query, and wait until the new tab has reconnected on the connection it took (Windows authentication).</summary>
    private static async Task NewTabAsync(SqlSession s, string database = "tempdb")
    {
        int before = await Tabs(s.Page).CountAsync();
        await s.Page.GetByTestId("doc-new").ClickAsync();
        await Assertions.Expect(Tabs(s.Page)).ToHaveCountAsync(before + 1);
        await Assertions.Expect(s.Page.GetByTestId("status-connection")).ToContainTextAsync($"localhost/{database}", new() { Timeout = 30_000 });
    }

    [SkippableFact]
    public async Task Each_tab_keeps_its_own_connection_text_and_results_and_a_switch_shows_them()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);   // tab 1 on localhost/tempdb
        var page = s.Page;
        await s.ExecuteAsync("SELECT DB_NAME() AS db;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("tempdb");

        await NewTabAsync(s);                                  // FR-002: the new tab starts on tab 1's connection
        await WebAppFixture.ConnectAsync(page, "master");      // FR-006: only tab 2 moves
        await s.ExecuteAsync("SELECT DB_NAME() AS db;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("master");

        var watch = Stopwatch.StartNew();
        await Tabs(page).First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/tempdb");
        await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync("tempdb");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("tempdb");
        await Assertions.Expect(Editor(page)).ToContainTextAsync("SELECT DB_NAME() AS db;");
        _output.WriteLine($"SC-003: tab switch shown in {watch.ElapsedMilliseconds} ms (click and three checks included).");
        Assert.True(watch.ElapsedMilliseconds < 2_000, $"The switch took {watch.ElapsedMilliseconds} ms.");
        // FR-005: the Schema panel follows the tab shown (tab 2's move to master left tab 1's alone).
        await Assertions.Expect(page.GetByTestId("schema-header")).ToContainTextAsync("tempdb", new() { Timeout = 1_000 });

        await Tabs(page).Nth(1).ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/master");
        await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync("master");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("master");
        await Assertions.Expect(page.GetByTestId("schema-header")).ToContainTextAsync("master", new() { Timeout = 1_000 });
    }

    /// <summary>T023: 30 runs across three tabs each land in their own tab's pane, and each tab shows its own last result.</summary>
    [SkippableFact]
    public async Task Thirty_runs_across_three_tabs_each_land_in_their_own_pane()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await NewTabAsync(s);
        await NewTabAsync(s);
        for (int i = 0; i < 10; i++)
        {
            for (int t = 0; t < 3; t++)
            {
                await Tabs(page).Nth(t).ClickAsync();
                int value = (t + 1) * 100 + i;
                await s.ExecuteAsync($"SELECT {value} AS v;");
                await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync(value.ToString());
            }
        }
        for (int t = 0; t < 3; t++)
        {
            await Tabs(page).Nth(t).ClickAsync();
            int last = (t + 1) * 100 + 9;
            await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync(last.ToString());
            await Assertions.Expect(Editor(page)).ToContainTextAsync($"SELECT {last} AS v;");
        }
    }

    [SkippableFact]
    public async Task A_temporary_table_lives_only_in_the_tab_that_made_it()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.ExecuteAsync("CREATE TABLE #t043 (i int); INSERT #t043 VALUES (1); SELECT i FROM #t043;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("1");

        await NewTabAsync(s);
        await s.ExecuteAsync("SELECT i FROM #t043;");   // FR-004: its own connection

        await Assertions.Expect(page.GetByTestId("execute-complete")).Not.ToHaveAttributeAsync("data-status", "0");
        await page.GetByTestId("results-tab-messages").ClickAsync();
        await Assertions.Expect(Pane(page)).ToContainTextAsync("#t043");
    }

    [SkippableFact]
    public async Task Overlapping_runs_show_their_own_state()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.SetEditorTextAsync("WAITFOR DELAY '00:00:05'; SELECT 1 AS slow;");
        await page.GetByTestId("execute-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("execute-running")).ToHaveCountAsync(1);

        await NewTabAsync(s);
        await Assertions.Expect(page.GetByTestId("execute-running")).ToHaveCountAsync(0);   // tab 2 is not running
        var watch = Stopwatch.StartNew();
        await s.ExecuteAsync("SELECT 2 AS fast;");
        _output.WriteLine($"FR-014: tab 2's run finished in {watch.ElapsedMilliseconds} ms while tab 1 waited.");
        Assert.True(watch.ElapsedMilliseconds < 4_000, "Tab 2's run waited for tab 1's.");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("2");
        await Assertions.Expect(page.GetByTestId("results-pane-inactive")).ToHaveAttributeAsync("aria-busy", "true");

        await Tabs(page).First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("execute-running")).ToHaveCountAsync(1);
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("1", new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("execute-running")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task A_reload_brings_back_every_tab_with_its_name_text_mark_and_connection()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.SetEditorTextAsync("SELECT 'one' AS t;");
        foreach (var text in new[] { "two", "three", "four", "five" })
        {
            await NewTabAsync(s);
            await s.SetEditorTextAsync($"SELECT '{text}' AS t;");
        }
        await Tabs(page).Nth(1).ClickAsync();
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery2");
        await page.WaitForTimeoutAsync(1_200);   // the tab records' 500 ms debounce

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });

        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(5);
        var names = (await page.Locator("[data-testid='doc-tab'] .akml-tab-label").AllInnerTextsAsync()).Select(n => n.Trim()).ToArray();
        Assert.Equal(new[] { "SQLQuery1", "SQLQuery2", "SQLQuery3", "SQLQuery4", "SQLQuery5" }, names);
        for (int i = 0; i < 5; i++) await Assertions.Expect(Tabs(page).Nth(i)).ToHaveAttributeAsync("data-modified", "true");
        await Assertions.Expect(ActiveTab(page)).ToContainTextAsync("SQLQuery2");
        await Assertions.Expect(Editor(page)).ToContainTextAsync("SELECT 'two' AS t;");
        // FR-009: the active tab connects again by itself (Windows authentication needs no password).
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/tempdb", new() { Timeout = 30_000 });

        var before = await s.ExecuteVersionAsync();
        await page.GetByTestId("execute-button").ClickAsync();
        await s.WaitForExecuteAfterAsync(before);
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("two");

        await Tabs(page).Nth(4).ClickAsync();
        await Assertions.Expect(Editor(page)).ToContainTextAsync("SELECT 'five' AS t;");
        // Its binding came back too: shown, it connects again by itself.
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/tempdb", new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Edge case "the engine is lost while tabs are connected" (data-model §3): after an engine restart, a tab's run finds that the
    /// engine no longer has its connection and says so; its next run connects it again with nothing typed — in each tab, each
    /// to its own database.
    /// </summary>
    [SkippableFact]
    public async Task After_an_engine_restart_each_tab_reconnects_on_its_next_run_with_nothing_typed()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);   // tab 1 on localhost/tempdb
        var page = s.Page;
        await NewTabAsync(s);
        await WebAppFixture.ConnectAsync(page, "master");   // tab 2 on localhost/master

        await s.Engine.RestartAsync();
        await Assertions.Expect(page.Locator("[data-testid='status-pill']")).ToContainTextAsync("Live", new() { Timeout = 60_000 });

        foreach (var (tab, database) in new[] { (1, "master"), (0, "tempdb") })
        {
            await Tabs(page).Nth(tab).ClickAsync();
            await s.SetEditorTextAsync("SELECT DB_NAME() AS db;");
            for (int attempt = 0; attempt < 2; attempt++)   // the first run may only find out that the engine forgot the session
            {
                var before = await s.ExecuteVersionAsync();
                await page.GetByTestId("execute-button").ClickAsync();
                await s.WaitForExecuteAfterAsync(before);
                if (await Cell(page, 0, 0).CountAsync() > 0 && (await Cell(page, 0, 0).InnerTextAsync()).Trim() == database) break;
                _output.WriteLine($"Tab {tab + 1}, run {attempt + 1}: the engine no longer had the connection.");
            }
            await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync(database);
            await Assertions.Expect(page.GetByTestId("connection-manager")).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync($"localhost/{database}");
        }
    }

    [SkippableFact]
    public async Task Closing_a_modified_tab_asks_to_save_discard_or_cancel()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await NewTabAsync(s);
        await s.SetEditorTextAsync("SELECT 'unsaved' AS t;");
        var close = ActiveTab(page).GetByTestId("doc-tab-close");

        await close.ClickAsync();
        await Assertions.Expect(page.Locator("[data-dialog-id='doc-close-dialog']")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("dialog-message")).ToHaveTextAsync("Save changes to SQLQuery2?");
        await page.GetByTestId("doc-close-cancel").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);
        await Assertions.Expect(Editor(page)).ToContainTextAsync("SELECT 'unsaved' AS t;");

        await close.ClickAsync();
        await page.GetByTestId("doc-close-discard").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");   // the tab before it, as SSMS
    }

    [SkippableFact]
    public async Task The_tab_keys_move_between_tabs_and_close_one()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await NewTabAsync(s);
        await Editor(page).ClickAsync();

        await page.Keyboard.PressAsync("Control+Alt+PageUp");
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");
        await page.Keyboard.PressAsync("Control+Alt+PageDown");
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery2");
        await page.Keyboard.PressAsync("Control+Alt+W");   // unmodified: it closes at once
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);

        // The strip's own keys: the arrows move and show; Delete closes the focused tab.
        await NewTabAsync(s);
        await ActiveTab(page).FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");
        await page.Keyboard.PressAsync("Delete");
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery3");
    }

    // ── Review of US1 (finding 25; gaps G4 to G9) ────────────────────────────────────────────────────────────────

    /// <summary>Finding 25: Delete closes the tab that has focus, and focus moves to the tab now active, so the next Delete
    /// closes that one — never a tab the user did not point at.</summary>
    [SkippableFact]
    public async Task Delete_in_the_strip_closes_the_focused_tab_and_hands_focus_to_the_active_one()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await NewTabAsync(s);
        await NewTabAsync(s);   // SQLQuery1, SQLQuery2, SQLQuery3
        await Tabs(page).Nth(1).ClickAsync();
        await ActiveTab(page).FocusAsync();

        await page.Keyboard.PressAsync("Delete");   // SQLQuery2 closes; the tab before it is active
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");
        var active = await ActiveTab(page).GetAttributeAsync("data-tab-id");
        await page.WaitForFunctionAsync("id => document.activeElement?.getAttribute('data-tab-id') === id", active);

        await page.Keyboard.PressAsync("Delete");   // the focused tab: SQLQuery1
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery3");
    }

    /// <summary>US1 scenario 4, FR-007 (gap G7): Save in the close question saves first; a cancelled name prompt keeps the tab.</summary>
    [SkippableFact]
    public async Task Closing_a_modified_tab_with_Save_closes_it_only_after_the_save()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await NewTabAsync(s);
        await s.SetEditorTextAsync("SELECT 'save me' AS t;");
        var close = ActiveTab(page).GetByTestId("doc-tab-close");

        await close.ClickAsync();
        await page.GetByTestId("doc-close-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-input")).ToBeVisibleAsync();   // a default name: the file name is asked
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);                         // not saved, so not closed
        await Assertions.Expect(page.GetByTestId("doc-modified")).ToHaveCountAsync(1);
        await Assertions.Expect(Editor(page)).ToContainTextAsync("SELECT 'save me' AS t;");

        await close.ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("doc-close-save").ClickAsync();
            await page.GetByTestId("dialog-input").FillAsync("close-save-043");
            await page.GetByTestId("dialog-confirm").ClickAsync();
        });
        Assert.Equal("close-save-043.sql", download.SuggestedFilename);
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");
    }

    /// <summary>FR-012 (gap G8): the palette offers the three tab commands, and a tab's keyboard help lists the tab keys.</summary>
    [SkippableFact]
    public async Task The_palette_offers_the_tab_commands_and_a_tab_help_lists_their_keys()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        // The full palette (Ctrl+P): Close is a File command, which command mode (Ctrl+Shift+P) leaves out by design.
        await page.Keyboard.PressAsync("Control+P");
        await page.GetByTestId("command-palette-input").FillAsync("query tab");
        var rows = page.GetByTestId("command-palette-row");
        foreach (var title in new[] { "Close query tab", "Next query tab", "Previous query tab" })
        {
            await Assertions.Expect(rows.Filter(new() { HasTextString = title })).ToHaveCountAsync(1);
        }
        await page.Keyboard.PressAsync("Escape");

        var help = await ActiveTab(page).GetAttributeAsync("title") ?? string.Empty;
        Assert.Contains("Next / previous tab:", help);
        Assert.Contains("PageDown", help);
        Assert.Contains("PageUp", help);
        Assert.Contains("Close:", help);
        Assert.Contains("W or Delete", help);
    }

    /// <summary>FR-010, US1 scenario 10 (gap G5): History keeps each tab's runs under that tab's own query session, also a run
    /// that finishes after another tab is shown. Were the late run recorded under the tab shown, History would hold one entry.</summary>
    [SkippableFact]
    public async Task History_keeps_each_tab_runs_under_its_own_query_session()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.SetEditorTextAsync("WAITFOR DELAY '00:00:03'; SELECT 'late in tab one' AS a;");
        await page.GetByTestId("execute-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("execute-running")).ToHaveCountAsync(1);

        await NewTabAsync(s);   // tab 1's run finishes while tab 2 is shown
        await s.ExecuteAsync("SELECT 'early in tab two' AS b;");
        await s.ExecuteAsync("SELECT 'again in tab two' AS b;");
        await Tabs(page).First.ClickAsync();
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("late in tab one", new() { Timeout = 30_000 });

        await page.GetByTestId("nav-history").ClickAsync();
        var rows = page.GetByTestId("history-row");
        await Assertions.Expect(rows).ToHaveCountAsync(2, new() { Timeout = 30_000 });
        await Assertions.Expect(rows.Filter(new() { HasTextString = "WAITFOR" })).ToHaveCountAsync(1);
        await Assertions.Expect(rows.Filter(new() { HasTextString = "in tab two" })).ToHaveCountAsync(1);
    }

    /// <summary>Edge case "closing a tab whose query is running" (gap G9): the app asks; the tab closes; the other tab keeps its
    /// results, never shows the closed tab's, and runs as before.</summary>
    [SkippableFact]
    public async Task Closing_a_tab_while_its_query_runs_asks_first_and_leaves_the_other_tab_alone()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.ExecuteAsync("SELECT 'tab one' AS t;");
        await NewTabAsync(s);
        await s.SetEditorTextAsync("WAITFOR DELAY '00:00:03'; SELECT 'closed tab' AS t;");
        await page.GetByTestId("execute-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("execute-running")).ToHaveCountAsync(1);

        await ActiveTab(page).GetByTestId("doc-tab-close").ClickAsync();
        await Assertions.Expect(page.Locator("[data-dialog-id='doc-close-running']")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-confirm").ClickAsync();
        await page.GetByTestId("doc-close-discard").ClickAsync();   // its text was never saved

        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("tab one");
        await page.WaitForTimeoutAsync(4_000);   // the closed tab's reply has come and gone
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("tab one");
        await s.ExecuteAsync("SELECT 'still mine' AS t;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("still mine");
    }

    /// <summary>
    /// FR-007, quickstart M6 (gap G1): closing a SQL Server tab with an open transaction asks first — the server is asked when the
    /// tab closes, as SSMS asks — and Cancel keeps the tab and its transaction; "Roll back and close" rolls it back.
    /// </summary>
    [SkippableFact]
    public async Task Closing_a_SQL_Server_tab_with_an_open_transaction_asks_first_and_rolls_it_back()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.ExecuteAsync("IF OBJECT_ID('tempdb.dbo.akml_043_tx') IS NOT NULL DROP TABLE tempdb.dbo.akml_043_tx; CREATE TABLE tempdb.dbo.akml_043_tx (v int); INSERT tempdb.dbo.akml_043_tx VALUES (1); SELECT v FROM tempdb.dbo.akml_043_tx;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("1");
        await NewTabAsync(s);
        await s.ExecuteAsync("BEGIN TRAN; UPDATE tempdb.dbo.akml_043_tx SET v = 2; SELECT @@TRANCOUNT AS open_transactions;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("1");

        var close = ActiveTab(page).GetByTestId("doc-tab-close");
        await close.ClickAsync();
        await Assertions.Expect(page.Locator("[data-dialog-id='doc-close-transaction']")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);   // kept, with its transaction

        await close.ClickAsync();
        await Assertions.Expect(page.Locator("[data-dialog-id='doc-close-transaction']")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByTestId("dialog-confirm").ClickAsync();   // Roll back and close
        await page.GetByTestId("doc-close-discard").ClickAsync();   // then its text, never saved (FR-007: the transaction is asked first)
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(1);

        await s.ExecuteAsync("SELECT v FROM tempdb.dbo.akml_043_tx; DROP TABLE tempdb.dbo.akml_043_tx;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("1", new() { Timeout = 30_000 });   // rolled back, never committed
    }

    private static async Task ConnectSqlServerAsync(IPage page, string database)
    {
        await page.ClickAsync("[data-testid='status-connection']");
        await page.WaitForSelectorAsync("[data-testid='connection-manager']");
        await page.ClickAsync("[data-testid='conn-kind-sqlserver']");
        await page.FillAsync("[data-testid='conn-server-input']", "localhost");
        await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
        await page.CheckAsync("[data-testid='conn-auth-windows']");
        await page.ClickAsync("[data-testid='conn-database-refresh']");
        await Assertions.Expect(page.Locator($"[data-testid='conn-database-select'] option[value='{database}']")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await page.SelectOptionAsync("[data-testid='conn-database-select']", database);
        await page.ClickAsync("[data-testid='conn-connect-btn']");
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync($"localhost/{database}", new() { Timeout = 30_000 });
    }

    /// <summary>US1 scenario 3, FR-005 (gap G6): a PostgreSQL tab beside a SQL Server tab. A switch shows each tab's kind and
    /// connection, and the Schema panel follows within a second; each tab runs in its own dialect.</summary>
    [SkippableFact]
    public async Task A_PostgreSQL_tab_and_a_SQL_Server_tab_each_show_their_own_and_the_schema_follows()
    {
        Skip.IfNot(await SandboxEngine.LocalSqlServerAnswersAsync(), "No local SQL Server answering on localhost:1433.");
        await using var s = await PgWebSession.StartOrSkipAsync(_output);   // tab 1 on PostgreSQL
        var page = s.Page;
        var schema = page.GetByTestId("schema-header");
        await Assertions.Expect(schema).ToContainTextAsync("postgres", new() { Timeout = 30_000 });

        await page.GetByTestId("doc-new").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);
        await ConnectSqlServerAsync(page, "tempdb");   // tab 2 only (FR-006)
        await Assertions.Expect(page.GetByTestId("status-kind")).ToContainTextAsync("SQL Server");
        await Assertions.Expect(schema).ToContainTextAsync("tempdb", new() { Timeout = 30_000 });

        var watch = Stopwatch.StartNew();
        await Tabs(page).First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-kind")).ToContainTextAsync("PostgreSQL", new() { Timeout = 1_000 });
        await Assertions.Expect(schema).ToContainTextAsync("postgres", new() { Timeout = 1_000 });
        _output.WriteLine($"FR-005: PostgreSQL tab and its Schema shown in {watch.ElapsedMilliseconds} ms.");

        watch.Restart();
        await Tabs(page).Nth(1).ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-kind")).ToContainTextAsync("SQL Server", new() { Timeout = 1_000 });
        await Assertions.Expect(schema).ToContainTextAsync("tempdb", new() { Timeout = 1_000 });
        _output.WriteLine($"FR-005: SQL Server tab and its Schema shown in {watch.ElapsedMilliseconds} ms.");

        await s.ExecuteAsync("SELECT DB_NAME() AS db;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("tempdb");
        await Tabs(page).First.ClickAsync();
        await s.ExecuteAsync("SELECT current_database() AS db;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("postgres");
    }

    /// <summary>US1 scenario 6, FR-008, FR-009 (gap G4): a restored tab whose password is not remembered connects only when it
    /// runs, through the Connect to database dialog filled in with its connection (only the password to type); then the run
    /// goes on, and neither the next run nor a switch away and back asks again.</summary>
    [SkippableFact]
    public async Task A_restored_tab_whose_password_is_not_remembered_asks_once_then_runs()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);   // connected with a typed password, not remembered
        var page = s.Page;
        var (host, port, user, password) = PgWebSession.Configured()!.Value;
        await s.SetEditorTextAsync("SELECT 'asked once' AS t;");
        await page.WaitForTimeoutAsync(1_200);   // the tab records' 500 ms debounce

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("connection-manager")).ToHaveCountAsync(0);   // nothing is asked before a run

        var before = await s.ExecuteVersionAsync();
        await page.GetByTestId("execute-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("connection-manager")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("conn-kind-postgresql")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.GetByTestId("conn-pg-host")).ToHaveValueAsync(host);
        await Assertions.Expect(page.GetByTestId("conn-pg-port")).ToHaveValueAsync(port.ToString());
        await Assertions.Expect(page.GetByTestId("conn-pg-user")).ToHaveValueAsync(user);
        await Assertions.Expect(page.GetByTestId("conn-pg-database")).ToHaveValueAsync("postgres");
        await Assertions.Expect(page.GetByTestId("conn-pg-password")).ToHaveValueAsync(string.Empty);
        await page.FillAsync("[data-testid='conn-pg-password']", password);
        await page.ClickAsync("[data-testid='conn-connect-btn']");

        await s.WaitForExecuteAfterAsync(before);   // the run that asked goes on
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("asked once");

        await s.ExecuteAsync("SELECT 'not asked again' AS t;");
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("not asked again");
        await page.GetByTestId("doc-new").ClickAsync();
        await Assertions.Expect(Tabs(page)).ToHaveCountAsync(2);
        await Tabs(page).First.ClickAsync();
        await s.ExecuteAsync("SELECT 'still not asked' AS t;");
        await Assertions.Expect(page.GetByTestId("connection-manager")).ToHaveCountAsync(0);
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("still not asked");
    }

    [SkippableFact]
    public async Task Running_a_tab_with_no_connection_asks_for_one_and_then_runs()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        // Settings › Editor › Reset editor session leaves one empty tab with no connection.
        await page.ClickAsync("nav >> text=Settings");
        await page.GetByTestId("settings-nav-editor").ClickAsync();
        await page.Locator("#setting-editor-reset-session-control").ClickAsync();
        await page.GetByTestId("dialog-confirm").ClickAsync();
        await page.ClickAsync("nav >> text=Editor");
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("Connect");

        await s.SetEditorTextAsync("SELECT 'after connect' AS t;");
        var before = await s.ExecuteVersionAsync();
        await page.GetByTestId("execute-button").ClickAsync();

        // US1 scenario 7: the Connect to database dialog opens; after a successful connect the query runs.
        await page.WaitForSelectorAsync("[data-testid='connection-manager']");
        await page.FillAsync("[data-testid='conn-server-input']", "localhost");
        await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
        await page.CheckAsync("[data-testid='conn-auth-windows']");
        await page.ClickAsync("[data-testid='conn-database-refresh']");
        await Assertions.Expect(page.Locator("[data-testid='conn-database-select'] option[value='tempdb']")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await page.SelectOptionAsync("[data-testid='conn-database-select']", "tempdb");
        await page.ClickAsync("[data-testid='conn-connect-btn']");

        await s.WaitForExecuteAfterAsync(before);
        await Assertions.Expect(Cell(page, 0, 0)).ToHaveTextAsync("after connect");
        await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/tempdb");
    }
}
