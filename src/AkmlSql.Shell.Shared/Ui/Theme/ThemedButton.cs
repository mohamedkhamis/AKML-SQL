#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Shared themed button templates. A plain WPF <see cref="Button"/> with a custom
    /// Background keeps the stock Aero chrome, whose hover layer repaints the face nearly
    /// white — behind white accent text that is exactly the "hover color is very bad, text
    /// disappears" report. These Border-based templates render the token colors themselves,
    /// so hover / pressed / disabled states stay readable in every theme.
    ///
    /// Templates are built fresh per button (never cached): the triggers carry DynamicResource
    /// references that resolve against the owning window's theme, and per-instance builds keep
    /// that resolution unambiguous.
    /// </summary>
    internal static class ThemedButton
    {
        /// <summary>Accent call-to-action: accent fill, on-accent text, hover/pressed accent steps.</summary>
        public static void ApplyPrimary(Button button)
        {
            button.Template = BuildTemplate(
                hoverToken: ThemeTokens.AccentPrimaryHover,
                pressedToken: ThemeTokens.AccentPrimaryPressed);
            button.SetResourceReference(Control.BackgroundProperty, ThemeTokens.AccentPrimary);
            button.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextOnAccent);
            button.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.AccentPrimaryPressed);
            button.BorderThickness = new Thickness(1);
        }

        /// <summary>Quiet action: panel surface + visible border, hover fills with Surface.Hover.</summary>
        public static void ApplySecondary(Button button)
        {
            button.Template = BuildTemplate(
                hoverToken: ThemeTokens.SurfaceHover,
                pressedToken: ThemeTokens.SurfaceElevated);
            button.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfacePanel);
            button.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            button.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderStrong);
            button.BorderThickness = new Thickness(1);
        }

        private static ControlTemplate BuildTemplate(string hoverToken, string pressedToken)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(hoverToken), "Bd"));
            template.Triggers.Add(hover);

            var pressed = new Trigger
            {
                Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,
                Value = true
            };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(pressedToken), "Bd"));
            template.Triggers.Add(pressed);

            // Disabled LAST so it wins over hover/pressed: muted surface, no hover response.
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceElevated), "Bd"));
            disabled.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.BorderSubtle), "Bd"));
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6, "Bd"));
            template.Triggers.Add(disabled);

            return template;
        }
    }
}
