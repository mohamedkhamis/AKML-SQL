#nullable enable
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Single home for theming a (non-editable) WPF <see cref="ComboBox"/>. The stock Aero2
    /// ComboBox cannot be themed by property or resource assignment alone: its ToggleButton
    /// template paints a HARDCODED light gradient face that ignores <c>Background</c>, which left
    /// near-white dark-theme text invisible on a light face in the Options dialog. Every themed
    /// combo therefore gets its own ControlTemplate built from <see cref="PageTheme"/> brushes —
    /// cached per theme; the palettes are singletons (<see cref="PageTheme.Dark"/>/<see cref="PageTheme.Light"/>).
    ///
    /// Items MUST be plain strings: a UIElement as item content makes WPF render the closed
    /// selection box as a Rectangle+VisualBrush snapshot (blurry, no ClearType) and breaks
    /// keyboard type-ahead, and a local Foreground on a ComboBoxItem beats the style triggers.
    /// </summary>
    internal static class ComboBoxTheming
    {
        // Template and item style are both immutable once sealed and built purely from frozen
        // singleton brushes, so one shared pair per theme serves every combo.
        private static readonly Dictionary<PageTheme, (ControlTemplate Template, Style ItemStyle)> ThemeCache = new();

        /// <summary>
        /// Themes <paramref name="combo"/> for the CURRENT theme variant. No-op under High
        /// Contrast — there the stock template's system colors are the accessible rendering and
        /// fixed palette brushes would regress it.
        /// </summary>
        public static void Apply(ComboBox combo)
        {
            if (ThemeRegistry.Instance.Current == ThemeVariant.HighContrast) return;
            Apply(combo, ThemeRegistry.Instance.Current == ThemeVariant.Dark ? PageTheme.Dark : PageTheme.Light);
        }

        public static void Apply(ComboBox combo, PageTheme theme)
        {
            var (template, itemStyle) = GetThemeArtifacts(theme);

            // Load-bearing: the template's ContentPresenter renders the selected string via a
            // TextBlock that INHERITS Foreground. Every other visual (face, border, popup) is
            // painted by the template itself — setting Background/BorderBrush here would be dead.
            combo.Foreground = theme.FgPrimary;
            combo.Template = template;
            combo.ItemContainerStyle = itemStyle;
        }

        private static (ControlTemplate Template, Style ItemStyle) GetThemeArtifacts(PageTheme theme)
        {
            lock (ThemeCache)
            {
                if (!ThemeCache.TryGetValue(theme, out var artifacts))
                {
                    artifacts = (BuildComboBoxTemplate(theme), BuildItemStyle(theme));
                    ThemeCache[theme] = artifacts;
                }
                return artifacts;
            }
        }

        private static Style BuildItemStyle(PageTheme theme)
        {
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, theme.Input));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));

            var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, theme.Selected));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, theme.SelectedText));
            itemStyle.Triggers.Add(hoverTrigger);

            // Selected item: same brush pair as hover. Selected is the accent SURFACE designed to
            // carry SelectedText (ThemeTokens.TextOnAccent); FgAccent is a TEXT color — white on
            // it is only ~2.2:1 and unreadable, so it must never be used as a row background here.
            var selectedTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, theme.Selected));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, theme.SelectedText));
            itemStyle.Triggers.Add(selectedTrigger);

            itemStyle.Seal();
            return itemStyle;
        }

        /// <summary>
        /// Non-editable ComboBox template: full-size ToggleButton face (own template — flat themed
        /// Border + arrow glyph), read-only ContentPresenter for the selection box, and a popup
        /// whose dropdown Border carries the theme brushes directly (no reliance on system-brush
        /// resource lookup reaching the popup's visual tree).
        /// </summary>
        private static ControlTemplate BuildComboBoxTemplate(PageTheme theme)
        {
            var root = new FrameworkElementFactory(typeof(Grid));

            var toggle = new FrameworkElementFactory(typeof(ToggleButton), "toggleButton");
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });
            toggle.SetValue(Control.TemplateProperty, BuildToggleTemplate(theme));
            root.AppendChild(toggle);

            var content = new FrameworkElementFactory(typeof(ContentPresenter), "contentPresenter");
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 24, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(content);

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.AppendChild(itemsPresenter);

            var dropDownBorder = new FrameworkElementFactory(typeof(Border), "dropDownBorder");
            dropDownBorder.SetValue(Border.BackgroundProperty, theme.Input);
            dropDownBorder.SetValue(Border.BorderBrushProperty, theme.ComboBorder);
            dropDownBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            dropDownBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth")
            {
                RelativeSource = RelativeSource.TemplatedParent
            });
            dropDownBorder.SetValue(FrameworkElement.MaxHeightProperty, new TemplateBindingExtension(ComboBox.MaxDropDownHeightProperty));
            dropDownBorder.AppendChild(scroll);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(UIElement.FocusableProperty, false);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent
            });
            popup.AppendChild(dropDownBorder);
            root.AppendChild(popup);

            var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
            template.Seal();
            return template;
        }

        private static ControlTemplate BuildToggleTemplate(PageTheme theme)
        {
            var face = new FrameworkElementFactory(typeof(Border));
            face.SetValue(Border.BackgroundProperty, theme.Input);
            face.SetValue(Border.BorderBrushProperty, theme.ComboBorder);
            face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            face.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));

            var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0 Z"));
            arrow.SetValue(System.Windows.Shapes.Shape.FillProperty, theme.FgSecondary);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            face.AppendChild(arrow);

            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = face };
            template.Seal();
            return template;
        }
    }
}
