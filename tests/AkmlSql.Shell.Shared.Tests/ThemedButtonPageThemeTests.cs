#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T154, OPT-09, FR-055, research R9) — Options buttons are painted by a themed
    /// template built from the window's <see cref="PageTheme"/>, not by the stock Aero chrome whose
    /// near-white hover layer hid the dark-theme caption. Resolution follows
    /// <see cref="OptionsHoverContrastTests"/>: the template's triggers are read directly.
    /// </summary>
    public class ThemedButtonPageThemeTests
    {
        private const double MinContrastRatio = 4.5;

        [StaFact]
        public void Secondary_in_Dark_paints_face_hover_and_pressed_from_the_dark_PageTheme()
        {
            var dark = PageTheme.Dark;
            var button = new Button { Content = "Cancel" };

            ThemedButton.ApplySecondary(button, dark);

            AssertOwnBorderTemplate(button);
            Assert.Same(dark.Button, button.Background);
            Assert.Same(dark.FgPrimary, button.Foreground);
            Assert.Same(dark.Border, button.BorderBrush);
            Assert.Same(dark.ButtonHover, TriggerBrush(button, UIElement.IsMouseOverProperty, true, Border.BackgroundProperty));
            Assert.Same(dark.Panel, TriggerBrush(button, ButtonBase.IsPressedProperty, true, Border.BackgroundProperty));

            // The face the template renders is the theme's, once applied.
            Assert.True(button.ApplyTemplate());
            var face = Assert.IsType<Border>(button.Template.FindName("Bd", button));
            Assert.Same(dark.Button, face.Background);
        }

        [StaFact]
        public void Primary_in_Dark_paints_the_accent_steps_from_the_dark_PageTheme()
        {
            var dark = PageTheme.Dark;
            var button = new Button { Content = "OK" };

            ThemedButton.ApplyPrimary(button, dark);

            AssertOwnBorderTemplate(button);
            Assert.Same(dark.Selected, button.Background);
            Assert.Same(dark.SelectedText, button.Foreground);
            Assert.Same(dark.AccentHover, TriggerBrush(button, UIElement.IsMouseOverProperty, true, Border.BackgroundProperty));
            Assert.Same(dark.AccentPressed, TriggerBrush(button, ButtonBase.IsPressedProperty, true, Border.BackgroundProperty));
        }

        [StaFact]
        public void Every_trigger_brush_belongs_to_the_buttons_PageTheme()
        {
            foreach (var theme in new[] { PageTheme.Light, PageTheme.Dark, PageTheme.HighContrast })
            {
                foreach (var primary in new[] { true, false })
                {
                    var button = new Button();
                    if (primary) ThemedButton.ApplyPrimary(button, theme);
                    else ThemedButton.ApplySecondary(button, theme);

                    var themeBrushes = BrushesOf(theme);
                    foreach (var setter in button.Template.Triggers.OfType<Trigger>().SelectMany(t => t.Setters.OfType<Setter>()))
                    {
                        var brush = Assert.IsAssignableFrom<SolidColorBrush>(setter.Value);
                        Assert.True(themeBrushes.Contains(brush),
                            $"{(primary ? "primary" : "secondary")} {setter.Property.Name} setter is not a brush of its PageTheme");
                    }
                }
            }
        }

        [StaFact]
        public void Templates_are_cached_per_PageTheme()
        {
            Button Secondary(PageTheme t) { var b = new Button(); ThemedButton.ApplySecondary(b, t); return b; }
            Button Primary(PageTheme t) { var b = new Button(); ThemedButton.ApplyPrimary(b, t); return b; }

            Assert.Same(Secondary(PageTheme.Dark).Template, Secondary(PageTheme.Dark).Template);
            Assert.Same(Primary(PageTheme.Dark).Template, Primary(PageTheme.Dark).Template);
            Assert.NotSame(Secondary(PageTheme.Dark).Template, Secondary(PageTheme.Light).Template);
            Assert.NotSame(Secondary(PageTheme.Dark).Template, Primary(PageTheme.Dark).Template);
            Assert.True(Secondary(PageTheme.Dark).Template.IsSealed);
        }

        [StaFact]
        public void Disabled_and_hovered_secondary_captions_stay_readable()
        {
            foreach (var theme in new[] { PageTheme.Light, PageTheme.Dark })
            {
                var button = new Button();
                ThemedButton.ApplySecondary(button, theme);

                var disabledFace = TriggerBrush(button, UIElement.IsEnabledProperty, false, Border.BackgroundProperty);
                var disabledText = TriggerBrush(button, UIElement.IsEnabledProperty, false, TextElement.ForegroundProperty);
                AssertContrast("disabled", disabledFace, disabledText);

                var hoverFace = TriggerBrush(button, UIElement.IsMouseOverProperty, true, Border.BackgroundProperty);
                AssertContrast("hovered", hoverFace, (SolidColorBrush)button.Foreground);
            }
        }

        [StaFact]
        public void The_token_overloads_are_unchanged()
        {
            var button = new Button();
            ThemedButton.ApplySecondary(button);

            var hover = button.Template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
            var setter = hover.Setters.OfType<Setter>().Single(s => s.Property == Border.BackgroundProperty);
            Assert.IsType<DynamicResourceExtension>(setter.Value);
        }

        [StaFact]
        public void Options_window_buttons_use_the_themed_templates()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "dark" });
            var window = dialog.TestBuildWindowForRenderTest("AI Assistance");
            var buttons = LogicalTree.Descendants<Button>(window).ToList();

            var cancel = Assert.Single(buttons, b => Equals(b.Content, "Cancel"));
            var ok = Assert.Single(buttons, b => Equals(b.Content, "OK"));
            Assert.Same(TemplateOf(PageTheme.Dark, primary: false), cancel.Template);
            Assert.Same(TemplateOf(PageTheme.Dark, primary: true), ok.Template);

            // The AI agent list's buttons live on the AI assistance page (spec 040 T167).
            var pages = (Dictionary<string, UIElement>)typeof(SettingsWindow)
                .GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var addAgent = Assert.Single(LogicalTree.Descendants<Button>(pages["AI Assistance"]),
                b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add a new AI agent");
            Assert.Same(TemplateOf(PageTheme.Dark, primary: false), addAgent.Template);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AssertOwnBorderTemplate(Button button)
        {
            Assert.NotNull(button.Template);
            Assert.Equal(typeof(Button), button.Template.TargetType);
            Assert.Equal(typeof(Border), button.Template.VisualTree.Type);
        }

        private static ControlTemplate TemplateOf(PageTheme theme, bool primary)
        {
            var b = new Button();
            if (primary) ThemedButton.ApplyPrimary(b, theme);
            else ThemedButton.ApplySecondary(b, theme);
            return b.Template;
        }

        private static SolidColorBrush TriggerBrush(Button button, DependencyProperty property, object value, DependencyProperty target)
        {
            var trigger = Assert.Single(button.Template.Triggers.OfType<Trigger>(),
                t => ReferenceEquals(t.Property, property) && Equals(t.Value, value));
            var setter = Assert.Single(trigger.Setters.OfType<Setter>(), s => ReferenceEquals(s.Property, target));
            return Assert.IsAssignableFrom<SolidColorBrush>(setter.Value);
        }

        private static HashSet<SolidColorBrush> BrushesOf(PageTheme theme) =>
            new HashSet<SolidColorBrush>(typeof(PageTheme)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(SolidColorBrush))
                .Select(p => (SolidColorBrush)p.GetValue(theme)!));

        private static void AssertContrast(string state, SolidColorBrush bg, SolidColorBrush fg)
        {
            double ratio = ContrastRatio(bg.Color, fg.Color);
            Assert.True(ratio >= MinContrastRatio,
                $"{state} button: contrast {ratio:F2}:1 is below {MinContrastRatio}:1 (background {bg.Color}, foreground {fg.Color})");
        }

        private static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
    }
}
