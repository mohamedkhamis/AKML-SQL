#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Update;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The guided update's download window shows how far the download has got — the size, the
    /// speed and the time left from the updater's progress snapshots — where it used to show only
    /// an endless bar, which on a slow connection looked like nothing was happening.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public sealed class UpdateDownloadProgressWindowTests : IDisposable
    {
        private const string Version = "1.26.1007.0243";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "akml-dlprogress-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static UpdateDownloadProgress Snapshot(string phase, long received, long? total) => new UpdateDownloadProgress
        {
            Version = Version,
            Phase = phase,
            BytesReceived = received,
            TotalBytes = total,
        };

        [Fact]
        public void The_status_line_names_each_phase()
        {
            Assert.Equal("Starting the download…", UpdateDownloadProgressWindow.Describe(null, null));
            Assert.Equal("Connecting to the download server…",
                UpdateDownloadProgressWindow.Describe(Snapshot(UpdateDownloadPhases.Connecting, 0, null), null));
            Assert.Equal("Checking the installer…",
                UpdateDownloadProgressWindow.Describe(Snapshot(UpdateDownloadPhases.Verifying, 59_000_000, 59_000_000), null));
        }

        [Fact]
        public void Downloading_shows_size_speed_and_time_left()
        {
            var halfway = Snapshot(UpdateDownloadPhases.Downloading, 28 * 1048576L, 56 * 1048576L);

            Assert.Equal("28.0 MB of 56.0 MB · 2.0 MB/s · about 14 s left",
                UpdateDownloadProgressWindow.Describe(halfway, 2 * 1048576.0).Replace(',', '.'));
            Assert.Equal("28.0 MB of 56.0 MB · 64 KB/s · about 8 min left",
                UpdateDownloadProgressWindow.Describe(halfway, 64 * 1024.0).Replace(',', '.'));
            Assert.Equal(0.5, UpdateDownloadProgressWindow.Fraction(halfway));

            // No size from the server: the bytes so far, no bar position, no time left.
            var unsized = Snapshot(UpdateDownloadPhases.Downloading, 5 * 1048576L, null);
            Assert.Equal("5.0 MB downloaded · 1.0 MB/s",
                UpdateDownloadProgressWindow.Describe(unsized, 1048576.0).Replace(',', '.'));
            Assert.Null(UpdateDownloadProgressWindow.Fraction(unsized));

            // Bytes arrived but none lately: say so rather than claim a speed of zero.
            Assert.Equal("28.0 MB of 56.0 MB · waiting for data…",
                UpdateDownloadProgressWindow.Describe(halfway, 0).Replace(',', '.'));
        }

        [Fact]
        public void The_speed_is_measured_over_the_last_few_seconds()
        {
            var meter = new UpdateDownloadProgressWindow.SpeedMeter();
            var t0 = new DateTime(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc);

            Assert.Null(meter.Add(t0, 0));                                    // one sample: no rate yet
            Assert.Equal(1048576, meter.Add(t0.AddSeconds(1), 1048576));      // 1 MB in 1 s
            Assert.Equal(1048576, meter.Add(t0.AddSeconds(2), 2 * 1048576));

            // Old samples fall out of the window: a stall shows up within seconds.
            var stalled = meter.Add(t0.AddSeconds(10), 2 * 1048576);
            Assert.NotNull(stalled);
            Assert.True(stalled < 300_000, $"rate after a stall {stalled}");
        }

        [StaFact]
        public void The_window_follows_the_updaters_snapshots()
        {
            var progressPath = Path.Combine(_root, "update-download-progress.json");
            var window = UpdateDownloadProgressWindow.CreateFor(Version, Process.GetCurrentProcess(), progressPath);
            try
            {
                var bar = (ProgressBar)typeof(UpdateDownloadProgressWindow).GetField("_bar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var status = (TextBlock)typeof(UpdateDownloadProgressWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

                window.Refresh();
                Assert.True(bar.IsIndeterminate);
                Assert.Equal("Starting the download…", status.Text);

                UpdateDownloadProgressStore.Save(Snapshot(UpdateDownloadPhases.Downloading, 14 * 1048576L, 56 * 1048576L), progressPath);
                window.Refresh();
                Assert.False(bar.IsIndeterminate);
                Assert.Equal(25, bar.Value, 3);
                Assert.StartsWith("14.0 MB of 56.0 MB", status.Text.Replace(',', '.'));

                // Another version's leftover snapshot is ignored.
                var other = Snapshot(UpdateDownloadPhases.Downloading, 1, 2);
                other.Version = "1.26.0101.0000";
                UpdateDownloadProgressStore.Save(other, progressPath);
                window.Refresh();
                Assert.True(bar.IsIndeterminate);
                Assert.Equal("Starting the download…", status.Text);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
