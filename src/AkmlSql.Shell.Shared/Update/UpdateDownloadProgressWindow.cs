#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Ui;
using AkmlSql.Shell.Shared.Ui.Theme;
using Constants = AkmlSql.Core.Constants;
using Orientation = System.Windows.Controls.Orientation;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// Modal progress surface for the guided update download (spec 036 US5 / FR-039a): shows
    /// how far the out-of-process download has got and offers a working Cancel. The updater
    /// process does the download and publishes <see cref="UpdateDownloadProgress"/>; this window
    /// polls it twice a second for the bar, the size, the speed and the time left (it used to show
    /// only an endless bar, so a slow connection looked like nothing was happening), and watches
    /// the process's <c>Exited</c> event.
    ///
    /// Cancel kills the updater (a killed process never runs its finally blocks) and then runs
    /// <see cref="UpdateDownloadCleanup"/> so no <c>.partial</c> survives and the offer returns
    /// to the available state. <see cref="Window.DialogResult"/> is <c>true</c> when the updater
    /// exited on its own (the caller then inspects the result file) and <c>false</c> on cancel.
    /// </summary>
    internal sealed class UpdateDownloadProgressWindow : Window
    {
        private static readonly FontFamily SegoeUiFont = new("Segoe UI");

        private readonly Process _process;
        private readonly string _version;
        private readonly SynchronizationContext _ui;
        private readonly string _progressFilePath;
        private readonly SpeedMeter _speed = new SpeedMeter();
        private ProgressBar? _bar;
        private TextBlock? _status;
        private DispatcherTimer? _poll;
        private bool _closed;

        private UpdateDownloadProgressWindow(string version, Process process, string progressFilePath)
        {
            _version = version;
            _process = process;
            _progressFilePath = progressFilePath;
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        /// <summary>Creates and wires the window; the process must already be started with
        /// <see cref="Process.EnableRaisingEvents"/> set (<see cref="UpdateLauncher.LaunchUpdaterDownload"/>).</summary>
        public static UpdateDownloadProgressWindow CreateFor(string version, Process process) =>
            CreateFor(version, process, Constants.UpdateDownloadProgressFilePath);

        /// <summary>Path-injected core (tests point it at a temp progress file).</summary>
        internal static UpdateDownloadProgressWindow CreateFor(string version, Process process, string progressFilePath)
        {
            var window = new UpdateDownloadProgressWindow(version, process, progressFilePath);
            window.Build();
            window.TryAttachOwnerToHost();
            process.Exited += (_, _) => window._ui.Post(_ => window.OnProcessExited(), null);
            window.StartPolling();
            return window;
        }

        private void StartPolling()
        {
            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _poll.Tick += (_, _) => Refresh();
            _poll.Start();
            Closed += (_, _) => _poll?.Stop();
        }

        /// <summary>Reads the updater's latest snapshot and shows it.</summary>
        internal void Refresh()
        {
            var progress = UpdateDownloadProgressStore.TryLoad(_progressFilePath);
            if (progress != null && !string.Equals(progress.Version, _version, StringComparison.OrdinalIgnoreCase))
            {
                progress = null; // left over from another version's download
            }

            double? rate = null;
            if (progress is { Phase: UpdateDownloadPhases.Downloading })
            {
                rate = _speed.Add(DateTime.UtcNow, progress.BytesReceived);
            }

            if (_status != null)
            {
                _status.Text = Describe(progress, rate);
            }

            if (_bar != null)
            {
                var fraction = Fraction(progress);
                _bar.IsIndeterminate = fraction == null;
                if (fraction != null)
                {
                    _bar.Value = fraction.Value * 100;
                }
            }
        }

        /// <summary>The status line for a snapshot: the phase, or the size, speed and time left.</summary>
        internal static string Describe(UpdateDownloadProgress? progress, double? bytesPerSecond)
        {
            if (progress == null)
            {
                return "Starting the download…";
            }

            switch (progress.Phase)
            {
                case UpdateDownloadPhases.Connecting:
                    return "Connecting to the download server…";
                case UpdateDownloadPhases.Verifying:
                    return "Checking the installer…";
            }

            var text = progress.TotalBytes is long total && total > 0
                ? $"{Megabytes(progress.BytesReceived)} of {Megabytes(total)}"
                : $"{Megabytes(progress.BytesReceived)} downloaded";

            if (bytesPerSecond is double rate)
            {
                if (rate < 1)
                {
                    return progress.BytesReceived > 0 ? text + " · waiting for data…" : text;
                }

                text += " · " + Rate(rate);
                if (progress.TotalBytes is long size && size > progress.BytesReceived)
                {
                    text += " · " + TimeLeft((size - progress.BytesReceived) / rate);
                }
            }

            return text;
        }

        /// <summary>How much is done (0–1) when the size is known and bytes are arriving; else null.</summary>
        internal static double? Fraction(UpdateDownloadProgress? progress)
        {
            if (progress == null)
            {
                return null;
            }

            if (progress.Phase == UpdateDownloadPhases.Verifying)
            {
                return 1;
            }

            if (progress.Phase == UpdateDownloadPhases.Downloading && progress.TotalBytes is long total && total > 0)
            {
                return Math.Max(0, Math.Min(1, (double)progress.BytesReceived / total));
            }

            return null;
        }

        private static string Megabytes(long bytes) =>
            (bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

        private static string Rate(double bytesPerSecond) =>
            bytesPerSecond >= 1048576
                ? (bytesPerSecond / 1048576).ToString("0.0", CultureInfo.CurrentCulture) + " MB/s"
                : Math.Max(1, Math.Round(bytesPerSecond / 1024)).ToString("0", CultureInfo.CurrentCulture) + " KB/s";

        private static string TimeLeft(double seconds)
        {
            if (seconds < 60)
            {
                return $"about {Math.Max(1, (int)Math.Ceiling(seconds))} s left";
            }

            var minutes = (int)Math.Ceiling(seconds / 60);
            return minutes < 60 ? $"about {minutes} min left" : $"about {minutes / 60} h {minutes % 60} min left";
        }

        /// <summary>
        /// Download speed over the last few seconds, from (time, bytes) samples, so one slow or
        /// fast moment does not swing the figure.
        /// </summary>
        internal sealed class SpeedMeter
        {
            private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);
            private readonly Queue<(DateTime At, long Bytes)> _samples = new Queue<(DateTime, long)>();

            /// <summary>Adds a sample; returns bytes per second, or null until two samples span time.</summary>
            internal double? Add(DateTime at, long bytes)
            {
                _samples.Enqueue((at, bytes));
                while (_samples.Count > 2 && at - _samples.Peek().At > Window)
                {
                    _samples.Dequeue();
                }

                var first = _samples.Peek();
                var seconds = (at - first.At).TotalSeconds;
                return seconds <= 0 ? (double?)null : Math.Max(0, (bytes - first.Bytes) / seconds);
            }
        }

        private void TryAttachOwnerToHost()
        {
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                {
                    new WindowInteropHelper(this).Owner = (IntPtr)dte.MainWindow.HWnd;
                }
            }
            catch
            {
                // Not critical if we can't set owner.
            }
        }

        private void Build()
        {
            var registry = ThemeRegistry.Instance.Resources;
            var chromeFg = (SolidColorBrush)registry[ThemeTokens.TextPrimary];
            var muted = (SolidColorBrush)registry[ThemeTokens.TextPlaceholder];

            Title = WindowTitles.For("Downloading update");
            WindowIcon.Apply(this);
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ThemeRegistry.Instance.AttachTo(this);
            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            FontFamily = SegoeUiFont;
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            root.Children.Add(new TextBlock
            {
                Text = $"Downloading AKML SQL v{_version}…",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = chromeFg
            });
            _bar = new ProgressBar
            {
                IsIndeterminate = true,
                Minimum = 0,
                Maximum = 100,
                Height = 6,
                Margin = new Thickness(0, 12, 0, 8)
            };
            root.Children.Add(_bar);
            _status = new TextBlock
            {
                Text = Describe(null, null),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = chromeFg,
                Margin = new Thickness(0, 0, 0, 4)
            };
            root.Children.Add(_status);
            root.Children.Add(new TextBlock
            {
                Text = "Its checksum is checked before the installer can run.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = muted,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                IsCancel = true,
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            ThemedButton.ApplySecondary(cancelBtn);
            cancelBtn.Click += OnCancelClicked;
            root.Children.Add(cancelBtn);

            Content = root;
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            // Kill + cleanup off the UI thread; the updater may be mid-write on the .partial.
            IsEnabled = false;
            Task.Run(() =>
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }

                    _process.WaitForExit(10_000);
                }
                catch (Exception)
                {
                    // Already exited or failed to kill — cleanup below is still correct.
                }

                UpdateDownloadCleanup.AfterCancel(_version);
                _ui.Post(_ => CloseOnce(false), null);
            });
        }

        private void OnProcessExited()
        {
            // Natural exit (verified, failed, or rolled back) — the caller reads the result file.
            CloseOnce(true);
        }

        private void CloseOnce(bool dialogResult)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _poll?.Stop();
            try
            {
                DialogResult = dialogResult;
            }
            catch (InvalidOperationException)
            {
                // DialogResult is modal-only; unit tests may show the window non-modally.
            }

            Close();
        }
    }
}
