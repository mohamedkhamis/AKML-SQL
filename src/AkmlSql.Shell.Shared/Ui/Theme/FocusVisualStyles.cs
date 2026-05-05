using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Focus-visual styles used on high-stakes interactive controls per FR-018 (contract O9):
    /// primary actions, destructive actions, navigation items, search inputs, toggle switches.
    ///
    /// Surfaces apply this via <c>control.FocusVisualStyle = FocusVisualStyles.HighStakes</c>.
    /// Other controls retain the WPF/OS default focus chrome.
    /// </summary>
    public static class FocusVisualStyles
    {
        /// <summary>
        /// 1.5px outer border in <see cref="ThemeTokens.BorderFocus"/>. The style itself uses
        /// <c>SetResourceReference</c> so the focus indicator tracks live theme changes.
        /// </summary>
        public static readonly Style HighStakes = BuildHighStakesStyle();

        private static Style BuildHighStakesStyle()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.MarginProperty, new Thickness(-2.0));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2.0));
            border.SetResourceBinding(Border.BorderBrushProperty, ThemeTokens.BorderFocus);

            var template = new ControlTemplate(typeof(Control)) { VisualTree = border };

            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            // WPF auto-seals styles when they're used; no explicit freeze step needed.
            return style;
        }
    }

    /// <summary>
    /// Convenience extension to invoke <see cref="FrameworkElement.SetResourceReference"/> on a
    /// <see cref="FrameworkElementFactory"/> (which doesn't expose the method directly).
    /// </summary>
    internal static class FrameworkElementFactoryExtensions
    {
        public static void SetResourceBinding(
            this FrameworkElementFactory factory,
            DependencyProperty property,
            object resourceKey)
        {
            factory.SetValue(property, new System.Windows.DynamicResourceExtension(resourceKey));
        }
    }
}
