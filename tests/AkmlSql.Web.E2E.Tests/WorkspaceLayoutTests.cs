using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 041 (T119, research R33–R38, R70) — quickstart §3, "Workspace", in a real browser: the panels' toggles,
/// edge strips, splitters (pointer and keyboard), maximise, folding in a narrow window, the View menu and the
/// palette, F6 pane cycling, Reset layout, and the layout surviving a reload and a browser restart.
///
/// <para><c>BridgeE2E</c>, <see cref="SkippableFactAttribute"/>: skipped without a local SQL Server, DEBUG builds
/// of AkmlSql.Web and AkmlSql.Engine, or Playwright Chromium. Run serially
/// (<c>-- xUnit.ParallelizeTestCollections=false</c>).</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class WorkspaceLayoutTests
{
    private readonly ITestOutputHelper _output;

    public WorkspaceLayoutTests(ITestOutputHelper output) => _output = output;

    private static ILocator Id(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static async Task<LocatorBoundingBoxResult> BoxAsync(IPage page, string selector)
    {
        var box = await page.Locator(selector).BoundingBoxAsync();
        Assert.NotNull(box);
        return box!;
    }

    private static Task<float> WidthAsync(IPage page, string selector) => BoxAsync(page, selector).ContinueWith(t => t.Result.Width);

    private static Task<float> HeightAsync(IPage page, string selector) => BoxAsync(page, selector).ContinueWith(t => t.Result.Height);

    private static async Task ClickEditorAsync(IPage page) => await page.ClickAsync("[data-testid='sql-editor'] .cm-content");

    private static async Task ViewMenuAsync(IPage page, string itemTestId)
    {
        await Id(page, "view-menu").ClickAsync();
        await Id(page, itemTestId).ClickAsync();
    }

    private static async Task DragAsync(IPage page, string splitterTestId, float dx, float dy)
    {
        var box = await BoxAsync(page, $"[data-testid='{splitterTestId}']");
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + dx / 2, y + dy / 2, new() { Steps = 4 });
        await page.Mouse.MoveAsync(x + dx, y + dy, new() { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(200);
    }

    private static async Task<string?> FocusedTestIdAsync(IPage page) =>
        await page.EvaluateAsync<string?>("() => document.activeElement?.closest('[data-testid]')?.dataset.testid ?? null");

    private static async Task AssertNear(float expected, Func<Task<float>> actual, float tolerance = 2)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        float last;
        while (true)
        {
            last = await actual();
            if (Math.Abs(last - expected) <= tolerance || DateTime.UtcNow > deadline) break;
            await Task.Delay(100);
        }
        Assert.InRange(last, expected - tolerance, expected + tolerance);
    }

    private static async Task WaitForEditorAsync(IPage page) =>
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 60_000 });

    [SkippableFact]
    public async Task Hiding_the_side_panels_gives_the_editor_the_width_and_a_strip_brings_them_back()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        // Default: Schema on the left, AI hidden (its strip on the right).
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "workspace-edge-right")).ToBeVisibleAsync();
        await AssertNear(260, () => WidthAsync(page, "#akml-ws-schema"));

        await ClickEditorAsync(page);
        await Keys.AssertClaimedAsync(page, "F8");
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeHiddenAsync();
        var strip = Id(page, "workspace-edge-left");
        await Assertions.Expect(strip).ToBeVisibleAsync();
        await Assertions.Expect(strip).ToHaveAttributeAsync("title", "Show Schema (F8)");

        // SC-003: both side panels hidden → the editor has at least 95 % of the window.
        Assert.True(await WidthAsync(page, "#akml-ws-centre") >= 0.95 * 1440, "The editor column is narrower than 95 % of the window.");

        await strip.ClickAsync();
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
        await AssertNear(260, () => WidthAsync(page, "#akml-ws-schema"));

        // At 1440 px the two default panels do not fit beside the editor's minimum: opening AI by hand folds Schema.
        await ViewMenuAsync(page, "view-toggle-ai");
        await Assertions.Expect(Id(page, "workspace-ai")).ToBeVisibleAsync();
        await AssertNear(380, () => WidthAsync(page, "#akml-ws-ai"));
        await Assertions.Expect(Id(page, "workspace-edge-left")).ToBeVisibleAsync();

        await Id(page, "ai-close").ClickAsync();
        await Assertions.Expect(Id(page, "workspace-ai")).ToBeHiddenAsync();
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Ctrl_R_hides_the_results_and_executing_brings_them_back_even_with_every_region_hidden()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await ClickEditorAsync(page);
        await Keys.AssertClaimedAsync(page, "Control+R");
        await Assertions.Expect(Id(page, "workspace-results")).ToBeHiddenAsync();
        await Assertions.Expect(Id(page, "workspace-edge-bottom")).ToBeVisibleAsync();

        await ClickEditorAsync(page);
        await Keys.AssertClaimedAsync(page, "F8");
        await ViewMenuAsync(page, "view-toggle-nav");
        await Assertions.Expect(Id(page, "workspace-edge-nav")).ToBeVisibleAsync();
        // Every region hidden: nothing comes back on its own during the session (only on the next load).
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeHiddenAsync();
        await Assertions.Expect(Id(page, "workspace-results")).ToBeHiddenAsync();

        await s.ExecuteAsync("SELECT 1 AS a;");

        await Assertions.Expect(Id(page, "workspace-results")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "results-tab-results")).ToHaveAttributeAsync("aria-selected", "true");

        // Hidden and shown again, the (virtualised) grid still draws its rows (T124).
        await ClickEditorAsync(page);
        await Keys.AssertClaimedAsync(page, "Control+R");
        await Assertions.Expect(Id(page, "workspace-results")).ToBeHiddenAsync();
        await Keys.AssertClaimedAsync(page, "Control+R");
        await Assertions.Expect(Id(page, "results-cell-0-0")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "results-cell-0-0")).ToHaveTextAsync("1");

        // The nav strip brings the bar back.
        await Id(page, "workspace-edge-nav").ClickAsync();
        await Assertions.Expect(page.Locator("nav.akml-nav")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Splitters_resize_by_pointer_and_keyboard_within_the_minimums()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await DragAsync(page, "workspace-splitter-left", 60, 0);
        await AssertNear(320, () => WidthAsync(page, "#akml-ws-schema"));
        await DragAsync(page, "workspace-splitter-left", -400, 0);
        await AssertNear(180, () => WidthAsync(page, "#akml-ws-schema"));   // the Schema minimum

        var splitter = Id(page, "workspace-splitter-left");
        await splitter.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "196");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await Assertions.Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "260");
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(splitter).ToHaveAttributeAsync("aria-valuenow", "180");
        await AssertNear(180, () => WidthAsync(page, "#akml-ws-schema"));

        // Enter collapses (focus lands on the strip); Enter on the strip restores (focus back on the splitter).
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeHiddenAsync();
        await AssertFocusAsync(page, "workspace-edge-left");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
        await AssertNear(180, () => WidthAsync(page, "#akml-ws-schema"));
        await AssertFocusAsync(page, "workspace-splitter-left");

        // The results splitter: dragging up makes the pane taller.
        float before = await HeightAsync(page, "#akml-ws-results");
        await DragAsync(page, "workspace-splitter-bottom", 0, -100);
        await AssertNear(before + 100, () => HeightAsync(page, "#akml-ws-results"), 3);

        // Tab still leaves a focused splitter (FR-103).
        await Id(page, "workspace-splitter-bottom").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.NotEqual("workspace-splitter-bottom", await FocusedTestIdAsync(page));
    }

    private static async Task AssertFocusAsync(IPage page, string testId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? focused;
        do
        {
            focused = await FocusedTestIdAsync(page);
            if (focused == testId) return;
            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);
        Assert.Equal(testId, focused);
    }

    [SkippableFact]
    public async Task Maximise_collapses_the_editor_row_and_one_click_restores_the_split()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;
        float split = await HeightAsync(page, "#akml-ws-results");

        await ViewMenuAsync(page, "view-toggle-maximise");
        await Assertions.Expect(Id(page, "workspace")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("akml-workspace--maximised"));
        float workspace = await HeightAsync(page, "[data-testid='workspace']");
        await AssertNear(workspace, () => HeightAsync(page, "#akml-ws-results"), 3);

        await Id(page, "view-menu").ClickAsync();
        await Assertions.Expect(Id(page, "view-toggle-maximise")).ToContainTextAsync("Restore results");
        await Id(page, "view-toggle-maximise").ClickAsync();
        await AssertNear(split, () => HeightAsync(page, "#akml-ws-results"), 3);
        Assert.True(await HeightAsync(page, "#akml-ws-centre") >= 320, "The editor did not get its height back.");
    }

    [SkippableFact]
    public async Task The_layout_survives_a_reload_and_Reset_layout_brings_the_defaults_back_for_good()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await ClickEditorAsync(page);
        await Keys.AssertClaimedAsync(page, "F8");
        await ViewMenuAsync(page, "view-toggle-ai");
        await Id(page, "ai-tab-chat").ClickAsync();
        await DragAsync(page, "workspace-splitter-bottom", 0, -80);
        float results = await HeightAsync(page, "#akml-ws-results");
        await page.WaitForTimeoutAsync(300);   // the IndexedDB writes

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForEditorAsync(page);

        await Assertions.Expect(Id(page, "workspace-edge-left")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "workspace-ai")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "ai-tab-chat")).ToHaveAttributeAsync("aria-selected", "true");   // the shared TabStrip (US5)
        await AssertNear(results, () => HeightAsync(page, "#akml-ws-results"), 3);

        await ViewMenuAsync(page, "workspace-reset-layout");
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "workspace-ai")).ToBeHiddenAsync();

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForEditorAsync(page);
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "workspace-edge-right")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task The_layout_survives_closing_and_reopening_the_browser()
    {
        WebAppFixture web;
        try { web = await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it Debug first): {ex.Message}"); }
        await using var _ = web;
        using var pw = await Playwright.CreateAsync();
        var profile = Path.Combine(Path.GetTempPath(), "akml-e2e-profile-" + Guid.NewGuid().ToString("N"));
        var options = new BrowserTypeLaunchPersistentContextOptions { ViewportSize = new() { Width = 1440, Height = 900 } };
        try
        {
            IBrowserContext context;
            try { context = await pw.Chromium.LaunchPersistentContextAsync(profile, options); }
            catch (PlaywrightException) { throw new SkipException("Playwright Chromium not installed (run playwright.ps1 install chromium)."); }

            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
            await page.GotoAsync(web.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await WaitForEditorAsync(page);
            await ClickEditorAsync(page);
            await page.Keyboard.PressAsync("F8");
            await Assertions.Expect(Id(page, "workspace-edge-left")).ToBeVisibleAsync();
            await page.WaitForTimeoutAsync(300);
            await context.CloseAsync();

            context = await pw.Chromium.LaunchPersistentContextAsync(profile, options);
            page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
            await page.GotoAsync(web.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await WaitForEditorAsync(page);
            await Assertions.Expect(Id(page, "workspace-edge-left")).ToBeVisibleAsync();
            await Assertions.Expect(Id(page, "workspace-schema")).ToBeHiddenAsync();
            await context.CloseAsync();
        }
        finally
        {
            try { Directory.Delete(profile, recursive: true); } catch { /* the browser may still hold a file */ }
        }
    }

    [SkippableFact]
    public async Task At_1100_px_both_side_panels_fold_and_a_120_character_line_fits()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output,
            contextOptions: new() { ViewportSize = new() { Width = 1100, Height = 700 } });
        var page = s.Page;

        await Assertions.Expect(Id(page, "workspace-edge-left")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "workspace-edge-right")).ToBeVisibleAsync();
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeHiddenAsync();

        await s.SetEditorTextAsync(new string('x', 120));
        var metrics = await page.EvaluateAsync<double[]>(@"() => {
            const scroller = document.querySelector(""[data-testid='sql-editor'] .cm-scroller"");
            const gutter = document.querySelector(""[data-testid='sql-editor'] .cm-gutters"");
            const line = document.querySelector(""[data-testid='sql-editor'] .cm-line"");
            const range = document.createRange(); range.selectNodeContents(line);
            return [scroller.scrollWidth - scroller.clientWidth, gutter ? gutter.getBoundingClientRect().width : 0,
                    range.getBoundingClientRect().width, scroller.offsetWidth - scroller.clientWidth];
        }");
        _output.WriteLine($"T110: overflow {metrics[0]} px, gutter {metrics[1]} px, 120 characters {metrics[2]} px, scrollbar {metrics[3]} px");
        Assert.True(metrics[0] <= 0, $"A 120-character line scrolls sideways by {metrics[0]} px.");
        var pageOverflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(pageOverflow <= 0, $"The page is {pageOverflow} px wider than the window.");

        // Wider: the Schema panel, never hidden by hand, comes back.
        await page.SetViewportSizeAsync(1600, 900);
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task At_800_px_the_page_scrolls_as_a_whole_and_Execute_stays_visible()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output,
            contextOptions: new() { ViewportSize = new() { Width = 800, Height = 700 } });
        var page = s.Page;

        await Assertions.Expect(Id(page, "execute-button")).ToBeInViewportAsync();
        var mainScroll = await page.EvaluateAsync<int>("() => { const m = document.querySelector('main'); return m.scrollWidth - m.clientWidth; }");
        Assert.True(mainScroll > 0, "At 800 px the editor's minimum should make the main area scroll.");
        await s.ExecuteAsync("SELECT 1 AS a;");
        await Assertions.Expect(Id(page, "results-tab-results")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [SkippableFact]
    public async Task At_150_percent_zoom_the_splitters_and_strips_can_still_be_hit()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output,
            contextOptions: new() { ViewportSize = new() { Width = 1280, Height = 1000 }, DeviceScaleFactor = 1.5f });
        var page = s.Page;
        await page.EvaluateAsync("() => { document.body.style.zoom = '1.5'; }");
        await page.WaitForTimeoutAsync(300);

        // 1280 / 1.5 ≈ 853 CSS px: both side panels fold to strips. (1000 high leaves the results pane room to grow
        // under a toolbar that wraps onto a second row at this width.)
        var strip = Id(page, "workspace-edge-left");
        await Assertions.Expect(strip).ToBeVisibleAsync();
        await strip.ClickAsync();
        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();

        float before = await HeightAsync(page, "#akml-ws-results");
        await Id(page, "workspace-splitter-bottom").ClickAsync();   // the hit area takes the pointer
        await Id(page, "workspace-splitter-bottom").FocusAsync();
        await page.Keyboard.PressAsync("Shift+ArrowUp");
        // The resize commits through the layout store and a re-render: wait for it rather than read at once.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        float after;
        while ((after = await HeightAsync(page, "#akml-ws-results")) <= before && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.True(after > before, $"The results splitter did not respond at 150 % zoom ({before} → {after}).");
    }

    [SkippableFact]
    public async Task The_View_menu_and_the_palette_list_every_toggle_with_its_key()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output);
        var page = s.Page;

        await Id(page, "view-menu").ClickAsync();
        var menu = Id(page, "view-menu-list");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        foreach (var item in new[] { "view-toggle-nav", "view-toggle-schema", "view-toggle-results", "view-toggle-ai", "view-toggle-maximise", "workspace-reset-layout" })
        {
            await Assertions.Expect(Id(page, item)).ToBeVisibleAsync();
        }
        await Assertions.Expect(Id(page, "view-toggle-schema")).ToContainTextAsync("F8");
        await Assertions.Expect(Id(page, "view-toggle-results")).ToContainTextAsync("Ctrl+R");
        await Assertions.Expect(Id(page, "view-toggle-schema")).ToHaveAttributeAsync("aria-checked", "true");
        await page.Keyboard.PressAsync("Escape");

        await ClickEditorAsync(page);
        await page.Keyboard.PressAsync("Control+Shift+P");
        await Assertions.Expect(Id(page, "command-palette")).ToBeVisibleAsync();
        await page.Keyboard.InsertTextAsync("panel");
        var rows = string.Join("\n", await Id(page, "command-palette-row").AllInnerTextsAsync());
        Assert.Contains("Schema panel", rows);
        Assert.Contains("AI panel", rows);
        await page.Keyboard.PressAsync("Escape");
    }

    [SkippableFact]
    public async Task F6_cycles_editor_results_Schema_AI_toolbar()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(_output,
            contextOptions: new() { ViewportSize = new() { Width = 1920, Height = 1000 } });
        var page = s.Page;
        await ViewMenuAsync(page, "view-toggle-ai");
        await Assertions.Expect(Id(page, "workspace-ai")).ToBeVisibleAsync();
        await s.ExecuteAsync("SELECT 1 AS a;");   // the results pane gets something to focus

        await ClickEditorAsync(page);
        async Task<string> RegionAsync() => await page.EvaluateAsync<string>(@"() => {
            const a = document.activeElement;
            for (const [name, sel] of [['centre', '#akml-ws-centre'], ['results', '#akml-ws-results'], ['schema', '#akml-ws-schema'], ['ai', '#akml-ws-ai'], ['toolbar', '.akml-editor-toolbar']])
                if (document.querySelector(sel)?.contains(a)) return name;
            return 'none';
        }");

        Assert.Equal("centre", await RegionAsync());
        var seen = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            await Keys.AssertClaimedAsync(page, "F6");
            seen.Add(await RegionAsync());
        }
        Assert.Equal(new[] { "results", "schema", "ai", "toolbar", "centre" }, seen);

        await Keys.AssertClaimedAsync(page, "Shift+F6");
        Assert.Equal("toolbar", await RegionAsync());
    }

    [SkippableFact]
    public async Task Without_a_connection_the_Schema_panel_offers_to_connect()
    {
        WebAppFixture web;
        try { web = await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it Debug first): {ex.Message}"); }
        await using var _ = web;
        using var pw = await Playwright.CreateAsync();
        IBrowser browser;
        try { browser = await pw.Chromium.LaunchAsync(); }
        catch (PlaywrightException) { throw new SkipException("Playwright Chromium not installed (run playwright.ps1 install chromium)."); }
        await using var __ = browser;

        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        await page.GotoAsync(web.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await WaitForEditorAsync(page);

        await Assertions.Expect(Id(page, "workspace-schema")).ToBeVisibleAsync();
        var connect = Id(page, "schema-connect");
        await Assertions.Expect(connect).ToHaveTextAsync("Connect to SQL Server…");
        await connect.ClickAsync();
        await Assertions.Expect(Id(page, "connection-manager")).ToBeVisibleAsync();
    }
}
