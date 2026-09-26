using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// "Shared with SSMS": a style saved on the web Format styles page while an engine is paired
/// lands in that engine's styles folder — the folder SSMS reads — as a SQL Prompt
/// style, and edits saved from the web update the same file.
///
/// <para>The engine is the one built from this working tree (Debug), started in web mode on a free
/// loopback port with <c>AKML_APP_DATA_ROOT</c> pointing at a temp folder, so neither the machine's
/// installed web engine nor the signed-in user's real styles are touched. Loopback needs no PIN.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class FormatStylesSharedEngineTests(ITestOutputHelper output)
{
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
        public string StylesFolder => Path.Combine(Root, "AKML SQL", "profiles");

        public async ValueTask DisposeAsync()
        {
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { }
            try { await Process.WaitForExitAsync(); } catch { }
            Process.Dispose();
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private static async Task<SandboxEngine> StartEngineOrSkipAsync()
    {
        var exe = Path.Combine(RepoRoot(), "src", "AkmlSql.Engine", "bin", "Debug", "net10.0", "win-x64", "AkmlSql.Engine.exe");
        if (!File.Exists(exe)) throw new SkipException($"Build the engine first (Debug): {exe}");

        var root = Directory.CreateTempSubdirectory("akml-styles-engine-").FullName;
        var port = FreePort();
        var config = Path.Combine(root, "engine-config.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            configVersion = 1,
            bridge = new
            {
                enabled = true,
                bindAddress = "127.0.0.1",
                port,
                tokenStorePath = Path.Combine(root, "tokens.json"),
                tokenTtlDays = 1,
            },
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
            if (process.HasExited)
            {
                await engine.DisposeAsync();
                throw new InvalidOperationException($"Engine exited with code {process.ExitCode}.");
            }
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
    public async Task A_style_saved_on_the_web_lands_in_the_engines_styles_folder_as_a_sql_prompt_style()
    {
        await using var engine = await StartEngineOrSkipAsync();
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
        page.Dialog += async (_, d) => { if (d.Type == DialogType.Prompt) await d.AcceptAsync("Shared E2E"); else await d.AcceptAsync(); };
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

        // The Format styles page now saves on the engine and lists its styles.
        await page.ClickAsync("nav >> text=Format styles");
        await Assertions.Expect(page.Locator("[data-testid=styles-location]")).ToContainTextAsync("engine", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".akml-styles-group", new() { HasTextString = "Shared with SSMS" })).ToBeVisibleAsync();

        await page.Locator("[data-testid=style-new]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=style-name]")).ToHaveTextAsync("Shared E2E");
        await Assertions.Expect(page.Locator("[data-testid=styles-status]")).ToContainTextAsync("on the engine");

        var file = Path.Combine(engine.StylesFolder, "Shared E2E.akmlstyle");
        Assert.True(File.Exists(file), $"The engine did not write {file}.");
        using (var stored = JsonDocument.Parse(File.ReadAllText(file)))
            Assert.True(stored.RootElement.TryGetProperty("sqlPrompt", out _), "The engine's copy is not a SQL Prompt style.");

        // An edit saved from the web updates the engine's file (and its AKML projection).
        await page.Locator("[data-testid=page-lists]").ClickAsync();
        await page.Locator("[data-testid='option-lists.placeCommasBeforeItems']").CheckAsync();
        await page.Locator("[data-testid=style-save]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=styles-status]")).ToContainTextAsync("Saved");

        using var updated = JsonDocument.Parse(File.ReadAllText(file));
        Assert.True(updated.RootElement.GetProperty("sqlPrompt").GetProperty("lists").GetProperty("placeCommasBeforeItems").GetBoolean());
        Assert.Equal("leading", updated.RootElement.GetProperty("list").GetProperty("commaPosition").GetString());
    }
}
