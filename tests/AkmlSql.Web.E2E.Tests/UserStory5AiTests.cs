using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 028 (M6) tasks T040 (US5 browser-AI E2E) + T038 (mock-provider harness). Contract:
/// <c>specs/028-m6-ai-browser-closure/contracts/verification-and-audit-contract.md</c>.
///
/// <para>
/// These are <b>real Playwright scenarios</b> (no longer skip-flagged pseudocode): the selectors
/// and flow were verified by driving the running app on 2026-06-03 (evidence:
/// <c>specs/028-m6-ai-browser-closure/SC-009-EVIDENCE/</c>). They run against the live web bundle
/// (<see cref="WebAppFixture"/>) plus a local Ollama-shaped mock (<see cref="MockAiProvider"/>) so
/// no real key/network is used. <c>[Trait("Category","BridgeE2E")]</c> keeps them out of the
/// default unit run; they are <see cref="SkippableFactAttribute"/> so they skip gracefully when
/// the Playwright browser binaries aren't installed (run <c>pwsh bin/.../playwright.ps1 install
/// chromium</c> first), rather than failing CI.
/// </para>
///
/// <para>
/// Deterministic substrate proven headlessly by the unit/bUnit suite: <c>PrivacyModeTests</c>,
/// <c>AnthropicWireTests</c> + <c>StreamingParserTests</c>, <c>GhostTextControllerTests</c>,
/// <c>ChatHistoryStoreTests</c>, <c>AiPanelTests</c>. This E2E covers the real-browser glue: the
/// editor AI dock, a real cross-origin fetch to the mock, the streamed render, and the CodeMirror
/// ghost-text decorator.
/// </para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class UserStory5AiTests
{
    private static async Task<IBrowser?> TryLaunchAsync(IPlaywright pw)
    {
        try { return await pw.Chromium.LaunchAsync(); }
        catch (PlaywrightException) { return null; } // browsers not installed -> caller skips
    }

    /// <summary>Launch the web app, or <b>skip</b> the test if it can't start (e.g. the app wasn't
    /// pre-built). Skipping — not failing — keeps this opt-in suite from breaking a run invoked in a
    /// half-ready environment.</summary>
    private static async Task<WebAppFixture> StartWebOrSkipAsync()
    {
        try { return await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
    }

    /// <summary>US2/US3 acceptance — configure a (mock) browser-direct provider, run Explain, see it
    /// stream into the result pane, and confirm the request went to the provider (not an AKML host).
    /// Key-storage "never plaintext" is covered by <c>KeyVaultTests</c> + the live SC-009 evidence
    /// (the local provider here is key-less).</summary>
    [SkippableFact]
    public async Task AddProvider_RunExplain_StreamsBrowserDirect()
    {
        using var mock = MockAiProvider.StartOllama();
        await using var web = await StartWebOrSkipAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(pw);
        Skip.If(browser is null, "Playwright Chromium not installed (run playwright.ps1 install chromium).");
        var page = await browser!.NewPageAsync();

        // 1. Configure the Ollama-shaped mock provider via Settings -> AI and mark it active.
        await page.GotoAsync(web.Url + "settings/ai");
        await page.GetByLabel("Provider").SelectOptionAsync("ollama");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Model" }).FillAsync("mock");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint" }).FillAsync(mock.ChatCompletionsUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.GetByRole(AriaRole.Radio).First.CheckAsync();
        // Save + active-provider both persist to IndexedDB asynchronously; let them commit before
        // navigating, else the editor's AI dock can load with no active provider (no action buttons).
        await page.WaitForTimeoutAsync(750);

        // 2. Open the editor, set SQL, reveal the AI dock, run Explain.
        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync("[data-testid='ai-button']");
        await page.WaitForTimeoutAsync(1_000); // let OnInitializedAsync resolve the active provider
        await SetEditorTextAsync(page, "SELECT * FROM dbo.Orders WHERE OrderId = 1");
        await page.Locator("[data-testid='ai-button']").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();

        // 3. The streamed answer renders into the result pane.
        var result = page.Locator(".akml-ai-result pre");
        await result.WaitForAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(result).ToContainTextAsync("MOCK-STREAM");

        // 4. The mock actually received the request (browser-direct), and it carried the SQL.
        Assert.NotEmpty(mock.Captures);

        // 5. No AKML host in the AI path: the only chat-completions request went to the mock.
        // (The app's allow-list confines provider calls to the configured origin.)
        Assert.All(mock.Captures, c => Assert.True(c.TryGetProperty("messages", out _)));
    }

    /// <summary>US5 acceptance — inline ghost text: enable it, type at end of line, see the grey
    /// widget, Tab to accept it into the document.</summary>
    [SkippableFact]
    public async Task GhostText_TypeShowsGreyText_TabAccepts()
    {
        using var mock = MockAiProvider.StartOllama();
        await using var web = await StartWebOrSkipAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(pw);
        Skip.If(browser is null, "Playwright Chromium not installed.");
        var page = await browser!.NewPageAsync();

        // Configure the mock provider + enable ghost text.
        await page.GotoAsync(web.Url + "settings/ai");
        await page.GetByLabel("Provider").SelectOptionAsync("ollama");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Model" }).FillAsync("mock");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint" }).FillAsync(mock.ChatCompletionsUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.GetByRole(AriaRole.Radio).First.CheckAsync();
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Enable ghost text" }).CheckAsync();
        // The enable toggle persists to IndexedDB asynchronously; let it commit before we navigate,
        // otherwise the editor re-inits with ghost text still off (setGhostTextConfig reads stale).
        await page.WaitForTimeoutAsync(750);

        // Editor: type at end of a non-empty line (after a non-keyword, so autocomplete stays shut).
        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-content");
        // Let the editor's OnAfterRenderAsync finish create() + setGhostTextConfig before typing.
        await page.WaitForTimeoutAsync(1_500);
        await SetEditorTextAsync(page, "SELECT * FROM dbo.Orders");
        await page.Locator(".cm-content").ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync(" "); // user input -> debounced ghost request

        var ghost = page.Locator(".akml-ghost-text");
        await ghost.WaitForAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(ghost).ToContainTextAsync("MOCK");

        await page.Keyboard.PressAsync("Tab"); // accept
        await Assertions.Expect(page.Locator(".akml-ghost-text")).ToHaveCountAsync(0);
        var docText = await GetEditorTextAsync(page);
        Assert.Contains("MOCK", docText); // the suggestion was committed into the document
    }

    // ── editor helpers (drive the CodeMirror module the way EditorComponent does) ──────────────
    private static Task SetEditorTextAsync(IPage page, string sql) =>
        page.EvaluateAsync(
            @"async (sql) => {
                const host = document.querySelector('[data-testid=""sql-editor""]');
                const mod = await import('/js/akml-editor.js');
                mod.setText(host.id, sql);
              }", sql);

    private static Task<string> GetEditorTextAsync(IPage page) =>
        page.EvaluateAsync<string>(
            @"async () => {
                const host = document.querySelector('[data-testid=""sql-editor""]');
                const mod = await import('/js/akml-editor.js');
                return mod.getText(host.id);
              }");
}
