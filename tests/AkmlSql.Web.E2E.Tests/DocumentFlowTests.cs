using System.Text;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 041 (T168, research R64, FR-072, FR-073, FR-089) — quickstart §5 steps 2–5: New query (with the
/// confirmation for a modified document), Open of an SSMS (UTF-16) and a VS Code (UTF-8) file, Save of a default-named
/// document under a name (UTF-8 with BOM), the browser title, the modified marker, Save as, and History naming a
/// session after the document.
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class DocumentFlowTests
{
    private readonly ITestOutputHelper _output;

    public DocumentFlowTests(ITestOutputHelper output) => _output = output;

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static async Task TypeAsync(IPage page, string text)
    {
        await page.ClickAsync("[data-testid='sql-editor'] .cm-content");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.InsertTextAsync(text);
    }

    [SkippableFact]
    public async Task New_query_confirms_a_modified_document_and_starts_the_next_number()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await Assertions.Expect(Id(page, "doc-name")).ToHaveTextAsync("SQLQuery1");

        await TypeAsync(page, "SELECT 1;");
        await Assertions.Expect(Id(page, "doc-modified")).ToBeVisibleAsync();
        await Id(page, "doc-new").ClickAsync();
        await Assertions.Expect(Id(page, "dialog-message")).ToContainTextAsync("SQLQuery1");
        await Id(page, "dialog-confirm").ClickAsync();

        await Assertions.Expect(Id(page, "doc-name")).ToHaveTextAsync("SQLQuery2");
        await Assertions.Expect(Id(page, "doc-modified")).ToHaveCountAsync(0);
        Assert.Equal(string.Empty, (await page.Locator("[data-testid='sql-editor'] .cm-content").InnerTextAsync()).Trim());
    }

    [SkippableFact]
    public async Task Open_reads_UTF16_and_UTF8_files_and_names_the_document_after_them()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        var dir = Path.Combine(Path.GetTempPath(), "akml-e2e-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ssms = Path.Combine(dir, "Monthly Close.sql");
            await File.WriteAllBytesAsync(ssms, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("SELECT N'Grüße' AS ssms;")).ToArray());
            var vscode = Path.Combine(dir, "vscode-query.sql");
            await File.WriteAllBytesAsync(vscode, Encoding.UTF8.GetBytes("SELECT N'Grüße' AS vscode;"));

            await page.Locator("[data-testid='doc-open-input']").SetInputFilesAsync(ssms);
            await Assertions.Expect(Id(page, "doc-name")).ToHaveTextAsync("Monthly Close");
            await Assertions.Expect(page.Locator("[data-testid='sql-editor'] .cm-content")).ToContainTextAsync("SELECT N'Grüße' AS ssms;");
            await Assertions.Expect(Id(page, "doc-modified")).ToHaveCountAsync(0);

            // A modified document asks first.
            await TypeAsync(page, " -- edited");
            await page.Locator("[data-testid='doc-open-input']").SetInputFilesAsync(vscode);
            await Assertions.Expect(Id(page, "dialog-message")).ToContainTextAsync("Monthly Close");
            await Id(page, "dialog-confirm").ClickAsync();
            await Assertions.Expect(Id(page, "doc-name")).ToHaveTextAsync("vscode-query");
            await Assertions.Expect(page.Locator("[data-testid='sql-editor'] .cm-content")).ToContainTextAsync("SELECT N'Grüße' AS vscode;");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Save_names_a_default_document_downloads_UTF8_with_a_BOM_and_titles_the_page()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.SetEditorTextAsync("SELECT 1 AS audit;");

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await Id(page, "doc-save").ClickAsync();
            await Assertions.Expect(Id(page, "dialog-input")).ToHaveValueAsync("SQLQuery1");
            await Id(page, "dialog-input").FillAsync("orders-audit");
            await Id(page, "dialog-confirm").ClickAsync();
        });

        Assert.Equal("orders-audit.sql", download.SuggestedFilename);
        var bytes = await File.ReadAllBytesAsync((await download.PathAsync())!);
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "The file has no UTF-8 BOM.");
        Assert.Equal("SELECT 1 AS audit;", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        await Assertions.Expect(Id(page, "doc-name")).ToHaveTextAsync("orders-audit");
        await Assertions.Expect(page).ToHaveTitleAsync("orders-audit - localhost.tempdb - AKML SQL");

        await TypeAsync(page, " -- more");
        await Assertions.Expect(Id(page, "doc-modified")).ToBeVisibleAsync();
        await Assertions.Expect(page).ToHaveTitleAsync("orders-audit* - localhost.tempdb - AKML SQL");

        // Save again: named now, so no prompt.
        var again = await page.RunAndWaitForDownloadAsync(() => Id(page, "doc-save").ClickAsync());
        Assert.Equal("orders-audit.sql", again.SuggestedFilename);
        await Assertions.Expect(Id(page, "dialog")).ToHaveCountAsync(0);

        // Save as always asks.
        await Id(page, "doc-saveas").ClickAsync();
        await Assertions.Expect(Id(page, "dialog-input")).ToHaveValueAsync("orders-audit");
        await Id(page, "dialog-cancel").ClickAsync();
    }

    [SkippableFact]
    public async Task History_names_a_session_after_its_document()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        await s.SetEditorTextAsync("SELECT 41 AS orders_audit;");
        await page.RunAndWaitForDownloadAsync(async () =>
        {
            await Id(page, "doc-save").ClickAsync();
            await Id(page, "dialog-input").FillAsync("orders-audit");
            await Id(page, "dialog-confirm").ClickAsync();
        });
        await s.ExecuteAsync("SELECT 41 AS orders_audit;");

        await Id(page, "doc-new").ClickAsync();
        await s.ExecuteAsync("SELECT 42 AS unnamed;");
        await page.WaitForTimeoutAsync(1_000);   // history records are written in the background

        await page.ClickAsync("nav >> text=History");
        var names = page.Locator("[data-testid='history-list'] .akml-hist-name");
        await Assertions.Expect(names.Filter(new() { HasTextString = "orders-audit" })).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        var all = (await names.AllInnerTextsAsync()).Select(n => n.Trim()).ToArray();
        _output.WriteLine("History: " + string.Join(" | ", all));
        Assert.Contains(all, n => n != "orders-audit");   // the new query's session is its own entry
        Assert.DoesNotContain(all, n => n.StartsWith("SQLQuery", StringComparison.Ordinal));
    }
}
