using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 041 (T005, research R70) — the Debug engine of THIS working tree, started with the WebSocket
/// bridge on a free loopback port and an app-data root of its own (<c>AKML_APP_DATA_ROOT</c>), so a test
/// never touches the installed engine service or the user's styles and history. Loopback pairing needs
/// no PIN. The same recipe <see cref="FormatStylesSharedEngineTests"/> and <c>SqlServerAliasWebTests</c>
/// use, shared for the spec 041 suites.
/// </summary>
public sealed class SandboxEngine : IAsyncDisposable
{
    private SandboxEngine(Process process, string root, int port, string exe, string config)
    {
        Process = process;
        Root = root;
        Port = port;
        _exe = exe;
        _config = config;
    }

    private readonly string _exe;
    private readonly string _config;

    public Process Process { get; private set; }

    /// <summary>The engine's private app-data root (deleted on dispose).</summary>
    public string Root { get; }

    /// <summary>The bridge port to pair with on 127.0.0.1.</summary>
    public int Port { get; }

    /// <param name="postgreSqlEnabled">
    /// Spec 042 (FR-093): false writes <c>"postgreSql": { "enabled": false }</c> into the engine's own config.json first, so the engine
    /// neither advertises <c>dialect.postgresql.v1</c> nor registers a PostgreSQL handler - an engine as it was before the feature.
    /// </param>
    public static async Task<SandboxEngine> StartAsync(bool postgreSqlEnabled = true, string? webRoot = null)
    {
        var exe = Path.Combine(RepoRoot(), "src", "AkmlSql.Engine", "bin", "Debug", "net10.0", "win-x64", "AkmlSql.Engine.exe");
        if (!File.Exists(exe)) throw new Xunit.SkipException($"Build the engine first (Debug): {exe}");

        var root = Directory.CreateTempSubdirectory("akml-e2e-engine-").FullName;
        var appData = Directory.CreateDirectory(Path.Combine(root, "AKML SQL")).FullName;
        File.WriteAllText(Path.Combine(appData, "config.json"), JsonSerializer.Serialize(new
        {
            telemetryEnabled = false,
            postgreSql = new { enabled = postgreSqlEnabled },
        }));
        var port = FreePort();
        var config = Path.Combine(root, "engine-config.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            configVersion = 1,
            bridge = new { enabled = true, bindAddress = "127.0.0.1", port, webRoot = webRoot ?? "", tokenStorePath = Path.Combine(root, "tokens.json"), tokenTtlDays = 1 },
        }));

        var engine = new SandboxEngine(Launch(exe, config, root), root, port, exe, config);
        try
        {
            await engine.WaitListeningAsync();
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Spec 043 (review of US1): stop the engine and start it again on the same port, app-data root and token store, as a service
    /// restart does. The engine forgets every session; a paired browser reconnects without pairing again.
    /// </summary>
    public async Task RestartAsync()
    {
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        await Process.WaitForExitAsync();
        Process.Dispose();
        Process = Launch(_exe, _config, Root);
        await WaitListeningAsync();
    }

    private static Process Launch(string exe, string config, string root)
    {
        var psi = new ProcessStartInfo(exe, $"--web --config \"{config}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.Environment["AKML_APP_DATA_ROOT"] = root;
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Engine did not start.");
        // Drain the pipes so a chatty engine can never block on a full stdout buffer.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private async Task WaitListeningAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (Process.HasExited) throw new InvalidOperationException($"Engine exited with code {Process.ExitCode}.");
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, Port);
                return;
            }
            catch (SocketException) { await Task.Delay(300); }
        }
        throw new TimeoutException("Engine bridge did not start listening.");
    }

    /// <summary>True when a default SQL Server instance answers on localhost:1433.</summary>
    public static async Task<bool> LocalSqlServerAnswersAsync()
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(IPAddress.Loopback, 1433);
            var done = await Task.WhenAny(connect, Task.Delay(2_000));
            return done == connect && client.Connected;
        }
        catch (SocketException) { return false; }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (AKML-SQL.slnx) not found.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        try { await Process.WaitForExitAsync(); } catch { /* ignore */ }
        Process.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch { /* files still locked: leave the temp folder */ }
    }
}
