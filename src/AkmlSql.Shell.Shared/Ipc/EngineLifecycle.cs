using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AkmlSql.Shell.Shared.Ipc
{
    /// <summary>
    /// Static helper for managing the engine process lifecycle from shell packages.
    /// All shell packages (SSMS 20/21/22, VS 2019/2022/2026) share this via the .projitems import.
    /// </summary>
    public static class EngineLifecycle
    {
        private static EngineProcessManager _manager;
        private static volatile bool _launching;
        private static readonly object SLock = new();

        /// <summary>
        /// Gets the current EngineProcessManager instance, or null if not yet launched.
        /// </summary>
        public static EngineProcessManager Manager => _manager;

        /// <summary>
        /// Launches the engine process in the background. Safe to call multiple times;
        /// only the first call creates the manager and starts the engine.
        /// </summary>
        public static async Task LaunchAsync(CancellationToken ct = default)
        {
            EngineProcessManager manager;
            lock (SLock)
            {
                if (_manager != null || _launching)
                {
                    return;
                }

                _launching = true;
                _manager = new EngineProcessManager();
                manager = _manager;
            }

            try
            {
                Log.Information("EngineLifecycle: launching engine process...");
                await manager.LaunchAsync(ct);
                Log.Information("EngineLifecycle: engine process launched successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EngineLifecycle: failed to launch engine process.");
            }
            finally
            {
                _launching = false;
            }
        }

        /// <summary>
        /// Shuts down the engine process and disposes resources.
        /// </summary>
        public static async Task ShutdownAsync()
        {
            EngineProcessManager manager;
            lock (SLock)
            {
                manager = _manager;
                _manager = null;
            }

            if (manager == null)
            {
                return;
            }

            try
            {
                Log.Information("EngineLifecycle: shutting down engine process...");
                await manager.ShutdownAsync();
                manager.Dispose();
                Log.Information("EngineLifecycle: engine process shut down.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EngineLifecycle: error during engine shutdown.");
            }
        }
    }
}
