using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Regression guard for the invisible-selected-row bug: the stock Aero2 ListBoxItem template
    /// paints its own ~24%-alpha highlight wash on selection and ignores the item's Background —
    /// the SelectedText white foreground then sat on a near-white wash (user report: the agent
    /// list row was invisible in light theme). BuildItemStyle must own a Border item template so
    /// the trigger-set Background actually paints, and the selected pairing must be the
    /// accent-surface/on-accent token pair.
    /// </summary>
    public class AiAgentItemStyleTests
    {
        [StaFact]
        public void Item_style_owns_the_item_template_and_pairs_the_selected_state()
        {
            var style = AiAgentListView.BuildItemStyle(PageTheme.Light);

            var item = new ListBoxItem();
            item.ApplyTemplate();
            item.Style = style;

            // The custom template must be in place (null would mean the stock Aero2 wash).
            Assert.NotNull(item.Template);
            Assert.Equal(typeof(Border), item.Template.VisualTree.Type);

            // Selected state: solid accent background with the on-accent foreground.
            item.IsSelected = true;
            Assert.Same(PageTheme.Light.Selected, item.Background);
            Assert.Same(PageTheme.Light.SelectedText, item.Foreground);
        }

        [StaFact]
        public void Item_style_pairs_hover_with_the_primary_foreground()
        {
            // IsMouseOver is computed from real mouse state (cannot be raised synthetically), so
            // assert the pairing structurally: an IsMouseOver trigger whose background AND
            // foreground are the hover-token pair (the spec-036 legibility guarantee).
            var style = AiAgentListView.BuildItemStyle(PageTheme.Dark);

            var hover = Assert.Single(
                style.Triggers.OfType<Trigger>(),
                t => ReferenceEquals(t.Property, UIElement.IsMouseOverProperty));

            Assert.Contains(hover.Setters, s =>
                s is Setter set
                && ReferenceEquals(set.Property, Control.BackgroundProperty)
                && ReferenceEquals(set.Value, PageTheme.Dark.TreeHover));
            Assert.Contains(hover.Setters, s =>
                s is Setter set
                && ReferenceEquals(set.Property, Control.ForegroundProperty)
                && ReferenceEquals(set.Value, PageTheme.Dark.FgPrimary));
        }
    }
}
