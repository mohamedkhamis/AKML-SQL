#nullable enable
using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;
using Serilog;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// 2026-11 master–detail redesign (SSMS layout B): the small "Add AI agent" / "Edit AI agent"
    /// window the AI assistance page opens over the Options window. It hosts one
    /// <see cref="AiAgentEditor"/>; OK accepts the draft only when it validates (the refusal is
    /// shown in the form), Cancel and Escape discard it. Nothing is written to disk here — the
    /// page copies the draft into its working copy and the Options window's OK saves it, the way
    /// the Windows Environment Variables dialog works.
    /// </summary>
    internal sealed class AiAgentEditorDialog : Window
    {
        private readonly AiAgentEditor _editor;

        internal AiAgentEditorDialog(AiAgentEditor editor, Window? owner)
        {
            _editor = editor;
            var theme = editor.Theme;

            Title = WindowTitles.For(editor.Title);
            Icon = Ui.WindowIcon.Source;
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MinWidth = 460;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            Background = theme.Main;
            Foreground = theme.FgPrimary;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            if (owner != null)
            {
                try { Owner = owner; }
                catch (InvalidOperationException ex)
                {
                    // An owner that was never shown (a test-built Options window) cannot own us.
                    Log.Debug(ex, "AiAgentEditorDialog: owner not usable; centring on screen");
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            var root = new DockPanel { Background = theme.Main, LastChildFill = true };

            var footer = new Border
            {
                Background = theme.Main,
                BorderBrush = theme.Sep,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10),
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", MinWidth = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ThemedButton.ApplyPrimary(ok, theme);
            AutomationProperties.SetName(ok, "OK");
            var cancel = new Button { Content = "Cancel", MinWidth = 80, Height = 28, IsCancel = true };
            ThemedButton.ApplySecondary(cancel, theme);
            AutomationProperties.SetName(cancel, "Cancel");
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            footer.Child = buttons;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var note = new TextBlock
            {
                Text = "OK adds the change to the agent list. It is saved when you click OK in the Options window.",
                FontSize = 11,
                Foreground = theme.FgSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 10) };
            body.Children.Add(editor.Root);
            body.Children.Add(note);
            root.Children.Add(body);

            Content = root;

            ok.Click += (_, _) => Accept();
            Loaded += (_, _) =>
            {
                TitleBarTheme.Apply(this, ReferenceEquals(theme, PageTheme.Dark));
                // Add… starts on the provider (the name is already suggested); Edit… on the name.
                var first = (UIElement)(editor.IsNew ? editor.ProviderCombo : editor.NameBox);
                first.Focus();
                if (!editor.IsNew) editor.NameBox.SelectAll();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            };
        }

        private void Accept()
        {
            if (_editor.TryAccept() != null) return;   // the refusal is shown in the form
            DialogResult = true;
        }

        /// <summary>Shows the dialog for <paramref name="editor"/>; true when the user accepted the draft.</summary>
        internal static bool ShowFor(AiAgentEditor editor, DependencyObject? anchor)
        {
            var owner = anchor != null ? GetWindow(anchor) : null;
            var dialog = new AiAgentEditorDialog(editor, owner);
            return dialog.ShowDialog() == true;
        }
    }
}
