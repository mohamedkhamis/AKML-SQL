#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Spec 029 follow-up. Lists the saved SQL-auth credentials by (server, login) and lets the user
    /// remove individual entries or clear all. Passwords are never shown — only the keys. Removing an
    /// entry simply means AKML re-inherits the password from SSMS (or prompts) next time. Programmatic
    /// WPF, matching the SqlCredentialDialog / SafetyWarningDialog house style.
    /// </summary>
    internal sealed class SqlCredentialManagerDialog : Window
    {
        private static readonly FontFamily SegoeUiFont = new FontFamily("Segoe UI");

        private SolidColorBrush _fgBrush = null!;
        private SolidColorBrush _mutedBrush = null!;
        private StackPanel _listPanel = null!;

        public SqlCredentialManagerDialog()
        {
            Build();
            TryAttachOwnerToHost();
        }

        private void TryAttachOwnerToHost()
        {
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                    new WindowInteropHelper(this).Owner = (IntPtr)dte.MainWindow.HWnd;
            }
            catch { /* not critical */ }
        }

        private void Build()
        {
            var registry = ThemeRegistry.Instance.Resources;
            _fgBrush = (SolidColorBrush)registry[ThemeTokens.TextPrimary];
            _mutedBrush = (SolidColorBrush)registry[ThemeTokens.TextPlaceholder];

            Title = "AKML SQL — saved SQL credentials";
            Width = 470;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ThemeRegistry.Instance.AttachTo(this);
            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            FontFamily = SegoeUiFont;
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = "SQL Server passwords AKML has stored (DPAPI-encrypted) so it can load IntelliSense " +
                       "for SQL-auth connections. Removing an entry just means AKML re-inherits it from " +
                       "SSMS — or prompts — next time.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = _fgBrush,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _listPanel = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _listPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 210,
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(scroll);

            root.Children.Add(BuildFooter());

            Content = root;
            RefreshList();
        }

        private void RefreshList()
        {
            _listPanel.Children.Clear();
            var entries = SqlCredentialStore.List();
            if (entries.Count == 0)
            {
                _listPanel.Children.Add(new TextBlock
                {
                    Text = "No saved SQL credentials.",
                    Foreground = _mutedBrush,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                return;
            }

            foreach (var (server, login) in entries)
            {
                var s = server; var l = login; // capture for the closure
                var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };

                var remove = new Button { Content = "Remove", MinWidth = 80, Height = 26, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
                remove.Click += (_, _) => { SqlCredentialStore.Remove(s, l); RefreshList(); };
                DockPanel.SetDock(remove, Dock.Right);
                dock.Children.Add(remove);

                dock.Children.Add(new TextBlock
                {
                    Text = server + "  ·  " + login,
                    Foreground = _fgBrush,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                _listPanel.Children.Add(dock);
            }
        }

        private DockPanel BuildFooter()
        {
            var footer = new DockPanel { LastChildFill = false };

            var clearAll = new Button { Content = "Clear all", Height = 28, MinWidth = 90, FontSize = 12 };
            clearAll.Click += (_, _) =>
            {
                foreach (var (s, l) in SqlCredentialStore.List())
                    SqlCredentialStore.Remove(s, l);
                RefreshList();
            };
            DockPanel.SetDock(clearAll, Dock.Left);
            footer.Children.Add(clearAll);

            var close = new Button { Content = "Close", Width = 80, Height = 28, FontSize = 13, IsCancel = true, IsDefault = true };
            close.Click += (_, _) => { DialogResult = true; Close(); };
            DockPanel.SetDock(close, Dock.Right);
            footer.Children.Add(close);

            return footer;
        }
    }
}
