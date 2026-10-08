#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Single source of truth for option-row WPF construction. Mirrors the existing
    /// per-instance Add* helpers on <c>SettingsWindow</c>, parameterized on a
    /// <see cref="PageTheme"/> so per-page builders (<see cref="IPageBuilder"/>) can
    /// own their controls without depending on <c>SettingsWindow</c> internals.
    ///
    /// Phase 2 B.1 introduces this class as additive infrastructure — pages still
    /// use the in-host helpers until B.2 begins migrating them. The duplication is
    /// intentional and temporary; the host helpers are deleted once all 15 pages
    /// have moved (B.17).
    ///
    /// Add* methods return tuples (Row, Control) so page builders can register the
    /// outer Border in the search index via <see cref="PageContext.RegisterSearch"/>.
    /// </summary>
    internal sealed class RowFactory
    {
        private readonly PageTheme _theme;

        // SQL Prompt indents the controls under a group header (~20px in the reference
        // screenshots). Set by AddGroupHeader; no explicit reset exists because SettingsWindow
        // constructs a fresh RowFactory per page build.
        private double _groupIndent;

        public RowFactory(PageTheme theme)
        {
            _theme = theme;
        }

        /// <summary>
        /// Wraps content in a row <see cref="Border"/>. Rows are flat — the SQL Prompt reference
        /// pages have no zebra striping — but the Border wrapper stays: SettingsWindow.FlashRow
        /// animates its Background and the search index targets it. Rows under a group header are
        /// indented to match the reference layout.
        /// </summary>
        public Border WrapZebraRow(UIElement content)
        {
            return new Border
            {
                Background = _theme.Transparent,
                Padding = new Thickness(12 + _groupIndent, 8, 12, 8),
                Margin = new Thickness(-12, 0, -12, 0),
                Child = content
            };
        }

        /// <summary>
        /// Section header inside a page — SQL Prompt style: a plain-weight label with a 1px rule
        /// filling the rest of the line ("Brackets ───────"). Rows added after the header are
        /// indented until the next page build resets the factory.
        /// </summary>
        public void AddGroupHeader(StackPanel panel, string text)
        {
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 16, 0, 8) };

            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = _theme.FgPrimary
            };
            DockPanel.SetDock(label, Dock.Left);
            header.Children.Add(label);

            header.Children.Add(new Border
            {
                Height = 1,
                Background = _theme.Sep,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 2, 0, 0)
            });

            panel.Children.Add(header);
            _groupIndent = 20;
        }

        /// <summary>Vertical whitespace between groups — the group header's inline rule (SQL
        /// Prompt style) replaced the old full-width separator line.</summary>
        public void AddGroupSeparator(StackPanel panel)
        {
            panel.Children.Add(new Border { Height = 0, Margin = new Thickness(0, 6, 0, 0) });
        }

        /// <summary>Secondary description line under a control — plain weight (the SQL Prompt
        /// reference uses no italics).</summary>
        private TextBlock MakeDescription(string description) => new TextBlock
        {
            Text = description,
            Foreground = _theme.FgSecondary,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        public (Border Row, CheckBox Control) AddToggle(StackPanel panel, string label, string description = "", CheckBox? parent = null)
        {
            var cb = new CheckBox
            {
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var contentPanel = new StackPanel();
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13
            };
            contentPanel.Children.Add(labelText);
            TextBlock? descriptionText = null;
            if (!string.IsNullOrEmpty(description))
            {
                descriptionText = MakeDescription(description);
                contentPanel.Children.Add(descriptionText);
            }

            cb.Content = contentPanel;
            var row = WrapZebraRow(cb);
            panel.Children.Add(row);
            ApplyParent(row, parent, labelText, descriptionText);
            return (row, cb);
        }

        /// <summary>A zebra row with a label (+ optional description) on the left and a right-docked
        /// action button. Caller wires <c>Control.Click</c>.</summary>
        public (Border Row, Button Control) AddButton(StackPanel panel, string label, string buttonText, string description = "", CheckBox? parent = null)
        {
            var btn = new Button
            {
                Content = buttonText,
                MinWidth = 130,
                Height = 28,
                FontSize = 12,
                Foreground = _theme.FgPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            // Spec 040 (T167): painted from the page theme, not the stock Aero chrome.
            ThemedButton.ApplySecondary(btn, _theme);

            var contentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var labelText = new TextBlock { Text = label, Foreground = _theme.FgPrimary, FontSize = 13 };
            contentPanel.Children.Add(labelText);
            TextBlock? descriptionText = null;
            if (!string.IsNullOrEmpty(description))
            {
                descriptionText = MakeDescription(description);
                contentPanel.Children.Add(descriptionText);
            }

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(btn, Dock.Right);
            dock.Children.Add(btn);
            dock.Children.Add(contentPanel);

            var row = WrapZebraRow(dock);
            panel.Children.Add(row);
            ApplyParent(row, parent, labelText, descriptionText);
            return (row, btn);
        }

        public (StackPanel Row, Slider Control, TextBlock ValueLabel) AddSlider(
            StackPanel panel, string label, double min, double max, double defaultValue,
            string description = "", bool largeRange = false, CheckBox? parent = null)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var valueLabel = new TextBlock
            {
                Text = defaultValue.ToString(CultureInfo.InvariantCulture),
                Foreground = _theme.FgAccent,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 60,
                TextAlignment = TextAlignment.Right
            };
            DockPanel.SetDock(valueLabel, Dock.Right);
            headerRow.Children.Add(valueLabel);
            var sliderLabel = new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13
            };
            headerRow.Children.Add(sliderLabel);
            container.Children.Add(headerRow);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = defaultValue,
                IsSnapToTickEnabled = true,
                TickFrequency = largeRange ? Math.Max(1, (max - min) / 100) : 1,
                Height = 22,
                Foreground = _theme.FgAccent
            };
            var valueLabelRef = valueLabel;
            slider.ValueChanged += (s, e) =>
            {
                valueLabelRef.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            };
            container.Children.Add(slider);

            TextBlock? sliderDescription = null;
            if (!string.IsNullOrEmpty(description))
            {
                sliderDescription = MakeDescription(description);
                container.Children.Add(sliderDescription);
            }

            panel.Children.Add(container);
            ApplyParent(container, parent, sliderLabel, sliderDescription);
            return (container, slider, valueLabel);
        }

        public (StackPanel Row, ComboBox Control) AddDropdown(StackPanel panel, string label, string[] items, string description = "", CheckBox? parent = null)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            var labelText = MakeFieldLabel(label);
            container.Children.Add(labelText);

            // Layout/focus properties only — StyleComboBox owns ALL painting (the combo's own
            // template ignores Background/BorderBrush/Padding; Foreground is set by the styler).
            var combo = new ComboBox
            {
                FontSize = 13,
                Height = 28,
                MaxWidth = 300,
                HorizontalAlignment = HorizontalAlignment.Left,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            StyleComboBox(combo);

            // Plain string items — NEVER pre-built ComboBoxItem/TextBlock content. A UIElement as
            // item content makes WPF render the closed selection box as a Rectangle+VisualBrush
            // snapshot (blurry, and unreadable over the light face) and breaks keyboard type-ahead;
            // a local Foreground on the item would beat the ItemContainerStyle triggers.
            foreach (var item in items)
                combo.Items.Add(item);
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;

            container.Children.Add(combo);

            TextBlock? descriptionText = null;
            if (!string.IsNullOrEmpty(description))
            {
                descriptionText = MakeDescription(description);
                container.Children.Add(descriptionText);
            }

            panel.Children.Add(container);
            ApplyParent(container, parent, labelText, descriptionText);
            return (container, combo);
        }

        public (StackPanel Row, TextBox Control) AddTextInput(StackPanel panel, string label, string description = "", bool isPassword = false, CheckBox? parent = null)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            var labelText = MakeFieldLabel(label);
            container.Children.Add(labelText);

            var textBox = new TextBox
            {
                Background = _theme.Input,
                Foreground = _theme.FgPrimary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = _theme.Caret,
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 300
            };
            if (isPassword) textBox.Tag = "password";

            container.Children.Add(textBox);

            TextBlock? descriptionText = null;
            if (!string.IsNullOrEmpty(description))
            {
                descriptionText = MakeDescription(description);
                container.Children.Add(descriptionText);
            }

            panel.Children.Add(container);
            ApplyParent(container, parent, labelText, descriptionText);
            return (container, textBox);
        }

        /// <summary>
        /// A multi-line text editor row (label + optional description + a wrapping, scrollable
        /// <see cref="TextBox"/> with <c>AcceptsReturn</c>). Themed from <see cref="PageTheme"/> so it
        /// matches <see cref="AddTextInput"/>; used for list-style settings edited one entry per line.
        /// </summary>
        public (StackPanel Row, TextBox Control) AddMultilineTextInput(
            StackPanel panel, string label, string description = "", double height = 90, CheckBox? parent = null)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            var labelText = MakeFieldLabel(label);
            container.Children.Add(labelText);

            var textBox = new TextBox
            {
                Background = _theme.Input,
                Foreground = _theme.FgPrimary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = _theme.Caret,
                FontSize = 13,
                MinHeight = height,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 300,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            container.Children.Add(textBox);

            TextBlock? descriptionText = null;
            if (!string.IsNullOrEmpty(description))
            {
                descriptionText = MakeDescription(description);
                container.Children.Add(descriptionText);
            }

            panel.Children.Add(container);
            ApplyParent(container, parent, labelText, descriptionText);
            return (container, textBox);
        }

        public (StackPanel Row, TextBox Control) AddReadOnlyField(StackPanel panel, string label, string value)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 8) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var textBox = new TextBox
            {
                Text = value,
                Background = _theme.InputReadOnly,
                Foreground = _theme.FgSecondary,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                IsReadOnly = true,
                Height = 26,
                Padding = new Thickness(6, 3, 6, 3),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 300
            };

            container.Children.Add(textBox);
            panel.Children.Add(container);
            return (container, textBox);
        }

        public DockPanel AddInfoRow(StackPanel panel, string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(_groupIndent, 2, 0, 6) };
            row.Children.Add(new TextBlock
            {
                Text = label + ":",
                Foreground = _theme.FgSecondary,
                FontSize = 12,
                MinWidth = 120,
                Margin = new Thickness(0, 0, 8, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = _theme.FgPrimary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(row);
            return row;
        }

        /// <summary>The label above a dropdown, text or number field.</summary>
        private TextBlock MakeFieldLabel(string label) => new TextBlock
        {
            Text = label,
            Foreground = _theme.FgPrimary,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4)
        };

        // ─── Spec 040 (OPT-05, research R5): child options ──────────────────────────────────────

        /// <summary>Indent of a child option under its parent check box (SQL Prompt's layout).</summary>
        internal const double ChildIndent = 20;

        /// <summary>
        /// Makes <paramref name="row"/> a child of <paramref name="parent"/>: indented
        /// <see cref="ChildIndent"/> px further, enabled only while the parent is checked, its labels
        /// greyed (<see cref="PageTheme.TextDisabled"/>) while it is off, and a tooltip naming the
        /// parent. It follows the parent's Checked/Unchecked events, so a page's Load, Restore defaults
        /// and Import all keep it right. No-op without a parent.
        /// </summary>
        private void ApplyParent(FrameworkElement row, CheckBox? parent, params TextBlock?[] labels)
        {
            if (parent == null) return;

            if (row is Border border)
            {
                var p = border.Padding;
                border.Padding = new Thickness(p.Left + ChildIndent, p.Top, p.Right, p.Bottom);
            }
            else
            {
                var m = row.Margin;
                row.Margin = new Thickness(m.Left + ChildIndent, m.Top, m.Right, m.Bottom);
            }

            row.ToolTip = "Takes effect when \"" + ParentLabel(parent) + "\" is on";
            ToolTipService.SetShowOnDisabled(row, true);

            // Each label keeps its own colour while on (a description stays secondary); all grey while off.
            var onBrushes = Array.ConvertAll(labels, l => l?.Foreground);
            void Refresh()
            {
                var on = parent.IsChecked == true;
                row.IsEnabled = on;
                for (var i = 0; i < labels.Length; i++)
                {
                    var text = labels[i];
                    if (text != null) text.Foreground = on ? onBrushes[i] : _theme.TextDisabled;
                }
            }

            parent.Checked += (_, __) => Refresh();
            parent.Unchecked += (_, __) => Refresh();
            parent.Indeterminate += (_, __) => Refresh();
            Refresh();
        }

        /// <summary>The visible text of a check box built by <see cref="AddToggle"/> (its first line).</summary>
        internal static string ParentLabel(CheckBox parent)
        {
            if (parent.Content is Panel content)
            {
                foreach (var child in content.Children)
                    if (child is TextBlock text) return text.Text;
            }
            return parent.Content?.ToString() ?? string.Empty;
        }

        // ─── Spec 040 (OPT-06, research R6): number fields ──────────────────────────────────────

        /// <summary>The range, step and last valid value of a number box (kept in its Tag).</summary>
        internal sealed class NumberState
        {
            public int Min { get; set; }
            public int Max { get; set; }
            public int Step { get; set; }
            public int Value { get; set; }
        }

        /// <summary>
        /// A number field for a wide range (it replaces a slider): label, a text box with ▲/▼ steppers
        /// and the unit after it. Typing anything that isn't a whole number in range turns the border
        /// red and keeps the last valid value; leaving the box (or Enter) clamps a number into the range
        /// and puts the last valid value back for anything else. Pages read and write it with
        /// <see cref="GetNumber"/> / <see cref="SetNumber"/>.
        /// </summary>
        public (FrameworkElement Row, TextBox Box) AddNumber(
            StackPanel panel, string label, int min, int max, int step, string unit,
            string description = "", CheckBox? parent = null)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };
            var labelText = MakeFieldLabel(label);
            container.Children.Add(labelText);

            var box = new TextBox
            {
                Background = _theme.Input,
                Foreground = _theme.FgPrimary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = _theme.Caret,
                FontSize = 13,
                Height = 28,
                Width = 96,
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                Tag = new NumberState { Min = min, Max = max, Step = Math.Max(1, step), Value = min },
                Text = min.ToString(CultureInfo.InvariantCulture)
            };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            box.TextChanged += (_, __) => ValidateNumber(box);
            box.LostKeyboardFocus += (_, __) => CommitNumber(box);
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) { CommitNumber(box); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Up) { StepNumber(box, +1); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Down) { StepNumber(box, -1); e.Handled = true; }
            };

            var steppers = new StackPanel { Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            steppers.Children.Add(MakeStepper("▲", "Increase " + label, () => StepNumber(box, +1)));
            steppers.Children.Add(MakeStepper("▼", "Decrease " + label, () => StepNumber(box, -1)));

            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(box);
            line.Children.Add(steppers);
            if (!string.IsNullOrEmpty(unit))
            {
                line.Children.Add(new TextBlock
                {
                    Text = unit,
                    Foreground = _theme.FgSecondary,
                    FontSize = 12,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            container.Children.Add(line);

            TextBlock? descriptionText = null;
            if (!string.IsNullOrEmpty(description))
            {
                descriptionText = MakeDescription(description);
                container.Children.Add(descriptionText);
            }

            panel.Children.Add(container);
            ApplyParent(container, parent, labelText, descriptionText);
            return (container, box);
        }

        /// <summary>The ▲/▼ buttons of a number field, in the box's tab order.</summary>
        internal static (RepeatButton Up, RepeatButton Down) SteppersOf(TextBox box)
        {
            var steppers = (StackPanel)((StackPanel)box.Parent).Children[1];
            return ((RepeatButton)steppers.Children[0], (RepeatButton)steppers.Children[1]);
        }

        /// <summary>A small ▲/▼ button that repeats while held, painted from the page theme.</summary>
        private RepeatButton MakeStepper(string glyph, string name, Action onClick)
        {
            var button = new RepeatButton
            {
                Content = glyph,
                FontSize = 7,
                Width = 18,
                Height = 13,
                Padding = new Thickness(0),
                Focusable = false,
                Background = _theme.Button,
                Foreground = _theme.FgSecondary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                Template = StepperTemplate(),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, __) => onClick();
            return button;
        }

        private ControlTemplate StepperTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(RepeatButton)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, _theme.ButtonHover, "bd"));
            template.Triggers.Add(hover);
            return template;
        }

        private static NumberState? StateOf(TextBox box) => box.Tag as NumberState;

        private static bool TryParseNumber(string? text, out long value) =>
            long.TryParse((text ?? string.Empty).Trim().Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <summary>Red border while the text isn't a whole number in range; a valid entry becomes the value.</summary>
        internal void ValidateNumber(TextBox box)
        {
            var state = StateOf(box);
            if (state == null) return;
            if (TryParseNumber(box.Text, out var v) && v >= state.Min && v <= state.Max)
            {
                state.Value = (int)v;
                box.BorderBrush = _theme.ComboBorder;
            }
            else
            {
                box.BorderBrush = InvalidBorder;
            }
        }

        /// <summary>Leaving the box: a number is clamped into range; anything else goes back to the last valid value.</summary>
        internal void CommitNumber(TextBox box)
        {
            var state = StateOf(box);
            if (state == null) return;
            if (TryParseNumber(box.Text, out var v))
                state.Value = (int)Math.Max(state.Min, Math.Min(state.Max, v));
            SetNumber(box, state.Value);
        }

        internal void StepNumber(TextBox box, int direction)
        {
            var state = StateOf(box);
            if (state == null || !box.IsEnabled) return;
            CommitNumber(box);
            SetNumber(box, (int)Math.Max(state.Min, Math.Min(state.Max, (long)state.Value + (long)direction * state.Step)));
        }

        /// <summary>Shows <paramref name="value"/> (clamped into the box's range) and makes it the value.</summary>
        internal static void SetNumber(TextBox box, int value)
        {
            var state = StateOf(box);
            if (state == null) { box.Text = value.ToString(CultureInfo.InvariantCulture); return; }
            state.Value = Math.Max(state.Min, Math.Min(state.Max, value));
            box.Text = state.Value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The box's last valid value — what a page saves. Never throws on half-typed text.</summary>
        internal static int GetNumber(TextBox box)
        {
            var state = StateOf(box);
            if (state != null) return state.Value;
            return TryParseNumber(box.Text, out var v) ? (int)v : 0;
        }

        /// <summary>The border of an invalid number: semantic red, the same in every theme.</summary>
        private static readonly SolidColorBrush InvalidBorder = PageTheme.Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)));

        /// <summary>Themes the dropdown via the shared helper — the stock Aero2 face cannot be
        /// dark-themed without retemplating; see <see cref="Ui.Theme.ComboBoxTheming"/>.</summary>
        private void StyleComboBox(ComboBox combo) => Ui.Theme.ComboBoxTheming.Apply(combo, _theme);
    }
}
