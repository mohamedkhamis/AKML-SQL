#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 033 (T035) — small themed name prompt for the Format Styles editor: New Style…
    /// (name + based-on picker) and Rename… (name pre-filled). Follows the ShowRuleEditor
    /// accepted-flag shape; callers set <c>Owner</c> to the styles window per the
    /// nested-modal rule documented on <see cref="ImportSummaryDialog"/> (WPF only disables
    /// and centres over the actual Owner).
    /// </summary>
    internal sealed class StyleNameDialog : ThemeAwareWindow
    {
        private readonly TextBox _nameBox;
        private readonly ComboBox? _basedOnCombo;
        private readonly TextBlock _validationText;
        private bool _accepted;

        private StyleNameDialog(string title, string prompt, string initialName, IReadOnlyList<string>? baseCandidates, string? defaultBase)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(Spacing.Lg) };

            var promptText = new TextBlock
            {
                Text = prompt,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
                TextWrapping = TextWrapping.Wrap,
            };
            promptText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            root.Children.Add(promptText);

            _nameBox = new TextBox
            {
                Text = initialName,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
            };
            _nameBox.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
            _nameBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _nameBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            _nameBox.TextChanged += (_, _) => Revalidate();
            root.Children.Add(_nameBox);

            if (baseCandidates != null)
            {
                var basedOnLabel = new TextBlock
                {
                    Text = "Based on:",
                    FontFamily = Typography.UiFont,
                    FontSize = Typography.Small,
                    Margin = new Thickness(0, Spacing.Xs, 0, Spacing.Xs),
                };
                basedOnLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                root.Children.Add(basedOnLabel);

                _basedOnCombo = new ComboBox
                {
                    FontFamily = Typography.UiFont,
                    FontSize = Typography.Body,
                    Margin = new Thickness(0, 0, 0, Spacing.Sm),
                };
                foreach (var c in baseCandidates) _basedOnCombo.Items.Add(c); // plain strings (ComboBoxTheming contract)
                _basedOnCombo.SelectedItem = defaultBase != null && baseCandidates.Contains(defaultBase)
                    ? defaultBase
                    : baseCandidates.FirstOrDefault();
                ComboBoxTheming.Apply(_basedOnCombo);
                root.Children.Add(_basedOnCombo);
            }

            _validationText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
            };
            // Semantic error red (theme-independent per CLAUDE.md).
            _validationText.Foreground = Freeze(new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE5, 0x14, 0x00)));
            root.Children.Add(_validationText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, Spacing.Sm, 0, 0),
            };
            var okBtn = new Button
            {
                Content = "OK",
                MinWidth = 80,
                IsDefault = true,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                Padding = new Thickness(Spacing.Lg, Spacing.Xs, Spacing.Lg, Spacing.Xs),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            okBtn.Click += (_, _) =>
            {
                if (!Revalidate()) return;
                _accepted = true;
                Close();
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                MinWidth = 80,
                IsCancel = true,
                Padding = new Thickness(Spacing.Lg, Spacing.Xs, Spacing.Lg, Spacing.Xs),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            buttons.Children.Add(okBtn);
            buttons.Children.Add(cancelBtn);
            root.Children.Add(buttons);

            Content = root;

            Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
        }

        private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        private bool Revalidate()
        {
            var name = _nameBox.Text?.Trim() ?? string.Empty;
            string? error = null;
            if (name.Length == 0)
                error = "Enter a style name.";
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
                error = "The name contains characters that cannot be used in a file name.";

            _validationText.Text = error ?? string.Empty;
            _validationText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            return error == null;
        }

        /// <summary>New Style… — returns (accepted, name, basedOn).</summary>
        internal static (bool Accepted, string Name, string BasedOn) ShowNewStyle(
            Window owner, IReadOnlyList<string> baseCandidates, string? defaultBase)
        {
            var dialog = new StyleNameDialog(
                "AKML SQL — New Style", "Name for the new style:", string.Empty, baseCandidates, defaultBase)
            {
                Owner = owner,
            };
            dialog.ShowDialog();
            return (dialog._accepted,
                dialog._nameBox.Text?.Trim() ?? string.Empty,
                dialog._basedOnCombo?.SelectedItem as string ?? string.Empty);
        }

        /// <summary>Rename… — returns (accepted, newName).</summary>
        internal static (bool Accepted, string Name) ShowRename(Window owner, string currentName)
        {
            var dialog = new StyleNameDialog(
                "AKML SQL — Rename Style", $"New name for '{currentName}':", currentName, null, null)
            {
                Owner = owner,
            };
            dialog.ShowDialog();
            return (dialog._accepted, dialog._nameBox.Text?.Trim() ?? string.Empty);
        }
    }
}
