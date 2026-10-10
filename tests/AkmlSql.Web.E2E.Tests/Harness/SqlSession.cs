using Microsoft.Playwright;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 041 (T005, research R70) — a browser on the Debug web app, paired with a <see cref="SandboxEngine"/>
/// and connected to the local SQL Server's tempdb with Windows authentication: the starting point of the
/// grid, messages, workspace and shell suites. Created by <see cref="WebAppFixture.WithSqlOrSkipAsync"/>,
/// which skips the test when anything it needs is missing. Disposing tears everything down.
/// </summary>
public sealed class SqlSession : IAsyncDisposable
{
    internal SqlSession(SandboxEngine engine, WebAppFixture web, IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)
    {
        Engine = engine;
        Web = web;
        Playwright = playwright;
        Browser = browser;
        Context = context;
        Page = page;
    }

    public SandboxEngine Engine { get; }
    public WebAppFixture Web { get; }
    public IPlaywright Playwright { get; }
    public IBrowser Browser { get; }
    public IBrowserContext Context { get; }
    public IPage Page { get; }

    /// <summary>Replace the editor's text and run it with the Execute button; waits for the run to end.</summary>
    public async Task ExecuteAsync(string sql, int timeoutMs = 60_000)
    {
        await SetEditorTextAsync(sql);
        var before = await ExecuteVersionAsync();
        await Page.ClickAsync("[data-testid='execute-button']");
        await WaitForExecuteAfterAsync(before, timeoutMs);
    }

    /// <summary>Replace the editor's whole text.</summary>
    public async Task SetEditorTextAsync(string sql)
    {
        await Page.ClickAsync("[data-testid='sql-editor'] .cm-content");
        await Page.Keyboard.PressAsync("Control+A");
        await Page.Keyboard.PressAsync("Delete");
        await Page.Keyboard.InsertTextAsync(sql);
    }

    /// <summary>The <c>data-version</c> of the hidden <c>execute-complete</c> marker (0 before the first run).</summary>
    public async Task<int> ExecuteVersionAsync()
    {
        var marker = Page.Locator("[data-testid='execute-complete']");
        if (await marker.CountAsync() == 0) return 0;
        var v = await marker.GetAttributeAsync("data-version");
        return int.TryParse(v, out var n) ? n : 0;
    }

    /// <summary>Wait until an execution newer than <paramref name="version"/> has completed. Falls back to
    /// the Execute button becoming enabled again when the page predates the execute-complete marker.</summary>
    public async Task WaitForExecuteAfterAsync(int version, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await ExecuteVersionAsync() > version) return;
            if (await Page.Locator("[data-testid='execute-complete']").CountAsync() == 0
                && await Page.Locator("[data-testid='execute-button']").IsEnabledAsync()
                && await Page.Locator("[data-testid='results-pane']").CountAsync() > 0)
            {
                return;
            }
            await Task.Delay(150);
        }
        throw new TimeoutException("The execution did not complete in time.");
    }

    public async ValueTask DisposeAsync()
    {
        try { await Context.CloseAsync(); } catch { /* ignore */ }
        try { await Browser.DisposeAsync(); } catch { /* ignore */ }
        Playwright.Dispose();
        await Web.DisposeAsync();
        await Engine.DisposeAsync();
    }

    internal static void Log(ITestOutputHelper? output, string text)
    {
        try { output?.WriteLine(text); } catch { /* test finished */ }
    }
}
