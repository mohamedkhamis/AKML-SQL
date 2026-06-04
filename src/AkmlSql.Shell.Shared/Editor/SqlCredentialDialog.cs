#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Orientation = System.Windows.Controls.Orientation;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Spec 029. Theme-aware modal that collects a SQL Server-auth password, validates it against the
    /// server via the engine (TestSqlConnection IPC), and stores it DPAPI-encrypted on success.
    /// Programmatic WPF (no XAML), matching SafetyWarningDialog's house style. Returns DialogResult=true
    /// when a password was saved (validated) OR an existing one was cleared; false on Cancel.
    /// </summary>
    internal sealed class SqlCredentialDialog : Window
    {
        private static readonly FontFamily SegoeUiFont = new FontFamily("Segoe UI");

        private readonly string _server;
        private readonly string _database;
        private readonly string _login;

        private PasswordBox _passwordBox = null!;
        private TextBlock _statusText = null!;
        private Button _saveBtn = null!;
        private Button? _clearBtn;

        private SolidColorBrush _mutedBrush = null!;
        private SolidColorBrush _fgBrush = null!;
        private readonly SolidColorBrush _errorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)));

        public SqlCredentialDialog(string server, string database, string login, bool hasExistingCredential)
        {
            _server = server ?? string.Empty;
            _database = database ?? string.Empty;
            _login = login ?? string.Empty;
            Build(hasExistingCredential);
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

        private void Build(bool hasExistingCredential)
        {
            var registry = ThemeRegistry.Instance.Resources;
            _fgBrush = (SolidColorBrush)registry[ThemeTokens.TextPrimary];
            _mutedBrush = (SolidColorBrush)registry[ThemeTokens.TextPlaceholder];

            Title = "AKML SQL — SQL authentication";
            Width = 430;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ThemeRegistry.Instance.AttachTo(this);
            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            FontFamily = SegoeUiFont;
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(18) };

            root.Children.Add(new TextBlock
            {
                Text = "Enter the SQL Server password to enable IntelliSense for this connection. " +
                       "It is validated against the server, then stored encrypted (Windows DPAPI) for this user.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = _fgBrush,
                Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(LabeledValue("Server", _server));
            root.Children.Add(LabeledValue("Database", _database));
            root.Children.Add(LabeledValue("Login", _login));

            root.Children.Add(new TextBlock
            {
                Text = "Password",
                Foreground = _mutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 4)
            });
            _passwordBox = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            _passwordBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            root.Children.Add(_passwordBox);

            _statusText = new TextBlock
            {
                Text = string.Empty,
                TextWrapping = TextWrapping.Wrap,
                Foreground = _mutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_statusText);

            root.Children.Add(BuildFooter(hasExistingCredential));

            Content = root;
            Loaded += (_, _) => _passwordBox.Focus();

            if (string.IsNullOrEmpty(_login))
            {
                _saveBtn.IsEnabled = false;
                ShowStatus(
                    "Couldn’t read the SQL login from the window title — reconnect this window so the login appears in the title, then reopen this prompt.",
                    isError: true);
            }
        }

        private UIElement LabeledValue(string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label + ":", Width = 72, Foreground = _mutedBrush, FontSize = 12 });
            row.Children.Add(new TextBlock { Text = value, Foreground = _fgBrush, FontSize = 12, FontWeight = FontWeights.SemiBold });
            return row;
        }

        private DockPanel BuildFooter(bool hasExistingCredential)
        {
            var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };

            if (hasExistingCredential)
            {
                _clearBtn = new Button
                {
                    Content = "Clear saved password",
                    Height = 30,
                    Padding = new Thickness(10, 0, 10, 0),
                    FontSize = 12
                };
                DockPanel.SetDock(_clearBtn, Dock.Left);
                _clearBtn.Click += (_, _) =>
                {
                    SqlCredentialStore.Remove(_server, _login);
                    DialogResult = true;
                    Close();
                };
                footer.Children.Add(_clearBtn);
            }

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btnPanel, Dock.Right);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true,
                FontSize = 13
            };
            cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

            // Save is the default button (Enter submits) — safe: validation always precedes storage.
            _saveBtn = new Button { Content = "Save", MinWidth = 90, Height = 30, FontSize = 13, IsDefault = true };
            _saveBtn.Click += OnSaveClick;

            btnPanel.Children.Add(_saveBtn);
            btnPanel.Children.Add(cancelBtn);
            footer.Children.Add(btnPanel);
            return footer;
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_login))
            {
                ShowStatus("No SQL login available for this window.", isError: true);
                return;
            }

            var pwd = _passwordBox.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                ShowStatus("Enter a password.", isError: true);
                return;
            }

            SetBusy(true);
            ShowStatus("Testing connection…", isError: false);
            try
            {
                var connStr = SsmsConnectionDetector.BuildSqlAuthConnectionString(_server, _database, _login, pwd);
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    ShowStatus("AKML SQL engine is not running yet — try again in a moment.", isError: true);
                    SetBusy(false);
                    return;
                }

                var resp = await client.SendRequestAsync<TestSqlConnectionResponse, TestSqlConnectionRequest>(
                    MessageTypes.TestSqlConnection,
                    new TestSqlConnectionRequest { ConnectionString = connStr },
                    timeoutMs: 8000);

                if (resp != null && resp.Ok)
                {
                    SqlCredentialStore.Save(_server, _login, pwd);
                    DialogResult = true;
                    Close();
                    return;
                }

                ShowStatus(resp?.ErrorMessage ?? "Could not connect with these credentials.", isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus("Validation failed: " + ex.Message, isError: true);
            }
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            _saveBtn.IsEnabled = !busy;
            if (_clearBtn != null) _clearBtn.IsEnabled = !busy;
            _passwordBox.IsEnabled = !busy;
        }

        private void ShowStatus(string text, bool isError)
        {
            _statusText.Text = text;
            _statusText.Foreground = isError ? _errorBrush : _mutedBrush;
            _statusText.Visibility = Visibility.Visible;
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { if (b.CanFreeze) b.Freeze(); return b; }
    }
}
