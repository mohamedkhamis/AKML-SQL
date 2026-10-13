using System.Text;
using System.Text.Json;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

// Browser-to-engine coverage of PR #255's review corrections.
[Trait("Category", "BridgeE2E")]
public sealed class ReviewRegressionTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Create_or_alter_view_runs_and_returns_the_updated_definition()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var view = "akml_review_" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (var value in new[] { 1, 2 })
            {
                await s.ExecuteAsync($"CREATE OR ALTER VIEW dbo.[{view}] AS SELECT {value} AS n;");
                await Assertions.Expect(s.Page.GetByTestId("status-outcome")).ToContainTextAsync("Query executed successfully.");
                await s.ExecuteAsync($"SELECT n FROM dbo.[{view}];");
                await Assertions.Expect(s.Page.GetByTestId("results-cell-0-0")).ToHaveTextAsync(value.ToString());
            }
        }
        finally
        {
            await s.ExecuteAsync($"DROP VIEW IF EXISTS dbo.[{view}];");
        }
    }

    [SkippableTheory]
    [InlineData("history-open")]
    [InlineData("history-reexecute")]
    public async Task History_cancel_preserves_work_and_confirm_opens_a_new_document(string action)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await s.ExecuteAsync("SELECT 255 AS review_history;");
        await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("doc-save").ClickAsync();
            await page.GetByTestId("dialog-input").FillAsync("audit.v2.255");
            await page.GetByTestId("dialog-confirm").ClickAsync();
        });
        await s.SetEditorTextAsync("SELECT 'unsaved review work';");
        await page.GetByTestId("nav-history").ClickAsync();
        var row = page.GetByTestId("history-row").First;
        await row.WaitForAsync(new() { Timeout = 30_000 });
        await row.ClickAsync();
        await page.GetByTestId(action).ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog-message")).ToContainTextAsync("audit.v2.255");
        await page.GetByTestId("dialog-cancel").ClickAsync();
        await page.GetByTestId("nav-editor").ClickAsync();
        await Assertions.Expect(page.Locator(".cm-content")).ToHaveTextAsync("SELECT 'unsaved review work';");
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("audit.v2.255");
        await page.GetByTestId("nav-history").ClickAsync();
        await row.ClickAsync();
        await page.GetByTestId(action).ClickAsync();
        await page.GetByTestId("dialog-confirm").ClickAsync();
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("SQLQuery2");
        await Assertions.Expect(page.Locator(".cm-content")).ToHaveTextAsync("SELECT 255 AS review_history;");
        await Assertions.Expect(page.GetByTestId("doc-modified")).ToHaveCountAsync(0);
        if (action == "history-reexecute")
            await Assertions.Expect(page.GetByTestId("results-cell-0-0")).ToHaveTextAsync("255");
    }

    [SkippableFact]
    public async Task Windows_1252_open_and_dotted_save_name_round_trip()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        var bytes = Encoding.ASCII.GetBytes("SELECT 'caf? ?' AS legacy;");
        bytes[Array.IndexOf(bytes, (byte)'?')] = 0xe9;
        bytes[Array.IndexOf(bytes, (byte)'?')] = 0x80;
        await page.GetByTestId("doc-open-input").SetInputFilesAsync(new FilePayload
        { Name = "report.v1.sql", MimeType = "text/plain", Buffer = bytes });
        await Assertions.Expect(page.Locator(".cm-content")).ToHaveTextAsync("SELECT 'café €' AS legacy;");
        await Assertions.Expect(page.GetByTestId("doc-name")).ToHaveTextAsync("report.v1");
        var file = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("doc-saveas").ClickAsync();
            await page.GetByTestId("dialog-input").FillAsync("report.v2.final");
            await page.GetByTestId("dialog-confirm").ClickAsync();
        });
        Assert.Equal("report.v2.final.sql", file.SuggestedFilename);
        Assert.Equal("SELECT 'café €' AS legacy;", await File.ReadAllTextAsync((await file.PathAsync())!, Encoding.UTF8));
    }

    [SkippableTheory]
    [InlineData("edit")]
    [InlineData("run")]
    [InlineData("parse")]
    public async Task Error_navigation_underline_clears_on_the_next_action(string action)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await s.ExecuteAsync("SELECT 1 / 0 AS review_failure;");
        await page.Locator("[data-testid^='messages-error-']").First.ClickAsync();
        var underline = page.Locator(".cm-lintRange-error");
        await Assertions.Expect(underline).ToHaveCountAsync(1);
        if (action == "edit")
        {
            await page.Locator(".cm-content").ClickAsync();
            await page.Keyboard.PressAsync("End");
            await page.Keyboard.InsertTextAsync(" -- corrected next");
        }
        else
        {
            // Keep the text unchanged so this specifically verifies Run/Parse clearing.
            await page.GetByTestId(action == "run" ? "execute-button" : "parse-button").ClickAsync();
        }
        await Assertions.Expect(underline).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Copy_JSON_and_INSERT_preserve_declared_text_and_numeric_types()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output,
            contextOptions: new() { Permissions = new[] { "clipboard-read", "clipboard-write" } });
        var page = s.Page;
        await s.ExecuteAsync("SELECT CAST('00501' AS varchar(5)) AS code, CAST('1,234' AS varchar(10)) AS label, CAST(12.5 AS decimal(5,1)) AS amount, CHAR(1) AS control, CAST(CAST('NaN' AS varchar(3)) AS sql_variant) AS variant_text;");
        await page.GetByTestId("results-rownum-0").ClickAsync();
        foreach (var format in new[] { "json", "insert" })
        {
            await page.GetByTestId("results-cell-0-0").ClickAsync(new() { Button = MouseButton.Right });
            await page.GetByTestId("results-menu-copy-as").ClickAsync();
            await page.GetByTestId("results-menu-copy-" + format).ClickAsync();
            await page.WaitForFunctionAsync("async prefix => (await navigator.clipboard.readText()).trimStart().startsWith(prefix)", format == "json" ? "[" : "INSERT");
            var text = await page.EvaluateAsync<string>("navigator.clipboard.readText()");
            if (format == "json")
            {
                using var json = JsonDocument.Parse(text);
                var row = json.RootElement[0];
                Assert.Equal("00501", row.GetProperty("code").GetString());
                Assert.Equal("1,234", row.GetProperty("label").GetString());
                Assert.Equal(12.5m, row.GetProperty("amount").GetDecimal());
                Assert.Equal("\u0001", row.GetProperty("control").GetString());
                Assert.Equal("NaN", row.GetProperty("variant_text").GetString());
            }
            else Assert.Contains("N'00501', N'1,234', 12.5", text);
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Displayed_date_time_and_binary_edits_apply_to_the_source_database_after_USE(bool doubleClick)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        var table = "akml_review_" + Guid.NewGuid().ToString("N");
        await s.ExecuteAsync($"CREATE TABLE tempdb.dbo.[{table}] (id int PRIMARY KEY, d date, t time(3), b varbinary(8)); INSERT tempdb.dbo.[{table}] VALUES (1,'2025-01-01','12:30:00',0x0FAB);");
        try
        {
            await s.ExecuteAsync($"USE tempdb; SELECT id,d,t,b FROM dbo.[{table}]; USE master;");
            await Assertions.Expect(page.GetByTestId("db-selector")).ToContainTextAsync("master");
            foreach (var (column, original, updated) in new[] { (1, "2025-01-01", "2025-10-02"), (2, "12:30:00.000", "14:15:16.123"), (3, "0x0FAB", "0x1F") })
            {
                var cell = page.GetByTestId($"results-cell-0-{column}");
                await Assertions.Expect(cell).ToHaveTextAsync(original);
                if (doubleClick) await cell.DblClickAsync();
                else
                {
                    await cell.ClickAsync();
                    await page.Keyboard.PressAsync("F2");
                }
                var input = page.GetByTestId("results-cell-input");
                await Assertions.Expect(input).ToHaveValueAsync(original);
                await input.FillAsync(updated);
                await input.PressAsync("Enter");
                await Assertions.Expect(page.GetByTestId($"results-cell-0-{column}")).ToHaveTextAsync(updated);
            }
            await page.GetByTestId("results-apply").ClickAsync();
            await Assertions.Expect(page.GetByTestId("results-applybar")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
            await s.ExecuteAsync($"SELECT CONVERT(char(10),d,120) AS d, CONVERT(varchar(20),t) AS t, CONVERT(varchar(20),b,1) AS b FROM tempdb.dbo.[{table}];");
            await Assertions.Expect(page.GetByTestId("results-cell-0-0")).ToHaveTextAsync("2025-10-02");
            await Assertions.Expect(page.GetByTestId("results-cell-0-1")).ToHaveTextAsync("14:15:16.123");
            await Assertions.Expect(page.GetByTestId("results-cell-0-2")).ToHaveTextAsync("0x1F");
        }
        finally
        {
            await page.ReloadAsync();
            await page.GetByTestId("status-pill").Filter(new() { HasTextString = "Live" }).WaitForAsync();
            // Reload clears pending grid edits and the browser's SQL connection state.
            await WebAppFixture.ConnectAsync(page, "tempdb");
            await s.ExecuteAsync($"DROP TABLE IF EXISTS tempdb.dbo.[{table}];");
        }
    }
}
