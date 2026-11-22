using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047 (T123, DOC-001…003) — the web screenshots the documentation shows, taken from the app itself: the status bar's
/// "No engine paired yet." with Use this computer's engine, the notice when nothing answers there (with Add engine…), and the
/// Connect to database dialog with "Remember the password on the engine machine" and its hint. They are written to
/// <c>doc/images/web/</c>, which <c>doc/topics/connecting.md</c> and <c>doc/topics/troubleshooting.md</c> show. (Settings ›
/// Connections › Paired browsers needs an engine that keeps pairings, a LAN install; it is not taken here.)
///
/// <para>Opt-in, like <c>WorkspaceScreenshotTour</c>: set <c>AKML_CAPTURE_DOC_SCREENSHOTS=1</c>, with Debug builds of the web app
/// and the engine. It changes files in the repository, so it is never part of a routine run.</para>
/// </summary>
[Trait("Category", "Screenshots")]
public sealed class DocsWebScreenshotTour(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Capture_the_connection_screenshots_the_docs_show()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("AKML_CAPTURE_DOC_SCREENSHOTS") == "1",
            "Set AKML_CAPTURE_DOC_SCREENSHOTS=1 to retake the documentation screenshots.");
        WebAppFixture web;
        try { web = await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
        await using var _ = web;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchForTestsAsync();
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }, ReducedMotion = ReducedMotion.Reduce, ColorScheme = ColorScheme.Light,
        });
        var page = await context.NewPageAsync();
        var folder = Path.Combine(SandboxEngine.RepoRoot(), "doc", "images", "web");
        Directory.CreateDirectory(folder);
        async Task ShootAsync(ILocator target, string name)
        {
            await target.ScreenshotAsync(new() { Path = Path.Combine(folder, name), Animations = ScreenshotAnimations.Disabled });
            output.WriteLine($"wrote doc/images/web/{name}");
        }

        // 1. A new browser with no engine: the status bar says "No engine paired yet." and offers this computer's engine.
        await page.GotoAsync(web.Url);
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
        await Assertions.Expect(page.GetByTestId("engine-issue")).ToContainTextAsync("No engine paired yet.", new() { Timeout = 30_000 });
        await ShootAsync(page.Locator(".akml-app-footer"), "status-no-engine.png");

        // 2. Nothing answers on this computer's engine port: the notice names the address and offers Add engine….
        await page.GetByTestId("engine-use-local").ClickAsync();
        await Assertions.Expect(page.GetByTestId("local-engine-failure")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("local-engine-add")).ToBeVisibleAsync();
        await ShootAsync(page.Locator(".akml-app-footer"), "status-engine-not-answering.png");
        await page.GetByTestId("local-engine-dismiss").ClickAsync();

        // 3. A connected engine (the Remember option needs one that keeps passwords). Paired browsers are listed only by an
        //    engine that keeps pairings (LAN mode, PIN): a loopback sandbox engine has none, so that shot needs a LAN install.
        await using var engine = await SandboxEngine.StartAsync();
        await WebAppFixture.PairAsync(page, engine.Port);

        // 4. Connect to database, SQL authentication: Remember the password on the engine machine, with its hint.
        await ShellPolishTests.NavigateAsync(page, "editor");
        await page.GetByTestId("status-connection").ClickAsync();
        var dialog = page.GetByTestId("connection-manager");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await page.GetByTestId("conn-kind-sqlserver").ClickAsync();
        await page.FillAsync("[data-testid='conn-server-input']", "localhost");
        await page.GetByTestId("conn-auth-sql").CheckAsync();
        await page.FillAsync("[data-testid='conn-login-input']", "report_reader");
        await Assertions.Expect(page.GetByTestId("conn-sql-remember-help")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ShootAsync(dialog, "connect-remember.png");
    }
}
