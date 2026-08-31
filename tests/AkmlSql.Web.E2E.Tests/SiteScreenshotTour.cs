using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Regenerates the web-edition screenshots used on the product site.
///
/// <para>
/// Not a test of behaviour — a repeatable way to produce marketing images, so they can be refreshed
/// whenever the UI changes instead of being hand-captured once and slowly going stale.
/// </para>
///
/// <para>
/// Everything is shot against <b>Northwind</b>. The images this replaces were taken against a real
/// working database and published to a public site showing live personal data; sample data is the
/// only safe thing to put in a screenshot.
/// </para>
///
/// <para>Run explicitly (it is skipped unless the web edition is reachable):</para>
/// <code>
/// dotnet test tests/AkmlSql.Web.E2E.Tests --filter FullyQualifiedName~SiteScreenshotTour
/// </code>
/// </summary>
public sealed class SiteScreenshotTour(ITestOutputHelper output)
{
    /// <summary>The deployed web edition (IIS site "AkmlSqlWeb", bound to port 80 with no host header).</summary>
    private const string WebUrl = "http://localhost/";

    /// <summary>Matches the dimensions the site's img tags already declare, so no layout shifts.</summary>
    private const int ShotWidth = 1920;
    private const int ShotHeight = 889;

    private static string SiteImageDirectory =>
        Path.Combine(RepoRoot(), "src", "AkmlSql.Site", "wwwroot", "img", "screenshots");

