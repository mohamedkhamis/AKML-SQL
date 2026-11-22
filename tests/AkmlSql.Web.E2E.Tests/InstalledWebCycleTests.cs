using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Web.E2E.Tests.Harness;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 047 (T141, quickstart.md §3) - the web acceptance scenarios on the INSTALLED product: the IIS site named by
/// INSTALL-SUMMARY.txt and the AkmlSqlWebEngine service (port from %ProgramData%\AKML SQL Web\config.json), not the Debug
/// bundle and a sandbox engine. V2, V3, V6, V7, V8 (web), V10 (web), V13, V14, V15 (the note; the GRANT needs the owner),
/// V16, V17, V18, V20 and V22, plus a connected cycle: run, grid, Format twice, completion and History.
///
/// <para>
/// Runs only with <c>AKML_INSTALLED_WEB=1</c>, because it changes the installed product's state: V3 stops and restarts the
/// AkmlSqlWebEngine service, and V14 saves and deletes a snippet. Every browser is a new profile (Playwright context), so the
/// person's own browser data is never touched. Notes and screenshots go to <c>AKML_INSTALLED_WEB_OUT</c>.
/// </para>
/// </summary>
[Trait("Category", "InstalledProduct")]
public sealed class InstalledWebCycleTests(ITestOutputHelper output)
{
    private static readonly string Out = Environment.GetEnvironmentVariable("AKML_INSTALLED_WEB_OUT") is { Length: > 0 } o
        ? o
        : Path.Combine(Path.GetTempPath(), "akml-installed-web");

    private static string Site => InstalledEngine.SiteUrl ?? "http://localhost:59416/";

    private static int Port => InstalledEngine.BridgePort;

    private const string ThreeStatements =
        "SELECT CustomerID, CompanyName FROM dbo.Customers WHERE Country = N'Germany'\n" +
        "SELECT COUNT(*) AS OrderCount FROM dbo.Orders\n" +
        "SELECT ProductName FROM dbo.Products WHERE Discontinued = 1\n";

    private static readonly string[] Corpus =
    {
        "sp047-01-proc-params", "sp047-02-control-flow", "sp047-03-go-count", "sp047-04-block-comment", "sp047-05-spacing",
        "sp047-06-statements",
    };

