#nullable enable
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T150, OPT-05/OPT-06, research R5/R6) — RowFactory's child options and number
    /// fields. A child row is indented, disabled and greyed while its parent check box is off, and
    /// comes back when it is checked. A number field accepts a whole number in range, refuses text
    /// (red border, the last valid value kept), clamps on leaving the box and steps by its step.
    /// Built headlessly (never shown).
    /// </summary>
    public sealed class RowFactoryGatingAndNumberTests
    {
        private static readonly PageTheme Theme = PageTheme.Dark;

        private static TextBlock FirstText(CheckBox box) => (TextBlock)((StackPanel)box.Content).Children[0];

        [StaFact]
        public void A_child_row_follows_its_parent()
        {
            var rows = new RowFactory(Theme);
            var panel = new StackPanel();
            var (_, parent) = rows.AddToggle(panel, "Enable IntelliSense");
            var (childRow, child) = rows.AddToggle(panel, "Trigger after dot", "", parent);

            parent.IsChecked = false;
            Assert.False(childRow.IsEnabled);
            Assert.Same(Theme.TextDisabled, FirstText(child).Foreground);
            Assert.Equal("Takes effect when \"Enable IntelliSense\" is on", childRow.ToolTip);

            parent.IsChecked = true;
            Assert.True(childRow.IsEnabled);
            Assert.Same(Theme.FgPrimary, FirstText(child).Foreground);
        }

        [StaFact]
        public void A_child_row_is_indented_under_its_parent()
        {
            var rows = new RowFactory(Theme);
            var panel = new StackPanel();
            var (parentRow, parent) = rows.AddToggle(panel, "Enable snippets");
            var (childRow, _) = rows.AddToggle(panel, "Format after expansion", "", parent);
            var (numberRow, _) = rows.AddNumber(panel, "Reminder interval", 30, 3600, 30, "seconds", "", parent);
            var (plainNumberRow, _) = rows.AddNumber(panel, "Notification threshold", 5, 300, 5, "seconds");

            Assert.Equal(parentRow.Padding.Left + RowFactory.ChildIndent, childRow.Padding.Left);
            Assert.Equal(plainNumberRow.Margin.Left + RowFactory.ChildIndent, numberRow.Margin.Left);
        }

        [StaFact]
        public void Other_row_kinds_take_a_parent_too()
        {
            var rows = new RowFactory(Theme);
            var panel = new StackPanel();
            var (_, parent) = rows.AddToggle(panel, "Enable SQL history");
            var (dropdownRow, _) = rows.AddDropdown(panel, "When restoring", new[] { "Always", "Ask" }, "", parent);
            var (textRow, _) = rows.AddTextInput(panel, "Team folder", parent: parent);
            var (buttonRow, _) = rows.AddButton(panel, "Formatting styles", "Edit…", "", parent);

            parent.IsChecked = false;
            Assert.False(dropdownRow.IsEnabled);
            Assert.False(textRow.IsEnabled);
            Assert.False(buttonRow.IsEnabled);

            parent.IsChecked = true;
            Assert.True(dropdownRow.IsEnabled);
            Assert.True(textRow.IsEnabled);
            Assert.True(buttonRow.IsEnabled);
        }

        [StaFact]
        public void A_number_field_takes_a_large_whole_number()
        {
            var (_, box) = new RowFactory(Theme).AddNumber(new StackPanel(), "Max entries", 1000, 1_000_000, 1000, "entries");

            box.Text = "250000";

            Assert.Equal(250000, RowFactory.GetNumber(box));
            Assert.Same(Theme.ComboBorder, box.BorderBrush);
        }

        [StaFact]
        public void Text_is_refused_and_the_last_valid_value_kept()
        {
            var rows = new RowFactory(Theme);
            var (_, box) = rows.AddNumber(new StackPanel(), "Retention", 1, 3650, 1, "days");
            RowFactory.SetNumber(box, 90);

            box.Text = "abc";
            Assert.Equal(90, RowFactory.GetNumber(box));
            Assert.Equal(Color.FromRgb(0xE8, 0x11, 0x23), ((SolidColorBrush)box.BorderBrush).Color);

            rows.CommitNumber(box);   // leaving the box puts the last valid value back
            Assert.Equal("90", box.Text);
            Assert.Same(Theme.ComboBorder, box.BorderBrush);
        }

        [StaFact]
        public void Numbers_out_of_range_are_clamped_on_leaving_the_box()
        {
            var rows = new RowFactory(Theme);
            var (_, box) = rows.AddNumber(new StackPanel(), "Timeout", 5, 300, 5, "seconds");

            box.Text = "999";
            rows.CommitNumber(box);
            Assert.Equal(300, RowFactory.GetNumber(box));
            Assert.Equal("300", box.Text);

            box.Text = "1";
            rows.CommitNumber(box);
            Assert.Equal(5, RowFactory.GetNumber(box));

            RowFactory.SetNumber(box, 10_000);   // a saved value out of range shows clamped too
            Assert.Equal(300, RowFactory.GetNumber(box));
        }

        [StaFact]
        public void The_arrows_step_by_the_step_and_stop_at_the_ends()
        {
            var rows = new RowFactory(Theme);
            var (_, box) = rows.AddNumber(new StackPanel(), "Auto-save interval", 30, 300, 15, "seconds");
            RowFactory.SetNumber(box, 60);

            rows.StepNumber(box, +1);
            Assert.Equal(75, RowFactory.GetNumber(box));
            rows.StepNumber(box, -1);
            rows.StepNumber(box, -1);
            Assert.Equal(45, RowFactory.GetNumber(box));

            RowFactory.SetNumber(box, 295);
            rows.StepNumber(box, +1);
            Assert.Equal(300, RowFactory.GetNumber(box));

            var (up, down) = RowFactory.SteppersOf(box);
            Assert.Equal("Increase Auto-save interval", System.Windows.Automation.AutomationProperties.GetName(up));
            Assert.Equal("Decrease Auto-save interval", System.Windows.Automation.AutomationProperties.GetName(down));
        }
    }
}
