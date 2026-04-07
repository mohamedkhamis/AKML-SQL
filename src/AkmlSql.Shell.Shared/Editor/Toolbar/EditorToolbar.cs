#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Toolbar
{
    /// <summary>
    /// SQL Prompt-style action bar that appears at the top of each SQL editor.
    /// Code-only WPF UserControl (no XAML) - 30px tall with flat icon+text buttons.
    /// Renders in Dark or Light theme based on <see cref="ThemeManager"/>.
    /// </summary>
    internal sealed class EditorToolbar : UserControl, IWpfTextViewMargin
    {
        private readonly IWpfTextView _textView;
        private bool _disposed;

        // Theme colors
        private readonly SolidColorBrush _bgBrush;
        private readonly SolidColorBrush _borderBrush;
        private readonly SolidColorBrush _normalFg;
        private readonly SolidColorBrush _hoverBg;
        private readonly SolidColorBrush _hoverFg;
        private readonly SolidColorBrush _activeBg;
        private readonly SolidColorBrush _activeFg;

        public EditorToolbar(IWpfTextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));

            // Detect theme
            var theme = ThemeManager.Instance.DetectTheme();
            var isDark = theme == VsThemeKind.Dark || theme == VsThemeKind.Blue;

            // Build frozen brushes
            if (isDark)
            {
                _bgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)));
                _borderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)));
                _normalFg = Freeze(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)));
                _hoverBg = Freeze(new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)));
                _hoverFg = Freeze(new SolidColorBrush(Colors.White));
                _activeBg = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)));
                _activeFg = Freeze(new SolidColorBrush(Colors.White));
            }
            else
            {
                _bgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)));
                _borderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0xCE, 0xDB)));
                _normalFg = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E)));
                _hoverBg = Freeze(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC)));
                _hoverFg = Freeze(new SolidColorBrush(Colors.Black));
                _activeBg = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)));
                _activeFg = Freeze(new SolidColorBrush(Colors.White));
            }

            BuildUi();

            _textView.Closed += OnTextViewClosed;
        }

        // ─── IWpfTextViewMargin ─────────────────────────────────────────────

        public FrameworkElement VisualElement => this;

        public double MarginSize => 30;

        public bool Enabled => !_disposed;

        public ITextViewMargin? GetTextViewMargin(string marginName)
        {
            return string.Equals(marginName, "AkmlSqlEditorToolbar", StringComparison.OrdinalIgnoreCase)
                ? this
                : null;
        }

        // ─── UI Construction ────────────────────────────────────────────────

        private void BuildUi()
        {
            Height = 30;
            Background = _bgBrush;
            BorderBrush = _borderBrush;
            BorderThickness = new Thickness(0, 0, 0, 1);
            SnapsToDevicePixels = true;

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };

            // Button definitions: icon, label, click handler
            panel.Children.Add(CreateButton("\u229E", "Format", OnFormatClick));
            panel.Children.Add(CreateButton("\u23F1", "History", OnHistoryClick));
            panel.Children.Add(CreateButton("\u2261", "Outline", OnOutlineClick));
            panel.Children.Add(CreateButton("\uD83D\uDD0D", "Search", OnSearchClick));
            panel.Children.Add(CreateButton("\u26A1", "Analysis", OnAnalysisClick));
            panel.Children.Add(CreateButton("\uD83D\uDCAC", "AI Chat", OnAiChatClick));

            // Separator
            var sep = new Border
            {
                Width = 1,
                Height = 16,
                Background = _borderBrush,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(sep);

            panel.Children.Add(CreateButton("\u2699", "Settings", OnSettingsClick));

            Content = panel;
        }

        private Border CreateButton(string icon, string label, Action clickHandler)
        {
            var textBlock = new TextBlock
            {
                Text = $"{icon}  {label}",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = _normalFg,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            var border = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(1, 0, 1, 0),
                Child = textBlock,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.MouseEnter += (s, e) =>
            {
                border.Background = _hoverBg;
                textBlock.Foreground = _hoverFg;
            };

            border.MouseLeave += (s, e) =>
            {
                border.Background = Brushes.Transparent;
                textBlock.Foreground = _normalFg;
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                border.Background = _activeBg;
                textBlock.Foreground = _activeFg;
            };

            border.MouseLeftButtonUp += (s, e) =>
            {
                border.Background = _hoverBg;
                textBlock.Foreground = _hoverFg;

                // Use BeginInvoke to avoid blocking the mouse event handler
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        clickHandler();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "EditorToolbar: button click failed for {Label}", label);
                    }
                }));
            };

            return border;
        }

        // ─── Click Handlers ─────────────────────────────────────────────────

        private void OnFormatClick()
        {
            try
            {
                // Try DTE command first (same approach as keyboard hook)
                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                    typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    dte.ExecuteCommand("AKML_SQL.FormatDocument");
                    Log.Debug("EditorToolbar: Format invoked via DTE command");
                    return;
                }
            }
            catch
            {
                // Fallback: invoke format directly
            }

            try
            {
                Completion.CompletionPopupProvider.FormatDirectly();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "EditorToolbar: Format fallback failed");
            }
        }

        private void OnHistoryClick()
        {
            InvokeVsCommand(CommandIds.CmdHistoryPanel, "HistoryPanel");
        }

        private void OnOutlineClick()
        {
            InvokeVsCommand(CommandIds.CmdDocumentOutline, "DocumentOutline");
        }

        private void OnSearchClick()
        {
            InvokeVsCommand(CommandIds.CmdObjectSearch, "ObjectSearch");
        }

        private void OnAnalysisClick()
        {
            InvokeVsCommand(CommandIds.CmdBulkAnalysis, "BulkAnalysis");
        }

        private void OnAiChatClick()
        {
            InvokeVsCommand(CommandIds.CmdAiChatPanel, "AiChatPanel");
        }

        private void OnSettingsClick()
        {
            InvokeVsCommand(CommandIds.CmdOptions, "Options");
        }

        /// <summary>
        /// Invokes a registered AKML SQL command by its command ID.
        /// Uses DTE.Commands to find the command by GUID+ID, then executes via its
        /// canonical name. This works reliably in SSMS 22's custom menu system
        /// (GlobalInvoke on OleMenuCommandService can silently fail).
        /// </summary>
        private static void InvokeVsCommand(int commandId, string commandName)
        {
            try
            {
                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                    typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    var cmd = dte.Commands.Item(
                        "{" + PackageGuids.AkmlSqlCmdSetString + "}", commandId);
                    if (cmd != null && !string.IsNullOrEmpty(cmd.Name))
                    {
                        dte.ExecuteCommand(cmd.Name);
                        Log.Debug("EditorToolbar: {Command} invoked via DTE.ExecuteCommand({Name})",
                            commandName, cmd.Name);
                        return;
                    }
                }

                // Fallback: try GlobalInvoke if DTE path failed
                var commandService = (Microsoft.VisualStudio.Shell.OleMenuCommandService?)
                    Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                        typeof(System.ComponentModel.Design.IMenuCommandService));
                if (commandService != null)
                {
                    var cmdId = new System.ComponentModel.Design.CommandID(
                        PackageGuids.AkmlSqlCmdSet, commandId);
                    commandService.GlobalInvoke(cmdId);
                    Log.Debug("EditorToolbar: {Command} invoked via GlobalInvoke fallback", commandName);
                }
                else
                {
                    Log.Warning("EditorToolbar: no command service available for {Command}", commandName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EditorToolbar: failed to invoke {Command}", commandName);
            }
        }

        // ─── Cleanup ────────────────────────────────────────────────────────

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _textView.Closed -= OnTextViewClosed;
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