    [Fact]
    public async Task Explore_the_deployed_web_edition()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = ShotWidth, Height = ShotHeight },
            DeviceScaleFactor = 1,
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(WebUrl, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        output.WriteLine($"GET {WebUrl} -> {response?.Status}");

        // Blazor WASM boots after the document is idle; wait for the app shell to actually exist.
        await page.WaitForSelectorAsync("[data-testid='execute-button'], .akml-shell, header",
            new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(2_000);

        output.WriteLine($"Title: {await page.TitleAsync()}");

        var ids = await page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('[data-testid]')).map(e => e.getAttribute('data-testid'))");
        output.WriteLine($"Visible data-testids ({ids.Length}):");
        foreach (var id in ids.Distinct().OrderBy(x => x)) output.WriteLine($"  {id}");

        var dir = Path.Combine(Path.GetTempPath(), "akml-web-tour");
        Directory.CreateDirectory(dir);
        var shot = Path.Combine(dir, "explore-initial.png");
        await page.ScreenshotAsync(new() { Path = shot });
        output.WriteLine($"Screenshot: {shot}");
    }

    /// <summary>
    /// The hero shot: a real Northwind query, executed, with the schema tree, the problems panel
    /// and the result grid all populated.
    /// </summary>
    [Fact]
    public async Task Capture_editor_with_northwind()
    {
        const string Query =
            """
            SELECT TOP (50)
                   c.CompanyName,
                   o.OrderID,
                   o.OrderDate,
                   SUM(od.UnitPrice * od.Quantity) AS OrderTotal
            FROM dbo.Orders AS o
                 INNER JOIN dbo.Customers AS c ON c.CustomerID = o.CustomerID
                 INNER JOIN dbo.[Order Details] AS od ON od.OrderID = o.OrderID
            WHERE o.OrderDate >= '1997-01-01'
            GROUP BY c.CompanyName, o.OrderID, o.OrderDate
            ORDER BY OrderTotal DESC;
            """;

        using var playwright = await Playwright.CreateAsync();
        // The engine bridge presents a self-signed certificate (CN=AKML SQL Web Engine), so without
        // this Chromium refuses the wss:// upgrade and the handshake sits at "Connecting" forever
        // with nothing in the UI to say why. A real user clicks through the browser's warning once;
        // an automated browser has to be told.
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Args = ["--ignore-certificate-errors"],
        });
        var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = ShotWidth, Height = ShotHeight },
            DeviceScaleFactor = 1,
            ColorScheme = ColorScheme.Dark,
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        var outDir = Path.Combine(Path.GetTempPath(), "akml-web-tour");
        Directory.CreateDirectory(outDir);

        // The bridge handshake happens entirely in the browser, so when it stalls the only
        // explanation is in the console / websocket events -- the UI just says "Connecting".
        page.Console += (_, m) =>
        {
            if (m.Type is "error" or "warning") output.WriteLine($"[console.{m.Type}] {m.Text}");
        };
        page.PageError += (_, e) => output.WriteLine($"[pageerror] {e}");
        page.WebSocket += (_, ws) =>
        {
            output.WriteLine($"[ws] open {ws.Url}");
            ws.SocketError += (_, err) => output.WriteLine($"[ws] error {err}");
            ws.Close += (_, _) => output.WriteLine($"[ws] closed {ws.Url}");
        };

        await page.GotoAsync(WebUrl, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(1_500);

        await PairEngineAndSetThemeAsync(page, outDir);

        // ---- connect to Northwind ----
        await page.ClickAsync("[data-testid='status-connection']");
        await page.WaitForSelectorAsync("[data-testid='connection-manager']", new() { Timeout = 15_000 });
        output.WriteLine("Connection manager opened.");

        await page.ClickAsync("[data-testid='conn-new-btn']");
        await page.FillAsync("[data-testid='conn-name-input']", "Northwind (local)");
        await page.FillAsync("[data-testid='conn-server-input']", "(local)");
        await page.CheckAsync("[data-testid='conn-auth-windows']");

        await page.WaitForTimeoutAsync(500);
        await DumpAsync(page, outDir, "conn-form");

        // The database list comes from the engine, so it only exists once the connection has been
        // proved. Test first, then refresh, then pick.
        if (await page.Locator("[data-testid='conn-test-btn']").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid='conn-test-btn']");
            await page.WaitForTimeoutAsync(6_000);
            output.WriteLine("After Test: " + await SafeTextAsync(page, "[data-testid='conn-message']"));
            await DumpAsync(page, outDir, "conn-after-test");
        }

        if (await page.Locator("[data-testid='conn-database-refresh']").IsVisibleAsync())
        {
            await page.ClickAsync("[data-testid='conn-database-refresh']");
            await page.WaitForTimeoutAsync(5_000);
        }

        var dbSelect = page.Locator("[data-testid='conn-database-select']");
        if (await dbSelect.IsVisibleAsync())
        {
            var options = await dbSelect.Locator("option").AllInnerTextsAsync();
            output.WriteLine("Databases offered: " + string.Join(" | ", options));
            await dbSelect.SelectOptionAsync(new SelectOptionValue { Label = "Northwind" });
        }
        else
        {
            output.WriteLine("conn-database-select never became visible; see the dumps.");
            await DumpAsync(page, outDir, "conn-no-db-select");
        }

        await page.ClickAsync("[data-testid='conn-save-btn']");
        await page.WaitForTimeoutAsync(1_200);

        // Site image 2 — the connection dialog. Captured as an element so the crop is the dialog
        // itself rather than a mostly-empty 1920px page.
        var manager = page.Locator("[data-testid='connection-manager']");
        await manager.ScreenshotAsync(new() { Path = Path.Combine(outDir, "connect-dialog.png") });

        // Site image 3 — the database picker, with the list the engine returned. Focusing the
        // select highlights it without opening a native dropdown, which does not appear in
        // screenshots on any platform.
        await page.FocusAsync("[data-testid='conn-database-select']");
        await page.WaitForTimeoutAsync(400);
        await manager.ScreenshotAsync(new() { Path = Path.Combine(outDir, "connect-dialog-server.png") });

        await page.ClickAsync("[data-testid='conn-connect-btn']");
        await page.WaitForTimeoutAsync(5_000);
        output.WriteLine($"Status after connect: {await page.InnerTextAsync("[data-testid='status-connection']")}");

        // ---- write and execute ----
        await page.ClickAsync("[data-testid='sql-editor']");
        await page.Keyboard.InsertTextAsync(Query);
        await page.WaitForTimeoutAsync(2_500);

        await page.ClickAsync("[data-testid='execute-button']");
        await page.WaitForTimeoutAsync(6_000);

        // Site image 1 — the hero.
        var shot = Path.Combine(outDir, "query-results.png");
        await page.ScreenshotAsync(new() { Path = shot });
        output.WriteLine($"Hero shot: {shot}");

        // Guard the whole point of this exercise: the images that ship must contain sample data,
        // never anything from a real database. A screenshot is published, and once published it is
        // out of your hands.
        var grid = await page.InnerTextAsync("body");
        foreach (var forbidden in new[] { "aqmar", "martyrs", "Toledo" })
        {
            Assert.DoesNotContain(forbidden, grid, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("Northwind", grid, StringComparison.OrdinalIgnoreCase);

        foreach (var f in new[] { "query-results.png", "connect-dialog.png", "connect-dialog-server.png" })
        {
            var src = Path.Combine(outDir, f);
            Assert.True(File.Exists(src), $"{f} was not captured.");
            output.WriteLine($"  {f}: {new FileInfo(src).Length:N0} bytes");
        }
    }

    /// <summary>
    /// Pairs the browser with the local engine bridge and switches the app to its dark theme.
    ///
    /// <para>
    /// The web edition reaches SQL Server through a paired engine over the WebSocket bridge, not
    /// directly — so without this the SQL connection dialog can only ever offer <c>master</c> and
    /// says "Pair an engine first". Localhost pairing skips the PIN.
    /// </para>
    /// <para>
    /// Theme is set through the app's own Settings radio rather than by emulating
    /// <c>prefers-color-scheme</c>, because the app persists an explicit choice that overrides the
    /// OS hint — emulation alone leaves it light.
    /// </para>
    /// </summary>
    private async Task PairEngineAndSetThemeAsync(IPage page, string outDir)
    {
        await page.ClickAsync("nav >> text=Settings");
        await page.WaitForTimeoutAsync(1_500);

        await page.Locator("label", new() { HasTextString = "Dark" }).First.ClickAsync();
        await page.WaitForTimeoutAsync(800);
        output.WriteLine("Theme set to dark.");

        var addButton = page.Locator("button", new() { HasTextString = "Add" }).First;
        await addButton.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // Scoped to the dialog and addressed positionally: the labels are not distinct enough for
        // text matching, because "Localhost (no PIN required)" also contains "Host".
        //
        // "Localhost" is deliberately left UNCHECKED. That checkbox does not just waive the PIN --
        // IEngineBridge builds "ws://" for it and "wss://" otherwise -- and the installer here
        // provisioned the bridge in LAN mode with TLS, so the plaintext path is reset by the
        // TLS listener. The host must also be a name the certificate covers: its SAN list is the
        // machine name and the public IP, not 127.0.0.1.
        var dialog = page.Locator(".akml-connections-dialog");
        await dialog.Locator("input:not([type=checkbox])").Nth(0).FillAsync("Local engine");
        await dialog.Locator("input:not([type=checkbox])").Nth(1).FillAsync(Environment.MachineName);
        await dialog.Locator("input:not([type=checkbox])").Nth(2).FillAsync(BridgePort.ToString());
        await dialog.Locator("input[type=checkbox]").First.UncheckAsync();
        await page.WaitForTimeoutAsync(400);

        // The PIN field only exists once Localhost is unchecked. The engine publishes its current
        // pairing PIN to %ProgramData%\AKML SQL Web\pairing-pin.txt (EngineHost FR-008, so the
        // installer can print it in INSTALL-SUMMARY.txt) -- read it from there rather than baking a
        // credential into the test.
        var pinBox = dialog.Locator("input:not([type=checkbox])").Nth(3);
        if (await pinBox.IsVisibleAsync())
        {
            await pinBox.FillAsync(ReadPairingPin());
            output.WriteLine("Pairing PIN supplied from the engine's published PIN file.");
        }

        await page.Locator("button", new() { HasTextString = "Pair" }).First.ClickAsync();

        // The handshake takes a while, and the status bar animates through it — which also means
        // Playwright's stability check will refuse to click anything until it settles.
        var pill = await WaitForPillAsync(page, s => s.Contains("Live", StringComparison.OrdinalIgnoreCase)
                                                  || s.Contains("Online", StringComparison.OrdinalIgnoreCase),
                                          timeoutSeconds: 90);
        output.WriteLine($"Bridge status pill: {pill}");
        await DumpAsync(page, outDir, "after-pair");

        // Back to the editor by URL: clicking the nav races the status bar's animation, and a
        // reload is harmless because the pairing lives in IndexedDB.
        await page.GotoAsync(WebUrl, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 30_000 });
        await WaitForPillAsync(page, s => s.Contains("Live", StringComparison.OrdinalIgnoreCase)
                                       || s.Contains("Online", StringComparison.OrdinalIgnoreCase),
                               timeoutSeconds: 60);
        await page.WaitForTimeoutAsync(1_500);
    }

    /// <summary>Bridge port from %ProgramData%\AKML SQL Web\config.json.</summary>
    private const int BridgePort = 47291;

    /// <summary>
    /// The engine's current pairing PIN, as it publishes it for the installer to surface. A PIN is
    /// single-use: once this pairing consumes it the engine mints a fresh one, so a later run reads
    /// the new value rather than a stale constant.
    /// </summary>
    private static string ReadPairingPin()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AKML SQL Web", "pairing-pin.txt");

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"No pairing PIN at {path}. Restart the AkmlSqlWebEngine service to mint one.");

        var pin = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(pin))
            throw new InvalidOperationException(
                "The published pairing PIN is empty, which means it has already been consumed. " +
                "Restart the AkmlSqlWebEngine service to mint a fresh one.");
        return pin;
    }

    /// <summary>Polls the connection pill until <paramref name="isReady"/> holds, and returns its text.</summary>
    private async Task<string> WaitForPillAsync(IPage page, Func<string, bool> isReady, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var last = "(none)";
        while (DateTime.UtcNow < deadline)
        {
            last = await SafeTextAsync(page, "[data-testid='status-pill']");
            if (isReady(last)) return last;
            await page.WaitForTimeoutAsync(1_000);
        }
        output.WriteLine($"Pill never reached the expected state; last value: '{last}'");
        return last;
    }

    /// <summary>Screenshots the page and lists the test ids currently in the DOM.</summary>
    private async Task DumpAsync(IPage page, string outDir, string name)
    {
        await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, $"dump-{name}.png") });
        var ids = await page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('[data-testid]')).map(e => e.getAttribute('data-testid'))");
        output.WriteLine($"[{name}] testids: {string.Join(", ", ids.Distinct().OrderBy(x => x))}");
    }

    private static async Task<string> SafeTextAsync(IPage page, string selector)
    {
        try { return await page.InnerTextAsync(selector, new() { Timeout = 3_000 }); }
        catch (Exception) { return "(absent)"; }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root.");
    }
}
