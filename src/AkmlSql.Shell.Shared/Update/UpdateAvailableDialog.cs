#nullable enable
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Ui.Theme;
using Orientation = System.Windows.Controls.Orientation;
using Serilog;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// The "update available" prompt (spec 036 US5 / FR-038): names the new version and gives a
    /// working route to the release notes. "Download and install" enters the guided flow
    /// (download → verify → confirm → launch); "Not now" keeps the offer on disk.
    /// FR-005 convention: the proceed button is never the default and Cancel holds the focus.
    /// </summary>
    internal sealed class UpdateAvailableDialog : Window
    {
        private static readonly Color BtnPrimary = Color.FromRgb(0x00, 0x78, 0xD4);
        private static readonly FontFamily SegoeUiFont = new("Segoe UI");

        private UpdateAvailableDialog() { }

        /// <summary>
        /// <c>true</c> = download and install, <c>false</c>/<c>null</c> = not now. Mirrors
        /// <see cref="Window.DialogResult"/> but stays readable when shown non-modally (tests).
        /// </summary>
        public bool? Outcome { get; private set; }

        /// <summary>The release-notes URL the dialog links to (empty when unusable).</summary>
        public string ReleaseNotesUrl { get; private set; } = string.Empty;

        public static UpdateAvailableDialog CreateFor(UpdateResult result)
        {
            var dlg = new UpdateAvailableDialog();
            dlg.Build(result);
            dlg.TryAttachOwnerToHost();
            return dlg;
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

        private void Build(UpdateResult result)
        {
            var registry = ThemeRegistry.Instance.Resources;
            var chromeFg = (SolidColorBrush)registry[ThemeTokens.TextPrimary];
            var muted = (SolidColorBrush)registry[ThemeTokens.TextPlaceholder];
            var linkBrush = (SolidColorBrush)registry[ThemeTokens.TextLink];
            var onAccent = (SolidColorBrush)registry[ThemeTokens.TextOnAccent];

            Title = "Update available";
            Width = 440;
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
                Text = $"AKML SQL v{result.Version} is available",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = chromeFg
            });

            // FR-038: a route to the release notes. Only absolute HTTPS links are navigable.
            if (IsValidHttpsUrl(result.ReleaseNotesUrl))
            {
                ReleaseNotesUrl = result.ReleaseNotesUrl;
                var link = new Hyperlink(new Run("Release notes")) { NavigateUri = new Uri(result.ReleaseNotesUrl) };
                link.RequestNavigate += (_, _) => OpenReleaseNotes(result.ReleaseNotesUrl);
                root.Children.Add(new TextBlock
                {
                    Inlines = { link },
                    FontSize = 12.5,
                    Foreground = linkBrush,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            root.Children.Add(new TextBlock
            {
                Text = "The installer will be downloaded and verified before anything is installed.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = muted,
                Margin = new Thickness(0, 10, 0, 14)
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // FR-005 — deliberately not the default button.
            var installBtn = new Button
            {
                Content = "Download and install",
                MinWidth = 130,
                Height = 32,
                FontSize = 13,
                Foreground = onAccent,
                Background = Freeze(new SolidColorBrush(BtnPrimary)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 0, 16, 0)
            };
            installBtn.Click += (_, _) => Complete(true);

            var laterBtn = new Button
            {
                Content = "Not now",
                Width = 80,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true,
                FontSize = 13
            };
            laterBtn.Click += (_, _) => Complete(false);

            btnPanel.Children.Add(installBtn);
            btnPanel.Children.Add(laterBtn);
            root.Children.Add(btnPanel);

            Content = root;

            // FR-005 — the dismiss button holds initial focus.
            Loaded += (_, _) => laterBtn.Focus();
        }

        private void OpenReleaseNotes(string url)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })) { }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to open release notes");
            }
        }

        private void Complete(bool approved)
        {
            Outcome = approved;
            try
            {
                DialogResult = approved;
            }
            catch (InvalidOperationException)
            {
                // DialogResult is modal-only; unit tests may show the window non-modally.
            }

            Close();
        }

        private static bool IsValidHttpsUrl(string url)
        {
            return !string.IsNullOrEmpty(url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps;
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
