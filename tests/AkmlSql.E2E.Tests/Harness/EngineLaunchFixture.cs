using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AkmlSql.E2E.Tests.Harness;

/// <summary>
/// Spec 025 (M3 bridge closure) US5 — Engine harness fixture per
/// <c>specs/025-m3-bridge-closure/contracts/bridge-e2e-harness-contract.md</c>.
///
/// Builds <c>src/AkmlSql.Engine</c> (Release), picks a free TCP port on loopback,
/// writes a temporary <c>config.json</c> with the <c>bridge</c> section enabled in
/// localhost mode, redirects <c>%AppData%</c> to a per-fixture temp directory, and
/// launches the engine. Tests share one fixture instance per class.
///
/// Localhost-only by design — LAN-mode tests are gated on admin rights + a netsh
/// cert binding (see <c>WebSocketTransportLanTests</c> in the engine test project).
/// </summary>
public sealed class EngineLaunchFixture : IAsyncLifetime
{
    public int Port { get; private set; }
    public Process? EngineProcess { get; private set; }
    public DateTimeOffset? LaunchedAt { get; private set; }
    public string AppDataRoot { get; private set; } = string.Empty;
    // Engine's BearerTokenStore writes to %AppData%\AKML SQL\tokens.json by default
    // (when the bridge section's tokenStorePath is empty) or wherever the bridge
    // option points. The fixture's config writes the explicit path so we know where.
    public string TokensJsonPath => Path.Combine(AppDataRoot, "AKML SQL", "tokens.json");
    public string ConfigJsonPath => Path.Combine(AppDataRoot, "AKML SQL", "config.json");

    private string _engineExePath = string.Empty;
    private string _pipeName = string.Empty;

