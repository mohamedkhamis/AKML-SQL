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
