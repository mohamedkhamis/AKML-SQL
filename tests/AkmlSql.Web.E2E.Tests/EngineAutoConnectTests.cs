using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Reproduces the reported problem: "the web edition needs a manual engine connect every time".
///
/// <para>
/// A persistent browser profile is the whole point. Every other test here uses a fresh context,
/// which starts with empty IndexedDB and therefore <em>always</em> has to pair — so it can never
/// see this bug. The user's browser keeps its profile between visits, and that is the case under
/// test: pair once, then prove that a reload, and a full browser restart, come back Live on their
/// own.
/// </para>
/// </summary>
public sealed class EngineAutoConnectTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int BridgePort = 47291;

    // Every test here drives the app built from the working tree, not the deployed one. That
    // distinction is not pedantry: the bug these tests exist for lives in wwwroot/js/akml-bridge.js,
    // so a suite pointed at the installed copy would happily pass while the fix sat unshipped —
    // or, as happened here, fail for a reason that has nothing to do with the code under review.
    private Harness.WebAppFixture? _app;
    private string WebUrl => _app?.Url ?? "http://localhost:5000/";

    public async Task InitializeAsync() => _app = await Harness.WebAppFixture.StartAsync();

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>A stable profile directory, so a second launch is genuinely a returning visitor.</summary>
    private static string ProfileDir =>
        Path.Combine(Path.GetTempPath(), "akml-web-profile");

    [Fact]
    public async Task A_paired_engine_reconnects_by_itself_on_reload_and_on_browser_restart()
    {
        if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true);
        Directory.CreateDirectory(ProfileDir);

        using var playwright = await Playwright.CreateAsync();

        // ---- visit 1: pair ----
        string pillAfterPair, pillAfterReload;
        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);

            await GoAsync(page);
            await PairAsync(page);
            pillAfterPair = await WaitForPillAsync(page, IsLive, 90);
            output.WriteLine($"after pairing:          '{pillAfterPair}'");

            // ---- reload in the same profile ----
            await GoAsync(page);
            pillAfterReload = await WaitForPillAsync(page, IsLive, 60);
            output.WriteLine($"after reload:           '{pillAfterReload}'");
        }

        // ---- visit 2: brand-new browser, same profile ----
        string pillAfterRestart;
        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);
            await GoAsync(page);
            pillAfterRestart = await WaitForPillAsync(page, IsLive, 90);
            output.WriteLine($"after browser restart:  '{pillAfterRestart}'");
        }

        Assert.True(IsLive(pillAfterPair), $"pairing itself did not go live (was '{pillAfterPair}')");
        Assert.True(IsLive(pillAfterReload),
            $"a reload did not reconnect on its own — the pill was '{pillAfterReload}'.");
        Assert.True(IsLive(pillAfterRestart),
            $"reopening the browser did not reconnect on its own — the pill was '{pillAfterRestart}'.");
    }

    /// <summary>
    /// The scope of the actual complaint. The bridge coming back is only half the job: what the
    /// user opens the app to do is run SQL, and if the server/database has to be re-picked on every
    /// visit then from where they sit nothing was restored at all.
    /// </summary>
    [Fact]
    public async Task A_saved_sql_connection_is_restored_on_reload_without_being_re_picked()
    {
        if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true);
        Directory.CreateDirectory(ProfileDir);

        using var playwright = await Playwright.CreateAsync();

        string pillAfterSql, connAfterSql, pillAfterReload, connAfterReload;
        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);

            await GoAsync(page);
            await PairAsync(page);
            await WaitForPillAsync(page, IsLive, 90);

            await ConnectSqlAsync(page, "Northwind");
            pillAfterSql = await WaitForPillAsync(page, p => IsLive(p) && !p.Contains("no SQL", StringComparison.OrdinalIgnoreCase), 60);
            connAfterSql = await SafeTextAsync(page, "[data-testid='status-connection']");
            output.WriteLine($"after connecting SQL:  pill='{pillAfterSql}'  connection='{connAfterSql}'");

            await GoAsync(page);
            pillAfterReload = await WaitForPillAsync(page, p => IsLive(p) && !p.Contains("no SQL", StringComparison.OrdinalIgnoreCase), 60);
            connAfterReload = await SafeTextAsync(page, "[data-testid='status-connection']");
            output.WriteLine($"after reload:          pill='{pillAfterReload}'  connection='{connAfterReload}'");
        }

        Assert.True(IsLive(pillAfterSql), $"connecting to SQL did not go live (was '{pillAfterSql}')");
        Assert.DoesNotContain("no SQL", pillAfterSql, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("no SQL", pillAfterReload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Northwind", connAfterReload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The scenario a developer machine actually hits: the engine service restarts — a reboot, a
    /// Windows update, an upgrade, a crash — while the browser profile still holds its pairing.
    ///
    /// <para>
    /// If the engine forgets the bearer tokens it issued, every restart silently demotes the user
    /// to "pair again with a fresh PIN", which is exactly what "it needs a manual connect every
    /// time" feels like from the outside.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_pairing_survives_an_engine_service_restart()
    {
        if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true);
        Directory.CreateDirectory(ProfileDir);

        using var playwright = await Playwright.CreateAsync();
        string pillBefore, pillAfter;

        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);
            await GoAsync(page);
            await PairAsync(page);
            pillBefore = await WaitForPillAsync(page, IsLive, 90);
            output.WriteLine($"paired:                 '{pillBefore}'");
        }

        RestartEngineService();

        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);
            await GoAsync(page);
            pillAfter = await WaitForPillAsync(page, IsLive, 90);
            output.WriteLine($"after service restart:  '{pillAfter}'");
        }

        Assert.True(IsLive(pillBefore), $"pairing did not go live (was '{pillBefore}')");
        Assert.True(IsLive(pillAfter),
            $"the pairing did not survive an engine restart — the pill was '{pillAfter}'. " +
            "The stored bearer token was rejected, so the user is forced to re-pair with a new PIN.");
    }

    /// <summary>
    /// The scenario behind "it needs a manual connect every time, and I want it to wake the engine
    /// up": the browser is opened while the engine is not running.
    ///
    /// <para>
    /// The startup auto-connect is a single fire-and-forget attempt. If it fails, the bridge goes to
    /// <c>Failed</c> and stays there — the backoff/reconnect loop only covers a connection that was
    /// already open and then dropped. So a page opened a moment too early never recovers on its own,
    /// no matter how healthy the engine becomes afterwards, and the only way out is a manual click.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_page_recovers_on_its_own_when_the_engine_starts_after_the_browser()
    {
        if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true);
        Directory.CreateDirectory(ProfileDir);

        using var playwright = await Playwright.CreateAsync();

        // Pair while the engine is up, so the profile holds a usable bearer.
        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);
            await GoAsync(page);
            await PairAsync(page);
            var paired = await WaitForPillAsync(page, IsLive, 90);
            Assert.True(IsLive(paired), $"pairing did not go live (was '{paired}')");
        }

        ServiceControl("stop");
        try
        {
            await using var ctx = await LaunchAsync(playwright);
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);

            // Open the app with the engine down; the first auto-connect must fail.
            await GoAsync(page);
            var offline = await WaitForPillAsync(page, p => !IsLive(p), 30);
            output.WriteLine($"engine down, page open: '{offline}'");

            // Now bring the engine back, exactly as a user would, and DO NOTHING in the browser.
            ServiceControl("start");
            output.WriteLine("engine restarted; waiting for the page to notice, without touching it...");

            var recovered = await WaitForPillAsync(page, IsLive, 90);
            output.WriteLine($"after engine came back: '{recovered}'");

            Assert.True(IsLive(recovered),
                $"the page never reconnected on its own — the pill sat at '{recovered}' for 90s after " +
                "the engine came back. A failed first connect is terminal, so the user has to click.");
        }
        finally
        {
            ServiceControl("start");
        }
    }

    private void ServiceControl(string verb)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", $"{verb} AkmlSqlWebEngine")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(30_000);
        output.WriteLine($"sc {verb}: exit {p.ExitCode}");
        Thread.Sleep(5_000);
    }

    /// <summary>
    /// The fix, end to end, against a freshly built app rather than the deployed one — so it proves
    /// the code in the working tree, not whatever was last installed.
    ///
    /// <para>
    /// Two behaviours that did not exist before: a first connect that fails keeps retrying instead
    /// of dying, and the status bar says what is wrong and offers a way out instead of showing a
    /// bare "Offline".
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_fresh_build_retries_a_failed_first_connect_and_explains_itself()
    {
        if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true);
        Directory.CreateDirectory(ProfileDir);


        using var playwright = await Playwright.CreateAsync();

        // Pair against the running engine first.
        await using (var ctx = await LaunchAsync(playwright))
        {
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);
            await GoAsync(page);
            await PairAsync(page);
            var paired = await WaitForPillAsync(page, IsLive, 90);
            Assert.True(IsLive(paired), $"pairing did not go live (was '{paired}')");
        }

        ServiceControl("stop");
        try
        {
            await using var ctx = await LaunchAsync(playwright);
            var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
            Wire(page);

            // Open with the engine down. The status bar must EXPLAIN, not just say "Offline".
            await GoAsync(page);

            // Diagnostics first: if the strip never appears, the useful evidence is what the status
            // bar DID render, not a bare selector timeout.
            for (var i = 0; i < 6; i++)
            {
                await page.WaitForTimeoutAsync(5_000);
                var bar = await page.InnerHTMLAsync(".akml-status");
                output.WriteLine($"[t+{(i + 1) * 5}s] status bar: {bar.Replace("\n", " ")}");
                if (await page.Locator("[data-testid='engine-issue']").CountAsync() > 0) break;
            }

            await page.WaitForSelectorAsync("[data-testid='engine-issue']", new() { Timeout = 20_000 });
            var issue = await page.InnerTextAsync("[data-testid='engine-issue']");
            output.WriteLine($"engine down, status bar says: {issue.Replace("\n", " | ")}");

            Assert.Contains("not responding", issue, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await page.Locator("[data-testid='engine-retry']").CountAsync());

            // Bring the engine back and touch nothing. The retry loop must recover on its own.
            ServiceControl("start");
            var recovered = await WaitForPillAsync(page, IsLive, 120);
            output.WriteLine($"after engine returned: '{recovered}'");
            Assert.True(IsLive(recovered),
                $"the page did not reconnect on its own — pill was '{recovered}'");

            // ...and the problem strip must go away once things are healthy again.
            Assert.Equal(0, await page.Locator("[data-testid='engine-issue']").CountAsync());
        }
        finally
        {
            ServiceControl("start");
        }
    }

    private void RestartEngineService()
    {
        foreach (var verb in new[] { "stop", "start" })
        {
            var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", $"{verb} AkmlSqlWebEngine")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(30_000);
            output.WriteLine($"sc {verb}: exit {p.ExitCode}");
            Thread.Sleep(4_000);
        }
        Thread.Sleep(4_000);   // let the bridge listener bind again
    }

    private async Task ConnectSqlAsync(IPage page, string database)
    {
        await page.ClickAsync("[data-testid='status-connection']");
        await page.WaitForSelectorAsync("[data-testid='connection-manager']", new() { Timeout = 15_000 });
        await page.ClickAsync("[data-testid='conn-new-btn']");
        await page.FillAsync("[data-testid='conn-name-input']", $"{database} (local)");
        await page.FillAsync("[data-testid='conn-server-input']", "(local)");
        await page.CheckAsync("[data-testid='conn-auth-windows']");
        await page.ClickAsync("[data-testid='conn-test-btn']");
        await page.WaitForTimeoutAsync(5_000);
        await page.ClickAsync("[data-testid='conn-database-refresh']");
        await page.WaitForTimeoutAsync(4_000);
        await page.SelectOptionAsync("[data-testid='conn-database-select']", new SelectOptionValue { Label = database });
        await page.ClickAsync("[data-testid='conn-save-btn']");
        await page.WaitForTimeoutAsync(800);
        await page.ClickAsync("[data-testid='conn-connect-btn']");
        await page.WaitForTimeoutAsync(4_000);
    }

    private static async Task<string> SafeTextAsync(IPage page, string selector)
    {
        try { return await page.InnerTextAsync(selector, new() { Timeout = 3_000 }); }
        catch (Exception) { return "(absent)"; }
    }

    // ---- helpers ------------------------------------------------------------

    private static bool IsLive(string pill) =>
        pill.Contains("Live", StringComparison.OrdinalIgnoreCase)
        || pill.Contains("Online", StringComparison.OrdinalIgnoreCase);

    private static Task<IBrowserContext> LaunchAsync(IPlaywright pw) =>
        pw.Chromium.LaunchPersistentContextAsync(ProfileDir, new()
        {
            Args = ["--ignore-certificate-errors"],
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1600, Height = 900 },
            ColorScheme = ColorScheme.Dark,
        });

    private void Wire(IPage page)
    {
        page.Console += (_, m) => { if (m.Type == "error") output.WriteLine($"[console.error] {m.Text}"); };
        page.WebSocket += (_, ws) =>
        {
            output.WriteLine($"[ws] {ws.Url}");
            ws.SocketError += (_, e) => output.WriteLine($"[ws error] {e}");
        };
    }

    private Task GoAsync(IPage page) => GoAsync(page, WebUrl);

    private static async Task GoAsync(IPage page, string url)
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 60_000 });
    }

    private async Task PairAsync(IPage page)
    {
        await page.ClickAsync("nav >> text=Settings");
        await page.WaitForTimeoutAsync(1_200);
        await page.Locator("button", new() { HasTextString = "Add" }).First.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var dialog = page.Locator(".akml-connections-dialog");
        await dialog.Locator("input:not([type=checkbox])").Nth(0).FillAsync("Local engine");
        await dialog.Locator("input:not([type=checkbox])").Nth(1).FillAsync(Environment.MachineName);
        await dialog.Locator("input:not([type=checkbox])").Nth(2).FillAsync(BridgePort.ToString());
        await dialog.Locator("input[type=checkbox]").First.UncheckAsync();
        await page.WaitForTimeoutAsync(300);

        var pin = dialog.Locator("input:not([type=checkbox])").Nth(3);
        if (await pin.IsVisibleAsync()) await pin.FillAsync(ReadPairingPin());

        await page.Locator("button", new() { HasTextString = "Pair" }).First.ClickAsync();
    }

    private static string ReadPairingPin()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AKML SQL Web", "pairing-pin.txt");
        var pin = File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        if (string.IsNullOrEmpty(pin))
            throw new InvalidOperationException(
                "No pairing PIN published. Restart AkmlSqlWebEngine to mint one.");
        return pin;
    }

    private async Task<string> WaitForPillAsync(IPage page, Func<string, bool> ready, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var last = "(none)";
        while (DateTime.UtcNow < deadline)
        {
            try { last = await page.InnerTextAsync("[data-testid='status-pill']", new() { Timeout = 3_000 }); }
            catch (Exception) { /* mid-render */ }
            if (ready(last)) return last;
            await page.WaitForTimeoutAsync(1_000);
        }
        return last;
    }
}