    public async Task InitializeAsync()
    {
        await BuildEngineAsync().ConfigureAwait(false);
        Port = PickFreePort();
        AppDataRoot = CreateTempAppData();
        WriteConfig(AppDataRoot, Port);
        await LaunchEngineAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await StopEngineAsync().ConfigureAwait(false);
        try { if (Directory.Exists(AppDataRoot)) Directory.Delete(AppDataRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    /// <summary>Kill the running engine + relaunch it against the same port + AppData. Used
    /// by tests that simulate engine restart.</summary>
    public async Task RelaunchAsync()
    {
        await StopEngineAsync().ConfigureAwait(false);
        // Re-pick port — the OS may still hold the previous port in TIME_WAIT, and a
        // fresh port avoids ECONNREFUSED races on rapid restart.
        Port = PickFreePort();
        WriteConfig(AppDataRoot, Port);
        await LaunchEngineAsync().ConfigureAwait(false);
    }

    /// <summary>Test helper: stop engine, wipe tokens.json, restart. Used by the
    /// revocation scenarios in <c>BridgeHandshakeTests</c>.</summary>
    public async Task ClearTokensAndRelaunchAsync()
    {
        await StopEngineAsync().ConfigureAwait(false);
        if (File.Exists(TokensJsonPath))
        {
            try { File.Delete(TokensJsonPath); }
            catch (Exception ex) { throw new InvalidOperationException("Failed to delete tokens.json", ex); }
        }
        Port = PickFreePort();
        WriteConfig(AppDataRoot, Port);
        await LaunchEngineAsync().ConfigureAwait(false);
    }

    private async Task BuildEngineAsync()
    {
        var repoRoot = LocateRepoRoot();
        var csproj = Path.Combine(repoRoot, "src", "AkmlSql.Engine", "AkmlSql.Engine.csproj");
        if (!File.Exists(csproj))
        {
            throw new InvalidOperationException($"Engine csproj not found at: {csproj}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", csproj, "-c", "Release", "-v:quiet" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("dotnet build failed to start.");
        var sb = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync().ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Engine build failed (exit={proc.ExitCode}). Output:\n{sb}");
        }

        _engineExePath = Path.Combine(
            repoRoot, "src", "AkmlSql.Engine", "bin", "Release", "net10.0", "win-x64", "AkmlSql.Engine.exe");
        if (!File.Exists(_engineExePath))
        {
            throw new InvalidOperationException(
                $"Built engine exe missing at: {_engineExePath}. Build output:\n{sb}");
        }
    }

    private static int PickFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string CreateTempAppData()
    {
        var root = Path.Combine(Path.GetTempPath(), "akml-engine-fixture-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        // The engine's Constants.AppDataPath returns "<root>/AKML SQL" when
        // AKML_APP_DATA_ROOT=<root>, so we create both folders one level under.
        Directory.CreateDirectory(Path.Combine(root, "AKML SQL"));
        Directory.CreateDirectory(Path.Combine(root, "AKML SQL", "logs"));
        return root;
    }

    private static void WriteConfig(string appDataRoot, int port)
    {
        // BridgeOptions defaults: Localhost only, no TLS, token store inside our temp tree.
        var configPath = Path.Combine(appDataRoot, "AKML SQL", "config.json");
        var tokenStorePath = Path.Combine(appDataRoot, "AKML SQL", "tokens.json").Replace("\\", "\\\\");
        var json = $@"{{
  ""bridge"": {{
    ""enabled"": true,
    ""bindAddress"": ""127.0.0.1"",
    ""port"": {port},
    ""tlsCertPath"": null,
    ""tlsCertPasswordRef"": null,
    ""tokenStorePath"": ""{tokenStorePath}"",
    ""tokenTtlDays"": 90
  }}
}}";
        File.WriteAllText(configPath, json);
    }

    private async Task LaunchEngineAsync()
    {
        _pipeName = "akml-bridge-e2e-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var psi = new ProcessStartInfo
        {
            FileName = _engineExePath,
            ArgumentList =
            {
                "--pipe", _pipeName,
                "--parent-pid", Environment.ProcessId.ToString(),
            },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Redirect AppData so the engine's ConfigManager + BearerTokenStore land inside
        // the fixture's temp tree. AKML_APP_DATA_ROOT is the test-affordance env var
        // honoured by Constants.AppDataPath + Constants.LocalAppDataPath (spec 025 US5);
        // the Windows %APPDATA% env var is not respected by Environment.GetFolderPath on
        // .NET, so we need our own override hook.
        psi.Environment["AKML_APP_DATA_ROOT"] = AppDataRoot;

        EngineProcess = Process.Start(psi) ?? throw new InvalidOperationException("Engine exe failed to start.");
        // Drain stdout/stderr so the pipes don't fill and stall the child.
        _ = Task.Run(async () => { try { await EngineProcess.StandardOutput.ReadToEndAsync(); } catch { } });
        _ = Task.Run(async () => { try { await EngineProcess.StandardError.ReadToEndAsync(); } catch { } });

        // Probe the WebSocket port for readiness — TcpClient.ConnectAsync is enough.
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (EngineProcess.HasExited)
            {
                throw new InvalidOperationException(
                    $"Engine exited early during launch with code {EngineProcess.ExitCode}.");
            }
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await client.ConnectAsync(IPAddress.Loopback, Port, cts.Token).ConfigureAwait(false);
                LaunchedAt = DateTimeOffset.UtcNow;
                return;
            }
            catch { /* not yet listening — keep polling */ }
            await Task.Delay(200).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Engine did not start listening on 127.0.0.1:{Port} within 30 s.");
    }

    private async Task StopEngineAsync()
    {
        if (EngineProcess == null) return;
        try
        {
            if (!EngineProcess.HasExited)
            {
                EngineProcess.Kill(entireProcessTree: true);
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await EngineProcess.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
        finally
        {
            try { EngineProcess.Dispose(); } catch { }
            EngineProcess = null;
        }
    }

    private static string LocateRepoRoot()
    {
        // Walk up from the test assembly until we find AKML-SQL.slnx (the repo marker).
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "AKML-SQL.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }
        throw new InvalidOperationException("Could not locate repo root (AKML-SQL.slnx).");
    }
}
