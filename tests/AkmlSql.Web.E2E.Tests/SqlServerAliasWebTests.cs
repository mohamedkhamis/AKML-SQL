using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// The web edition reaches a SQL Server through a SQL Server client alias: the engine resolves the
/// alias (the ConnectTo registry key on the ENGINE's machine) before the bridge guard judges the
/// target and before it connects — database list, Test, Connect and schema-backed completion.
///
/// <para>Needs, on this machine: the alias <c>AkmlAliasTest</c> (created by the tester, e.g.
/// <c>DBMSSOCN,127.0.0.1,1433</c>) and a SQL login given in <c>AKML_ALIAS_TEST_LOGIN</c> /
/// <c>AKML_ALIAS_TEST_PASSWORD</c> that can read Northwind; skipped otherwise. The engine is the
/// Debug build of this working tree, started sandboxed like <see cref="FormatStylesSharedEngineTests"/>;
/// the installed web engine is not touched.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class SqlServerAliasWebTests(ITestOutputHelper output)
{
    private const string Alias = "AkmlAliasTest";

    private static bool AliasExists()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\MSSQLServer\Client\ConnectTo");
            if (key?.GetValue(Alias) is string) return true;
        }
        return false;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    private sealed class SandboxEngine : IAsyncDisposable
    {
        public required Process Process { get; init; }
        public required string Root { get; init; }
        public required int Port { get; init; }

        public async ValueTask DisposeAsync()
        {
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { }
            try { await Process.WaitForExitAsync(); } catch { }
            Process.Dispose();
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private static async Task<SandboxEngine> StartEngineAsync()
    {
        var exe = Path.Combine(RepoRoot(), "src", "AkmlSql.Engine", "bin", "Debug", "net10.0", "win-x64", "AkmlSql.Engine.exe");
        if (!File.Exists(exe)) throw new SkipException($"Build the engine first (Debug): {exe}");

        var root = Directory.CreateTempSubdirectory("akml-alias-engine-").FullName;
        var port = FreePort();
        var config = Path.Combine(root, "engine-config.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            configVersion = 1,
            bridge = new { enabled = true, bindAddress = "127.0.0.1", port, tokenStorePath = Path.Combine(root, "tokens.json"), tokenTtlDays = 1 },
        }));

        var psi = new ProcessStartInfo(exe, $"--web --config \"{config}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.Environment["AKML_APP_DATA_ROOT"] = root;
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Engine did not start.");
        var engine = new SandboxEngine { Process = process, Root = root, Port = port };

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) { await engine.DisposeAsync(); throw new InvalidOperationException($"Engine exited with code {process.ExitCode}."); }
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                return engine;
            }
            catch (SocketException) { await Task.Delay(300); }
        }
        await engine.DisposeAsync();
        throw new TimeoutException("Engine bridge did not start listening.");
    }

    [SkippableFact]
    public async Task A_client_alias_works_for_the_database_list_test_connect_and_completion()
    {
        var login = Environment.GetEnvironmentVariable("AKML_ALIAS_TEST_LOGIN");
        var password = Environment.GetEnvironmentVariable("AKML_ALIAS_TEST_PASSWORD");
        Skip.If(string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password), "No test login (AKML_ALIAS_TEST_LOGIN / _PASSWORD).");
        Skip.IfNot(AliasExists(), $"No SQL Server client alias '{Alias}' on this machine.");

        await using var engine = await StartEngineAsync();
        WebAppFixture web;
        try { web = await WebAppFixture.StartAsync(); }
        catch (Exception ex) { throw new SkipException($"Web app could not start (build it first): {ex.Message}"); }
        await using var webScope = web;

        using var pw = await Playwright.CreateAsync();
        IBrowser browser;
        try { browser = await pw.Chromium.LaunchAsync(); }
        catch (PlaywrightException) { throw new SkipException("Playwright Chromium not installed (run playwright.ps1 install chromium)."); }
        await using var browserScope = browser;
        var page = await browser.NewPageAsync();
        page.Console += (_, m) => { if (m.Type is "error" or "warning") output.WriteLine($"[browser {m.Type}] {m.Text}"); };

        // Pair with the sandbox engine (loopback: no PIN).
        await page.GotoAsync(web.Url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForSelectorAsync("[data-testid='execute-button']", new() { Timeout = 60_000 });
        await page.ClickAsync("nav >> text=Settings");
        await page.WaitForTimeoutAsync(1_200);
        await page.Locator("button", new() { HasTextString = "Add" }).First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
        await page.FillAsync("[data-testid='engine-add-name']", "Sandbox engine");
        await page.FillAsync("[data-testid='engine-add-host']", "127.0.0.1");
        await page.FillAsync("[data-testid='engine-add-port']", engine.Port.ToString());
        await page.ClickAsync("[data-testid='engine-add-pair']");
        await Assertions.Expect(page.Locator("[data-testid='status-pill']")).ToContainTextAsync("Live", new() { Timeout = 60_000 });

        // The connection manager, with the alias as the server.
        await page.ClickAsync("[data-testid='status-connection']");
        await page.WaitForSelectorAsync("[data-testid='connection-manager']");
        await page.FillAsync("[data-testid='conn-server-input']", Alias);
        await page.Locator("[data-testid='conn-server-input']").DispatchEventAsync("change");
        await page.CheckAsync("[data-testid='conn-auth-sql']");
        await page.FillAsync("[data-testid='conn-login-input']", login!);
        await page.FillAsync("[data-testid='conn-password-input']", password!);

        // The database list comes through the engine, over the alias.
        await page.ClickAsync("[data-testid='conn-database-refresh']");
        await Assertions.Expect(page.Locator("[data-testid='conn-database-select'] option[value='Northwind']"))
            .ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await page.SelectOptionAsync("[data-testid='conn-database-select']", "Northwind");

        await page.ClickAsync("[data-testid='conn-test-btn']");
        await Assertions.Expect(page.Locator("[data-testid='conn-message']")).ToContainTextAsync("Connection test succeeded", new() { Timeout = 30_000 });

        await page.ClickAsync("[data-testid='conn-connect-btn']");
        await Assertions.Expect(page.Locator("[data-testid='status-connection']")).ToContainTextAsync($"{Alias}/Northwind", new() { Timeout = 30_000 });

        // Completion lists Northwind's tables: the engine loaded the schema through the alias.
        await page.ClickAsync("nav >> text=Editor");
        await page.WaitForSelectorAsync("[data-testid='sql-editor'] .cm-content", new() { Timeout = 30_000 });
        var labels = Array.Empty<string>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            await page.ClickAsync("[data-testid='sql-editor'] .cm-content");
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await page.Keyboard.TypeAsync("SELECT * FROM dbo.Pro");
            await page.Keyboard.PressAsync("Control+Space");
            await page.WaitForTimeoutAsync(1_500);
            labels = await page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.cm-completionLabel')).map(e => e.textContent)");
            if (labels.Any(l => l.Contains("Products", StringComparison.OrdinalIgnoreCase))) break;
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(2_000);
        }
        await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "akml-web-alias.png") });
        output.WriteLine("completion: " + string.Join(", ", labels.Take(8)));
        Assert.Contains(labels, l => l.Contains("Products", StringComparison.OrdinalIgnoreCase));
    }
}
