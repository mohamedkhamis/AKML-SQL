#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;
using Orientation = System.Windows.Controls.Orientation;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// Modal progress surface for the guided update download (spec 036 US5 / FR-039a): shows
    /// that the out-of-process download is running and offers a working Cancel. The updater
    /// process does the download; this window only watches its <c>Exited</c> event.
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
        private bool _closed;

        private UpdateDownloadProgressWindow(string version, Process process)
        {
            _version = version;
            _process = process;
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        /// <summary>Creates and wires the window; the process must already be started with
        /// <see cref="Process.EnableRaisingEvents"/> set (<see cref="UpdateLauncher.LaunchUpdaterDownload"/>).</summary>
        public static UpdateDownloadProgressWindow CreateFor(string version, Process process)
        {
            var window = new UpdateDownloadProgressWindow(version, process);
            window.Build();
            window.TryAttachOwnerToHost();
            process.Exited += (_, _) => window._ui.Post(_ => window.OnProcessExited(), null);
            return window;
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

            Title = "Downloading update";
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
            root.Children.Add(new ProgressBar
            {
                IsIndeterminate = true,
                Height = 6,
                Margin = new Thickness(0, 12, 0, 8)
            });
            root.Children.Add(new TextBlock
            {
                Text = "The installer is being downloaded and its checksum verified.",
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
