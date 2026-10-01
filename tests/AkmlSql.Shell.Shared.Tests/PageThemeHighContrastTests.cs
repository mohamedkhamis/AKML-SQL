#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T157, T169, OPT-09, FR-055) — under Windows high contrast the Options window is
    /// painted with <see cref="PageTheme.HighContrast"/>, whatever theme is saved, and every brush in
    /// that theme comes from <see cref="ThemePalette.HighContrast"/> (Windows system colours).
    /// The high-contrast switch is <see cref="HostThemeWatcher.HighContrastOverrideForTests"/>, which
    /// is per thread, so the Options tests building Light and Dark windows in parallel never see it.
    /// </summary>
    public class PageThemeHighContrastTests
    {
        // PageTheme brush → the ThemePalette token FromPalette maps it to. A brush missing here fails
        // the sweep, so a new PageTheme brush has to say where its high-contrast colour comes from.
        private static readonly Dictionary<string, string> TokenFor = new Dictionary<string, string>
        {
            [nameof(PageTheme.Main)] = ThemeTokens.SurfaceCanvas,
            [nameof(PageTheme.Sidebar)] = ThemeTokens.SurfaceSidebar,
            [nameof(PageTheme.Panel)] = ThemeTokens.SurfacePanel,
            [nameof(PageTheme.Input)] = ThemeTokens.SurfaceInput,
            [nameof(PageTheme.InputReadOnly)] = ThemeTokens.SurfaceInputReadOnly,
            [nameof(PageTheme.Button)] = ThemeTokens.SurfaceElevated,
            [nameof(PageTheme.ButtonHover)] = ThemeTokens.SurfaceHover,
            [nameof(PageTheme.Selected)] = ThemeTokens.AccentPrimary,
            [nameof(PageTheme.Border)] = ThemeTokens.BorderDefault,
            [nameof(PageTheme.ComboBorder)] = ThemeTokens.BorderDefault,
            [nameof(PageTheme.FgPrimary)] = ThemeTokens.TextPrimary,
            [nameof(PageTheme.FgSecondary)] = ThemeTokens.TextSecondary,
            [nameof(PageTheme.FgAccent)] = ThemeTokens.TextLink,
            [nameof(PageTheme.FgWhite)] = ThemeTokens.TextPrimary,
            [nameof(PageTheme.SelectedText)] = ThemeTokens.TextOnAccent,
            [nameof(PageTheme.Sep)] = ThemeTokens.BorderSubtle,
            [nameof(PageTheme.TreeHover)] = ThemeTokens.SurfaceHover,
            [nameof(PageTheme.Caret)] = ThemeTokens.TextPrimary,
            [nameof(PageTheme.SelectionTint)] = ThemeTokens.SurfaceSelection,
            [nameof(PageTheme.TextDisabled)] = ThemeTokens.TextDisabled,
            [nameof(PageTheme.AccentHover)] = ThemeTokens.AccentPrimaryHover,
            [nameof(PageTheme.AccentPressed)] = ThemeTokens.AccentPrimaryPressed,
        };

        [StaFact]
        public void With_high_contrast_on_the_Options_window_builds_with_the_high_contrast_theme()
        {
            HostThemeWatcher.HighContrastOverrideForTests = () => true;
            try
            {
                foreach (var saved in new[] { "dark", "light", "system", null })
                    Assert.Same(PageTheme.HighContrast, SettingsWindow.ResolvePageTheme(saved));

                var dialog = new SettingsWindow(new AppSettings { Theme = "dark" });
                var window = dialog.TestBuildWindowForRenderTest();

                Assert.Same(PageTheme.HighContrast.Main, window.Background);
                Assert.Same(PageTheme.HighContrast.FgPrimary, window.Foreground);
            }
            finally
            {
                HostThemeWatcher.HighContrastOverrideForTests = null;
            }
        }

        [StaFact]
        public void With_high_contrast_off_the_saved_theme_applies()
        {
            HostThemeWatcher.HighContrastOverrideForTests = () => false;
            try
            {
                Assert.Same(PageTheme.Dark, SettingsWindow.ResolvePageTheme("dark"));
                Assert.Same(PageTheme.Light, SettingsWindow.ResolvePageTheme("light"));
            }
            finally
            {
                HostThemeWatcher.HighContrastOverrideForTests = null;
            }
        }

        [StaFact]
        public void Every_high_contrast_brush_comes_from_the_high_contrast_palette()
        {
            var palette = ThemePalette.HighContrast;
            foreach (var (name, brush) in Brushes(PageTheme.HighContrast))
            {
                Assert.True(brush.IsFrozen, name + " is not frozen");
                if (name == nameof(PageTheme.Transparent))
                {
                    Assert.Equal(Colors.Transparent, brush.Color);
                    continue;
                }

                Assert.True(TokenFor.TryGetValue(name, out var token),
                    $"PageTheme.{name} has no token in this test — say which high-contrast colour it uses");
                Assert.Equal(palette.Brushes[token!].Color, brush.Color);
            }

            // The palette is Windows' own colours, so the window follows the user's contrast theme.
            Assert.Equal(SystemColors.WindowColor, PageTheme.HighContrast.Main.Color);
            Assert.Equal(SystemColors.WindowTextColor, PageTheme.HighContrast.FgPrimary.Color);
            Assert.Equal(SystemColors.HighlightColor, PageTheme.HighContrast.Selected.Color);
            Assert.Equal(SystemColors.HighlightTextColor, PageTheme.HighContrast.SelectedText.Color);
            Assert.Equal(SystemColors.GrayTextColor, PageTheme.HighContrast.TextDisabled.Color);
        }

        [StaFact]
        public void The_high_contrast_theme_is_not_the_Light_or_Dark_one()
        {
            var hc = Brushes(PageTheme.HighContrast).Select(b => b.Brush.Color).ToList();

            Assert.NotSame(PageTheme.Light, PageTheme.HighContrast);
            Assert.NotSame(PageTheme.Dark, PageTheme.HighContrast);
            Assert.NotEqual(Brushes(PageTheme.Light).Select(b => b.Brush.Color).ToList(), hc);
            Assert.NotEqual(Brushes(PageTheme.Dark).Select(b => b.Brush.Color).ToList(), hc);
        }

        private static IEnumerable<(string Name, SolidColorBrush Brush)> Brushes(PageTheme theme) =>
            typeof(PageTheme)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(SolidColorBrush))
                .OrderBy(p => p.Name)
                .Select(p => (p.Name, (SolidColorBrush)p.GetValue(theme)!));
    }
}
