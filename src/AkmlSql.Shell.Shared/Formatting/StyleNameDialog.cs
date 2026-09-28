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
    /// (name + based-on picker), Rename… (name pre-filled), Copy… and Import… of a taken name.
    /// Follows the ShowRuleEditor accepted-flag shape; callers set <c>Owner</c> to the styles
    /// window per the nested-modal rule documented on <see cref="ImportSummaryDialog"/> (WPF only
    /// disables and centres over the actual Owner).
    /// <para>
    /// Spec 040 (T101, STY-07): the name is checked as it is typed — not empty, at most
    /// <see cref="MaxNameLength"/> characters, no characters a file name cannot hold, and not the
    /// name of another style (case-insensitive, trimmed; Rename accepts the style's own name).
    /// OK stays disabled until it is valid.
    /// </para>
    /// </summary>
    internal sealed class StyleNameDialog : ThemeAwareWindow
    {
        internal const int MaxNameLength = 80;

        private readonly TextBox _nameBox;
        private readonly ComboBox? _basedOnCombo;
        private readonly TextBlock _validationText;
        private readonly Button _okBtn;
        private readonly HashSet<string> _existingNames;
        private readonly string? _currentName;
        private bool _accepted;

        private StyleNameDialog(string title, string prompt, string initialName, IReadOnlyList<string>? baseCandidates, string? defaultBase,
            IReadOnlyCollection<string>? existingNames = null, string? currentName = null)
        {
            _existingNames = new HashSet<string>((existingNames ?? new string[0]).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
            _currentName = currentName?.Trim();

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
            var okBtn = _okBtn = new Button
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

            _nameBox.TextChanged += (_, _) => Revalidate();
            Revalidate();

            Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
        }

        /// <summary>Test seam: builds the dialog without showing it.</summary>
        internal static StyleNameDialog ForTests(string initialName, IReadOnlyCollection<string>? existingNames, string? currentName) =>
            new StyleNameDialog("AKML SQL — Style name", "Name:", initialName, null, null, existingNames, currentName);

        /// <summary>Test seam: the name box's text.</summary>
        internal string NameText
        {
            get => _nameBox.Text;
            set => _nameBox.Text = value;
        }

        /// <summary>Test seam: what is wrong with the name, or null when it is valid.</summary>
        internal string? ValidationMessage => _validationText.Visibility == Visibility.Visible ? _validationText.Text : null;

        /// <summary>Test seam: whether OK is enabled.</summary>
        internal bool CanAccept => _okBtn.IsEnabled;

        private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        private bool Revalidate()
        {
            var error = Validate(_nameBox.Text, _existingNames, _currentName);
            _validationText.Text = error ?? string.Empty;
            _validationText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            _okBtn.IsEnabled = error == null;
            return error == null;
        }

        /// <summary>What is wrong with <paramref name="rawName"/> as a style name, or null when it is valid.</summary>
        internal static string? Validate(string? rawName, ICollection<string> existingNames, string? currentName)
        {
            var name = rawName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                return "Enter a style name.";
            if (name.Length > MaxNameLength)
                return $"Use {MaxNameLength} characters or fewer.";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
                return "The name contains characters that cannot be used in a file name.";
            var ownName = currentName != null && string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase);
            if (!ownName && existingNames.Contains(name))
                return $"A style named '{name}' already exists.";
            return null;
        }

        /// <summary>New Style… — returns (accepted, name, basedOn).</summary>
        internal static (bool Accepted, string Name, string BasedOn) ShowNewStyle(
            Window owner, IReadOnlyList<string> baseCandidates, string? defaultBase, IReadOnlyCollection<string> existingNames)
        {
            var dialog = new StyleNameDialog(
                "AKML SQL — New Style", "Name for the new style:", string.Empty, baseCandidates, defaultBase, existingNames)
            {
                Owner = owner,
            };
            dialog.ShowDialog();
            return (dialog._accepted,
                dialog._nameBox.Text?.Trim() ?? string.Empty,
                dialog._basedOnCombo?.SelectedItem as string ?? string.Empty);
        }

        /// <summary>
        /// Spec 040 (T081) — Import… of a style whose name is taken: asks for another name,
        /// pre-filled with <paramref name="suggested"/>. A taken name is refused, so an import never
        /// overwrites a built-in or one of the user's styles. Returns null when cancelled.
        /// </summary>
        internal static string? ShowImportName(Window owner, string takenName, string suggested, IReadOnlyCollection<string> existingNames)
        {
            var dialog = new StyleNameDialog(
                "AKML SQL — Import Style",
                $"A style named '{takenName}' already exists. Import this one as:",
                suggested, null, null, existingNames)
            {
                Owner = owner,
            };
            dialog.ShowDialog();
            return dialog._accepted ? dialog._nameBox.Text?.Trim() : null;
        }

        /// <summary>
        /// Spec 040 (T101) — Copy…: the name for the copy, pre-filled with <paramref name="suggested"/>.
        /// Returns null when cancelled.
        /// </summary>
        internal static string? ShowCopyStyle(Window owner, string sourceName, IReadOnlyCollection<string> existingNames, string suggested)
        {
            var dialog = new StyleNameDialog(
                "AKML SQL — Copy Style", $"Name for the copy of '{sourceName}':", suggested, null, null, existingNames)
            {
                Owner = owner,
            };
            dialog.ShowDialog();
            return dialog._accepted ? dialog._nameBox.Text?.Trim() : null;
        }

        /// <summary>Rename… — returns (accepted, newName).</summary>
        internal static (bool Accepted, string Name) ShowRename(Window owner, string currentName, IReadOnlyCollection<string> existingNames)
        {
            var dialog = new StyleNameDialog(
                "AKML SQL — Rename Style", $"New name for '{currentName}':", currentName, null, null, existingNames, currentName)
            {
                Owner = owner,
            };
            dialog.ShowDialog();
            return (dialog._accepted, dialog._nameBox.Text?.Trim() ?? string.Empty);
        }
    }
}
