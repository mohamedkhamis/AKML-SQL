#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Toolbar
{
    /// <summary>
    /// SQL Prompt-style action bar that appears at the top of each SQL editor.
    /// Code-only WPF UserControl (no XAML) — 30 px tall with flat icon+text buttons.
    /// Chrome flows through <see cref="ThemeRegistry"/>; theme variants are resolved by the
    /// registry, not branched per-call-site.
    /// </summary>
    internal sealed class EditorToolbar : ThemeAwareUserControl, IWpfTextViewMargin
    {
        private readonly IWpfTextView _textView;
        private bool _disposed;

        // Cached per-instance Style for the toolbar buttons. The Style hosts the rounded chrome
        // template and the IsMouseOver / IsPressed triggers so each button reacts to hover/press
        // without us having to wire mouse handlers manually.
        private readonly Style _buttonStyle;

        public EditorToolbar(IWpfTextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));

            _buttonStyle = BuildButtonStyle();

            BuildUi();

            _textView.Closed += OnTextViewClosed;
        }

        // --- IWpfTextViewMargin -----------------------------------------------

        public FrameworkElement VisualElement => this;

        public double MarginSize => 30;

        public bool Enabled => !_disposed;

        public ITextViewMargin? GetTextViewMargin(string marginName)
        {
            return string.Equals(marginName, "AkmlSqlEditorToolbar", StringComparison.OrdinalIgnoreCase)
                ? this
                : null;
        }

        // --- UI Construction --------------------------------------------------

        private void BuildUi()
        {
            Height = 30;
            BorderThickness = new Thickness(0, 0, 0, 1);
            SnapsToDevicePixels = true;

            // Override the SurfacePanel default that ThemeAwareUserControl applies — for an editor
            // margin the EditorMarginBackground role is more semantic.
            SetResourceReference(BackgroundProperty, ThemeTokens.EditorMarginBackground);
            SetResourceReference(BorderBrushProperty, ThemeTokens.BorderDefault);

            var panel = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(Spacing.Xs, 0, Spacing.Xs, 0)
            };

            // Button definitions: icon, label, click handler.
            panel.Children.Add(CreateButton("⊞", "Format",   OnFormatClick));
            panel.Children.Add(CreateButton("⏱", "History",  OnHistoryClick));
            panel.Children.Add(CreateButton("≡", "Outline",  OnOutlineClick));
            panel.Children.Add(CreateButton("🔍", "Search",   OnSearchClick));
            panel.Children.Add(CreateButton("⚡", "Analysis", OnAnalysisClick));
            panel.Children.Add(CreateButton("💬", "AI Chat",  OnAiChatClick));

            // Vertical separator
            var sep = new Border
            {
                Width             = 1,
                Height            = 16,
                Margin            = new Thickness(Spacing.Xs, 0, Spacing.Xs, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sep.SetResourceReference(Border.BackgroundProperty, ThemeTokens.BorderDefault);
            panel.Children.Add(sep);

            panel.Children.Add(CreateButton("⚙", "Settings", OnSettingsClick));

            Content = panel;
        }

        private Button CreateButton(string icon, string label, Action clickHandler)
        {
            var button = new Button
            {
                Content           = $"{icon}  {label}",
                FontFamily        = Typography.UiFont,
                FontSize          = Typography.Small,
                Margin            = new Thickness(1, 0, 1, 0),
                Padding           = new Thickness(6, 2, 6, 2),
                Cursor            = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Style             = _buttonStyle,
                FocusVisualStyle  = FocusVisualStyles.HighStakes
            };

            button.Click += (_, _) =>
            {
                // Defer the work via dispatcher so the visual press-state can render before the
                // command runs — preserves the ergonomic feedback the original mouse-down path had.
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

            return button;
        }

        /// <summary>
        /// Builds the toolbar Button style: a rounded <see cref="Border"/> wrapping a
        /// <see cref="ContentPresenter"/>, with hover and pressed triggers that swap Background
        /// and Foreground via <see cref="DynamicResourceExtension"/> so the colours track
        /// theme switches.
        /// </summary>
        private static Style BuildButtonStyle()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.BackgroundProperty,  new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty,     new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,   VerticalAlignment.Center);
            // ContentPresenter inherits Foreground via TextElement attached-property propagation
            // from the Button's Foreground (which the Style + triggers below drive). No explicit
            // template binding needed.
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.TemplateProperty,        template));
            style.Setters.Add(new Setter(Control.BackgroundProperty,      Brushes.Transparent));   // theme-independent placeholder
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(ThemeTokens.TextSecondary)));

            // Hover: subtle row tint + primary text colour
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceHover)));
            hover.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(ThemeTokens.TextPrimary)));
            style.Triggers.Add(hover);

            // Pressed: accent fill, on-accent text
            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.AccentPrimary)));
            pressed.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(ThemeTokens.TextOnAccent)));
            style.Triggers.Add(pressed);

            return style;
        }

        // --- Click Handlers ---------------------------------------------------

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

        private void OnHistoryClick()  => InvokeVsCommand(CommandIds.CmdHistoryPanel,    "HistoryPanel");
        private void OnOutlineClick()  => InvokeVsCommand(CommandIds.CmdDocumentOutline, "DocumentOutline");
        private void OnSearchClick()   => InvokeVsCommand(CommandIds.CmdObjectSearch,    "ObjectSearch");
        private void OnAnalysisClick() => InvokeVsCommand(CommandIds.CmdBulkAnalysis,    "BulkAnalysis");
        private void OnAiChatClick()   => InvokeVsCommand(CommandIds.CmdAiChatPanel,     "AiChatPanel");
        private void OnSettingsClick() => InvokeVsCommand(CommandIds.CmdOptions,         "Options");

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
                    // Commands.Item(guid, id) can throw COMException in SSMS 22 for some
                    // command IDs — wrap in its own try-catch so failures fall through to
                    // the GlobalInvoke path instead of being caught by the outer handler.
                    string? resolvedName = null;
                    try
                    {
                        var cmd = dte.Commands.Item(
                            "{" + PackageGuids.AkmlSqlCmdSetString + "}", commandId);
                        resolvedName = cmd?.Name;
                    }
                    catch
                    {
                        // Fall through to GlobalInvoke below
                    }

                    if (!string.IsNullOrEmpty(resolvedName))
                    {
                        dte.ExecuteCommand(resolvedName);
                        Log.Debug("EditorToolbar: {Command} invoked via DTE.ExecuteCommand({Name})",
                            commandName, resolvedName);
                        return;
                    }
                }

                // Fallback: try GlobalInvoke if DTE path failed or Commands.Item threw
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

        // --- Cleanup ----------------------------------------------------------

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
    }
}
