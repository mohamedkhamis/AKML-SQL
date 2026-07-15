using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Regression tests for the dark-theme ComboBox invisibility bug. Two proven mechanisms:
    /// (1) items created as ComboBoxItem-wrapping-TextBlock made WPF render the CLOSED selection
    /// box as a Rectangle+VisualBrush snapshot (blurry) over the stock Aero2 toggle template's
    /// HARDCODED light gradient face — near-white dark-theme text at ~1.08:1 contrast; and
    /// (2) a LOCAL Foreground on each ComboBoxItem beat the ItemContainerStyle triggers, leaving
    /// light text on the selected row's accent background. The fix: plain string items and an
    /// own ControlTemplate whose face/popup carry the PageTheme brushes directly.
    /// </summary>
    public class OptionsDarkComboTests
    {
        private static (StackPanel Panel, ComboBox Combo) BuildDarkDropdown()
        {
            var factory = new RowFactory(PageTheme.Dark);
            var panel = new StackPanel();
            var (_, combo) = factory.AddDropdown(panel, "Keyword casing",
                new[] { "UPPERCASE", "lowercase", "PascalCase" }, "how keywords are cased");
            return (panel, combo);
        }

        [StaFact]
        public void Dropdown_Items_ArePlainStrings_NeverUiElements()
        {
            var (_, combo) = BuildDarkDropdown();

            Assert.True(combo.Items.Count > 0);
            foreach (var item in combo.Items)
            {
                // A UIElement item (ComboBoxItem/TextBlock) regresses the closed-face rendering
                // to a blurry VisualBrush snapshot and breaks keyboard type-ahead.
                Assert.IsType<string>(item);
            }
        }

        [StaFact]
        public void Dropdown_HasOwnTemplate_TheStockFaceCannotBeThemed()
        {
            var (_, combo) = BuildDarkDropdown();

            // The stock Aero2 toggle face is a hardcoded light gradient that ignores Background;
            // only an explicitly assigned ControlTemplate can render a dark closed face.
            Assert.NotEqual(DependencyProperty.UnsetValue, combo.ReadLocalValue(Control.TemplateProperty));
            Assert.NotNull(combo.Template);
        }

        [StaFact]
        public void Dropdown_ItemStyle_SelectedAndHover_UseAccentSurfaceWithOnAccentText()
        {
            var theme = PageTheme.Dark;
            var (_, combo) = BuildDarkDropdown();
            var style = combo.ItemContainerStyle;
            Assert.NotNull(style);

            // No item carries a local Foreground (it would beat the style triggers)…
            foreach (var item in combo.Items)
                Assert.IsNotType<ComboBoxItem>(item);

            // …and both state triggers pair the accent SURFACE with on-accent text. FgAccent is a
            // TEXT color — as a row background it left the selected row at ~2:1 contrast.
            foreach (var property in new[] { ComboBoxItem.IsSelectedProperty, ComboBoxItem.IsHighlightedProperty })
            {
                var trigger = style!.Triggers.OfType<Trigger>().FirstOrDefault(t => t.Property == property);
                Assert.NotNull(trigger);

                var bg = trigger!.Setters.OfType<Setter>().First(s => s.Property == Control.BackgroundProperty);
                var fg = trigger.Setters.OfType<Setter>().First(s => s.Property == Control.ForegroundProperty);
                Assert.Same(theme.Selected, bg.Value);
                Assert.Same(theme.SelectedText, fg.Value);
            }
        }

        [StaFact]
        public void Dropdown_ClosedFace_ShowsSelectionAsString_NotVisualBrushSnapshot()
        {
            var (panel, combo) = BuildDarkDropdown();

            var window = new Window
            {
                Content = panel,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
                Width = 400,
                Height = 200,
                Left = -10_000, // off-screen
                Top = -10_000
            };
            try
            {
                window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

                // With string items the selection box holds the string itself, which the template's
                // ContentPresenter renders as a real, ClearType TextBlock inheriting the themed
                // Foreground — not a Rectangle filled with a VisualBrush snapshot of a displaced
                // TextBlock (the "blurred / not shown" closed-state bug).
                Assert.IsType<string>(combo.SelectionBoxItem);
                Assert.Equal("UPPERCASE", (string)combo.SelectionBoxItem);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// SQL Prompt-parity header: the band shows the page's full breadcrumb (its Display),
        /// e.g. "Inserted Code › Special characters" — not the short page Title.
        /// </summary>
        [StaFact]
        public void PageHeader_ShowsBreadcrumbDisplay_ForSpecialCharactersPage()
        {
            var settings = new AppSettings { Theme = "Dark" };
            var dialog = new SettingsWindow(settings);
            var window = dialog.TestBuildWindowForRenderTest();

            // The page enters the window's logical tree only once its nav leaf is selected.
            var leaf = LogicalTree.Descendants<TreeViewItem>(window)
                .FirstOrDefault(item => (item.Tag as string) == "SpecialCharacters");
            Assert.NotNull(leaf);
            leaf!.IsSelected = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

            bool found = false;
            foreach (var tb in LogicalTree.Descendants<TextBlock>(window))
            {
                if (tb.Text == "Inserted Code › Special characters")
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, "expected a page-header TextBlock with the full breadcrumb Display");
        }
    }
}
