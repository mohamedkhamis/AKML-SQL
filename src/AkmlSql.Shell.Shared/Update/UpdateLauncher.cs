#nullable enable
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
        // Test hooks (Shell.Shared.Tests). Production defaults are restored by ResetTestHooks.
        internal static Func<string?> UpdaterPathProvider { get; set; } = FindUpdaterPath;
        internal static Func<ProcessStartInfo, Process?> ProcessStarter { get; set; } = StartProcessDefault;

        internal static void ResetTestHooks()
        {
            UpdaterPathProvider = FindUpdaterPath;
            ProcessStarter = StartProcessDefault;
        }

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
        /// Launches the updater process fire-and-forget (used for background auto-check and —
        /// spec 036 US5 / FR-042 — the manual "Check for updates" path, which bypasses the
        /// 24-hour throttle in <see cref="LaunchIfDue"/>).
        /// </summary>
        public static void LaunchUpdater()
        {
            var updaterPath = UpdaterPathProvider();
            if (updaterPath == null)
            {
                return;
            }

            Log.Information("Launching update checker: {Path}", updaterPath);
            using (ProcessStarter(new ProcessStartInfo
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
        public static Process? LaunchUpdaterAndWait(TimeSpan timeout)
        {
            var updaterPath = UpdaterPathProvider();
            if (updaterPath == null)
            {
                return null;
            }

            Log.Information("Launching update checker (synchronous): {Path}", updaterPath);
            var process = ProcessStarter(new ProcessStartInfo
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

        /// <summary>
        /// Launches the updater in <c>--download</c> mode (spec 036 US5 / FR-039): downloads and
        /// verifies the offered installer out of process. Returns the live process with
        /// <see cref="Process.EnableRaisingEvents"/> set so the progress window can watch
        /// <c>Exited</c> — and kill it on cancel (the shell then deletes the <c>.partial</c>
        /// itself, because a killed process never runs its finally blocks).
        /// </summary>
        public static Process? LaunchUpdaterDownload()
        {
            var updaterPath = UpdaterPathProvider();
            if (updaterPath == null)
            {
                return null;
            }

            Log.Information("Launching update downloader: {Path}", updaterPath);
            var process = ProcessStarter(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = "--download",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process != null)
            {
                process.EnableRaisingEvents = true;
            }
            return process;
        }

        private static Process? StartProcessDefault(ProcessStartInfo info) => Process.Start(info);

        private static string? FindUpdaterPath()
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
                {
                    return path;
                }
            }

            Log.Debug("Updater not found in any known location");
            return null;
        }
    }
}