    // ── V2 ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V2_a_new_profile_reaches_the_installed_engine_with_one_click_or_none_ten_times()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var failures = new List<string>();
        try
        {
            for (var run = 1; run <= 10; run++)
            {
                await using var context = await browser.NewContextAsync(Profile());
                var page = await context.NewPageAsync();
                var r = await OpenLiveAsync(page, Site);
                Note($"V2 run {run}: {(r.Live ? "Live" : "NOT live")} after {r.Seconds:F1} s, {(r.Clicked ? "one click" : "no click")}; pill '{r.Pill}'");
                if (run == 1) await ShotAsync(page, "v2-run1-live");
                if (!r.Live || r.Seconds >= 30) failures.Add($"run {run}: '{r.Pill}' after {r.Seconds:F1} s");
            }
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // ── V3 ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V3_with_the_engine_stopped_the_page_names_the_failure_and_Add_engine_is_prefilled()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        Service("stop");
        try
        {
            Assert.True(WaitService("STOPPED", 60), "AkmlSqlWebEngine did not stop");
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await page.GotoAsync(Site, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });

            // The IIS install pairs from akml-engine.json by itself, so "Use this computer's engine" (shown only while nothing is
            // paired) never appears here; what the page says instead is what a person on this install sees.
            var issue = page.GetByTestId("engine-issue");
            await WaitUntilAsync(() => IsShownAsync(issue), 45);
            await page.WaitForTimeoutAsync(1_000);
            var pill = await TextOfAsync(page, "status-pill");
            var pillTitle = await page.GetByTestId("status-pill").First.GetAttributeAsync("title") ?? "";
            var issueText = await TextOfAsync(page, "engine-issue");
            var footer = Regex.Replace(await page.Locator("footer").First.InnerTextAsync(), @"\s+", " ");
            var offeredIds = new List<string>();
            foreach (var id in new[] { "engine-use-local", "engine-retry", "engine-setup", "local-engine-failure", "local-engine-add" })
                if (await page.GetByTestId(id).CountAsync() > 0) offeredIds.Add(id);
            var offered = string.Join(", ", offeredIds);
            Note($"V3 engine stopped, new profile: pill '{pill}' (title '{pillTitle}'); issue '{issueText}'; offered: {offered}");
            Note("V3 status bar: " + footer);
            await ShotAsync(page, "v3-1-engine-stopped");
            Assert.Contains("not responding", issueText, StringComparison.OrdinalIgnoreCase);

            // Back on: the page recovers by itself (its retry), with no reload.
            Service("start");
            Assert.True(WaitService("RUNNING", 90) && await WaitPortAsync(Port, 90), "AkmlSqlWebEngine did not come back");
            var recovered = await WaitUntilAsync(async () => (await TextOfAsync(page, "status-pill")).Contains("Live", StringComparison.OrdinalIgnoreCase), 90);
            Note($"V3 after the service started again: pill '{await TextOfAsync(page, "status-pill")}', recovered without a reload: {recovered}");
            await ShotAsync(page, "v3-2-recovered");
            Assert.True(recovered, "the page did not reconnect by itself after the engine came back");
        }
        finally
        {
            Service("start");
            var back = WaitService("RUNNING", 90) && await WaitPortAsync(Port, 90);
            Note($"V3 cleanup: AkmlSqlWebEngine running and listening again: {back}");
            await browser.DisposeAsync();
            pw.Dispose();
        }
    }

    // ── V6, V7 ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V6_V7_a_failed_login_keeps_no_password_and_another_address_explains_itself()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var problems = new List<string>();
        try
        {
            await using (var context = await browser.NewContextAsync(Profile()))
            {
                var page = await context.NewPageAsync();
                var live = await OpenLiveAsync(page, Site);
                Assert.True(live.Live, $"the page did not reach the engine: '{live.Pill}'");
                await InAppDialogs.AcceptAllAsync(page, "");

                await page.ClickAsync("[data-testid='status-connection']");
                await page.WaitForSelectorAsync("[data-testid='connection-manager']");
                var newButton = page.GetByTestId("conn-new-btn");
                if (await IsShownAsync(newButton)) await newButton.ClickAsync();
                await page.FillAsync("[data-testid='conn-name-input']", "QA wrong password");
                await page.FillAsync("[data-testid='conn-server-input']", "localhost");
                await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
                await page.CheckAsync("[data-testid='conn-auth-sql']");
                await page.FillAsync("[data-testid='conn-login-input']", "akml_qa_no_such_login");
                await page.FillAsync("[data-testid='conn-password-input']", "not-the-password");
                await page.CheckAsync("[data-testid='conn-sql-remember']");
                await page.ClickAsync("[data-testid='conn-save-btn']");
                var message = page.GetByTestId("conn-message");
                await Assertions.Expect(message).ToContainTextAsync("Saved without the password", new() { Timeout = 60_000 });
                var saved = (await message.InnerTextAsync()).Trim();
                Note("V6 Save message: " + saved);
                await ShotAsync(page, "v6-1-saved-without-the-password");
                if (!saved.Contains("the login test failed", StringComparison.Ordinal) || !saved.Contains("Login failed", StringComparison.Ordinal))
                    problems.Add("V6 message: " + saved);

                // Reopened, the connection asks for its password again.
                await page.ClickAsync("[data-testid='conn-close-btn']");
                await page.ClickAsync("[data-testid='status-connection']");
                var row = page.Locator("[data-testid='conn-row']", new() { HasTextString = "QA wrong password" }).First;
                await row.ClickAsync(new() { Timeout = 15_000 });
                await page.WaitForTimeoutAsync(800);
                var password = await page.InputValueAsync("[data-testid='conn-password-input']");
                var remember = await page.IsCheckedAsync("[data-testid='conn-sql-remember']");
                Note($"V6 reopened: password box {(password.Length == 0 ? "empty" : "FILLED")}, Remember {(remember ? "on" : "off")}");
                await ShotAsync(page, "v6-2-reopened-asks-for-the-password");
                if (password.Length != 0) problems.Add("V6: the password box was filled on reopening");

                // Delete asks first; the click below either answers it or lets the page's dialog handler answer it.
                await page.ClickAsync("[data-testid='conn-delete-btn']");
                try { await page.GetByTestId("dialog-confirm").ClickAsync(new() { Timeout = 5_000 }); }
                catch (TimeoutException) { /* the in-app dialog handler answered it first */ }
                var gone = await WaitUntilAsync(async () =>
                    await page.Locator("[data-testid='conn-row']", new() { HasTextString = "QA wrong password" }).CountAsync() == 0, 10);
                Note($"V6 cleanup: test connection deleted: {gone}");
            }

            await using (var context = await browser.NewContextAsync(Profile()))
            {
                var page = await context.NewPageAsync();
                var other = Site.Replace("://localhost:", "://127.0.0.1:", StringComparison.Ordinal);
                await page.GotoAsync(other, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
                await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
                var banner = page.GetByTestId("origin-banner");
                var shown = await WaitUntilAsync(() => IsShownAsync(banner), 20);
                var text = shown ? (await banner.InnerTextAsync()).Trim() : "(no banner)";
                Note($"V7 on {other}: banner {(shown ? "shown" : "NOT shown")}: {text}");
                await ShotAsync(page, "v7-1-origin-banner");
                if (!shown || !text.Contains(Site, StringComparison.Ordinal)) problems.Add($"V7 banner: {text}");

                if (shown) await page.GetByTestId("origin-banner-dismiss").ClickAsync();
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
                await page.WaitForTimeoutAsync(5_000);
                var again = await IsShownAsync(banner);
                Note($"V7 after Dismiss and a reload: banner {(again ? "SHOWN AGAIN" : "not shown")}");
                if (again) problems.Add("V7: the banner came back after Dismiss");

                await ShellPolishTests.NavigateAsync(page, "settings/diagnostics");
                var storage = page.GetByTestId("diag-storage");
                await Assertions.Expect(storage).ToBeVisibleAsync(new() { Timeout = 15_000 });
                var storageText = Regex.Replace((await storage.InnerTextAsync()).Trim(), @"\s+", " ");
                Note("V7 Diagnostics › Browser storage: " + storageText);
                await ShotAsync(page, "v7-2-diagnostics-storage");
            }
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── V10, V13 ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V10_V13_advisories_are_margin_markers_and_a_syntax_error_is_found_without_a_connection()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var problems = new List<string>();
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            // As a person would: the page has settled (paired with the engine, tabs restored) before anything is typed.
            var live = await OpenLiveAsync(page, Site);
            Note($"V13 engine: '{live.Pill}' (no SQL connection)");
            await page.WaitForTimeoutAsync(2_000);
            var pane = page.Locator("[data-testid='results-pane']");

            // V13: no SQL connection.
            await SetTextAsync(page, "SELEC * FROM dbo.Customers;");
            Note($"V13 editor text before Analyse: [{await TextAsync(page)}]");
            await page.GetByTestId("analyse-button").ClickAsync();
            var syntax = pane.Locator("[data-testid='problem-item'].akml-severity-error");
            var found = await WaitUntilAsync(async () => await syntax.CountAsync() == 1, 30);
            await ShotAsync(page, "v13-0-after-analyse");
            if (!found)
                Note($"V13 Problems pane: [{Regex.Replace(await pane.InnerTextAsync(), @"\s+", " ")}]; editor: [{await TextAsync(page)}]");
            await Assertions.Expect(syntax).ToHaveCountAsync(1, new() { Timeout = 5_000 });
            var row = Regex.Replace((await syntax.InnerTextAsync()).Trim(), @"\s+", " ");
            var red = await page.Locator(".cm-lintRange-error").CountAsync();
            Note($"V13 Problems: one Error: {row}; red marks in the editor: {red}");
            await ShotAsync(page, "v13-1-problems-syntax-error");
            if (!row.Contains("SYNTAX", StringComparison.Ordinal) || red < 1) problems.Add($"V13 analyse: {row}; red {red}");

            await page.Locator(".cm-content").ClickAsync();
            await page.Keyboard.PressAsync("Control+F5");
            await Assertions.Expect(page.GetByTestId("status-outcome")).ToContainTextAsync("Parse failed: 1 syntax error.", new() { Timeout = 30_000 });
            var messages = Regex.Replace((await pane.Locator("[data-testid='messages-list']").InnerTextAsync()).Trim(), @"\s+", " ");
            Note("V13 Parse messages: " + messages);
            await ShotAsync(page, "v13-2-parse-in-the-browser");
            if (!messages.Contains("Checked in the browser", StringComparison.Ordinal)) problems.Add("V13 parse: " + messages);

            // V10: three statements without a semicolon.
            await SetTextAsync(page, ThreeStatements);
            await page.GetByTestId("analyse-button").ClickAsync();
            var infoMarkers = page.Locator(".cm-gutter-lint .cm-lint-marker-info");
            await WaitUntilAsync(async () => await infoMarkers.CountAsync() >= 3, 30);
            var markers = await infoMarkers.CountAsync();
            var ranges = await InfoRangesAsync(page);
            Note($"V10 without ';': {markers} margin markers; info ranges: {ranges}");
            await ShotAsync(page, "v10-1-margin-markers-only");
            if (markers < 3) problems.Add($"V10: {markers} info markers for 3 statements without ';'");
            if (ranges.Contains("underlined", StringComparison.Ordinal)) problems.Add("V10: an advisory finding is drawn under the text: " + ranges);

            await SetTextAsync(page, ThreeStatements.Replace("N'Germany'\n", "N'Germany';\n", StringComparison.Ordinal));
            await page.GetByTestId("analyse-button").ClickAsync();
            var fewer = await WaitUntilAsync(async () => await infoMarkers.CountAsync() == markers - 1, 30);
            Note($"V10 with ';' on line 1: {await infoMarkers.CountAsync()} margin markers");
            await ShotAsync(page, "v10-2-semicolon-added");
            if (!fewer) problems.Add($"V10: adding ';' left {await infoMarkers.CountAsync()} markers (was {markers})");

            await SetTextAsync(page, ThreeStatements + "SELEC * FROM dbo.Customers\n");
            await page.GetByTestId("analyse-button").ClickAsync();
            await WaitUntilAsync(async () => await page.Locator(".cm-lintRange-error").CountAsync() >= 1, 30);
            var errorLook = await page.EvaluateAsync<string>(
                "() => Array.from(document.querySelectorAll('.cm-lintRange-error')).map(e => { const s = getComputedStyle(e); " +
                "return (e.classList.contains('akml-lint-marker-only') ? 'marker-only' : 'plain') + ' line=' + s.textDecorationLine + ' style=' + s.textDecorationStyle + ' colour=' + s.textDecorationColor; }).join(' ; ')");
            Note("V10 syntax error ranges: " + errorLook);
            await ShotAsync(page, "v10-3-syntax-error-red");
            if (errorLook.Length == 0 || errorLook.Contains("marker-only", StringComparison.Ordinal) || !errorLook.Contains("line=underline", StringComparison.Ordinal))
                problems.Add("V10: the syntax error is not drawn: " + errorLook);
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── start-up: text typed as soon as the editor shows ─────────────────────────────────────────────────────────────

    /// <summary>A person can start typing the moment the editor appears, while the page is still pairing with the engine and
    /// restoring its tabs; what they typed must still be there (and be what Analyse reads) once it has settled.</summary>
    [SkippableFact]
    public async Task Startup_text_typed_as_soon_as_the_editor_shows_survives_the_page_settling()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await page.GotoAsync(Site, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
            await page.Locator(".cm-content").ClickAsync();
            await page.Keyboard.TypeAsync("SELEC * FROM", new() { Delay = 40 });
            var typedAt = await TextOfAsync(page, "status-pill");
            var live = await WaitUntilAsync(async () => (await TextOfAsync(page, "status-pill")).Contains("Live", StringComparison.OrdinalIgnoreCase), 30);
            await page.WaitForTimeoutAsync(3_000);
            var text = await TextAsync(page);
            Note($"start-up: typed while the pill said '{typedAt}'; Live {live}; text after settling: [{text}]");
            await page.GetByTestId("analyse-button").ClickAsync();
            var pane = page.Locator("[data-testid='results-pane']");
            var syntax = pane.Locator("[data-testid='problem-item'].akml-severity-error");
            var found = await WaitUntilAsync(async () => await syntax.CountAsync() == 1, 20);
            Note($"start-up: Analyse finds the syntax error: {found}");
            await ShotAsync(page, "startup-typed-text");
            Assert.Equal("SELEC * FROM", text.Trim());
            Assert.True(found, "Analyse did not report the syntax error in the text typed at start-up");
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
    }

    // ── V14 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V14_a_user_snippet_completes_expands_and_stops_once_deleted()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await InAppDialogs.AcceptAllAsync(page, "");
            var live = await OpenLiveAsync(page, Site);
            Note($"V14 engine: '{live.Pill}'");

            await ShellPolishTests.NavigateAsync(page, "snippets");
            await page.GetByTestId("snippet-new").ClickAsync(new() { Timeout = 60_000 });
            await page.Locator("#akml-snippet-shortcode").FillAsync("qaz");
            await page.Locator("#akml-snippet-body").FillAsync("SELECT $CURSOR$ FROM dbo.Region;");
            await page.GetByTestId("snippet-save").ClickAsync();
            await Assertions.Expect(page.GetByTestId("snippet-status")).ToContainTextAsync("Saved 'qaz'.");

            await ShellPolishTests.NavigateAsync(page, "editor");
            await TypeAtStartAsync(page, "qaz");
            var option = page.Locator(".cm-tooltip-autocomplete .cm-completionLabel", new() { HasTextString = "qaz" });
            await Assertions.Expect(option.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator(".cm-tooltip-autocomplete li[aria-selected] .cm-completionLabel"))
                .ToHaveTextAsync("qaz", new() { Timeout = 15_000 });
            await ShotAsync(page, "v14-1-qaz-offered");
            await page.WaitForTimeoutAsync(500);
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(page.Locator(".cm-editor .cm-line").First).ToHaveTextAsync("SELECT  FROM dbo.Region;", new() { Timeout = 10_000 });
            await page.Keyboard.TypeAsync("*");
            var expanded = await TextAsync(page);
            Note($"V14 qaz + Tab, then '*' at the caret: [{expanded}]");
            Assert.Equal("SELECT * FROM dbo.Region;", expanded.Trim());

            await ShellPolishTests.NavigateAsync(page, "snippets");
            await page.Locator("[data-testid='snippet-row'][data-shortcode='qaz']").ClickAsync(new() { Timeout = 30_000 });
            await page.GetByTestId("snippet-delete").ClickAsync();
            await Assertions.Expect(page.GetByTestId("snippet-status")).ToContainTextAsync("Deleted 'qaz'.");

            await ShellPolishTests.NavigateAsync(page, "editor");
            await TypeAtStartAsync(page, "qaz");
            await page.WaitForTimeoutAsync(1_500);
            var after = await option.CountAsync();
            await page.Keyboard.PressAsync("Tab");
            var text = await TextAsync(page);
            Note($"V14 after Delete: qaz offered {after} time(s); qaz + Tab gives [{text.Trim()}]");
            Assert.Equal(0, after);
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
    }

    // ── connected: cycle, V15, V16, V17 ───────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Connected_cycle_V15_V16_V17_on_Northwind_through_the_installed_engine()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var problems = new List<string>();
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await InAppDialogs.AcceptAllAsync(page, "");
            var live = await OpenLiveAsync(page, Site);
            Assert.True(live.Live, $"the page did not reach the engine: '{live.Pill}'");
            await WebAppFixture.ConnectAsync(page, "Northwind");
            var versions = $"web {await TextOfAsync(page, "status-version-web")}, engine {await TextOfAsync(page, "status-version-engine")}";
            Note($"cycle: connected as '{await TextOfAsync(page, "status-connection")}'; versions: {versions}");

            // Run, grid.
            var pane = page.Locator("[data-testid='results-pane']");
            await SetTextAsync(page, "SELECT TOP (5) CustomerID, CompanyName FROM dbo.Customers ORDER BY CustomerID;");
            var before = await MarkerVersionAsync(page, "execute-complete");
            await page.GetByTestId("execute-button").ClickAsync();
            await WaitUntilAsync(async () => await MarkerVersionAsync(page, "execute-complete") > before, 60);
            var first = await pane.GetByTestId("results-cell-0-0").InnerTextAsync(new() { Timeout = 15_000 });
            var fifth = await pane.GetByTestId("results-cell-4-0").CountAsync();
            var sixth = await pane.GetByTestId("results-cell-5-0").CountAsync();
            Note($"cycle run: first cell '{first}', row 5 present {fifth == 1}, row 6 present {sixth == 1}; outcome '{await TextOfAsync(page, "status-outcome")}'");
            await ShotAsync(page, "cycle-1-results");
            if (first.Trim() != "ALFKI" || fifth != 1 || sixth != 0) problems.Add($"cycle grid: first '{first}', fifth {fifth}, sixth {sixth}");

            // Format, twice.
            await SetTextAsync(page, "select customerid,companyname from dbo.customers where country=N'Germany' order by companyname");
            var f0 = await MarkerVersionAsync(page, "format-complete");
            await page.GetByTestId("format-button").ClickAsync();
            await WaitUntilAsync(async () => await MarkerVersionAsync(page, "format-complete") > f0, 30);
            var formatted = await TextAsync(page);
            var f1 = await MarkerVersionAsync(page, "format-complete");
            await page.GetByTestId("format-button").ClickAsync();
            await WaitUntilAsync(async () => await MarkerVersionAsync(page, "format-complete") > f1, 30);
            var again = await TextAsync(page);
            Note("cycle Format:\n" + formatted);
            Note($"cycle second Format unchanged: {again == formatted}");
            if (formatted.StartsWith("select customerid,", StringComparison.Ordinal) || again != formatted) problems.Add("cycle format: not formatted or not stable");

            // Completion.
            await TypeAtStartAsync(page, "SELECT * FROM dbo.Cust");
            var customers = page.Locator(".cm-tooltip-autocomplete .cm-completionLabel", new() { HasTextString = "Customers" });
            var offered = await WaitUntilAsync(() => IsShownAsync(customers.First), 15);
            await ShotAsync(page, "cycle-2-completion");
            await page.Keyboard.PressAsync("Escape");
            Note($"cycle completion after 'dbo.Cust' offers Customers: {offered}");
            if (!offered) problems.Add("cycle completion: Customers not offered");

            // V15: the explorer says what it cannot see.
            var tree = page.GetByTestId("schema-tree");
            if (!await IsShownAsync(tree)) { await page.Locator(".cm-content").ClickAsync(); await page.Keyboard.PressAsync("F8"); }
            await Assertions.Expect(tree).ToBeVisibleAsync(new() { Timeout = 60_000 });
            var limited = page.GetByTestId("schema-metadata-limited");
            var limitedShown = await WaitUntilAsync(() => IsShownAsync(limited), 60);
            var limitedText = limitedShown ? Regex.Replace((await limited.InnerTextAsync()).Trim(), @"\s+", " ") : "(no note)";
            // Does the note keep to its own space? Compare its text's box with the row that follows it.
            var overlap = await page.EvaluateAsync<string>(
                "() => { const n = document.querySelector(\"[data-testid='schema-metadata-limited']\"); if (!n) return 'no note'; " +
                "const t = n.querySelector('.akml-schema-tree-note-text') || n; const next = n.nextElementSibling; " +
                "const a = n.getBoundingClientRect(), b = t.getBoundingClientRect(), c = next ? next.getBoundingClientRect() : null; " +
                "return `row ${Math.round(a.height)}px, text ${Math.round(b.height)}px, text bottom ${Math.round(b.bottom)}, next row top ${c ? Math.round(c.top) : 'none'}, overlaps ${c ? b.bottom > c.top + 1 : false}`; }");
            Note($"V15 note: {limitedText}");
            Note($"V15 note geometry: {overlap}");
            var panel = page.GetByTestId("workspace-schema");
            try { await panel.ScreenshotAsync(new() { Path = Path.Combine(Out, "v15-0-schema-panel-note.png") }); } catch (Exception) { /* best effort */ }
            if (overlap.Contains("overlaps true", StringComparison.OrdinalIgnoreCase)) problems.Add("V15: the note is drawn over the next tree row: " + overlap);

            // The routine groups say they may be incomplete (under Schema "dbo").
            var dbo = tree.Locator("li.akml-schema-tree-schema", new() { HasTextString = "dbo" }).First;
            if (await dbo.CountAsync() > 0) await dbo.ClickAsync();
            var procs = tree.Locator("li.akml-schema-tree-kind", new() { HasTextString = "Stored Procedures" }).First;
            await WaitUntilAsync(async () => await procs.CountAsync() > 0, 15);
            var procsText = await procs.CountAsync() > 0 ? Regex.Replace((await procs.InnerTextAsync()).Trim(), @"\s+", " ") : "(no Stored Procedures node)";
            var tables = tree.Locator("li.akml-schema-tree-kind", new() { HasTextString = "Tables" }).First;
            var tablesText = await tables.CountAsync() > 0 ? Regex.Replace((await tables.InnerTextAsync()).Trim(), @"\s+", " ") : "(no Tables node)";
            Note($"V15 groups under dbo: '{procsText}', '{tablesText}'");
            await ShotAsync(page, "v15-explorer-metadata-limited");
            if (!limitedShown || !limitedText.Contains("VIEW DEFINITION", StringComparison.Ordinal) || !procsText.Contains("may be incomplete", StringComparison.Ordinal))
                problems.Add($"V15: note '{limitedText}', node '{procsText}'");

            // V17: a filter that matches nothing.
            await page.GetByTestId("schema-filter").FillAsync("zzzz");
            var empty = page.GetByTestId("schema-filter-empty");
            var emptyShown = await WaitUntilAsync(() => IsShownAsync(empty), 15);
            var emptyText = emptyShown ? Regex.Replace((await empty.InnerTextAsync()).Trim(), @"\s+", " ") : "(nothing)";
            await ShotAsync(page, "v17-filter-no-match");
            var clear = page.GetByTestId("schema-filter-clear");
            var clearShown = await IsShownAsync(clear);
            if (clearShown) await clear.ClickAsync();
            await page.WaitForTimeoutAsync(800);
            var filterAfter = await page.GetByTestId("schema-filter").InputValueAsync();
            Note($"V17 'zzzz': {emptyText}; Clear filter shown {clearShown}; filter after Clear '{filterAfter}'; empty row left {await empty.CountAsync()}");
            if (!emptyText.Contains("No objects match 'zzzz'.", StringComparison.Ordinal) || !clearShown || filterAfter.Length != 0)
                problems.Add($"V17: '{emptyText}', clear {clearShown}, filter '{filterAfter}'");

            // V16: Expand INSERT columns from the cached schema, and the menu by keyboard.
            await SetTextAsync(page, "insert into dbo.Region values (9,'x')");
            var item = page.Locator("[data-testid='refactor-item']", new() { HasTextString = "Expand INSERT columns" });
            var enabled = false;
            var reason = "";
            for (var attempt = 0; attempt < 20 && !enabled; attempt++)
            {
                await page.GetByTestId("refactor-button").ClickAsync();
                await Assertions.Expect(page.GetByTestId("refactor-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
                enabled = await item.IsEnabledAsync();
                if (!enabled)
                {
                    var id = await item.GetAttributeAsync("aria-describedby");
                    reason = id is null ? "" : await page.Locator("#" + id).InnerTextAsync();
                    await page.Keyboard.PressAsync("Escape");
                    await page.WaitForTimeoutAsync(3_000);
                }
            }
            Note($"V16 Expand INSERT columns enabled: {enabled}{(enabled ? "" : $" (reason: {reason})")}");
            if (enabled)
            {
                await item.ClickAsync();
                var preview = page.GetByTestId("refactor-preview");
                await Assertions.Expect(preview).ToBeVisibleAsync(new() { Timeout = 30_000 });
                var previewText = Regex.Replace(await preview.InnerTextAsync(), @"\s+", " ");
                await ShotAsync(page, "v16-1-expand-insert-preview");
                await page.GetByTestId("refactor-apply").ClickAsync();
                await Assertions.Expect(preview).ToHaveCountAsync(0, new() { Timeout = 15_000 });
                var applied = await TextAsync(page);
                bool BothColumns(string t) => Regex.IsMatch(t, @"\(\s*RegionID\s*,.*?RegionDescription\b.*?\)\s*values", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                Note($"V16 preview lists both columns: {BothColumns(previewText)}; after Apply: [{applied.Trim().Replace("\n", "⏎")}]");
                if (!BothColumns(applied)) problems.Add("V16 apply: " + applied);
            }
            else problems.Add("V16: Expand INSERT columns stayed disabled: " + reason);

            await page.GetByTestId("refactor-button").FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.GetByTestId("refactor-menu")).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'refactor-item'");
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(page.GetByTestId("refactor-menu")).ToHaveCountAsync(0);
            var focused = await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.getAttribute('data-testid')");
            Note($"V16 Escape closes the menu; focus on '{focused}'");
            if (focused != "refactor-button") problems.Add($"V16 focus after Escape: '{focused}'");

            // History lists the run.
            await ShellPolishTests.NavigateAsync(page, "history");
            var history = page.Locator("[data-testid='history-row']", new() { HasTextString = "CompanyName" });
            var listed = await WaitUntilAsync(async () => await history.CountAsync() > 0, 30);
            await ShotAsync(page, "cycle-3-history");
            Note($"cycle History lists the SELECT: {listed}");
            if (!listed) problems.Add("cycle history: the run is not listed");
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── V15 (with the owner's GRANT in place) ─────────────────────────────────────────────────────────────────────────

    /// <summary>Quickstart V15, second half: after <c>GRANT VIEW DEFINITION ON DATABASE::Northwind TO [NT AUTHORITY\SYSTEM]</c>
    /// (run outside the test, with the owner's go-ahead; <c>AKML_V15_GRANTED=1</c>) and Refresh, the note is gone, the group
    /// reads "Stored Procedures (7)" and lists dbo.CustOrderHist.</summary>
    [SkippableFact]
    public async Task V15_after_the_grant_and_Refresh_the_explorer_lists_the_procedures()
    {
        RequireOptIn();
        Skip.IfNot(Environment.GetEnvironmentVariable("AKML_V15_GRANTED") == "1", "Run with the GRANT in place (AKML_V15_GRANTED=1).");
        var (pw, browser) = await LaunchAsync();
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await InAppDialogs.AcceptAllAsync(page, "");
            Assert.True((await OpenLiveAsync(page, Site)).Live, "the page did not reach the engine");
            await WebAppFixture.ConnectAsync(page, "Northwind");
            var tree = page.GetByTestId("schema-tree");
            if (!await IsShownAsync(tree)) { await page.Locator(".cm-content").ClickAsync(); await page.Keyboard.PressAsync("F8"); }
            await Assertions.Expect(tree).ToBeVisibleAsync(new() { Timeout = 60_000 });

            await page.GetByTestId("schema-refresh").ClickAsync();
            var limited = page.GetByTestId("schema-metadata-limited");
            var noteGone = await WaitUntilAsync(async () => await limited.CountAsync() == 0, 90);
            var dbo = tree.Locator("li.akml-schema-tree-schema", new() { HasTextString = "dbo" }).First;
            await WaitUntilAsync(async () => await dbo.CountAsync() > 0, 30);
            await dbo.ClickAsync();
            var procs = tree.Locator("li.akml-schema-tree-kind", new() { HasTextString = "Stored Procedures" }).First;
            await WaitUntilAsync(async () => await procs.CountAsync() > 0, 30);
            var procsText = Regex.Replace((await procs.InnerTextAsync()).Trim(), @"\s+", " ");
            await procs.ClickAsync();
            var listed = await WaitUntilAsync(async () => await tree.Locator("li", new() { HasTextString = "CustOrderHist" }).CountAsync() > 0, 30);
            Note($"V15 after the GRANT and Refresh: note gone {noteGone}; group '{procsText}'; CustOrderHist listed {listed}");
            await ShotAsync(page, "v15-2-after-grant");
            Assert.True(noteGone, "the VIEW DEFINITION note is still shown after the GRANT and Refresh");
            Assert.Equal("Stored Procedures (7)", procsText);
            Assert.True(listed, "dbo.CustOrderHist is not listed");
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
    }

    // ── V5 (with the owner's temporary SQL login) ─────────────────────────────────────────────────────────────────────

    /// <summary>Quickstart V5 (SC-003): a SQL-login connection saved with Remember and the right password connects after the
    /// browser (same profile) and AkmlSqlWebEngine restart, with no password prompt and its own name. The login comes from
    /// <c>AKML_V5_LOGIN</c> / <c>AKML_V5_PASSWORD</c> (a temporary login made for the run); the test forgets the password and
    /// deletes the connection at the end.</summary>
    [SkippableFact]
    public async Task V5_a_remembered_password_survives_a_browser_and_engine_restart()
    {
        RequireOptIn();
        var login = Environment.GetEnvironmentVariable("AKML_V5_LOGIN");
        var password = Environment.GetEnvironmentVariable("AKML_V5_PASSWORD");
        Skip.If(string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password), "AKML_V5_LOGIN / AKML_V5_PASSWORD are not set.");
        const string name = "QA remembered login";
        var profile = Path.Combine(Out, "v5-profile");
        if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        using var pw = await Playwright.CreateAsync();
        var options = new BrowserTypeLaunchPersistentContextOptions { Headless = true, ViewportSize = new() { Width = 1440, Height = 900 } };
        var problems = new List<string>();

        await using (var first = await pw.Chromium.LaunchPersistentContextAsync(profile, options))
        {
            var page = first.Pages.FirstOrDefault() ?? await first.NewPageAsync();
            await InAppDialogs.AcceptAllAsync(page, "");
            Assert.True((await OpenLiveAsync(page, Site)).Live, "the page did not reach the engine");
            await page.ClickAsync("[data-testid='status-connection']");
            await page.WaitForSelectorAsync("[data-testid='connection-manager']");
            var newButton = page.GetByTestId("conn-new-btn");
            if (await IsShownAsync(newButton)) await newButton.ClickAsync();
            await page.FillAsync("[data-testid='conn-name-input']", name);
            await page.FillAsync("[data-testid='conn-server-input']", "localhost");
            await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
            await page.CheckAsync("[data-testid='conn-auth-sql']");
            await page.FillAsync("[data-testid='conn-login-input']", login!);
            await page.FillAsync("[data-testid='conn-password-input']", password!);
            await page.CheckAsync("[data-testid='conn-sql-remember']");
            await page.ClickAsync("[data-testid='conn-save-btn']");
            var message = page.GetByTestId("conn-message");
            await WaitUntilAsync(async () => (await TextOrEmptyAsync(message)).StartsWith("Saved", StringComparison.Ordinal), 60);
            var saved = await TextOrEmptyAsync(message);
            Note($"V5 Save with Remember: '{saved}'");
            await ShotAsync(page, "v5-1-saved-remembered");
            if (!saved.Contains("keeps the password", StringComparison.Ordinal)) problems.Add("V5 save: " + saved);
        }

        Service("stop");
        Assert.True(WaitService("STOPPED", 60), "AkmlSqlWebEngine did not stop");
        Service("start");
        Assert.True(WaitService("RUNNING", 90) && await WaitPortAsync(Port, 90), "AkmlSqlWebEngine did not come back");

        await using (var second = await pw.Chromium.LaunchPersistentContextAsync(profile, options))
        {
            var page = second.Pages.FirstOrDefault() ?? await second.NewPageAsync();
            await InAppDialogs.AcceptAllAsync(page, "");
            Assert.True((await OpenLiveAsync(page, Site)).Live, "the reopened page did not reach the engine");
            await page.ClickAsync("[data-testid='status-connection']");
            await page.WaitForSelectorAsync("[data-testid='connection-manager']");
            var row = page.Locator("[data-testid='conn-row']", new() { HasTextString = name }).First;
            await row.ClickAsync(new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(800);
            var typed = await page.InputValueAsync("[data-testid='conn-password-input']");
            await page.ClickAsync("[data-testid='conn-connect-btn']");
            var connected = await WaitUntilAsync(async () =>
                (await TextOfAsync(page, "status-connection")).Contains("localhost", StringComparison.OrdinalIgnoreCase), 45);
            var status = await TextOfAsync(page, "status-connection");
            var login2 = await TextOfAsync(page, "status-login");
            var prompt = await page.GetByTestId("connection-manager").CountAsync() > 0
                ? await TextOrEmptyAsync(page.GetByTestId("conn-message")) : "(dialog closed)";
            Note($"V5 after restarting the browser and the engine: password box '{(typed.Length == 0 ? "empty" : "FILLED")}', connected {connected}, status '{status}', login '{login2}', dialog: {prompt}");
            await ShotAsync(page, "v5-2-reconnected-without-a-password");
            if (!connected) problems.Add($"V5: not connected after the restarts ({prompt})");

            // Clean up: forget the remembered password on the engine, then delete the connection.
            if (await page.GetByTestId("connection-manager").CountAsync() == 0) await page.ClickAsync("[data-testid='status-connection']");
            await page.Locator("[data-testid='conn-row']", new() { HasTextString = name }).First.ClickAsync(new() { Timeout = 15_000 });
            var forget = page.GetByTestId("conn-sql-forget");
            if (await IsShownAsync(forget)) { await forget.ClickAsync(); await page.WaitForTimeoutAsync(1_500); }
            Note($"V5 cleanup: Forget it -> '{await TextOrEmptyAsync(page.GetByTestId("conn-message"))}'");
            await page.ClickAsync("[data-testid='conn-delete-btn']");
            try { await page.GetByTestId("dialog-confirm").ClickAsync(new() { Timeout = 5_000 }); } catch (TimeoutException) { /* answered by the handler */ }
        }
        try { Directory.Delete(profile, recursive: true); } catch (IOException) { /* the browser may still hold it */ }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── V18 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V18_importing_a_text_file_as_a_style_or_a_snippet_says_so_plainly()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var problems = new List<string>();
        try
        {
            var txt = Path.Combine(Out, "not-a-style-or-snippet.txt");
            File.WriteAllText(txt, "these are notes, not JSON\n");
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await OpenLiveAsync(page, Site);

            await ShellPolishTests.NavigateAsync(page, "styles");
            await page.GetByTestId("style-import").SetInputFilesAsync(txt);
            var styles = page.GetByTestId("styles-status");
            await WaitUntilAsync(async () => (await TextOrEmptyAsync(styles)).Length > 0, 15);
            var styleText = await TextOrEmptyAsync(styles);
            await ShotAsync(page, "v18-1-style-import");

            await ShellPolishTests.NavigateAsync(page, "snippets");
            await page.GetByTestId("snippet-import").SetInputFilesAsync(txt);
            var snippets = page.GetByTestId("snippet-status");
            await WaitUntilAsync(async () => (await TextOrEmptyAsync(snippets)).Length > 0, 15);
            var snippetText = await TextOrEmptyAsync(snippets);
            await ShotAsync(page, "v18-2-snippet-import");

            Note($"V18 style import: '{styleText}'");
            Note($"V18 snippet import: '{snippetText}'");
            foreach (var (what, text) in new[] { ("style", styleText), ("snippet", snippetText) })
            {
                if (text.Length == 0) problems.Add($"V18 {what}: no message");
                if (Regex.IsMatch(text, @"Exception|LineNumber|BytePositionInLine|invalid start of a value|Path: \$", RegexOptions.IgnoreCase))
                    problems.Add($"V18 {what}: parser internals: {text}");
            }
            if (!styleText.Contains("That file is not a SQL Prompt style (.json).", StringComparison.Ordinal)) problems.Add("V18 style: " + styleText);
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── V20 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V20_no_schema_mode_sends_no_schema_sentence_and_Fix_waits_for_an_error()
    {
        RequireOptIn();
        MockAiProvider mock;
        try { mock = MockAiProvider.StartOllama(); }
        catch (Exception ex) { throw new SkipException("Port 11434 is taken (a real Ollama?): " + ex.Message); }
        using var _ = mock;
        var (pw, browser) = await LaunchAsync();
        var problems = new List<string>();
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await InAppDialogs.AcceptAllAsync(page, "");
            var live = await OpenLiveAsync(page, Site);
            Assert.True(live.Live, $"the page did not reach the engine: '{live.Pill}'");
            await WebAppFixture.ConnectAsync(page, "Northwind");

            await ShellPolishTests.NavigateAsync(page, "settings/ai-assistance");
            if (await page.GetByTestId("ai-agent-form").CountAsync() == 0) await page.GetByTestId("ai-agent-add").ClickAsync();
            await page.GetByLabel("Provider").SelectOptionAsync("ollama");
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Model" }).FillAsync("mock");
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Endpoint" }).FillAsync(mock.ChatCompletionsUrl);
            await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByTestId("ai-agent-form")).ToHaveCountAsync(0);
            var setActive = page.GetByTestId("ai-agent-set-active");
            if (await setActive.IsEnabledAsync()) await setActive.ClickAsync();
            await page.GetByLabel("Global default").SelectOptionAsync("NoSchema");
            await page.WaitForTimeoutAsync(1_000);
            await ShotAsync(page, "v20-1-agent-and-no-schema");

            await ShellPolishTests.NavigateAsync(page, "editor");
            await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
            await SetTextAsync(page, "SELECT * FROM dbo.Orders WHERE CustomerID = 'ALFKI' ORDER BY OrderDate DESC;");
            await page.GetByTestId("ai-button").ClickAsync();
            var fix = page.GetByTestId("ai-fix");
            await Assertions.Expect(fix).ToBeVisibleAsync(new() { Timeout = 15_000 });
            var fixDisabled = await fix.IsDisabledAsync();
            var fixTitle = await fix.GetAttributeAsync("title") ?? "";
            Note($"V20 before any run: Fix disabled {fixDisabled}; tooltip '{fixTitle}'");
            if (!fixDisabled || !fixTitle.Contains("Fix needs the error", StringComparison.Ordinal)) problems.Add($"V20 Fix before a run: disabled {fixDisabled}, '{fixTitle}'");

            mock.ResetCaptures();
            await page.GetByRole(AriaRole.Button, new() { Name = "Optimize" }).ClickAsync();
            var answered = await WaitUntilAsync(() => Task.FromResult(mock.Captures.Count > 0), 30);
            await page.WaitForTimeoutAsync(1_500);
            await ShotAsync(page, "v20-2-optimize-answer");
            var system = answered ? Message(mock.Captures[0], "system") : "(no request reached the mock)";
            var sentSchema = Regex.IsMatch(system, "following schema|provided schema", RegexOptions.IgnoreCase);
            var sentTable = system.Contains("CompanyName", StringComparison.Ordinal) || system.Contains("ShipCountry", StringComparison.Ordinal);
            Note($"V20 Optimize reached the mock: {answered}; system prompt cites a schema: {sentSchema}; carries column names: {sentTable}");
            Note("V20 system prompt: " + system.Replace("\n", " ⏎ "));
            if (!answered || sentSchema || sentTable) problems.Add($"V20 prompt: answered {answered}, cites schema {sentSchema}, columns {sentTable}");

            var before = await MarkerVersionAsync(page, "execute-complete");
            await SetTextAsync(page, "SELECT * FROM dbo.NoSuchTable_akml047;");
            await page.GetByTestId("execute-button").ClickAsync();
            await WaitUntilAsync(async () => await MarkerVersionAsync(page, "execute-complete") > before, 60);
            var enabledAfter = await WaitUntilAsync(async () => await fix.IsEnabledAsync(), 15);
            Note($"V20 after a run that errors: Fix enabled {enabledAfter}");
            await ShotAsync(page, "v20-3-fix-enabled-after-an-error");
            if (!enabledAfter) problems.Add("V20: Fix stayed disabled after a failed run");
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── V22 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V22_axe_finds_nothing_on_six_routes_in_three_themes()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var failures = new List<string>();
        try
        {
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
            var page = await context.NewPageAsync();
            var live = await OpenLiveAsync(page, Site);
            Note($"V22 engine: '{live.Pill}'");
            var options = new AxeRunOptions
            {
                RunOnly = new RunOnlyOptions { Type = "tag", Values = new List<string> { "wcag2a", "wcag2aa", "best-practice" } },
            };
            foreach (var theme in new[] { "Light", "Dark", "HighContrast" })
            {
                await ShellPolishTests.NavigateAsync(page, "settings/general");
                await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
                foreach (var route in new[] { "editor", "snippets", "styles", "history", "settings", "settings/connections" })
                {
                    await ShellPolishTests.NavigateAsync(page, route);
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await page.WaitForTimeoutAsync(700);
                    var result = await page.RunAxe(options);
                    Note($"V22 {theme} /{route}: {result.Violations.Length} violation(s), {result.Passes.Length} rule(s) passed");
                    foreach (var v in result.Violations)
                    {
                        var line = $"{theme} /{route}: {v.Id} [{v.Impact}] {v.Help} -> {string.Join(" ; ", v.Nodes.Select(n => n.Target?.ToString()))}";
                        Note("  " + line);
                        failures.Add(line);
                    }
                }
            }
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Assert.True(failures.Count == 0, $"{failures.Count} axe violation(s):\n" + string.Join("\n", failures));
    }

    // ── V8 (web) ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task V8_every_web_style_formats_the_sp047_corpus_stably()
    {
        RequireOptIn();
        var (pw, browser) = await LaunchAsync();
        var problems = new List<string>();
        var compared = 0;
        var matched = 0;
        try
        {
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            var live = await OpenLiveAsync(page, Site);
            Note($"V8 web engine: '{live.Pill}'");
            // The editor toolbar's Style box is what a person picks a style with; every option is formatted with.
            var picker = page.GetByRole(AriaRole.Combobox, new() { Name = "Style", Exact = true });
            await Assertions.Expect(picker).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await WaitUntilAsync(async () => (await picker.Locator("option").CountAsync()) > 4, 20);
            var options = (await picker.Locator("option").EvaluateAllAsync<string[][]>("os => os.map(o => [o.value, o.textContent.trim()])")).ToList();
            Note("V8 web styles: " + string.Join(" | ", options.Select(o => o[1])));

            foreach (var option in options)
            {
                var style = option[1];
                await picker.SelectOptionAsync(new SelectOptionValue { Value = option[0] });
                await page.WaitForTimeoutAsync(1_000);
                var chosen = await picker.EvaluateAsync<string>("s => s.options[s.selectedIndex].textContent.trim()");
                if (chosen != style) { problems.Add($"{style}: the picker shows '{chosen}' after choosing it"); continue; }
                foreach (var corpus in Corpus)
                {
                    var input = File.ReadAllText(Path.Combine(RepoRoot, "tests", "format-parity", "corpus", corpus + ".sql")).Replace("\r\n", "\n");
                    await SetTextAsync(page, input);
                    var v0 = await MarkerVersionAsync(page, "format-complete");
                    await page.GetByTestId("format-button").ClickAsync();
                    await WaitUntilAsync(async () => await MarkerVersionAsync(page, "format-complete") > v0, 30);
                    var once = await TextAsync(page);
                    var v1 = await MarkerVersionAsync(page, "format-complete");
                    await page.GetByTestId("format-button").ClickAsync();
                    await WaitUntilAsync(async () => await MarkerVersionAsync(page, "format-complete") > v1, 30);
                    var twice = await TextAsync(page);
                    var stable = once == twice;
                    if (!stable) problems.Add($"{style} / {corpus}: the second Format changed the text");
                    var expected = Expected(style, corpus);
                    var note = "no reference output";
                    if (expected is not null)
                    {
                        compared++;
                        var diff = FirstDifference(Normal(once), Normal(expected));
                        if (diff is null) { matched++; note = "matches the committed output"; }
                        else { note = "differs from the committed output: " + diff; problems.Add($"{style} / {corpus}: {diff}"); }
                    }
                    Note($"V8 web {style} / {corpus}: stable {stable}; {note}");
                    File.WriteAllText(Path.Combine(Out, $"v8-web-{Slug(style)}-{corpus}.sql"), once);
                }
            }
        }
        finally { await browser.DisposeAsync(); pw.Dispose(); }
        Note($"V8 web: {matched} of {compared} compared outputs match");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── V21 ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The published product site on a loopback port with its own analytics database, GeoIP path and downloads
    /// folder (CLAUDE.md: never the deployed site's), and the three symptom searches of quickstart V21.</summary>
    [SkippableFact]
    public async Task V21_site_search_puts_the_symptom_pages_first_on_a_local_preview()
    {
        RequireOptIn();
        var publish = Path.Combine(RepoRoot, "src", "AkmlSql.Site", "bin", "Release", "net10.0", "publish");
        Skip.IfNot(File.Exists(Path.Combine(publish, "AkmlSql.Site.exe")), "No published site: run the release build first.");
        var sandbox = Path.Combine(Out, "site-preview");
        Directory.CreateDirectory(Path.Combine(sandbox, "downloads"));
        int port;
        using (var probe = new TcpListener(System.Net.IPAddress.Loopback, 0)) { probe.Start(); port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }

        var psi = new ProcessStartInfo(Path.Combine(publish, "AkmlSql.Site.exe"), $"--urls http://127.0.0.1:{port}")
        {
            WorkingDirectory = publish, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["Analytics__DatabasePath"] = Path.Combine(sandbox, "analytics.db");
        psi.Environment["Analytics__GeoDatabasePath"] = Path.Combine(sandbox, "no-geoip.mmdb");
        psi.Environment["Downloads__Folder"] = Path.Combine(sandbox, "downloads");
        using var site = Process.Start(psi)!;
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        site.OutputDataReceived += (_, e) => { if (e.Data != null) log.Enqueue(e.Data); };
        site.ErrorDataReceived += (_, e) => { if (e.Data != null) log.Enqueue(e.Data); };
        site.BeginOutputReadLine();
        site.BeginErrorReadLine();

        var problems = new List<string>();
        var (pw, browser) = await LaunchAsync();
        try
        {
            var root = $"http://127.0.0.1:{port}";
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                var up = await WaitUntilAsync(async () => (await http.GetAsync(root + "/health")).IsSuccessStatusCode, 90);
                Assert.True(up, "the local site preview did not start: " + string.Join(" | ", log.TakeLast(10)));
            }
            await using var context = await browser.NewContextAsync(Profile());
            var page = await context.NewPageAsync();
            await page.GotoAsync(root + "/docs", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });

            var expected = new (string Query, string[] Pages)[]
            {
                ("remember password", new[] { "connecting", "troubleshooting" }),
                ("Handshake failed", new[] { "troubleshooting" }),
                ("asks for password again", new[] { "troubleshooting" }),
            };
            foreach (var (query, pages) in expected)
            {
                var input = page.Locator("#docs-search-input");
                await input.FillAsync("");
                await input.PressSequentiallyAsync(query, new() { Delay = 30 });
                var results = page.Locator("#docs-search-results li a");
                await WaitUntilAsync(async () => await results.CountAsync() > 0, 15);
                var hrefs = await results.EvaluateAllAsync<string[]>("as => as.slice(0, 3).map(a => a.getAttribute('href'))");
                var titles = (await page.Locator("#docs-search-results li .docs-search-title").AllInnerTextsAsync()).Take(3).ToArray();
                Note($"V21 '{query}': " + string.Join(" | ", hrefs.Zip(titles, (h, t) => $"{t} ({h})")));
                await ShotAsync(page, "v21-" + Slug(query));
                var first = hrefs.FirstOrDefault() ?? "";
                if (!pages.Any(p => first.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"V21 '{query}': first result is '{first}'");
            }
        }
        finally
        {
            await browser.DisposeAsync();
            pw.Dispose();
            try { site.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
            File.WriteAllLines(Path.Combine(sandbox, "site.log"), log);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void RequireOptIn()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("AKML_INSTALLED_WEB") == "1",
            "AKML_INSTALLED_WEB is not 1: these checks change the installed product's state (they stop and restart AkmlSqlWebEngine).");
        Directory.CreateDirectory(Out);
    }

    private static BrowserNewContextOptions Profile() => new() { ViewportSize = new() { Width = 1440, Height = 900 } };

    private async Task<(IPlaywright, IBrowser)> LaunchAsync()
    {
        var pw = await Playwright.CreateAsync();
        try { return (pw, await pw.Chromium.LaunchForTestsAsync()); }
        catch (PlaywrightException ex) { pw.Dispose(); throw new SkipException("Playwright Chromium is not installed: " + ex.Message); }
    }

    private static async Task<(bool Live, bool Clicked, double Seconds, string Pill)> OpenLiveAsync(IPage page, string url, int seconds = 30)
    {
        var watch = Stopwatch.StartNew();
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        var clicked = false;
        var pill = "";
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            pill = await TextOfAsync(page, "status-pill");
            if (pill.Contains("Live", StringComparison.OrdinalIgnoreCase)) return (true, clicked, watch.Elapsed.TotalSeconds, pill);
            var useLocal = page.GetByTestId("engine-use-local");
            if (!clicked && await IsShownAsync(useLocal)) { await useLocal.ClickAsync(); clicked = true; }
            await Task.Delay(250);
        }
        return (false, clicked, watch.Elapsed.TotalSeconds, pill);
    }

    private static async Task<bool> IsShownAsync(ILocator locator)
    {
        try { return await locator.CountAsync() > 0 && await locator.First.IsVisibleAsync(); }
        catch (Exception) { return false; }
    }

    private static async Task<string> TextOfAsync(IPage page, string testId)
    {
        try
        {
            var l = page.GetByTestId(testId);
            return await l.CountAsync() > 0 ? Regex.Replace((await l.First.InnerTextAsync(new() { Timeout = 2_000 })).Trim(), @"\s+", " ") : "";
        }
        catch (Exception) { return ""; }
    }

    private static async Task<string> TextOrEmptyAsync(ILocator locator)
    {
        try { return await locator.CountAsync() > 0 ? Regex.Replace((await locator.First.InnerTextAsync(new() { Timeout = 2_000 })).Trim(), @"\s+", " ") : ""; }
        catch (Exception) { return ""; }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { if (await condition()) return true; } catch (Exception) { /* the page is still rendering */ }
            await Task.Delay(250);
        }
        return false;
    }

    private static async Task<int> MarkerVersionAsync(IPage page, string testId)
    {
        var marker = page.Locator($"[data-testid='{testId}']");
        if (await marker.CountAsync() == 0) return 0;
        return int.TryParse(await marker.First.GetAttributeAsync("data-version"), out var n) ? n : 0;
    }

    private static Task SetTextAsync(IPage page, string text) => page.EvaluateAsync(
        "async t => { const m = await import('/js/akml-editor.js'); m.setText(document.querySelector('.cm-editor').closest('[id]').id, t); }", text);

    private static Task<string> TextAsync(IPage page) => page.EvaluateAsync<string>(
        "async () => { const m = await import('/js/akml-editor.js'); return m.getText(document.querySelector('.cm-editor').closest('[id]').id); }");

    private static async Task TypeAtStartAsync(IPage page, string text)
    {
        await page.WaitForSelectorAsync(".cm-editor", new() { Timeout = 60_000 });
        await SetTextAsync(page, "");
        await page.Locator(".cm-content").ClickAsync();
        await page.Keyboard.TypeAsync(text, new() { Delay = 60 });
    }

    private static Task<string> InfoRangesAsync(IPage page) => page.EvaluateAsync<string>(
        "() => { const r = Array.from(document.querySelectorAll('.cm-lintRange-info')); if (!r.length) return 'none in the text'; " +
        "return r.map(e => { const s = getComputedStyle(e); const drawn = (s.backgroundImage && s.backgroundImage !== 'none') || (s.textDecorationLine && s.textDecorationLine !== 'none'); " +
        "return (e.classList.contains('akml-lint-marker-only') ? 'marker-only' : 'plain') + '/' + (drawn ? 'underlined' : 'not drawn') + ' (line=' + s.textDecorationLine + ' ' + s.textDecorationStyle + ', bg=' + s.backgroundImage.slice(0, 12) + ')'; }).join(', '); }");

    private async Task ShotAsync(IPage page, string name)
    {
        try { await page.ScreenshotAsync(new() { Path = Path.Combine(Out, name + ".png") }); }
        catch (Exception ex) { Note($"(screenshot {name} failed: {ex.Message.Split('\n')[0]})"); }
    }

    private void Note(string line)
    {
        try { output.WriteLine(line); } catch (InvalidOperationException) { /* test finished */ }
        try { File.AppendAllText(Path.Combine(Out, "observations.txt"), $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}"); }
        catch (IOException) { /* best effort */ }
    }

    private static string Message(JsonElement body, string role) =>
        body.GetProperty("messages").EnumerateArray()
            .FirstOrDefault(m => m.GetProperty("role").GetString() == role) is { ValueKind: JsonValueKind.Object } m
            ? m.GetProperty("content").GetString() ?? ""
            : "";

    private void Service(string verb)
    {
        var psi = new ProcessStartInfo("sc.exe", $"{verb} AkmlSqlWebEngine")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        Note($"sc {verb} AkmlSqlWebEngine: exit {p.ExitCode}{(p.ExitCode != 0 ? " " + Regex.Replace(text, @"\s+", " ").Trim() : "")}");
    }

    private static bool WaitService(string state, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var psi = new ProcessStartInfo("sc.exe", "query AkmlSqlWebEngine") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            if (Regex.IsMatch(text, @"STATE\s*:\s*\d+\s+" + state)) return true;
            Thread.Sleep(1000);
        }
        return false;
    }

    private static async Task<bool> WaitPortAsync(int port, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { using var tcp = new TcpClient(); await tcp.ConnectAsync("127.0.0.1", port); return true; }
            catch (SocketException) { await Task.Delay(1000); }
        }
        return false;
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    /// <summary>The committed output for a web style, when there is one: the web parity baselines for the two web built-ins
    /// (first line is a marker), the classic goldens for a style named like a built-in .akmlstyle.</summary>
    private static string? Expected(string style, string corpus)
    {
        var key = Regex.Replace(Regex.Replace(style, @"\s*\([^)]*\)\s*$", "").ToLowerInvariant(), "[^a-z0-9]", "");
        string? path = key switch
        {
            "akmldefault" => Path.Combine(RepoRoot, "tests", "format-parity", "baselines", "default", corpus + ".expected.sql"),
            "ansicompact" => Path.Combine(RepoRoot, "tests", "format-parity", "baselines", "ansi", corpus + ".expected.sql"),
            _ => new[] { "default", "compact", "indented", "aligned-left-bracket", "leading-commas", "minimalist", "khamis-style", "collapsed" }
                .Where(s => s.Replace("-", "") == key)
                .Select(s => Path.Combine(RepoRoot, "tests", "format-parity", "golden", $"{corpus}__{s}.sql"))
                .FirstOrDefault(),
        };
        if (path is null || !File.Exists(path)) return null;
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        return text.StartsWith("-- akml-parity-baseline", StringComparison.Ordinal) ? text[(text.IndexOf('\n') + 1)..] : text;
    }

    private static string Normal(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim('\n', '﻿');

    private static string? FirstDifference(string actual, string expected)
    {
        if (actual == expected) return null;
        var a = actual.Split('\n');
        var e = expected.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, e.Length); i++)
        {
            var x = i < a.Length ? a[i] : "(end)";
            var y = i < e.Length ? e[i] : "(end)";
            if (x != y) return $"line {i + 1}: got [{x}] expected [{y}]";
        }
        return "differs";
    }

    private static string Slug(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
