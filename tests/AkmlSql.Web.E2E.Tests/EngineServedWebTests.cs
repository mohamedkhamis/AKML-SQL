using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

[CollectionDefinition("Engine served web", DisableParallelization = true)]
public sealed class EngineServedWebCollection;

[Collection("Engine served web")]
public sealed class EngineServedWebTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_browser_HEAD_fetch_finishes_without_the_WASM_runtime()
    {
        using var bundle = new EngineServedBundle();
        await using var engine = await SandboxEngine.StartAsync(webRoot: bundle.Root);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchForTestsAsync();
        var page = await browser.NewPageAsync();
        var finished = new TaskCompletionSource<string>();
        page.RequestFailed += (_, request) => { if (request.Method == "HEAD") finished.TrySetResult(request.Failure ?? "failed"); };
        page.RequestFinished += (_, request) => { if (request.Method == "HEAD") finished.TrySetResult("finished"); };
        await page.GotoAsync($"http://127.0.0.1:{engine.Port}/missing.txt");
        Assert.Equal(200, await page.EvaluateAsync<int>("async () => { const initial = await fetch('/'); await initial.arrayBuffer(); const controller = new AbortController(); const r = await fetch('/', {method: 'HEAD', cache: 'no-cache', headers: {'Cache-Control':'no-cache'}, signal: controller.signal}); await r.arrayBuffer(); return r.status; }"));
        Assert.Equal("finished", await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Engine_serves_the_real_app_reloads_routes_and_runs_SQL_without_pairing()
    {
        using var bundle = new EngineServedBundle();
        await using var engine = await SandboxEngine.StartAsync(webRoot: bundle.Root);
        var origin = $"http://127.0.0.1:{engine.Port}";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchForTestsAsync();
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add("Page: " + error);
        page.Console += (_, message) => { if (message.Type == "error") errors.Add("Console: " + message.Text); };
        page.RequestFailed += (_, request) => errors.Add($"Request: {request.Method} {request.Url}: {request.Failure}");
        page.Response += (_, response) => { if (response.Status >= 400) errors.Add($"HTTP {response.Status}: {response.Url}"); };
        foreach (var route in new[] { "/", "/editor", "/history", "/settings", "/settings/connections", "/settings/ai", "/snippets", "/styles", "/diagnostics", "/spike" })
        {
            var response = await page.GotoAsync(origin + route, new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            var headers = response.Headers;
            Assert.Equal("engine", headers["x-akml-host"]);
            Assert.Equal("no-cache", headers["cache-control"]);
            Assert.Equal("nosniff", headers["x-content-type-options"]);
            Assert.Contains("script-src 'self' 'wasm-unsafe-eval'", headers["content-security-policy"]);
            try
            {
                await Assertions.Expect(page.GetByTestId("status-pill")).ToContainTextAsync("Live", new() { Timeout = 60_000 });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"App did not connect on {route}:\n" + string.Join("\n", errors), ex);
            }
            output.WriteLine($"{route}: 200, contract headers, Live");
        }

        // Selecting a corpus item fetches its .sql over the engine's static responder.
        await page.Locator(".spike-input-row select").SelectOptionAsync("00-simple");
        var sql = File.ReadAllText(Path.Combine(bundle.Root, "spike-corpus", "00-simple.sql"));
        await Assertions.Expect(page.Locator(".spike-input")).ToHaveValueAsync(sql.Replace("\r\n", "\n"));
        using var http = new HttpClient();
        using var sqlResponse = await http.GetAsync(origin + "/spike-corpus/00-simple.sql");
        Assert.Equal("text/plain", sqlResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(sql, await sqlResponse.Content.ReadAsStringAsync());
        await page.Locator("#spike-parse-format").ClickAsync();
        await Assertions.Expect(page.Locator(".spike-output")).ToContainTextAsync("SELECT", new() { Timeout = 120_000 });
        Assert.DoesNotContain("<html", await page.Locator(".spike-output").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        await page.Locator("#spike-run-corpus").ClickAsync();
        await Assertions.Expect(page.Locator("#spike-corpus-table tbody tr")).ToHaveCountAsync(7, new() { Timeout = 120_000 });
        await Assertions.Expect(page.Locator("#spike-corpus-summary")).ToContainTextAsync("No silent failure");
        output.WriteLine(".sql text/plain and byte-exact; SQL formatted; all seven corpus rows completed.");
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public async Task Raw_hostile_paths_never_return_private_state_or_directory_contents()
    {
        using var bundle = new EngineServedBundle();
        await using var engine = await SandboxEngine.StartAsync(webRoot: bundle.Root);
        const string secret = "AKML-PRIVATE-STATE-SENTINEL-045";
        foreach (var name in new[] { "pairing-pin.txt", "tokens.json", "config.json", "INSTALL-SUMMARY.txt" })
            File.WriteAllText(Path.Combine(engine.Root, name), secret);
        var index = File.ReadAllText(Path.Combine(bundle.Root, "index.html"));
        foreach (var path in new[]
        {
            "/..%2F..%2FProgramData/AKML%20SQL%20Web/pairing-pin.txt", "/%2e%2e/%2e%2e/Windows/win.ini",
            "/%5C..%5Cweb.config", "/C:/Windows/win.ini", "/C%3A/Windows/win.ini", "//localhost/c$/Windows/win.ini",
            "/index.html::$DATA", "/index.html.", "/index.html%20", "/WEB~1.CON", "/web.config", "/CON", "/con.txt",
            "/_framework/", "/_framework/dotnet.js/", "/certs/bridge.cer", "/pairing-pin.txt", "/tokens.json", "/config.json",
            "/INSTALL-SUMMARY.txt", "/index.html.br", "/%252e%252e/%252e%252e/Windows/win.ini", "/a//b",
        })
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, engine.Port);
            tcp.ReceiveTimeout = 15_000;
            await using var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{engine.Port}\r\nConnection: close\r\n\r\n"));
            using var reader = new StreamReader(stream);
            var response = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var split = response.Split("\r\n\r\n", 2);
            var body = split.Length == 2 ? split[1] : "";
            output.WriteLine($"{path}: {split[0].Split('\r')[0]}");
            Assert.DoesNotContain(secret, response);
            Assert.DoesNotContain("[extensions]", response, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Index of /", response, StringComparison.OrdinalIgnoreCase);
            if (split[0].Contains("X-AKML-Host: engine", StringComparison.OrdinalIgnoreCase))
                Assert.True(body == index || body == "Bad request." || body == "Not found.", $"Unexpected body for {path}: {body[..Math.Min(100, body.Length)]}");
        }
    }
}
