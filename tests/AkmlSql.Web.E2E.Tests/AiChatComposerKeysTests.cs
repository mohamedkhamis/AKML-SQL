using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// The web chat composer's key contract, verified in a real browser.
///
/// <para>The desktop panel (<c>AkmlSql.Shell.Shared/Ai/AiChatPanel.cs</c>) sends on unmodified
/// Enter and inserts a newline on Shift+Enter. The web panel shipped the OPPOSITE contract
/// (Ctrl+Enter sends, Enter adds a newline) — same product, same feature, both edited in the same
/// commit without reconciling them, so a user who learned one surface got the wrong behaviour on
/// the other. The web panel now matches the desktop.</para>
///
/// <para>Why this has to be an E2E rather than a bUnit test: a <c>&lt;textarea&gt;</c> inserts the
/// newline as the DEFAULT ACTION of Enter's keypress. Suppressing it depends on Blazor flushing a
/// dynamic <c>@onkeypress:preventDefault</c> flag to the DOM between the keydown handler and the
/// keypress dispatch — real browser event-ordering behaviour that no headless component test
/// exercises. The failure mode if that ordering does not hold is precisely a stray newline left in
/// the box after a send, which is what <see cref="Enter_sends_and_leaves_no_stray_newline"/>
/// asserts.</para>
///
/// <para><c>[Trait("Category","BridgeE2E")]</c> keeps these out of the default unit run, and they
/// are <see cref="SkippableFactAttribute"/> so they skip rather than fail when the Playwright
/// browser binaries or the built web bundle are absent — the same discipline as
/// <see cref="UserStory5AiTests"/>.</para>
///
/// <para><b>Build the web app in DEBUG before running these.</b> <see cref="WebAppFixture"/> starts
/// it with <c>dotnet run --no-build -c Debug</c>, so a Release-only build leaves a STALE Debug
/// bundle serving on :5000 and the tests silently exercise whatever code was there before —
/// a false negative that looks exactly like a broken fix. Run
/// <c>dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug</c> first.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class AiChatComposerKeysTests
{
    private static async Task<IBrowser?> TryLaunchAsync(IPlaywright pw)
    {
        try { return await pw.Chromium.LaunchAsync(); }
        catch (PlaywrightException) { return null; } // browsers not installed -> caller skips
    }

    private static async Task<WebAppFixture> StartWebOrSkipAsync()
    {
        try { return await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
    }

    /// <summary>
    /// Drives the app to a chat panel with an active (mock) provider, and returns the composer.
    /// </summary>
    private static async Task<ILocator> OpenChatAsync(IPage page, WebAppFixture web, MockAiProvider mock)
    {
        await page.GotoAsync(web.Url + "settings/ai");
        await page.GetByLabel("Provider").SelectOptionAsync("ollama");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Model" }).FillAsync("mock");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint" }).FillAsync(mock.ChatCompletionsUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.GetByRole(AriaRole.Radio).First.CheckAsync();
        // Save + active-provider both commit to IndexedDB asynchronously (see UserStory5AiTests).
        await page.WaitForTimeoutAsync(750);

        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync("[data-testid='ai-button']");
        await page.WaitForTimeoutAsync(1_000);   // let OnInitializedAsync resolve the active provider
        await page.Locator("[data-testid='ai-button']").ClickAsync();
        await page.Locator("[data-testid='ai-tab-chat']").ClickAsync();

        var composer = page.Locator(".akml-chat-input");
        await composer.WaitForAsync(new() { Timeout = 10_000 });
        return composer;
    }

    /// <summary>
    /// Plain Enter sends — and the newline Enter would otherwise type must not survive into the
    /// emptied box. A stray "\n" here is the exact regression this contract risks.
    /// </summary>
    [SkippableFact]
    public async Task Enter_sends_and_leaves_no_stray_newline()
    {
        using var mock = MockAiProvider.StartOllama();
        await using var web = await StartWebOrSkipAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(pw);
        Skip.If(browser is null, "Playwright Chromium not installed (run playwright.ps1 install chromium).");
        var page = await browser!.NewPageAsync();

        var composer = await OpenChatAsync(page, web, mock);

        await composer.FillAsync("what does this query do");
        await composer.PressAsync("Enter");

        // The question became a user turn — i.e. Enter really sent it.
        var userTurn = page.Locator(".akml-chat-turn.akml-chat-role-user .akml-chat-turn-text");
        await userTurn.First.WaitForAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(userTurn.First).ToContainTextAsync("what does this query do");

        // ...and the composer is genuinely EMPTY. If preventDefault did not land in time, the
        // browser's own newline lands here after SendAsync cleared _pending, and oninput writes it
        // back — so this is the assertion that catches a broken suppression.
        await Assertions.Expect(composer).ToHaveValueAsync(string.Empty);

        Assert.NotEmpty(mock.Captures);
    }

    /// <summary>
    /// Shift+Enter must still insert a newline and must NOT send — the other half of the contract,
    /// and the half a blanket static <c>preventDefault</c> would silently break.
    /// </summary>
    [SkippableFact]
    public async Task ShiftEnter_inserts_a_newline_and_does_not_send()
    {
        using var mock = MockAiProvider.StartOllama();
        await using var web = await StartWebOrSkipAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await TryLaunchAsync(pw);
        Skip.If(browser is null, "Playwright Chromium not installed (run playwright.ps1 install chromium).");
        var page = await browser!.NewPageAsync();

        var composer = await OpenChatAsync(page, web, mock);

        await composer.FillAsync("first line");
        await composer.PressAsync("Shift+Enter");
        // Focus stays in the composer after the chord, so type straight into it.
        await page.Keyboard.TypeAsync("second line");

        // Nothing was sent...
        await page.WaitForTimeoutAsync(1_000);
        Assert.Equal(0, await page.Locator(".akml-chat-turn.akml-chat-role-user").CountAsync());
        Assert.Empty(mock.Captures);

        // ...and the newline is really in the box, between the two lines.
        var value = await composer.InputValueAsync();
        Assert.Equal("first line\nsecond line", value.Replace("\r\n", "\n"));
    }
}
