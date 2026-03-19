using System;
using System.Diagnostics;
using System.IO;
using Constants = AkmlSql.Core.Constants;
using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Shell.Shared.Update
{
    internal static class UpdateLauncher
    {
        public static void LaunchIfDue()
        {
            try
            {
                var settings = ConfigManager.Load();
                if (!settings.AutoUpdateEnabled)
                {
                    Log.Debug("Auto-update is disabled, skipping update check");
                    return;
                }

                if (settings.LastUpdateCheck.HasValue)
                {
                    var elapsed = DateTimeOffset.UtcNow - settings.LastUpdateCheck.Value;
                    if (elapsed.TotalHours < Constants.UpdateCheckIntervalHours)
                    {
                        Log.Debug("Last update check was {Hours:F1}h ago, skipping", elapsed.TotalHours);
                        return;
                    }
                }

                LaunchUpdater();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to launch update checker");
            }
        }

        /// <summary>
        /// Launches the updater process fire-and-forget (used for background auto-check).
        /// </summary>
        public static void LaunchUpdater()
        {
            var updaterPath = FindUpdaterPath();
            if (updaterPath == null) return;

            Log.Information("Launching update checker: {Path}", updaterPath);
            using (Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = "--check",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            })) { }
        }

        /// <summary>
        /// Launches the updater and waits for it to exit. Returns the process, or null if not found.
        /// </summary>
        public static Process LaunchUpdaterAndWait(TimeSpan timeout)
        {
            var updaterPath = FindUpdaterPath();
            if (updaterPath == null) return null;

            Log.Information("Launching update checker (synchronous): {Path}", updaterPath);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = "--check",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            process?.WaitForExit((int)timeout.TotalMilliseconds);
            return process;
        }

        private static string FindUpdaterPath()
        {
            // Try both ProgramFiles locations to handle x86 and x64 host processes.
            // On x86 processes, Environment.SpecialFolder.ProgramFiles resolves to
            // "Program Files (x86)", but the installer puts files in "Program Files".
            var candidates = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "AKML SQL", "AkmlSql.Updater.exe"),
                Path.Combine(
                    Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
                    "AKML SQL", "AkmlSql.Updater.exe")
            };

            foreach (var path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }

            Log.Debug("Updater not found in any known location");
            return null;
        }
    }
}
