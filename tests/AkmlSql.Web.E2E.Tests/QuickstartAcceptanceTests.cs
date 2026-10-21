using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

// Remaining cross-story checks from quickstart sections 1–3 (T215).
[Trait("Category", "BridgeE2E")]
public sealed class QuickstartAcceptanceTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Sql_authenticated_database_switch_preserves_the_session_without_a_password_prompt()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await s.ExecuteAsync("SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) AS windows_only, HAS_PERMS_BY_NAME(NULL,NULL,'ALTER ANY LOGIN') AS can_create_login;");
        Skip.If(await page.GetByTestId("results-cell-0-0").InnerTextAsync() != "0" || await page.GetByTestId("results-cell-0-1").InnerTextAsync() != "1",
            "SQL-auth acceptance requires mixed authentication and permission to create a temporary test login.");
        var login = "akml_acceptance_" + Guid.NewGuid().ToString("N");
        var password = "Aa1!" + Guid.NewGuid().ToString("N");
        await s.ExecuteAsync($"CREATE LOGIN [{login}] WITH PASSWORD=N'{password}', DEFAULT_DATABASE=tempdb;");
        await Assertions.Expect(page.GetByTestId("status-outcome")).ToContainTextAsync("successfully");
        try
        {
            await page.GetByTestId("status-connection").ClickAsync();
            await page.GetByTestId("conn-auth-sql").CheckAsync();
            await page.GetByTestId("conn-login-input").FillAsync(login);
            await page.GetByTestId("conn-password-input").FillAsync(password);
            await page.GetByTestId("conn-connect-btn").ClickAsync();
            await Assertions.Expect(page.GetByTestId("connection-manager")).ToHaveCountAsync(0);
            await s.ExecuteAsync("SELECT ORIGINAL_LOGIN() AS who; CREATE TABLE #sql_auth_041(n int); INSERT #sql_auth_041 VALUES(41);");
            await Assertions.Expect(page.GetByTestId("results-cell-0-0")).ToHaveTextAsync(login);
            await page.GetByTestId("db-selector").ClickAsync();
            await page.GetByTestId("db-selector-list").GetByText("master", new() { Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByTestId("status-connection")).ToContainTextAsync("localhost/master");
            await Assertions.Expect(page.GetByTestId("connection-manager")).ToHaveCountAsync(0);
            await s.ExecuteAsync("SELECT n FROM #sql_auth_041; USE tempdb;");
            await Assertions.Expect(page.GetByTestId("results-cell-0-0")).ToHaveTextAsync("41");
            await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync("tempdb");
        }
        finally
        {
            await page.Keyboard.PressAsync("Escape");
            if (await page.GetByTestId("connection-manager").CountAsync() > 0)
                await page.GetByTestId("connection-manager").GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
            await WebAppFixture.ConnectAsync(page, "tempdb");
            // End only this randomly named test login's pooled sessions before removing it.
            await s.ExecuteAsync($"DECLARE @kill nvarchar(max)=N''; SELECT @kill += N'KILL '+CONVERT(nvarchar(10),session_id)+N';' FROM sys.dm_exec_sessions WHERE login_name=N'{login}'; EXEC(@kill); DROP LOGIN [{login}];");
            await Assertions.Expect(page.GetByTestId("status-outcome")).ToContainTextAsync("successfully");
        }
    }

    [SkippableTheory]
    [InlineData("new")]
    [InlineData("open")]
    [InlineData("history")]
    public async Task Pending_grid_edits_can_cancel_document_replacement_or_navigation(string action)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        // A local temporary table cannot survive the sandbox connection, even on assertion failure.
        await s.ExecuteAsync("CREATE TABLE #pending041 (id int PRIMARY KEY, name nvarchar(50)); INSERT #pending041 VALUES(1,N'alpha');");
        await s.ExecuteAsync("SELECT id, name FROM #pending041;");
        await page.GetByTestId("results-cell-0-1").ClickAsync();
        await page.Keyboard.TypeAsync("changed");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("results-applybar")).ToBeVisibleAsync();

        if (action == "history")
        {
            // Leaving the page unmounts the grids: it asks first.
            await page.GetByTestId("nav-history").ClickAsync();
            await Assertions.Expect(page.GetByTestId("dialog-title")).ToContainTextAsync("pending edit");
            await page.GetByTestId("dialog-cancel").ClickAsync();
        }
        else
        {
            // Spec 043 (FR-002, FR-011): New and Open go to another tab — the grid and its edits stay in theirs, nothing is asked.
            if (action == "new") await page.GetByTestId("doc-new").ClickAsync();
            else await page.GetByTestId("doc-open-input").SetInputFilesAsync(new FilePayload { Name = "replacement.sql", MimeType = "text/plain", Buffer = System.Text.Encoding.UTF8.GetBytes("SELECT 99;") });
            await Assertions.Expect(page.GetByTestId("doc-tab")).ToHaveCountAsync(2);
            await Assertions.Expect(page.GetByTestId("dialog")).ToHaveCountAsync(0);
            await page.GetByTestId("doc-tab").First.ClickAsync();
        }
        await Assertions.Expect(page.Locator("[data-testid='results-pane'] [data-testid='results-applybar']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='results-pane'] [data-testid='results-cell-0-1']")).ToHaveTextAsync("changed");
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery1");
    }

    [SkippableFact]
    public async Task Selected_text_error_maps_back_to_document_and_later_GO_batch_runs()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await s.SetEditorTextAsync("SELECT 'not selected';\nGO\nSELECT 1 AS first;\nGO\nSELECT * FROM dbo.akml_missing_041_acceptance;\nGO\nSELECT 3 AS last;");
        await page.Keyboard.PressAsync("Control+Home");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Control+Shift+End");
        var before = await s.ExecuteVersionAsync();
        await page.GetByTestId("execute-button").ClickAsync();
        await s.WaitForExecuteAfterAsync(before);
        await page.GetByTestId("results-tab-messages").ClickAsync();
        await page.Locator("[data-testid^='messages-error-']").First.ClickAsync();
        await Assertions.Expect(page.Locator(".cm-activeLine")).ToHaveTextAsync("SELECT * FROM dbo.akml_missing_041_acceptance;");
        await page.GetByTestId("results-tab-results").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid^='results-set-header-']")).ToHaveCountAsync(2);
        await Assertions.Expect(page.GetByTestId("results-set-1")).ToContainTextAsync("last");
        await Assertions.Expect(page.GetByTestId("results-set-1")).ToContainTextAsync("3");
    }

    [SkippableFact]
    public async Task Downloads_keep_full_values_and_truncated_results_require_confirmation()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await s.ExecuteAsync("SELECT REPLICATE('x',2000) AS wide, CAST('2026-10-10' AS date) AS d, CAST(NULL AS int) AS missing;");
        foreach (var format in new[] { "csv", "tsv" })
        {
            await page.GetByTestId("results-cell-0-0").ClickAsync(new() { Button = MouseButton.Right });
            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByTestId("results-save-" + format).ClickAsync());
            Assert.EndsWith("." + format, download.SuggestedFilename);
            var text = await File.ReadAllTextAsync((await download.PathAsync())!);
            Assert.Contains(new string('x', 2000), text);
            Assert.Contains("2026-10-10", text);
            Assert.Contains("NULL", text);
            Assert.Contains(format == "csv" ? ',' : '\t', text);
        }
        await page.GetByTestId("caps-maxrows").FillAsync("1");
        await page.GetByTestId("caps-maxrows").BlurAsync();
        await s.ExecuteAsync("SELECT n FROM (VALUES(1),(2),(3)) v(n);");
        await Assertions.Expect(page.GetByTestId("results-truncated")).ToBeVisibleAsync();
        await page.GetByTestId("results-cell-0-0").ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByTestId("results-save-csv").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-title")).ToHaveTextAsync("Save an incomplete result?");
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await page.GetByTestId("results-cell-0-0").ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByTestId("results-save-csv").ClickAsync();
        var partial = await page.RunAndWaitForDownloadAsync(() => page.GetByTestId("dialog-confirm").ClickAsync());
        var rows = (await File.ReadAllLinesAsync((await partial.PathAsync())!)).Where(line => line.Length > 0).ToArray();
        Assert.Equal(2, rows.Length); // Header plus the one fetched row.
    }

    [SkippableFact]
    public async Task Open_refuses_a_file_over_ten_MB_and_preserves_the_document()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        await s.SetEditorTextAsync("SELECT 'keep me';");
        await s.Page.GetByTestId("doc-open-input").SetInputFilesAsync(new FilePayload
        {
            Name = "over-limit.sql", MimeType = "text/plain", Buffer = System.Text.Encoding.UTF8.GetBytes(new string('x', 10 * 1024 * 1024 + 1))
        });
        // Spec 043 (FR-011): a file opens into another tab, so nothing is asked first; one too large opens nothing at all.
        await Assertions.Expect(s.Page.GetByRole(AriaRole.Alert).Filter(new() { HasTextString = "10 MB" })).ToBeVisibleAsync();
        await Assertions.Expect(s.Page.GetByTestId("dialog")).ToHaveCountAsync(0);
        await Assertions.Expect(s.Page.GetByTestId("doc-tab")).ToHaveCountAsync(1);
        await Assertions.Expect(s.Page.Locator(".cm-content").First).ToHaveTextAsync("SELECT 'keep me';");
    }

    [SkippableFact]
    public async Task User_widths_survive_virtualization_and_layout_changes_and_schema_controls_work()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await page.GetByTestId("schema-filter").FillAsync("akml_no_such_object_041");
        await Assertions.Expect(page.Locator(".akml-schema-tree-schema")).ToHaveCountAsync(0);
        await page.GetByTestId("schema-filter").FillAsync("");
        await page.GetByTestId("schema-refresh").ClickAsync();
        await Assertions.Expect(page.GetByTestId("schema-refresh")).ToBeEnabledAsync();
        await Assertions.Expect(page.GetByTestId("schema-tree-root")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("schema-refresh-error")).ToHaveCountAsync(0);

        await s.ExecuteAsync("SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY a.object_id, b.object_id) AS n, 'sample' AS label FROM sys.all_objects a CROSS JOIN sys.all_objects b;");
        var grid = page.GetByTestId("results-grid");
        var original = await grid.GetAttributeAsync("style");
        foreach (var col in new[] { 0, 1 })
        {
            var box = (await page.GetByTestId($"results-col-resizer-{col}").BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(box.X + box.Width / 2 + 90, box.Y + box.Height / 2, new() { Steps = 8 });
            await page.Mouse.UpAsync();
        }
        var resized = await grid.GetAttributeAsync("style");
        Assert.NotEqual(original, resized);
        await grid.EvaluateAsync("el => { el.scrollTop = el.scrollHeight; }");
        await Assertions.Expect(page.GetByTestId("results-cell-999-0")).ToBeVisibleAsync();
        await grid.EvaluateAsync("el => { el.scrollTop = 0; }");
        await Assertions.Expect(page.GetByTestId("results-cell-0-0")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("F8");
        await page.SetViewportSizeAsync(1600, 950);
        await Assertions.Expect(grid).ToHaveAttributeAsync("style", resized!);
        await s.ExecuteAsync("SELECT 1 AS n, 'sample' AS label;");
        Assert.NotEqual(resized, await grid.GetAttributeAsync("style"));

        await page.GetByTestId("view-menu").ClickAsync();
        await page.GetByTestId("view-toggle-nav").ClickAsync();
        await Assertions.Expect(page.GetByTestId("workspace-edge-nav")).ToBeVisibleAsync();
        await page.GetByTestId("workspace-edge-nav").ClickAsync();
        await Assertions.Expect(page.GetByTestId("nav-history")).ToBeVisibleAsync();
        await ShellPolishTests.NavigateAsync(page, "history");
        await Assertions.Expect(page.GetByTestId("nav-editor")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Losing_the_engine_ends_the_running_state_without_claiming_the_statement_was_cancelled()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        await s.SetEditorTextAsync("WAITFOR DELAY '00:00:20'; SELECT 1;");
        var before = await s.ExecuteVersionAsync();
        await s.Page.GetByTestId("execute-button").ClickAsync();
        await Assertions.Expect(s.Page.GetByTestId("execute-running")).ToHaveCountAsync(1);
        s.Engine.Process.Kill(entireProcessTree: true);
        await s.Engine.Process.WaitForExitAsync();
        await s.WaitForExecuteAfterAsync(before);
        await Assertions.Expect(s.Page.GetByTestId("execute-running")).ToHaveCountAsync(0);
        await s.Page.GetByTestId("results-tab-messages").ClickAsync();
        await Assertions.Expect(s.Page.GetByTestId("results-panel-messages")).ToContainTextAsync("the statement may still have run");
    }
}
