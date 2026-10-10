using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 028 (M6) — launches the Blazor WASM web edition via <c>dotnet run</c> and waits for it to
/// serve, so a Playwright test can drive the real app. The scaffolds for US2/US4/US5 referred to
/// this as the "spec-024 DotnetRunFixture"; this is the concrete implementation.
///
/// <para>The app has no launchSettings, so Kestrel binds the default <c>http://localhost:5000</c>.</para>
/// </summary>
public sealed class WebAppFixture : IAsyncDisposable
{
    private Process? _proc;

    public string Url { get; private set; } = "http://localhost:5000/";

    public static async Task<WebAppFixture> StartAsync(CancellationToken ct = default)
    {
        var fixture = new WebAppFixture();
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, "src", "AkmlSql.Web", "AkmlSql.Web.csproj");

        // --no-launch-profile + explicit --urls: the app acquired a launchSettings.json after this
        // fixture was written, and its profile binds two random ports (52118/52119 on this machine).
        // Without overriding it, the fixture polls localhost:5000 for 90s while the app serves
        // happily somewhere else entirely, and reports "did not start".
        var psi = new ProcessStartInfo(
            "dotnet",
            $"run --project \"{csproj}\" --no-build -c Debug --no-launch-profile --urls {fixture.Url.TrimEnd('/')}")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        fixture._proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet run.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var res = await http.GetAsync(fixture.Url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode) return fixture;
            }
            catch (Exception) { /* not up yet */ }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        await fixture.DisposeAsync().ConfigureAwait(false);
        throw new TimeoutException($"Web app did not start at {fixture.Url} within 90s.");
    }

    /// <summary>
    /// Spec 041 (T005, research R70) — the starting point of the grid, messages, workspace and shell
    /// suites: the Debug web app, a <see cref="SandboxEngine"/> paired over loopback (no PIN), and a
    /// connection to the local SQL Server's <paramref name="database"/> with Windows authentication.
    /// Skips the test when there is no local SQL Server, no Debug build, or no Playwright Chromium.
    /// The key recorder (<see cref="Keys"/>) is installed on the browser context.
    /// </summary>
    public static async Task<SqlSession> WithSqlOrSkipAsync(
        ITestOutputHelper? output = null,
        string database = "tempdb",
        BrowserNewContextOptions? contextOptions = null)
    {
        Skip.IfNot(await SandboxEngine.LocalSqlServerAnswersAsync(), "No local SQL Server answering on localhost:1433.");

        var engine = await SandboxEngine.StartAsync();
        WebAppFixture? web = null;
        IPlaywright? pw = null;
        IBrowser? browser = null;
        try
        {
            try { web = await StartAsync(); }
            catch (Exception ex) { throw new SkipException($"Web app could not start (build it Debug first): {ex.Message}"); }

            pw = await Playwright.CreateAsync();
            try { browser = await pw.Chromium.LaunchAsync(); }
            catch (PlaywrightException) { throw new SkipException("Playwright Chromium not installed (run playwright.ps1 install chromium)."); }

            var context = await browser.NewContextAsync(contextOptions ?? new BrowserNewContextOptions { ViewportSize = new() { Width = 1440, Height = 900 } });
            await Keys.InstallAsync(context);
            var page = await context.NewPageAsync();
            page.Console += (_, m) => { if (m.Type is "error") SqlSession.Log(output, $"[browser {m.Type}] {m.Text}"); };

            await page.GotoAsync(web.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
            await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 60_000 });

            await PairAsync(page, engine.Port);
            await ConnectAsync(page, database);

            await page.ClickAsync("nav >> text=Editor");
            await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
            return new SqlSession(engine, web, pw, browser, context, page);
        }
        catch
        {
            if (browser != null) await browser.DisposeAsync();
            pw?.Dispose();
            if (web != null) await web.DisposeAsync();
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>Pair the page with an engine on 127.0.0.1 (loopback needs no PIN). Works with the
    /// single Settings page and with spec 041's Settings › Connections section.</summary>
    public static async Task PairAsync(IPage page, int port)
    {
        await page.ClickAsync("nav >> text=Settings");
        var connections = page.Locator("[data-testid='settings-nav-connections']");
        try { await connections.WaitForAsync(new() { Timeout = 3_000 }); await connections.ClickAsync(); }
        catch (TimeoutException) { /* the pre-041 Settings page shows the engine list directly */ }
        await page.WaitForTimeoutAsync(800);
        await page.Locator("button", new() { HasTextString = "Add" }).First.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await page.FillAsync("[data-testid='engine-add-name']", "Sandbox engine");
        await page.FillAsync("[data-testid='engine-add-host']", "127.0.0.1");
        await page.FillAsync("[data-testid='engine-add-port']", port.ToString());
        await page.ClickAsync("[data-testid='engine-add-pair']");
        await Assertions.Expect(page.Locator("[data-testid='status-pill']")).ToContainTextAsync("Live", new() { Timeout = 60_000 });
    }

    /// <summary>Connect to localhost/<paramref name="database"/> with Windows authentication through the
    /// connection manager.</summary>
    public static async Task ConnectAsync(IPage page, string database)
    {
        await page.ClickAsync("[data-testid='status-connection']");
        await page.WaitForSelectorAsync("[data-testid='connection-manager']");
        await page.FillAsync("[data-testid='conn-server-input']", "localhost");
        await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
        await page.CheckAsync("[data-testid='conn-auth-windows']");
        await page.ClickAsync("[data-testid='conn-database-refresh']");
        await Assertions.Expect(page.Locator($"[data-testid='conn-database-select'] option[value='{database}']"))
            .ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await page.SelectOptionAsync("[data-testid='conn-database-select']", database);
        await page.ClickAsync("[data-testid='conn-connect-btn']");
        await Assertions.Expect(page.Locator("[data-testid='status-connection']"))
            .ToContainTextAsync($"localhost/{database}", new() { Timeout = 30_000 });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (AKML-SQL.slnx) above the test bin directory.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_proc is { HasExited: false })
        {
            try { _proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            try { await _proc.WaitForExitAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
        _proc?.Dispose();
    }
}
