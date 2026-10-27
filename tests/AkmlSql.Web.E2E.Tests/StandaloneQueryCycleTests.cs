using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

[Collection("Engine served web")]
public sealed class StandaloneQueryCycleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Engine_served_editor_executes_preserves_session_and_reloads()
    {
        using var bundle = new EngineServedBundle();
        await using var engine = await SandboxEngine.StartAsync(webRoot: bundle.Root);
        await VerifyAsync($"http://127.0.0.1:{engine.Port}/");
    }

    [SkippableFact]
    public async Task A_dedicated_Windows_service_serves_and_executes_the_same_cycle()
    {
        var url = Environment.GetEnvironmentVariable("AKML_STANDALONE_VERIFY_URL");
        Skip.If(string.IsNullOrWhiteSpace(url), "Set AKML_STANDALONE_VERIFY_URL to an isolated loopback test service; never run installer smoke tests on an owner machine.");
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Scheme == "http",
            "The dedicated service test accepts only a loopback HTTP origin.");
        await VerifyAsync(url!);
    }

    private async Task VerifyAsync(string origin)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchForTestsAsync();
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.RequestFailed += (_, request) => errors.Add($"{request.Method} {request.Url}: {request.Failure}");
        await page.GotoAsync(origin, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("status-pill")).ToContainTextAsync("Live", new() { Timeout = 60_000 });
        await WebAppFixture.ConnectAsync(page, "tempdb");
        await RunAsync(page, "CREATE TABLE #akml_cycle_045 (n int); INSERT #akml_cycle_045 VALUES (42); SELECT n AS AKML_CYCLE_RESULT_045 FROM #akml_cycle_045;");
        await Assertions.Expect(page.GetByTestId("results-pane")).ToContainTextAsync("42");
        await RunAsync(page, "SELECT n + 1 AS AKML_CYCLE_RESULT_045 FROM #akml_cycle_045; DROP TABLE #akml_cycle_045;");
        await Assertions.Expect(page.GetByTestId("results-pane")).ToContainTextAsync("43");

        // Tab records have a 500 ms debounce. Observe the saved text rather than guessing a delay.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await page.EvaluateAsync<bool>("async () => { const db = await import('./js/akml-indexeddb.js'); return (await db.list('editorTabs')).some(row => row.key.startsWith('tab:') && row.value.includes('DROP TABLE #akml_cycle_045')); }"))
        {
            Assert.True(DateTime.UtcNow < deadline, "The query tab was not persisted before reload.");
            await Task.Delay(100);
        }
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("status-pill")).ToContainTextAsync("Live", new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator("[data-testid='sql-editor'] .cm-content"))
            .ToContainTextAsync("DROP TABLE #akml_cycle_045", new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("status-connection"))
            .ToContainTextAsync("localhost/tempdb", new() { Timeout = 30_000 });
        await RunAsync(page, "SELECT 44 AS AKML_CYCLE_RESULT_045;");
        await Assertions.Expect(page.GetByTestId("results-pane")).ToContainTextAsync("44");
        Assert.Empty(errors);
        output.WriteLine("Auto-connected; SQL returned 42 then 43 on the same temp-table session; saved text/connection reloaded; SQL returned 44 with no failed requests.");
    }

    private static async Task RunAsync(IPage page, string sql)
    {
        var editor = page.Locator("[data-testid='sql-editor'] .cm-content");
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.InsertTextAsync(sql);
        var completed = page.GetByTestId("execute-complete");
        var before = await completed.CountAsync() == 0 ? 0 : int.Parse(await completed.GetAttributeAsync("data-version") ?? "0");
        await page.GetByTestId("execute-button").ClickAsync();
        try { await completed.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60_000 }); }
        catch (TimeoutException ex)
        {
            var shot = Path.Combine(Path.GetTempPath(), $"akml-standalone-failure-{Guid.NewGuid():N}.png");
            await page.ScreenshotAsync(new() { Path = shot });
            throw new InvalidOperationException($"No execution result; connection: {await page.GetByTestId("status-connection").InnerTextAsync()}; connection dialog visible: {await page.GetByTestId("connection-manager").IsVisibleAsync()}; editor: {await editor.InnerTextAsync()}; screenshot: {shot}", ex);
        }
        await Assertions.Expect(completed).Not.ToHaveAttributeAsync("data-version", before.ToString(), new() { Timeout = 60_000 });
        await Assertions.Expect(page.GetByTestId("status-outcome")).ToContainTextAsync("Query executed successfully.");
    }
}
