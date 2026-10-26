using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using AkmlSql.Engine.Transports;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

[CollectionDefinition("Static web responder", DisableParallelization = true)]
public sealed class StaticWebResponderCollection { }

[Collection("Static web responder")]
public sealed class StaticWebResponderTests
{
    [Theory]
    [InlineData("/", "text/html", "INDEX")]
    [InlineData("/editor?autorun=1", "text/html", "INDEX")]
    [InlineData("/settings/connections", "text/html", "INDEX")]
    [InlineData("/_framework/dotnet.js", "text/javascript", "SCRIPT")]
    [InlineData("/file.css", "text/css", "CSS")]
    [InlineData("/file.wasm", "application/wasm", "WASM")]
    [InlineData("/file.json", "application/json", "JSON")]
    [InlineData("/file.map", "application/json", "MAP")]
    [InlineData("/file.svg", "image/svg+xml", "SVG")]
    [InlineData("/file.txt", "text/plain", "TXT")]
    [InlineData("/file.sql", "text/plain", "SQL")]
    [InlineData("/file.pdb", "application/octet-stream", "PDB")]
    public async Task Files_and_routes_have_the_contract_headers_and_bytes(string path, string type, string body)
    {
        await using var host = await StaticWebTestHost.StartAsync();
        using var response = await host.Http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Headers(response);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Content.Headers.LastModified);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        Assert.False(response.Headers.Contains("Accept-Ranges"));
    }

    [Fact]
    public async Task Head_and_validators_use_whole_second_timestamps_and_ETag_precedence()
    {
        await using var host = await StaticWebTestHost.StartAsync();
        using var get = await host.Http.GetAsync("/");
        using var head = await host.Http.SendAsync(new(HttpMethod.Head, "/"));
        Assert.Equal(get.Headers.ETag, head.Headers.ETag);
        Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        var modified = get.Content.Headers.LastModified!.Value;
        Assert.Equal(0, modified.Ticks % TimeSpan.TicksPerSecond);

        using var etag = new HttpRequestMessage(HttpMethod.Get, "/editor?autorun=1");
        etag.Headers.IfNoneMatch.Add(get.Headers.ETag!);
        using var cached = await host.Http.SendAsync(etag);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        Headers(cached);
        Assert.Empty(await cached.Content.ReadAsByteArrayAsync());

        using var date = new HttpRequestMessage(HttpMethod.Get, "/");
        date.Headers.IfModifiedSince = modified;
        using var byDate = await host.Http.SendAsync(date);
        Assert.Equal(HttpStatusCode.NotModified, byDate.StatusCode);

        using var both = new HttpRequestMessage(HttpMethod.Get, "/");
        both.Headers.IfModifiedSince = modified.AddDays(1);
        both.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"different\""));
        using var notCached = await host.Http.SendAsync(both);
        Assert.Equal(HttpStatusCode.OK, notCached.StatusCode);
    }

    [Fact]
    public async Task Missing_files_methods_hosts_and_ranges_have_explicit_outcomes()
    {
        await using var host = await StaticWebTestHost.StartAsync();
        using var missing = await host.Http.GetAsync("/missing.js");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Not found.", await missing.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", missing.Content.Headers.ContentType?.MediaType);
        Headers(missing);
        using var method = await host.Http.PostAsync("/", new StringContent("x"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, method.StatusCode);
        Assert.Equal(new[] { "GET", "HEAD" }, method.Content.Headers.Allow);
        Headers(method);
        using var range = new HttpRequestMessage(HttpMethod.Get, "/");
        range.Headers.Range = new RangeHeaderValue(0, 1);
        using var full = await host.Http.SendAsync(range);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal("INDEX", await full.Content.ReadAsStringAsync());
        foreach (var allowed in new[] { "localhost", "127.0.0.1" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Host = allowed + ":" + host.Port;
            using var response = await host.Http.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, allowed + ": " + await response.Content.ReadAsStringAsync());
        }
        foreach (var refused in new[] { "evil.example", Environment.MachineName })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Host = refused + ":" + host.Port;
            using var response = await host.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Bad host.", await response.Content.ReadAsStringAsync());
            Headers(response);
        }
    }

    [Theory]
    [InlineData("localhost:80", true)]
    [InlineData("LOCALHOST:59417", true)]
    [InlineData("127.0.0.1:1234", true)]
    [InlineData("[::1]:59417", true)]
    [InlineData("localhost.evil.example:59417", false)]
    [InlineData("evil@localhost:59417", false)]
    [InlineData("localhost:59417/other", false)]
    [InlineData(null, false)]
    public void Host_gate_accepts_only_the_loopback_names_on_any_port(string? value, bool allowed) =>
        Assert.Equal(allowed, WebSocketTransport.IsAllowedLocalHost(value));

    [Theory]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("no-index")]
    [InlineData("volume")]
    [InlineData("junction")]
    [InlineData("config-equal")]
    [InlineData("config-parent")]
    [InlineData("config-child")]
    [InlineData("token-equal")]
    [InlineData("token-parent")]
    [InlineData("token-child")]
    public async Task Invalid_roots_fall_back_with_exactly_one_warning(string kind)
    {
        var sink = new WarningSink();
        var previous = Serilog.Log.Logger;
        using var logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Serilog.Log.Logger = logger;
        try
        {
            string? Root(StaticWebTestHost host)
            {
                if (kind == "relative") return "relative";
                if (kind == "missing") return Path.Combine(host.DirectoryPath, "missing");
                if (kind == "no-index") { File.Delete(Path.Combine(host.Root, "index.html")); return host.Root; }
                if (kind == "volume") return Path.GetPathRoot(host.Root);
                if (kind == "junction")
                {
                    var link = Path.Combine(host.DirectoryPath, "link");
                    Junction(link, host.Root);
                    return link;
                }
                return host.Root;
            }
            string? StateFile(StaticWebTestHost host, string prefix)
            {
                if (kind == prefix + "-equal") return Path.Combine(host.Root, "state.json");
                if (kind == prefix + "-parent") return Path.Combine(host.Root, "private", "state.json");
                if (kind == prefix + "-child") return Path.Combine(host.DirectoryPath, "state.json");
                return Path.Combine(host.State, "state.json");
            }
            await using var host = await StaticWebTestHost.StartAsync(Root, h => StateFile(h, "config"), h => StateFile(h, "token"));
            using var response = await host.Http.GetAsync("/");
            Assert.Contains("AKML SQL engine: reachable", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-AKML-Host"));
            Assert.Single(sink.Warnings);
            Assert.Contains("not serving the web edition", sink.Warnings[0].RenderMessage());
        }
        finally { Serilog.Log.Logger = previous; }
    }

    [Fact]
    public async Task Manifest_skips_hidden_system_and_reparse_point_entries()
    {
        await using var host = await StaticWebTestHost.StartAsync(h =>
        {
            File.WriteAllText(Path.Combine(h.Root, "hidden.sql"), "SECRET");
            File.SetAttributes(Path.Combine(h.Root, "hidden.sql"), FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(h.Root, "system.sql"), "SECRET");
            File.SetAttributes(Path.Combine(h.Root, "system.sql"), FileAttributes.System);
            File.WriteAllText(Path.Combine(h.State, "outside.sql"), "SECRET");
            Junction(Path.Combine(h.Root, "link"), h.State);
            return h.Root;
        });
        foreach (var path in new[] { "/hidden.sql", "/system.sql", "/link/outside.sql" })
        {
            using var response = await host.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("SECRET", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Slow_readers_are_bounded_and_failed_writes_release_their_slots()
    {
        await using var host = await StaticWebTestHost.StartAsync(h =>
        {
            using var file = File.Create(Path.Combine(h.Root, "large.wasm"));
            file.SetLength(16 * 1024 * 1024);
            return h.Root;
        });
        var readers = new List<HttpResponseMessage>();
        try
        {
            for (var i = 0; i < 32; i++)
            {
                var response = await host.Http.GetAsync("/large.wasm", HttpCompletionOption.ResponseHeadersRead);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                readers.Add(response);
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var next = await host.Http.GetAsync("/");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
            Assert.True(next.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
            if (next.StatusCode == HttpStatusCode.ServiceUnavailable)
                Assert.Equal(TimeSpan.FromSeconds(5), next.Headers.RetryAfter?.Delta);
        }
        finally { foreach (var reader in readers) reader.Dispose(); }
        for (var i = 0; i < 33; i++)
        {
            using var response = await host.Http.GetAsync("/large.wasm", HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var recovered = await host.Http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    private static void Junction(string path, string target)
    {
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '{path}' -Target '{target}' | Out-Null" })
            info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info)!;
        var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, text);
    }

    private sealed class WarningSink : Serilog.Core.ILogEventSink
    {
        internal List<Serilog.Events.LogEvent> Warnings { get; } = new();
        public void Emit(Serilog.Events.LogEvent value)
        {
            if (value.Level == Serilog.Events.LogEventLevel.Warning) Warnings.Add(value);
        }
    }

    [Theory]
    [InlineData("/..%2F..%2FProgramData/AKML%20SQL%20Web/pairing-pin.txt")]
    [InlineData("/%2e%2e/%2e%2e/Windows/win.ini")]
    [InlineData("/%5C..%5Cweb.config")]
    [InlineData("/C:/Windows/win.ini")]
    [InlineData("/C%3A/Windows/win.ini")]
    [InlineData("//localhost/c$/Windows/win.ini")]
    [InlineData("/index.html::$DATA")]
    [InlineData("/index.html.")]
    [InlineData("/index.html%20")]
    [InlineData("/WEB~1.CON")]
    [InlineData("/web.config")]
    [InlineData("/CON")]
    [InlineData("/con.txt")]
    [InlineData("/_framework/")]
    [InlineData("/_framework/dotnet.js/")]
    [InlineData("/certs/bridge.cer")]
    [InlineData("/pairing-pin.txt")]
    [InlineData("/tokens.json")]
    [InlineData("/config.json")]
    [InlineData("/INSTALL-SUMMARY.txt")]
    [InlineData("/index.html.br")]
    [InlineData("/%252e%252e/%252e%252e/Windows/win.ini")]
    [InlineData("/a//b")]
    public async Task Raw_SC013_requests_never_disclose_files_or_list_directories(string path)
    {
        await using var host = await StaticWebTestHost.StartAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{host.Port}\r\nConnection: close\r\n\r\n"), timeout.Token);
        using var reader = new StreamReader(stream);
        var response = await reader.ReadToEndAsync(timeout.Token);
        Assert.DoesNotContain("SECRET", response, StringComparison.Ordinal);
        Assert.DoesNotContain("SCRIPT", response, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory Listing", response, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"SC-013 {path}: {response.Split('\r')[0]}");
    }

    private static void Headers(HttpResponseMessage response)
    {
        Assert.Equal("engine", Assert.Single(response.Headers.GetValues("X-AKML-Host")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(StaticWebResponder.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.True(response.Headers.CacheControl?.NoCache);
    }
}

internal sealed class StaticWebTestHost : IAsyncDisposable
{
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "akml-static-web-" + Guid.NewGuid().ToString("N"));
    internal string Root => Path.Combine(DirectoryPath, "Web");
    internal string State => Path.Combine(DirectoryPath, "State");
    internal int Port { get; private set; }
    internal HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(20) };
    internal WebSocketTransport Transport { get; private set; } = null!;

    internal static async Task<StaticWebTestHost> StartAsync(Func<StaticWebTestHost, string?>? root = null,
        Func<StaticWebTestHost, string?>? config = null, Func<StaticWebTestHost, string?>? tokens = null)
    {
        var host = new StaticWebTestHost();
        Directory.CreateDirectory(Path.Combine(host.Root, "_framework"));
        Directory.CreateDirectory(host.State);
        File.WriteAllText(Path.Combine(host.Root, "index.html"), "INDEX");
        File.SetLastWriteTimeUtc(Path.Combine(host.Root, "index.html"), new DateTime(2026, 10, 26, 1, 2, 3, 456, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(host.Root, "_framework", "dotnet.js"), "SCRIPT");
        foreach (var extension in new[] { "css", "wasm", "json", "map", "svg", "txt", "sql", "pdb" })
            File.WriteAllText(Path.Combine(host.Root, "file." + extension), extension.ToUpperInvariant());
        foreach (var name in new[] { "web.config", "index.html.br", "pairing-pin.txt", "tokens.json", "config.json", "INSTALL-SUMMARY.txt" })
            File.WriteAllText(Path.Combine(host.Root, name), "SECRET");
        File.WriteAllText(Path.Combine(host.State, "config.json"), "SECRET");
        var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        host.Port = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        host.Transport = new WebSocketTransport(new WebSocketTransportOptions
        {
            Port = host.Port, WebRoot = root == null ? host.Root : root(host),
            ConfigFilePath = config == null ? Path.Combine(host.State, "config.json") : config(host),
            TokenStorePath = (tokens == null ? Path.Combine(host.State, "tokens.json") : tokens(host))!,
        });
        try { await host.Transport.StartAsync(CancellationToken.None); }
        catch { await host.DisposeAsync(); throw; }
        host.Http.BaseAddress = new Uri($"http://127.0.0.1:{host.Port}/");
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        if (Transport != null) await Transport.DisposeAsync();
        try
        {
            // Unlink the owned junctions directly; recursive mount-point removal needs extra Windows rights.
            foreach (var link in new[] { Path.Combine(DirectoryPath, "link"), Path.Combine(Root, "link") })
                if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(link, recursive: false);
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
