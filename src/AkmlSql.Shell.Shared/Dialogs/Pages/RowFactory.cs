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

        public (Border Row, CheckBox Control) AddToggle(StackPanel panel, string label, string description = "")
        {
            var cb = new CheckBox
            {
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var contentPanel = new StackPanel();
            contentPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13
            });
            if (!string.IsNullOrEmpty(description))
            {
                contentPanel.Children.Add(MakeDescription(description));
            }

            cb.Content = contentPanel;
            var row = WrapZebraRow(cb);
            panel.Children.Add(row);
            return (row, cb);
        }

        /// <summary>A zebra row with a label (+ optional description) on the left and a right-docked
        /// action button. Caller wires <c>Control.Click</c>.</summary>
        public (Border Row, Button Control) AddButton(StackPanel panel, string label, string buttonText, string description = "")
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

            var contentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            contentPanel.Children.Add(new TextBlock { Text = label, Foreground = _theme.FgPrimary, FontSize = 13 });
            if (!string.IsNullOrEmpty(description))
            {
                contentPanel.Children.Add(MakeDescription(description));
            }

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(btn, Dock.Right);
            dock.Children.Add(btn);
            dock.Children.Add(contentPanel);

            var row = WrapZebraRow(dock);
            panel.Children.Add(row);
            return (row, btn);
        }

        public (StackPanel Row, Slider Control, TextBlock ValueLabel) AddSlider(
            StackPanel panel, string label, double min, double max, double defaultValue,
            string description = "", bool largeRange = false)
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
            headerRow.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13
            });
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

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(MakeDescription(description));
            }

            panel.Children.Add(container);
            return (container, slider, valueLabel);
        }

        public (StackPanel Row, ComboBox Control) AddDropdown(StackPanel panel, string label, string[] items, string description = "")
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

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

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(MakeDescription(description));
            }

            panel.Children.Add(container);
            return (container, combo);
        }

        public (StackPanel Row, TextBox Control) AddTextInput(StackPanel panel, string label, string description = "", bool isPassword = false)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

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

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(MakeDescription(description));
            }

            panel.Children.Add(container);
            return (container, textBox);
        }

        /// <summary>
        /// A multi-line text editor row (label + optional description + a wrapping, scrollable
        /// <see cref="TextBox"/> with <c>AcceptsReturn</c>). Themed from <see cref="PageTheme"/> so it
        /// matches <see cref="AddTextInput"/>; used for list-style settings edited one entry per line.
        /// </summary>
        public (StackPanel Row, TextBox Control) AddMultilineTextInput(
            StackPanel panel, string label, string description = "", double height = 90)
        {
            var container = new StackPanel { Margin = new Thickness(_groupIndent, 0, 0, 12) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

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

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(MakeDescription(description));
            }

            panel.Children.Add(container);
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

        /// <summary>Themes the dropdown via the shared helper — the stock Aero2 face cannot be
        /// dark-themed without retemplating; see <see cref="Ui.Theme.ComboBoxTheming"/>.</summary>
        private void StyleComboBox(ComboBox combo) => Ui.Theme.ComboBoxTheming.Apply(combo, _theme);
    }
}
