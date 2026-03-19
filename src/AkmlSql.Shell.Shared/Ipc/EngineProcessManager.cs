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
        public bool IsRunning => _engineProcess != null && !_engineProcess.HasExited;

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
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch engine");
            }
        }

        private void OnEngineExited(object sender, EventArgs e)
        {
            if (_disposed) return;

            Log.Warning("Engine process exited. Restart count: {Count}", _restartCount);

            if (_restartCount >= 5)
            {
                Log.Error("Engine crashed too many times. Giving up.");
                return;
            }

            _restartCount++;
            _client?.Dispose();

            // Use Task.Run to avoid async void — all exceptions are caught inside
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    await LaunchAsync();
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
                catch { }
            }

            _engineProcess?.Kill();
        }

        private static string FindEnginePath()
        {
            var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (extensionDir == null) return null;

            // Check Engine subfolder first
            var enginePath = Path.Combine(extensionDir, "Engine", "AkmlSql.Engine.exe");
            if (File.Exists(enginePath)) return enginePath;

            // Check same directory
            enginePath = Path.Combine(extensionDir, "AkmlSql.Engine.exe");
            if (File.Exists(enginePath)) return enginePath;

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client?.Dispose();

            if (_engineProcess != null)
            {
                _engineProcess.Exited -= OnEngineExited;
                if (!_engineProcess.HasExited)
                {
                    try { _engineProcess.Kill(); } catch { }
                }
                _engineProcess.Dispose();
            }
        }
    }
}
