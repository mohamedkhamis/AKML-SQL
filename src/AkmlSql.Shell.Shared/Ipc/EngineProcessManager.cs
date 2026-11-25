using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AkmlSql.Shell.Shared.Ipc
{
    public class EngineProcessManager : IDisposable
    {
        private Process _engineProcess;
        private PipeRpcClient _client;
        private readonly string _pipeName;
        private bool _disposed;
        private int _restartCount;

        public PipeRpcClient Client => _client;

        /// <summary>Raised (on a background thread) once a crashed or killed engine runs again.</summary>
        public event EventHandler Restarted;
        public bool IsRunning => _engineProcess is { HasExited: false };

        public EngineProcessManager()
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
            var pid = Process.GetCurrentProcess().Id;
            _pipeName = $"akmlsql-engine-{sid}-{pid}";
        }

        public async Task LaunchAsync(CancellationToken ct = default)
        {
            var enginePath = FindEnginePath();
            if (enginePath == null)
            {
                Log.Warning("Engine binary not found. IntelliSense will be unavailable.");
                return;
            }

            try
            {
                var pid = Process.GetCurrentProcess().Id;
                _engineProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = enginePath,
                        Arguments = $"--pipe {_pipeName} --parent-pid {pid}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };

                _engineProcess.Exited += OnEngineExited;
                _engineProcess.Start();
                Log.Information("Engine launched: PID={Pid}, Pipe={Pipe}", _engineProcess.Id, _pipeName);

                _client = new PipeRpcClient(_pipeName);
                await _client.ConnectAsync(ct: ct);
                // Reset crash counter after a successful connection
                Interlocked.Exchange(ref _restartCount, 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch engine");
                throw;
            }
        }

        private void OnEngineExited(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var restartCount = Interlocked.Increment(ref _restartCount);
            Log.Warning("Engine process exited. Restart attempt {Count}", restartCount);

            if (restartCount > 5)
            {
                Log.Error("Engine crashed too many times ({Count}). Giving up.", restartCount);
                return;
            }
            _client?.Dispose();

            // Use Task.Run to avoid async void — all exceptions are caught inside
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    await LaunchAsync();
                    // The new engine knows no editor yet: the shell sends each open one again.
                    Restarted?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restart engine");
                }
            });
        }

        public async Task ShutdownAsync()
        {
            if (_client?.IsConnected == true)
            {
                try
                {
                    await _client.SendNotificationAsync(Core.Ipc.MessageTypes.Shutdown, new Core.Ipc.Messages.EngineStatusInfo());
                }
                catch { /* Intentional: best-effort cleanup during shutdown/dispose */ }
            }

            _engineProcess?.Kill();
        }

        private static string FindEnginePath()
        {
            var shell = Assembly.GetExecutingAssembly().Location;
            var extensionDir = Path.GetDirectoryName(shell);
            if (extensionDir == null)
            {
                return null;
            }

            // The places an engine can be, in the order they were always tried: the extension's Engine subfolder and the
            // extension folder (developer deployments), then the installed {app}\Engine (setup deploys it there).
            var places = new System.Collections.Generic.List<string>
            {
                Path.Combine(extensionDir, "Engine", "AkmlSql.Engine.exe"),
                Path.Combine(extensionDir, "AkmlSql.Engine.exe"),
            };
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            {
                var root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(root)) places.Add(Path.Combine(root, "AKML SQL", "Engine", "AkmlSql.Engine.exe"));
            }

            var found = new System.Collections.Generic.List<(string Path, string Version)>();
            foreach (var place in places)
                if (File.Exists(place)) found.Add((place, FileVersionOf(place)));

            var shellVersion = FileVersionOf(shell);
            var chosen = ChooseEngine(found, shellVersion);
            if (chosen is { } engine)
            {
                if (string.Equals(engine.Version, shellVersion, StringComparison.Ordinal))
                    Log.Information("Engine: {Path} ({Version})", engine.Path, engine.Version);
                else
                    Log.Warning("Engine: {Path} is version {EngineVersion} and this extension {ShellVersion}; no engine of the extension's version was found",
                        engine.Path, engine.Version ?? "unknown", shellVersion ?? "unknown");
            }
            return chosen?.Path;
        }

        /// <summary>
        /// Spec 047 installed-product run (F8): the engine whose version is the extension's own, wherever it is; else the first
        /// one found, in the order above. An engine left by hand in the extension's Engine folder (a private update of
        /// 2026-10-10) used to win over every later install's engine, so SSMS ran an engine weeks older than its extension.
        /// </summary>
        internal static (string Path, string Version)? ChooseEngine(
            System.Collections.Generic.IReadOnlyList<(string Path, string Version)> found, string shellVersion)
        {
            if (found == null || found.Count == 0) return null;
            if (!string.IsNullOrEmpty(shellVersion))
            {
                foreach (var candidate in found)
                    if (string.Equals(candidate.Version, shellVersion, StringComparison.Ordinal)) return candidate;
            }
            return found[0];
        }

        private static string FileVersionOf(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).FileVersion; }
            catch (Exception) { return null; }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client?.Dispose();

            if (_engineProcess != null)
            {
                _engineProcess.Exited -= OnEngineExited;
                if (!_engineProcess.HasExited)
                {
                    try { _engineProcess.Kill(); } catch { /* Intentional: best-effort cleanup during shutdown/dispose */ }
                }
                _engineProcess.Dispose();
            }
        }
    }
}
