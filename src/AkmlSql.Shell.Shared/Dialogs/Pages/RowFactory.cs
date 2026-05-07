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
        private int _zebraIndex;

        public RowFactory(PageTheme theme)
        {
            _theme = theme;
        }

        /// <summary>Resets the zebra-striping counter. Call at the start of each page build.</summary>
        public void ResetZebra() => _zebraIndex = 0;

        /// <summary>
        /// Wraps content in a zebra-striped <see cref="Border"/>. Alternates between
        /// transparent and <see cref="PageTheme.InputReadOnly"/>.
        /// </summary>
        public Border WrapZebraRow(UIElement content)
        {
            var bg = (_zebraIndex++ % 2 == 0) ? _theme.InputReadOnly : _theme.Transparent;
            return new Border
            {
                Background = bg,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(-12, 0, -12, 0),
                Child = content
            };
        }

        /// <summary>Section header inside a page (bold, foreground primary).</summary>
        public void AddGroupHeader(StackPanel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = _theme.FgPrimary,
                Margin = new Thickness(0, 8, 0, 6)
            });
        }

        public void AddGroupSeparator(StackPanel panel)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = _theme.Sep,
                Margin = new Thickness(0, 14, 0, 10)
            });
        }

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
                contentPanel.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            cb.Content = contentPanel;
            var row = WrapZebraRow(cb);
            panel.Children.Add(row);
            return (row, cb);
        }

        public (StackPanel Row, Slider Control, TextBlock ValueLabel) AddSlider(
            StackPanel panel, string label, double min, double max, double defaultValue,
            string description = "", bool largeRange = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

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
                container.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            panel.Children.Add(container);
            return (container, slider, valueLabel);
        }

        public (StackPanel Row, ComboBox Control) AddDropdown(StackPanel panel, string label, string[] items, string description = "")
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var combo = new ComboBox
            {
                Background = _theme.Input,
                Foreground = _theme.FgPrimary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 300,
                HorizontalAlignment = HorizontalAlignment.Left,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            StyleComboBox(combo);

            foreach (var item in items)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = new TextBlock { Text = item },
                    Foreground = _theme.FgPrimary
                });
            }
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;

            container.Children.Add(combo);

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            panel.Children.Add(container);
            return (container, combo);
        }

        public (StackPanel Row, TextBox Control) AddTextInput(StackPanel panel, string label, string description = "", bool isPassword = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

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
                container.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            panel.Children.Add(container);
            return (container, textBox);
        }

        public (StackPanel Row, TextBox Control) AddReadOnlyField(StackPanel panel, string label, string value)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

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
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
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

        // ─── ComboBox theming (mirrors SettingsWindow.StyleComboBox) ─────────

        private void StyleComboBox(ComboBox combo)
        {
            combo.Background = _theme.Input;
            combo.Foreground = _theme.FgPrimary;
            combo.BorderBrush = _theme.ComboBorder;
            combo.SetValue(TextElement.ForegroundProperty, _theme.FgPrimary);

            combo.Resources[SystemColors.WindowBrushKey] = _theme.Input;
            combo.Resources[SystemColors.WindowTextBrushKey] = _theme.FgPrimary;
            combo.Resources[SystemColors.HighlightBrushKey] = _theme.Selected;
            combo.Resources[SystemColors.HighlightTextBrushKey] = _theme.SelectedText;
            combo.Resources[SystemColors.ControlBrushKey] = _theme.Input;
            combo.Resources[SystemColors.ControlTextBrushKey] = _theme.FgPrimary;
            combo.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = _theme.Selected;
            combo.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = _theme.SelectedText;
            combo.Resources[SystemColors.ActiveBorderBrushKey] = _theme.ComboBorder;
            combo.Resources[SystemColors.InactiveBorderBrushKey] = _theme.ComboBorder;

            var theme = _theme;
            combo.Loaded += (s, e) => ThemeComboBoxVisualTree((ComboBox)s!, theme);
            combo.DropDownOpened += (s, e) => ThemeComboBoxPopup((ComboBox)s!, theme);

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Input));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            itemStyle.Setters.Add(new Setter(TextElement.ForegroundProperty, _theme.FgPrimary));

            var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Selected));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.SelectedText));
            hoverTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, _theme.SelectedText));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.FgAccent));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                PageTheme.Freeze(new SolidColorBrush(Colors.White))));
            selectedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty,
                PageTheme.Freeze(new SolidColorBrush(Colors.White))));
            itemStyle.Triggers.Add(selectedTrigger);

            combo.ItemContainerStyle = itemStyle;
        }

        private static void ThemeComboBoxVisualTree(ComboBox combo, PageTheme theme)
        {
            try
            {
                var toggleButton = FindChild<ToggleButton>(combo);
                if (toggleButton != null)
                {
                    toggleButton.Background = theme.Input;
                    toggleButton.BorderBrush = theme.ComboBorder;
                    toggleButton.Foreground = theme.FgPrimary;
                    toggleButton.SetValue(TextElement.ForegroundProperty, theme.FgPrimary);

                    toggleButton.Resources[SystemColors.ControlBrushKey] = theme.Input;
                    toggleButton.Resources[SystemColors.ControlTextBrushKey] = theme.FgPrimary;
                    toggleButton.Resources[SystemColors.ControlLightBrushKey] = theme.Input;
                    toggleButton.Resources[SystemColors.ControlDarkBrushKey] = theme.ComboBorder;

                    var arrow = FindChild<System.Windows.Shapes.Path>(toggleButton);
                    if (arrow != null) arrow.Fill = theme.FgSecondary;
                }

                var contentSite = FindChild<ContentPresenter>(combo);
                if (contentSite != null)
                    contentSite.SetValue(TextElement.ForegroundProperty, theme.FgPrimary);

                ThemeComboBoxPopup(combo, theme);
            }
            catch { /* non-fatal */ }
        }

        private static void ThemeComboBoxPopup(ComboBox combo, PageTheme theme)
        {
            try
            {
                var popup = FindChild<Popup>(combo);
                if (popup?.Child is Border popupBorder)
                {
                    popupBorder.Background = theme.Input;
                    popupBorder.BorderBrush = theme.ComboBorder;
                    popupBorder.SetValue(TextElement.ForegroundProperty, theme.FgPrimary);

                    popupBorder.Resources[SystemColors.WindowBrushKey] = theme.Input;
                    popupBorder.Resources[SystemColors.WindowTextBrushKey] = theme.FgPrimary;
                    popupBorder.Resources[SystemColors.HighlightBrushKey] = theme.Selected;
                    popupBorder.Resources[SystemColors.HighlightTextBrushKey] = theme.SelectedText;
                    popupBorder.Resources[SystemColors.ControlBrushKey] = theme.Input;
                    popupBorder.Resources[SystemColors.ControlTextBrushKey] = theme.FgPrimary;
                }
            }
            catch { /* non-fatal */ }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
