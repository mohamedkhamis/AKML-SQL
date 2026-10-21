using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 041 (T088, research R16–R24, R70) — quickstart §2, "Results and Messages", in a real browser against
/// the local SQL Server: the Results / Messages tabs and their choice after a run, SSMS-worded messages with
/// click-to-line, GO batches and loops, the timeout path, the running state, F5 wherever the focus is, Parse,
/// and values shown as SSMS shows them.
///
/// <para><c>BridgeE2E</c>, <see cref="SkippableFactAttribute"/>: skipped without a local SQL Server, DEBUG builds
/// of AkmlSql.Web and AkmlSql.Engine, or Playwright Chromium. Run serially
/// (<c>-- xUnit.ParallelizeTestCollections=false</c>).</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class ResultsMessagesTests
{
    private readonly ITestOutputHelper _output;

    public ResultsMessagesTests(ITestOutputHelper output) => _output = output;

    private static ILocator Tab(IPage page, string name) => page.Locator($"[data-testid='results-tab-{name}']");

    private static async Task<string[]> MessageLinesAsync(IPage page) =>
        (await page.Locator("[data-testid^='messages-line-']").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray();

    private static Task<string> ActiveEditorLineAsync(IPage page) =>
        page.Locator("[data-testid='sql-editor'] .cm-activeLine").InnerTextAsync();

    [SkippableFact]
    public async Task A_SELECT_shows_Results_and_the_messages_end_with_the_completion_time()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT TOP (3) name FROM sys.objects ORDER BY name;");

        await Assertions.Expect(Tab(s.Page, "results")).ToHaveAttributeAsync("aria-selected", "true");
        await Tab(s.Page, "messages").ClickAsync();
        Assert.Contains("(3 rows affected)", await MessageLinesAsync(s.Page));
        await Assertions.Expect(s.Page.Locator("[data-testid='messages-completion']")).ToContainTextAsync("Completion time: ");
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query executed successfully.");
    }

    [SkippableFact]
    public async Task Two_SELECTs_a_PRINT_and_an_UPDATE_stack_two_grids_and_list_every_message_in_order()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("CREATE TABLE #m (n int); INSERT #m VALUES (1),(2);\nSELECT n FROM #m;\nPRINT 'step 1 done';\nUPDATE #m SET n = n + 1;\nSELECT n FROM #m WHERE n > 2;");

        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-1']")).ToBeVisibleAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-header-0']")).ToHaveTextAsync("Result 1 (2 rows)");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-header-1']")).ToHaveTextAsync("Result 2 (1 row)");
        await Tab(s.Page, "messages").ClickAsync();
        Assert.Equal(new[] { "(2 rows affected)", "(2 rows affected)", "step 1 done", "(2 rows affected)", "(1 row affected)" }, await MessageLinesAsync(s.Page));
    }

    [SkippableFact]
    public async Task An_error_opens_Messages_and_clicking_it_goes_to_its_document_line()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT 1 AS a;\nGO\nSELECT 2 AS b;\nSELECT * FROM dbo.akml_no_such_041;\nSELECT 3 AS c;");

        await Assertions.Expect(Tab(s.Page, "messages")).ToHaveAttributeAsync("aria-selected", "true");
        var error = s.Page.Locator("[data-testid^='messages-error-']").First;
        await Assertions.Expect(error).ToContainTextAsync("Msg 208, Level 16, State 1, Line 2");   // line 2 of batch 2
        await error.ClickAsync();

        // Batch 2 starts on document line 3: its line 2 is document line 4.
        Assert.Equal("SELECT * FROM dbo.akml_no_such_041;", (await ActiveEditorLineAsync(s.Page)).Trim());
        await Assertions.Expect(s.Page.Locator("[data-testid='sql-editor'] .cm-lintRange-error")).ToHaveCountAsync(1);
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query completed with errors.");
    }

    [SkippableFact]
    public async Task A_divide_by_zero_keeps_the_sets_around_it()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT 1 AS a;\nSELECT 1/0 AS b;\nSELECT 2 AS c;");

        await Tab(s.Page, "results").ClickAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-2']")).ToBeVisibleAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-header-1']")).ToHaveTextAsync("Result 2 (0 rows)");
        await Tab(s.Page, "messages").ClickAsync();
        Assert.Contains(await MessageLinesAsync(s.Page), l => l.StartsWith("Msg 8134, Level 16, State 1, Line 2", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task GO_loops_print_SSMS_lines_and_a_bad_GO_line_refuses_the_script()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT 1 AS n;\nGO 3");

        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-2']")).ToBeVisibleAsync();
        await Tab(s.Page, "messages").ClickAsync();
        var lines = await MessageLinesAsync(s.Page);
        Assert.Equal("Beginning execution loop", lines.First());
        Assert.Equal("Batch execution completed 3 times.", lines.Last());

        await s.ExecuteAsync("SELECT 1;\nGO SELECT 2");
        await Assertions.Expect(Tab(s.Page, "messages")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Contains(await MessageLinesAsync(s.Page), l => l.Contains("Incorrect syntax was encountered while parsing GO.", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task A_CREATE_PROCEDURE_batch_then_EXEC_runs()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync("CREATE PROCEDURE #akml_e2e_p AS SELECT 1 AS n;\nGO\nEXEC #akml_e2e_p;\nGO\nDROP PROCEDURE #akml_e2e_p;");

        await Assertions.Expect(Tab(s.Page, "results")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(s.Page.Locator("[data-testid^='results-set-header-']")).ToHaveCountAsync(1);
    }

    [SkippableFact]
    public async Task While_running_Execute_is_not_offered_and_a_timeout_skips_the_rest()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.Page.FillAsync("[data-testid='caps-timeout']", "2");
        await s.Page.Locator("[data-testid='caps-timeout']").PressAsync("Tab");

        await s.SetEditorTextAsync("WAITFOR DELAY '00:00:05';\nGO\nSELECT 1 AS never;");
        var before = await s.ExecuteVersionAsync();
        await s.Page.ClickAsync("[data-testid='execute-button']");

        await Assertions.Expect(s.Page.Locator("[data-testid='execute-running']")).ToHaveCountAsync(1);
        await Assertions.Expect(s.Page.Locator("[data-testid='results-running']")).ToContainTextAsync("A running statement can't be interrupted.");
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Executing query…");
        await Assertions.Expect(s.Page.Locator("[data-testid='execute-button']")).ToBeDisabledAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid='execute-cancel']")).ToHaveTextAsync("Cancel");
        await Assertions.Expect(s.Page.Locator("[data-testid='execute-cancel']")).ToHaveAttributeAsync("aria-label", "Cancel if not started");

        await s.WaitForExecuteAfterAsync(before);
        Assert.Equal("3", await s.Page.Locator("[data-testid='execute-complete']").GetAttributeAsync("data-status"));   // TimedOut
        await Assertions.Expect(s.Page.Locator("[data-testid='execute-button']")).ToBeEnabledAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid^='results-set-header-']")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task F5_runs_the_query_wherever_the_focus_is_on_the_editor_page_and_not_on_Settings()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.SetEditorTextAsync("SELECT 1 AS n;");

        async Task RunsFromAsync(string what)
        {
            var before = await s.ExecuteVersionAsync();
            await Keys.AssertClaimedAsync(s.Page, "F5");
            await s.WaitForExecuteAfterAsync(before);
            SqlSession.Log(_output, $"F5 ran the query from {what}.");
        }

        await s.Page.ClickAsync("[data-testid='sql-editor'] .cm-content");
        await RunsFromAsync("the editor");

        await s.Page.ClickAsync("[data-testid='results-cell-0-0']");
        await RunsFromAsync("the results grid");

        await s.Page.FocusAsync("[data-testid='analyse-button']");
        await RunsFromAsync("the toolbar");

        if (await s.Page.Locator("[data-testid='schema-tree']").CountAsync() > 0)
        {
            await s.Page.ClickAsync("[data-testid='schema-tree']");
            await RunsFromAsync("the Schema panel");
        }

        // Leaving the editor page releases its keys (its DisposeAsync): wait until the editor is gone.
        await s.Page.ClickAsync("nav >> text=Settings");
        await Assertions.Expect(s.Page.Locator("[data-testid='sql-editor']")).ToHaveCountAsync(0);
        await s.Page.WaitForTimeoutAsync(300);
        await Keys.AssertNotClaimedAsync(s.Page, "F5");
    }

    [SkippableFact]
    public async Task Parse_checks_the_syntax_without_running_anything()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var table = "akml_e2e_parse_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        await s.SetEditorTextAsync($"CREATE TABLE dbo.{table} (n int);\nGO\nSELECT 1 AS n;");
        var before = await s.ExecuteVersionAsync();
        await s.Page.ClickAsync("[data-testid='parse-button']");
        await s.WaitForExecuteAfterAsync(before);
        await Assertions.Expect(Tab(s.Page, "messages")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal("Commands completed successfully.", (await MessageLinesAsync(s.Page)).Single());

        await s.ExecuteAsync($"SELECT OBJECT_ID('dbo.{table}') AS id;");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-0']")).ToHaveTextAsync("NULL");

        await s.SetEditorTextAsync("SELECT 1;\nGO\nSELECT 1;\nSELECT 2;\nSELEC 3;");
        before = await s.ExecuteVersionAsync();
        await Keys.AssertClaimedAsync(s.Page, "Control+F5");
        await s.WaitForExecuteAfterAsync(before);
        var error = s.Page.Locator("[data-testid^='messages-error-']").First;
        await Assertions.Expect(error).ToContainTextAsync("Line 3");
        await error.ClickAsync();
        Assert.Equal("SELEC 3;", (await ActiveEditorLineAsync(s.Page)).Trim());

        await s.ExecuteAsync("SELECT 42 AS n;");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-0']")).ToHaveTextAsync("42");
    }

    [SkippableFact]
    public async Task Values_read_as_SSMS_shows_them()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        await s.ExecuteAsync(
            "SELECT CAST('2025-10-01' AS date) AS d, CAST('2025-10-01T23:57:23.1234567' AS datetime2(7)) AS dt2, " +
            "CAST('2025-10-01T23:57:23.88' AS datetime) AS dt, CAST('2025-10-01T23:57:23+02:00' AS datetimeoffset(0)) AS dto, " +
            "CAST(0x0FAB AS varbinary(4)) AS bin, CAST('6f9619ff-8b86-d011-b42d-00c04fc964ff' AS uniqueidentifier) AS g, " +
            "CAST(12.5 AS money) AS m;");

        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-0']")).ToHaveTextAsync("2025-10-01");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-1']")).ToHaveTextAsync("2025-10-01 23:57:23.1234567");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-2']")).ToHaveTextAsync("2025-10-01 23:57:23.880");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-3']")).ToHaveTextAsync("2025-10-01 23:57:23 +02:00");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-4']")).ToHaveTextAsync("0x0FAB");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-5']")).ToHaveTextAsync("6F9619FF-8B86-D011-B42D-00C04FC964FF");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-cell-0-6']")).ToHaveTextAsync("12.5000");
    }
}
