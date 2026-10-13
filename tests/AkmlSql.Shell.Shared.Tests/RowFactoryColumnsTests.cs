#nullable enable
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Design B of the Options redesign (2026-10-13): the fitted rows. <c>AddColumns</c> gives a
    /// builder two equal columns so runs of short toggles sit side by side; <c>Tip</c> keeps the
    /// long explanation a short description no longer carries.
    /// </summary>
    public class RowFactoryColumnsTests
    {
        [StaFact]
        public void AddColumns_appends_one_grid_with_two_equal_columns()
        {
            var rows = new RowFactory(PageTheme.Light);
            var panel = new StackPanel();
            rows.AddGroupHeader(panel, "Group");

            var (left, right) = rows.AddColumns(panel);

            var grid = Assert.IsType<Grid>(panel.Children[panel.Children.Count - 1]);
            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.All(grid.ColumnDefinitions, c => Assert.Equal(GridUnitType.Star, c.Width.GridUnitType));
            Assert.Same(grid, left.Parent);
            Assert.Same(grid, right.Parent);
            Assert.Equal(1, Grid.GetColumn(right));
        }

        [StaFact]
        public void Rows_added_to_a_column_keep_the_group_indent()
        {
            var rows = new RowFactory(PageTheme.Light);
            var panel = new StackPanel();
            rows.AddGroupHeader(panel, "Group");
            var (left, _) = rows.AddColumns(panel);

            var (row, _) = rows.AddToggle(left, "Left toggle", "");

            Assert.Same(left, row.Parent);
            Assert.Equal(12 + RowFactory.GroupIndentWidth, row.Padding.Left);
        }

        [StaFact]
        public void Tip_puts_the_long_text_on_the_row()
        {
            var rows = new RowFactory(PageTheme.Light);
            var panel = new StackPanel();
            var (row, _) = rows.AddToggle(panel, "Short", "One line");

            rows.Tip(row, "The long explanation.");

            Assert.Equal("The long explanation.", row.ToolTip);
        }
    }
}
