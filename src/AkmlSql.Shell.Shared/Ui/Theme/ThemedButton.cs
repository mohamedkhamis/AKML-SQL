#nullable enable
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Shared themed button templates. A plain WPF <see cref="Button"/> with a custom
    /// Background keeps the stock Aero chrome, whose hover layer repaints the face nearly
    /// white — behind white accent text that is exactly the "hover color is very bad, text
    /// disappears" report. These Border-based templates render the token colors themselves,
    /// so hover / pressed / disabled states stay readable in every theme.
    ///
    /// Exactly two templates exist (primary + secondary), built once and sealed. A template is
    /// safe to share across windows and themes because its triggers carry DynamicResource
    /// references, and WPF resolves those at render time against each templated parent's own
    /// resource chain — so one instance still picks up whatever theme its owning window has
    /// active. Same reasoning as <see cref="ComboBoxTheming"/>'s sealed template cache; the
    /// earlier per-button rebuild duplicated the whole factory/trigger graph for no benefit and
    /// left every copy unsealed, so WPF re-validated the template on each instantiation.
    ///
    /// <para>Spec 040 (OPT-09, research R9): the Options window does not follow
    /// <see cref="ThemeRegistry"/> — it paints from its own <see cref="PageTheme"/> — so its buttons
    /// use the <see cref="PageTheme"/> overloads, whose templates carry that theme's frozen brushes
    /// and are cached per theme like <see cref="ComboBoxTheming"/>'s.</para>
    /// </summary>
    internal static class ThemedButton
    {
        private static readonly ControlTemplate PrimaryTemplate = BuildTemplate(
            hoverToken: ThemeTokens.AccentPrimaryHover,
            pressedToken: ThemeTokens.AccentPrimaryPressed);

        private static readonly ControlTemplate SecondaryTemplate = BuildTemplate(
            hoverToken: ThemeTokens.SurfaceHover,
            pressedToken: ThemeTokens.SurfaceElevated);

        /// <summary>Accent call-to-action: accent fill, on-accent text, hover/pressed accent steps.</summary>
        public static void ApplyPrimary(Button button)
        {
            button.Template = PrimaryTemplate;
            button.SetResourceReference(Control.BackgroundProperty, ThemeTokens.AccentPrimary);
            button.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextOnAccent);
            button.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.AccentPrimaryPressed);
            button.BorderThickness = new Thickness(1);
        }

        /// <summary>Quiet action: panel surface + visible border, hover fills with Surface.Hover.</summary>
        public static void ApplySecondary(Button button)
        {
            button.Template = SecondaryTemplate;
            button.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfacePanel);
            button.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            button.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderStrong);
            button.BorderThickness = new Thickness(1);
        }

        // One sealed (primary, secondary) template pair per PageTheme. The palettes are singletons
        // (Light / Dark / HighContrast) and every brush in them is frozen, so a pair is immutable
        // and shared by every Options button of that theme.
        private static readonly Dictionary<PageTheme, (ControlTemplate Primary, ControlTemplate Secondary)> PageThemeCache = new();

        /// <summary>
        /// Spec 040 (OPT-09): the Options window's call-to-action (OK) — accent face and border,
        /// on-accent text, hover and pressed one and two accent steps deeper, all from
        /// <paramref name="theme"/>.
        /// </summary>
        public static void ApplyPrimary(Button button, PageTheme theme)
        {
            button.Template = GetPageThemeTemplates(theme).Primary;
            button.Background = theme.Selected;
            button.Foreground = theme.SelectedText;
            button.BorderBrush = theme.Selected;
            button.BorderThickness = new Thickness(1);
        }

        /// <summary>
        /// Spec 040 (OPT-09): every other Options button — raised face with a visible border,
        /// <see cref="PageTheme.ButtonHover"/> under the mouse, <see cref="PageTheme.Panel"/> while
        /// pressed, all from <paramref name="theme"/>. Replaces the stock Aero chrome, whose
        /// near-white hover layer hid the dark-theme caption.
        /// </summary>
        public static void ApplySecondary(Button button, PageTheme theme)
        {
            button.Template = GetPageThemeTemplates(theme).Secondary;
            button.Background = theme.Button;
            button.Foreground = theme.FgPrimary;
            button.BorderBrush = theme.Border;
            button.BorderThickness = new Thickness(1);
        }

        private static (ControlTemplate Primary, ControlTemplate Secondary) GetPageThemeTemplates(PageTheme theme)
        {
            lock (PageThemeCache)
            {
                if (!PageThemeCache.TryGetValue(theme, out var templates))
                {
                    templates = (
                        BuildPageThemeTemplate(theme, hover: theme.AccentHover, pressed: theme.AccentPressed, hoverBorder: theme.AccentHover),
                        BuildPageThemeTemplate(theme, hover: theme.ButtonHover, pressed: theme.Panel, hoverBorder: theme.FgAccent));
                    PageThemeCache[theme] = templates;
                }
                return templates;
            }
        }

        /// <summary>
        /// The <see cref="PageTheme"/> twin of <see cref="BuildTemplate"/>: the same Border-based
        /// shape and trigger order, with frozen theme brushes in place of token references. The
        /// border turns <paramref name="hoverBorder"/> under the mouse as well, because in Dark the
        /// raised face and the hover face are the same slate — the fill alone would not move.
        /// </summary>
        private static ControlTemplate BuildPageThemeTemplate(
            PageTheme theme, SolidColorBrush hover, SolidColorBrush pressed, SolidColorBrush hoverBorder)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Bd"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, hoverBorder, "Bd"));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger
            {
                Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,
                Value = true
            };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressed, "Bd"));
            template.Triggers.Add(pressedTrigger);

            // Disabled last so it wins, with its own foreground for the reason given in BuildTemplate.
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Border.BackgroundProperty, theme.Panel, "Bd"));
            disabled.Setters.Add(new Setter(Border.BorderBrushProperty, theme.Sep, "Bd"));
            disabled.Setters.Add(new Setter(TextElement.ForegroundProperty, theme.FgSecondary, "Bd"));
            template.Triggers.Add(disabled);

            template.Seal();
            return template;
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

            // Disabled LAST so it wins over hover/pressed: quiet flat face, no hover response.
            // Surface.Panel + Text.Secondary (not Surface.Elevated, and no blanket opacity): the
            // old version recolored only the face, so a disabled PRIMARY button kept ApplyPrimary's
            // white Text.OnAccent label over Surface.Elevated — 1.23:1 in Light, then faded further
            // by Opacity 0.6, i.e. the caption disappeared for the whole of every AI request while
            // the Send button is disabled. This pairing clears WCAG AA in both themes
            // (Light #FFFFFF/#475569 = 7.58:1, Dark #1E293B/#94A3B8 = 5.71:1) and is still clearly
            // distinct from the accent-filled enabled state. Opacity is gone on purpose: it
            // composites the TEXT toward the face too, giving back the contrast just bought.
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfacePanel), "Bd"));
            disabled.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.BorderSubtle), "Bd"));
            // TextElement.Foreground targeted at "Bd", NOT an untargeted Control.Foreground setter:
            // Apply* sets the button's Foreground as a local value (SetResourceReference), and a
            // local value outranks a template trigger on the templated parent. Setting the
            // inheritable property on the Border overrides what the ContentPresenter's generated
            // TextBlock inherits, so the label actually repaints.
            disabled.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(ThemeTokens.TextSecondary), "Bd"));
            template.Triggers.Add(disabled);

            // Seal after the last trigger — a sealed template is immutable and shareable.
            template.Seal();
            return template;
        }
    }
}
