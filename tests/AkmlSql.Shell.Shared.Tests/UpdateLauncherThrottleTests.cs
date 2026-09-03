#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Update;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 US5 / FR-042 at the launcher level: <see cref="UpdateLauncher.LaunchIfDue"/>
    /// enforces the 24-hour throttle, while the manual path (<see cref="UpdateLauncher.LaunchUpdater"/>,
    /// and <c>--download</c> for the guided flow) always starts the updater. Process starts are
    /// observed through the launcher's test hooks — no real updater is spawned.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public class UpdateLauncherThrottleTests : AppDataIsolatedTest
    {
        private readonly List<ProcessStartInfo> _starts = new();

        public UpdateLauncherThrottleTests() : base("akml-updatelauncher-")
        {
            UpdateLauncher.UpdaterPathProvider = () => @"C:\fake\AkmlSql.Updater.exe";
            UpdateLauncher.ProcessStarter = info => { _starts.Add(info); return null; };
        }

        public override void Dispose()
        {
            UpdateLauncher.ResetTestHooks();
            base.Dispose();
        }

        [Fact]
        public void LaunchIfDue_with_recent_LastUpdateCheck_does_not_start_a_process()
        {
            SaveLastCheck(DateTimeOffset.UtcNow.AddMinutes(-5));

            UpdateLauncher.LaunchIfDue();

            Assert.Empty(_starts);
        }

        [Fact]
        public void LaunchIfDue_with_a_stale_LastUpdateCheck_launches_a_check()
        {
            SaveLastCheck(DateTimeOffset.UtcNow.AddHours(-(AkmlSql.Core.Constants.UpdateCheckIntervalHours + 1)));

            UpdateLauncher.LaunchIfDue();

            var start = Assert.Single(_starts);
            Assert.Equal("--check", start.Arguments);
        }

        [Fact]
        public void LaunchUpdater_bypasses_the_throttle_with_a_recent_LastUpdateCheck()
        {
            // FR-042: the manual command's launch path never consults LastUpdateCheck.
            SaveLastCheck(DateTimeOffset.UtcNow.AddMinutes(-5));

            UpdateLauncher.LaunchUpdater();

            var start = Assert.Single(_starts);
            Assert.Equal("--check", start.Arguments);
            Assert.False(start.UseShellExecute);
        }

        [Fact]
        public void LaunchUpdaterDownload_passes_download_to_the_updater()
        {
            UpdateLauncher.LaunchUpdaterDownload();

            var start = Assert.Single(_starts);
            Assert.Equal("--download", start.Arguments);
        }

        private static void SaveLastCheck(DateTimeOffset when)
        {
            var settings = ConfigManager.Load();
            settings.AutoUpdateEnabled = true;
            settings.LastUpdateCheck = when;
            ConfigManager.Save(settings);
        }
    }
}
