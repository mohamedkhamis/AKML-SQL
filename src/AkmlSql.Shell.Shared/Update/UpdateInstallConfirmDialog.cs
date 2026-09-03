#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// FR-039 confirmation before launching a downloaded-and-verified update installer.
    /// Names the new version and the applications the installer must close (SSMS and Visual
    /// Studio — the installer's <c>CloseApplicationsFilter</c> in AkmlSqlSetup.iss).
    ///
    /// FR-005 safety convention (see <c>SafetyWarningDialog</c>): Cancel is
    /// <c>IsCancel = true</c> and holds the initial focus on <c>Loaded</c>; the "Install now"
    /// button is deliberately never the default. Declining installs nothing, closes nothing
    /// and retains the offer (spec scenario 4a).
    /// </summary>
    internal sealed class UpdateInstallConfirmDialog : Window
    {
        private static readonly Color AmberBorder = Color.FromRgb(0xFF, 0xC1, 0x07);
        private static readonly Color BtnPrimary = Color.FromRgb(0x00, 0x78, 0xD4);
        private static readonly FontFamily SegoeUiFont = new("Segoe UI");

        private SolidColorBrush _mutedBrush = null!;
        private SolidColorBrush _dividerBrush = null!;
        private SolidColorBrush _cardBgBrush = null!;
        private SolidColorBrush _chromeFgBrush = null!;
        private SolidColorBrush _onAccentBrush = null!;
        private SolidColorBrush _accentBrush = null!;

        private UpdateInstallConfirmDialog() { }

        /// <summary>
        /// <c>true</c> = user approved the install, <c>false</c>/<c>null</c> = declined.
        /// Mirrors <see cref="Window.DialogResult"/> but stays readable when the window is
        /// shown non-modally (unit tests); production callers use <c>ShowDialog()</c>.
        /// </summary>
        public bool? Outcome { get; private set; }

        /// <summary>
        /// Creates a ready-to-show dialog for the verified update. Caller owns the lifetime:
        /// <c>ShowDialog()</c>, then proceed only when the result is <c>true</c>.
        /// </summary>
        public static UpdateInstallConfirmDialog CreateForUpdate(string version)
        {
            var dlg = new UpdateInstallConfirmDialog();
            dlg.Build(version);
            dlg.TryAttachOwnerToHost();
            return dlg;
        }

        /// <summary>Parents the dialog to the VS/SSMS main window; silent no-op without DTE.</summary>
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

        private void Build(string version)
        {
            var registry = ThemeRegistry.Instance.Resources;
            _chromeFgBrush = (SolidColorBrush)registry[ThemeTokens.TextPrimary];
            _mutedBrush = (SolidColorBrush)registry[ThemeTokens.TextPlaceholder];
            _dividerBrush = (SolidColorBrush)registry[ThemeTokens.BorderDefault];
            _cardBgBrush = (SolidColorBrush)registry[ThemeTokens.SurfaceElevated];
            _onAccentBrush = (SolidColorBrush)registry[ThemeTokens.TextOnAccent];
            _accentBrush = Freeze(new SolidColorBrush(AmberBorder));

            Title = "Install update";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ThemeRegistry.Instance.AttachTo(this);
            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            FontFamily = SegoeUiFont;
            FontSize = 13;

            var root = new StackPanel();
            root.Children.Add(BuildHeader(version));
            root.Children.Add(BuildBody());
            root.Children.Add(BuildFooter(out var cancelBtn));
            Content = root;

            // FR-005 — Cancel is the default focus so Enter/Space defaults to "don't install".
            Loaded += (_, _) => cancelBtn.Focus();
        }

        private Border BuildHeader(string version)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "⬆",
                FontSize = 22,
                Foreground = _accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Ready to install AKML SQL v{version}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = _chromeFgBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

            return new Border
            {
                BorderBrush = _accentBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 12, 16, 12),
                Child = stack
            };
        }

        private Border BuildBody()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "The installer has been downloaded and its checksum verified.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = _chromeFgBrush,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 8)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "The following applications must close during the installation — save your work in them first:",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = _chromeFgBrush,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 6)
            });
            // AkmlSqlSetup.iss CloseApplicationsFilter: Ssms.exe,devenv.exe
            stack.Children.Add(new TextBlock
            {
                Text = "   •  SQL Server Management Studio\n   •  Visual Studio",
                FontSize = 12.5,
                Foreground = _chromeFgBrush,
                LineHeight = 18
            });

            return new Border
            {
                Background = _cardBgBrush,
                BorderBrush = _dividerBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(16, 14, 16, 0),
                Child = stack
            };
        }

        private DockPanel BuildFooter(out Button cancelBtn)
        {
            var footer = new DockPanel
            {
                Margin = new Thickness(16, 12, 16, 16),
                LastChildFill = false
            };

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btnPanel, Dock.Right);

            // FR-005 — deliberately not the default button; installing must be a deliberate click.
            var installBtn = new Button
            {
                Content = "Install now",
                MinWidth = 110,
                Height = 32,
                FontSize = 13,
                Foreground = _onAccentBrush,
                Background = Freeze(new SolidColorBrush(BtnPrimary)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 0, 16, 0)
            };
            installBtn.Click += (_, _) => Complete(true);

            cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true,
                FontSize = 13
            };
            cancelBtn.Click += (_, _) => Complete(false);

            btnPanel.Children.Add(installBtn);
            btnPanel.Children.Add(cancelBtn);
            footer.Children.Add(btnPanel);

            return footer;
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
                // DialogResult is modal-only; unit tests show the window non-modally.
            }

            Close();
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
