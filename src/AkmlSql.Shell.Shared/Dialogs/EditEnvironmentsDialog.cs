#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.Tabs;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs
{
    /// <summary>What the Edit environments dialog returns on Save.</summary>
    internal sealed class EnvironmentEditResult
    {
        public EnvironmentEditResult(IReadOnlyList<TabEnvironment> environments, IReadOnlyDictionary<string, string> renames, bool gradientColors)
        {
            Environments = environments;
            Renames = renames;
            GradientColors = gradientColors;
        }

        /// <summary>The environments, in the dialog's order (names trimmed, colours <c>#RRGGBB</c>).</summary>
        public IReadOnlyList<TabEnvironment> Environments { get; }

        /// <summary>Each environment the dialog was given that still exists: its old name (case-insensitive
        /// key) → its name now, so the rules that use it can follow a rename.</summary>
        public IReadOnlyDictionary<string, string> Renames { get; }

        /// <summary><c>Tabs.GradientColors</c>.</summary>
        public bool GradientColors { get; }
    }

    /// <summary>
    /// Spec 040 (T166, OPT-08, FR-054, research R8) — "AKML SQL – Edit environments", opened from
    /// Options › Queries › Color. A list of environments (a colour swatch and an editable name);
    /// the swatch opens a grid of the eight <c>ThemeTokens.TabColor*</c> colours plus
    /// <b>Custom…</b> (the Windows colour dialog), so no colour code is ever typed. Also <b>Use
    /// gradient colors</b> and <b>Restore default environments</b>. Save validates through
    /// <see cref="EnvironmentValidator"/> (names unique and 1–40 characters, <c>#RRGGBB</c>);
    /// deleting an environment that rules use is refused, naming the rules. Follows the current
    /// theme (<see cref="ThemeAwareWindow"/>).
    /// </summary>
    internal sealed class EditEnvironmentsDialog : ThemeAwareWindow
    {
        internal static readonly string WindowTitle = WindowTitles.For("Edit environments");

        // The swatch grid: the TabColor tokens in palette order. Light and Dark carry the same
        // values (High Contrast maps them all to a system colour), so the Light palette is the source.
        private static readonly (string Name, string Token)[] PaletteTokens =
        {
            ("Red", ThemeTokens.TabColorRed),
            ("Amber", ThemeTokens.TabColorAmber),
            ("Green", ThemeTokens.TabColorGreen),
            ("Blue", ThemeTokens.TabColorBlue),
            ("Teal", ThemeTokens.TabColorTeal),
            ("Purple", ThemeTokens.TabColorPurple),
            ("Pink", ThemeTokens.TabColorPink),
            ("Gray", ThemeTokens.TabColorGray),
        };

        private static readonly ControlTemplate SwatchTemplate = BuildSwatchTemplate();

        private readonly IReadOnlyList<ColoringRule> _rules;
        private readonly HashSet<string> _givenNames;
        private readonly List<EnvironmentRow> _rows = new List<EnvironmentRow>();
        private readonly StackPanel _rowsPanel;
        private readonly CheckBox _gradient;
        private readonly TextBlock _status;
        private readonly Popup _palette;
        private EnvironmentRow? _paletteTarget;

        internal EditEnvironmentsDialog(IReadOnlyList<TabEnvironment> environments, IReadOnlyList<ColoringRule> rules, bool gradientColors)
        {
            _rules = rules ?? Array.Empty<ColoringRule>();
            _givenNames = new HashSet<string>(
                (environments ?? Array.Empty<TabEnvironment>()).Select(e => (e.Name ?? string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);

            Title = WindowTitle;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = Typography.UiFont;
            FontSize = Typography.Body;

            var root = new DockPanel { Margin = new Thickness(Spacing.Lg), LastChildFill = true };

            var intro = Text("Each rule picks an environment; the environment gives its query tabs their color.", ThemeTokens.TextSecondary);
            intro.TextWrapping = TextWrapping.Wrap;
            intro.Margin = new Thickness(0, 0, 0, Spacing.Md);
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            var header = new Grid { Margin = new Thickness(0, 0, 0, Spacing.Xs) };
            AddColumns(header);
            var colorHeader = Text("Color", ThemeTokens.TextSecondary);
            var nameHeader = Text("Name", ThemeTokens.TextSecondary);
            Grid.SetColumn(nameHeader, 1);
            header.Children.Add(colorHeader);
            header.Children.Add(nameHeader);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Bottom-up: Save/Cancel, status line, options, Add.
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, Spacing.Md, 0, 0),
            };
            SaveButton = new Button
            {
                Content = "Save",
                MinWidth = 80,
                IsDefault = true,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplyPrimary(SaveButton);
            SaveButton.Click += (_, __) =>
            {
                Result = TryBuildResult();
                if (Result != null)
                {
                    DialogResult = true;
                }
            };
            var cancel = new Button
            {
                Content = "Cancel",
                MinWidth = 80,
                IsCancel = true,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplySecondary(cancel);
            footer.Children.Add(SaveButton);
            footer.Children.Add(cancel);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            _status = Text(string.Empty, ThemeTokens.TextSecondary);
            _status.TextWrapping = TextWrapping.Wrap;
            _status.Visibility = Visibility.Collapsed;
            _status.Margin = new Thickness(0, Spacing.Sm, 0, 0);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            var options = new StackPanel { Margin = new Thickness(0, Spacing.Md, 0, 0) };
            _gradient = new CheckBox
            {
                Content = "Use gradient colors",
                IsChecked = gradientColors,
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            _gradient.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            options.Children.Add(_gradient);
            var restore = SecondaryButton("Restore default environments", "Restore the default environments");
            restore.HorizontalAlignment = HorizontalAlignment.Left;
            restore.Click += (_, __) => RestoreDefaults();
            options.Children.Add(restore);
            DockPanel.SetDock(options, Dock.Bottom);
            root.Children.Add(options);

            var add = SecondaryButton("+ Add environment", "Add an environment");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, Spacing.Sm, 0, 0);
            add.Click += (_, __) => AddEnvironment();
            DockPanel.SetDock(add, Dock.Bottom);
            root.Children.Add(add);

            _rowsPanel = new StackPanel();
            root.Children.Add(new ScrollViewer
            {
                Content = _rowsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 300,
            });

            _palette = BuildPalette();

            Content = root;

            foreach (var environment in environments ?? Array.Empty<TabEnvironment>())
            {
                if (environment == null) continue;
                AddRow(environment.Name, environment.Color, environment.Name);
            }

            Loaded += (_, __) => _rows.FirstOrDefault()?.NameBox.Focus();
        }

        /// <summary>The Save result; null until Save succeeds.</summary>
        internal EnvironmentEditResult? Result { get; private set; }

        internal Button SaveButton { get; }

        /// <summary>The rows, in order (for tests).</summary>
        internal IReadOnlyList<EnvironmentRow> Rows => _rows;

        /// <summary>The status line's text, or null when hidden (for tests).</summary>
        internal string? StatusText => _status.Visibility == Visibility.Visible ? _status.Text : null;

        internal bool GradientColors
        {
            get => _gradient.IsChecked == true;
            set => _gradient.IsChecked = value;
        }

        /// <summary>
        /// Shows the dialog over <paramref name="owner"/> (the Options window) on the given working
        /// copy; <paramref name="rules"/> are the rules as currently edited, for the delete check.
        /// Returns the edit, or null for Cancel.
        /// </summary>
        internal static EnvironmentEditResult? Edit(Window? owner, IReadOnlyList<TabEnvironment> environments,
            IReadOnlyList<ColoringRule> rules, bool gradientColors)
        {
            var dialog = new EditEnvironmentsDialog(environments, rules, gradientColors);
            if (owner != null)
            {
                try { dialog.Owner = owner; }
                catch (InvalidOperationException) { /* owner not shown yet: ThemeAwareWindow parents to SSMS */ }
            }
            return dialog.ShowDialog() == true ? dialog.Result : null;
        }

        /// <summary>The eight swatch colours as <c>(name, #RRGGBB)</c>.</summary>
        internal static IReadOnlyList<(string Name, string Hex)> PaletteColors =>
            PaletteTokens.Select(p => (p.Name, ToHex(ThemePalette.Light.Brushes[p.Token].Color))).ToList();

        // ─── Commands ───────────────────────────────────────────────────────────────────────────

        /// <summary>Adds a new environment (a free "New environment" name, the next unused swatch colour)
        /// and puts the caret in its name.</summary>
        internal EnvironmentRow AddEnvironment()
        {
            var used = new HashSet<string>(_rows.Select(r => r.Color), StringComparer.OrdinalIgnoreCase);
            var color = PaletteColors.Select(p => p.Hex).FirstOrDefault(h => !used.Contains(h)) ?? PaletteColors[0].Hex;

            var names = new HashSet<string>(_rows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            var name = "New environment";
            for (var n = 2; names.Contains(name); n++)
                name = "New environment " + n.ToString(CultureInfo.InvariantCulture);

            var row = AddRow(name, color, originalName: null);
            ClearStatus();
            row.NameBox.Focus();
            row.NameBox.SelectAll();
            return row;
        }

        /// <summary>
        /// Deletes <paramref name="row"/>, unless rules use the environment it was given as: then
        /// nothing is deleted and the status line names those rules. Returns whether it was deleted.
        /// </summary>
        internal bool TryDelete(EnvironmentRow row)
        {
            if (row.OriginalName != null &&
                !EnvironmentValidator.CanDelete(row.OriginalName, _rules, out var usedBy))
            {
                var shown = row.Name.Length > 0 ? row.Name : row.OriginalName;
                SetStatus(EnvironmentValidator.DeleteRefusedMessage(shown, usedBy), error: true);
                return false;
            }

            _rows.Remove(row);
            _rowsPanel.Children.Remove(row.Element);
            ClearStatus();
            return true;
        }

        /// <summary>
        /// Back to PRODUCTION, STAGING, DEV and AZURE with their colours. Environments that rules
        /// use and that aren't one of the four are kept (they can't be deleted), and the status line
        /// says so.
        /// </summary>
        internal void RestoreDefaults()
        {
            var defaults = TabEnvironment.CreateDefaults();
            var kept = _rows.Where(r =>
                    r.OriginalName != null &&
                    !EnvironmentValidator.CanDelete(r.OriginalName, _rules, out _) &&
                    !defaults.Any(d => string.Equals(d.Name, r.OriginalName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _rows.Clear();
            _rowsPanel.Children.Clear();
            foreach (var environment in defaults)
            {
                var given = _givenNames.FirstOrDefault(n => string.Equals(n, environment.Name, StringComparison.OrdinalIgnoreCase));
                AddRow(environment.Name, environment.Color, given);
            }
            foreach (var row in kept)
            {
                _rows.Add(row);
                _rowsPanel.Children.Add(row.Element);
            }

            if (kept.Count == 0)
                ClearStatus();
            else
                SetStatus("Kept " + string.Join(", ", kept.Select(r => "'" + r.Name + "'")) +
                          " because rules use " + (kept.Count == 1 ? "it." : "them."), error: false);
        }

        /// <summary>Sets <paramref name="row"/>'s colour (the swatch grid's click).</summary>
        internal void PickColor(EnvironmentRow row, string hex)
        {
            row.Color = hex;
            _palette.IsOpen = false;
        }

        /// <summary>
        /// Validates the rows; on success returns the edit, otherwise shows every problem in the status
        /// line and returns null.
        /// </summary>
        internal EnvironmentEditResult? TryBuildResult()
        {
            var environments = _rows.Select(r => new TabEnvironment { Name = r.Name, Color = r.Color }).ToList();
            var errors = EnvironmentValidator.Validate(environments);
            if (errors.Count > 0)
            {
                SetStatus(string.Join(System.Environment.NewLine, errors), error: true);
                return null;
            }

            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                if (row.OriginalName != null)
                    renames[row.OriginalName.Trim()] = row.Name;
            }
            ClearStatus();
            return new EnvironmentEditResult(environments, renames, _gradient.IsChecked == true);
        }

        // ─── Rows ───────────────────────────────────────────────────────────────────────────────

        private EnvironmentRow AddRow(string? name, string? color, string? originalName)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, Spacing.Xs) };
            AddColumns(grid);

            var swatch = new Button
            {
                Height = 26,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                Template = SwatchTemplate,
                Content = "▾",
                FontSize = Typography.Small,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            swatch.SetResourceReference(BorderBrushProperty, ThemeTokens.BorderStrong);
            grid.Children.Add(swatch);

            var nameBox = new TextBox
            {
                Text = (name ?? string.Empty).Trim(),
                MaxLength = EnvironmentValidator.MaxNameLength,
                Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            nameBox.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceInput);
            nameBox.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            nameBox.SetResourceReference(BorderBrushProperty, ThemeTokens.BorderDefault);
            nameBox.SetResourceReference(TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            AutomationProperties.SetName(nameBox, "Environment name");
            Grid.SetColumn(nameBox, 1);
            grid.Children.Add(nameBox);

            var delete = SecondaryButton("✕", "Delete environment");
            delete.MinWidth = 28;
            delete.Padding = new Thickness(Spacing.Sm, 0, Spacing.Sm, 0);
            delete.Margin = new Thickness(Spacing.Sm, 0, 0, 0);
            delete.ToolTip = "Delete";
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);

            var row = new EnvironmentRow(originalName == null ? null : originalName.Trim(), nameBox, swatch, grid);
            row.Color = EnvironmentValidator.IsValidColor(color) ? color! : PaletteColors[PaletteColors.Count - 1].Hex;
            nameBox.TextChanged += (_, __) => AutomationProperties.SetName(swatch, "Color of " + row.Name);
            AutomationProperties.SetName(swatch, "Color of " + row.Name);

            swatch.Click += (_, __) => OpenPalette(row);
            delete.Click += (_, __) => TryDelete(row);

            _rows.Add(row);
            _rowsPanel.Children.Add(grid);
            return row;
        }

        private static void AddColumns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        // ─── Colour picker ──────────────────────────────────────────────────────────────────────

        private void OpenPalette(EnvironmentRow row)
        {
            _paletteTarget = row;
            _palette.PlacementTarget = row.ColorButton;
            _palette.IsOpen = true;
        }

        private Popup BuildPalette()
        {
            var card = new Border
            {
                Padding = new Thickness(Spacing.Sm),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            };
            // The popup is its own visual tree: give it the theme resources directly.
            ThemeRegistry.Instance.AttachTo(card);
            card.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);

            var stack = new StackPanel();
            var swatches = new UniformGrid { Columns = 4, Rows = 2 };
            foreach (var (name, hex) in PaletteColors)
            {
                var button = new Button
                {
                    Width = 28,
                    Height = 22,
                    Margin = new Thickness(2),
                    Template = SwatchTemplate,
                    Background = HexBrush.Get(hex),
                    BorderThickness = new Thickness(1),
                    ToolTip = name + " (" + hex + ")",
                    Cursor = Cursors.Hand,
                    FocusVisualStyle = FocusVisualStyles.HighStakes,
                };
                button.SetResourceReference(BorderBrushProperty, ThemeTokens.BorderStrong);
                AutomationProperties.SetName(button, name);
                var pick = hex;
                button.Click += (_, __) =>
                {
                    if (_paletteTarget != null) PickColor(_paletteTarget, pick);
                };
                swatches.Children.Add(button);
            }
            stack.Children.Add(swatches);

            var custom = SecondaryButton("Custom…", "Choose a custom color");
            custom.Margin = new Thickness(2, Spacing.Sm, 2, 0);
            custom.Click += (_, __) =>
            {
                if (_paletteTarget != null) PickCustomColor(_paletteTarget);
            };
            stack.Children.Add(custom);
            card.Child = stack;

            return new Popup
            {
                Child = card,
                StaysOpen = false,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
            };
        }

        /// <summary>The Windows colour dialog, starting from the row's colour; stores <c>#RRGGBB</c>.</summary>
        private void PickCustomColor(EnvironmentRow row)
        {
            _palette.IsOpen = false;
            using (var dialog = new System.Windows.Forms.ColorDialog { AnyColor = true, FullOpen = true })
            {
                if (HexBrush.TryParse(row.Color, out var current))
                    dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    row.Color = ToHex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────────────────────────

        private void SetStatus(string text, bool error)
        {
            _status.Text = text;
            // Error red is the documented semantic exception; information follows the theme.
            _status.SetResourceReference(TextBlock.ForegroundProperty, error ? ThemeTokens.StatusDanger : ThemeTokens.TextSecondary);
            _status.Visibility = Visibility.Visible;
        }

        private void ClearStatus()
        {
            _status.Text = string.Empty;
            _status.Visibility = Visibility.Collapsed;
        }

        private static TextBlock Text(string text, string foregroundToken)
        {
            var block = new TextBlock { Text = text };
            block.SetResourceReference(TextBlock.ForegroundProperty, foregroundToken);
            return block;
        }

        private static Button SecondaryButton(string content, string automationName)
        {
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplySecondary(button);
            AutomationProperties.SetName(button, automationName);
            return button;
        }

        private static string ToHex(Color color)
            => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);

        /// <summary>
        /// A flat colour chip: the button's Background as the face, a border that turns
        /// <c>Border.Focus</c> under the mouse or keyboard focus. Sealed and shared.
        /// </summary>
        private static ControlTemplate BuildSwatchTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.BorderFocus), "Bd"));
            template.Triggers.Add(hover);
            var focused = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.BorderFocus), "Bd"));
            template.Triggers.Add(focused);
            template.Seal();
            return template;
        }

        /// <summary>One environment in the list.</summary>
        internal sealed class EnvironmentRow
        {
            private string _color = string.Empty;

            internal EnvironmentRow(string? originalName, TextBox nameBox, Button colorButton, FrameworkElement element)
            {
                OriginalName = originalName;
                NameBox = nameBox;
                ColorButton = colorButton;
                Element = element;
            }

            /// <summary>The name the dialog was given it under; null for an environment added here.</summary>
            internal string? OriginalName { get; }

            internal TextBox NameBox { get; }
            internal Button ColorButton { get; }
            internal FrameworkElement Element { get; }

            internal string Name
            {
                get => (NameBox.Text ?? string.Empty).Trim();
                set => NameBox.Text = value;
            }

            /// <summary><c>#RRGGBB</c>; repaints the swatch (and its ▾ in black or white to suit).</summary>
            internal string Color
            {
                get => _color;
                set
                {
                    _color = value ?? string.Empty;
                    ColorButton.Background = HexBrush.Get(_color);
                    ColorButton.Foreground = HexBrush.ContrastFor(_color);
                    ColorButton.ToolTip = "Color " + _color + " — click to change";
                }
            }
        }
    }
}
