using AkmlSql.Web.Shared.Grid;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T039, research R4, FR-008/FR-010) — spreadsheet selection over one result set, in display
/// coordinates (row = position on screen; a sort changes which fetched row is there). Click selects a
/// cell, Shift extends from the anchor, Ctrl adds or removes; the row number, the header and the corner
/// select a row, a column and everything; the keyboard moves the focus like a spreadsheet.
/// </summary>
public sealed class GridSelectionTests
{
    private static GridSelection New(int rows = 10, int cols = 4) => new(rows, cols);

    [Fact]
    public void Starts_empty()
    {
        var s = New();
        Assert.True(s.IsEmpty);
        Assert.False(s.IsSelected(0, 0));
    }

    [Fact]
    public void Click_selects_one_cell_and_moves_the_focus_there()
    {
        var s = New();
        s.Click(2, 1, shift: false, ctrl: false);

        Assert.True(s.IsSelected(2, 1));
        Assert.False(s.IsSelected(2, 2));
        Assert.Equal(new GridCell(2, 1), s.Focus);
        Assert.Equal(new GridCell(2, 1), s.Anchor);
    }

    [Fact]
    public void Shift_click_extends_a_rectangle_from_the_anchor()
    {
        var s = New();
        s.Click(1, 1, false, false);
        s.Click(3, 2, shift: true, ctrl: false);

        Assert.Equal(6, s.EnumerateSelected().Count());
        Assert.True(s.IsSelected(2, 2));
        Assert.False(s.IsSelected(3, 3));
        Assert.Equal(new GridCell(1, 1), s.Anchor);
        Assert.Equal(new GridCell(3, 2), s.Focus);
    }

    [Fact]
    public void A_second_shift_click_replaces_the_extension_rather_than_adding_to_it()
    {
        var s = New();
        s.Click(1, 1, false, false);
        s.Click(4, 3, true, false);
        s.Click(2, 1, true, false);

        Assert.Equal(2, s.EnumerateSelected().Count());
    }

    [Fact]
    public void Ctrl_click_adds_a_cell_and_removes_it_again()
    {
        var s = New();
        s.Click(0, 0, false, false);
        s.Click(5, 3, shift: false, ctrl: true);

        Assert.True(s.IsSelected(0, 0));
        Assert.True(s.IsSelected(5, 3));

        s.Click(5, 3, false, ctrl: true);
        Assert.False(s.IsSelected(5, 3));
        Assert.True(s.IsSelected(0, 0));
    }

    [Fact]
    public void Ctrl_click_removes_one_cell_from_a_larger_selection()
    {
        var s = New();
        s.SelectAll();
        s.Click(4, 2, false, ctrl: true);

        Assert.False(s.IsSelected(4, 2));
        Assert.True(s.IsSelected(4, 1));
        Assert.Equal(10 * 4 - 1, s.EnumerateSelected().Count());
    }

    [Fact]
    public void A_row_number_selects_the_whole_row_and_shift_extends_rows()
    {
        var s = New();
        s.ClickRowHeader(3, false, false);
        Assert.Equal(new[] { 3 }, s.SelectedRows());
        Assert.Equal(new[] { 0, 1, 2, 3 }, s.SelectedColumns());

        s.ClickRowHeader(6, shift: true, ctrl: false);
        Assert.Equal(new[] { 3, 4, 5, 6 }, s.SelectedRows());
        Assert.True(s.IsSelected(5, 3));
    }

    [Fact]
    public void Ctrl_on_a_row_number_adds_or_removes_that_row()
    {
        var s = New();
        s.ClickRowHeader(1, false, false);
        s.ClickRowHeader(4, false, ctrl: true);
        Assert.Equal(new[] { 1, 4 }, s.SelectedRows());

        s.ClickRowHeader(1, false, ctrl: true);
        Assert.Equal(new[] { 4 }, s.SelectedRows());
    }

    [Fact]
    public void A_column_header_selects_the_whole_column_and_shift_extends_columns()
    {
        var s = New();
        s.ClickColumnHeader(1, false, false);
        Assert.Equal(new[] { 1 }, s.SelectedColumns());
        Assert.Equal(Enumerable.Range(0, 10), s.SelectedRows());

        s.ClickColumnHeader(3, shift: true, ctrl: false);
        Assert.Equal(new[] { 1, 2, 3 }, s.SelectedColumns());
    }

    [Fact]
    public void Ctrl_on_a_column_header_removes_a_selected_column()
    {
        var s = New();
        s.SelectAll();
        s.ClickColumnHeader(2, false, ctrl: true);

        Assert.Equal(new[] { 0, 1, 3 }, s.SelectedColumns());
    }

    [Fact]
    public void The_corner_and_Ctrl_A_select_everything_with_one_operation()
    {
        var s = new GridSelection(100_000, 200);
        s.SelectAll();

        Assert.True(s.IsSelected(99_999, 199));
        Assert.Equal(1, s.OperationCount);
        Assert.True(s.IsAll);
    }

    [Fact]
    public void Dragging_selects_the_rectangle_between_the_first_and_the_current_cell()
    {
        var s = New();
        s.Click(2, 2, false, false);
        s.DragTo(5, 0);

        Assert.Equal(new[] { 2, 3, 4, 5 }, s.SelectedRows());
        Assert.Equal(new[] { 0, 1, 2 }, s.SelectedColumns());
        Assert.Equal(new GridCell(5, 0), s.Focus);

        s.DragTo(2, 2);
        Assert.Single(s.EnumerateSelected());
    }

    [Theory]
    [InlineData(GridMove.Down, 3, 1)]
    [InlineData(GridMove.Up, 1, 1)]
    [InlineData(GridMove.Left, 2, 0)]
    [InlineData(GridMove.Right, 2, 2)]
    [InlineData(GridMove.Home, 2, 0)]
    [InlineData(GridMove.End, 2, 3)]
    [InlineData(GridMove.CtrlHome, 0, 0)]
    [InlineData(GridMove.CtrlEnd, 9, 3)]
    [InlineData(GridMove.PageDown, 7, 1)]
    [InlineData(GridMove.PageUp, 0, 1)]
    public void Keys_move_the_focus_like_a_spreadsheet(GridMove move, int row, int col)
    {
        var s = New();
        s.Click(2, 1, false, false);
        s.Move(move, extend: false, pageRows: 5);

        Assert.Equal(new GridCell(row, col), s.Focus);
        Assert.Single(s.EnumerateSelected());
        Assert.True(s.IsSelected(row, col));
    }

    [Fact]
    public void Moves_stop_at_the_edges()
    {
        var s = New();
        s.Click(0, 0, false, false);
        s.Move(GridMove.Up, false, 5);
        s.Move(GridMove.Left, false, 5);
        Assert.Equal(new GridCell(0, 0), s.Focus);

        s.Move(GridMove.CtrlEnd, false, 5);
        s.Move(GridMove.Down, false, 5);
        s.Move(GridMove.Right, false, 5);
        Assert.Equal(new GridCell(9, 3), s.Focus);
    }

    [Fact]
    public void Shift_with_a_move_extends_from_the_anchor()
    {
        var s = New();
        s.Click(2, 1, false, false);
        s.Move(GridMove.Down, extend: true, pageRows: 5);
        s.Move(GridMove.Right, extend: true, pageRows: 5);

        Assert.Equal(4, s.EnumerateSelected().Count());
        Assert.Equal(new GridCell(2, 1), s.Anchor);
        Assert.Equal(new GridCell(3, 2), s.Focus);
    }

    [Fact]
    public void A_move_from_nothing_starts_at_the_first_cell()
    {
        var s = New();
        s.Move(GridMove.Down, false, 5);
        Assert.Equal(new GridCell(0, 0), s.Focus);
    }

    [Fact]
    public void EnumerateSelected_yields_grid_order()
    {
        var s = New();
        s.Click(3, 2, false, false);
        s.Click(1, 0, false, ctrl: true);
        s.Click(1, 3, false, ctrl: true);

        Assert.Equal(new[] { new GridCell(1, 0), new GridCell(1, 3), new GridCell(3, 2) }, s.EnumerateSelected());
    }

    [Fact]
    public void Clear_empties_the_selection()
    {
        var s = New();
        s.SelectAll();
        s.Clear();
        Assert.True(s.IsEmpty);
        Assert.Empty(s.EnumerateSelected());
    }

    [Fact]
    public void A_sort_keeps_the_focus_on_the_same_fetched_row()
    {
        var s = New(rows: 4, cols: 2);
        int[] before = { 0, 1, 2, 3 };    // display → fetched
        int[] after = { 3, 2, 1, 0 };     // the sort reversed the rows
        s.Click(1, 1, false, false);      // fetched row 1

        s.RemapThroughPermutation(before, after);

        Assert.Equal(new GridCell(2, 1), s.Focus);   // fetched row 1 is now shown third
        Assert.True(s.IsSelected(2, 1));
    }

    [Fact]
    public void A_whole_column_or_everything_survives_a_sort_unchanged()
    {
        var s = New(rows: 4, cols: 3);
        s.ClickColumnHeader(1, false, false);

        s.RemapThroughPermutation(new[] { 0, 1, 2, 3 }, new[] { 2, 0, 3, 1 });

        Assert.Equal(new[] { 1 }, s.SelectedColumns());
        Assert.Equal(new[] { 0, 1, 2, 3 }, s.SelectedRows());
    }

    [Fact]
    public void Clicks_outside_the_grid_are_clamped()
    {
        var s = New(rows: 3, cols: 2);
        s.Click(10, 10, false, false);
        Assert.Equal(new GridCell(2, 1), s.Focus);
    }

    [Fact]
    public void An_empty_result_has_nothing_to_select()
    {
        var s = new GridSelection(0, 3);
        s.SelectAll();
        s.Move(GridMove.Down, false, 5);
        Assert.Empty(s.EnumerateSelected());
    }

    [Fact]
    public void A_plain_click_is_a_single_cell_and_anything_wider_is_not()
    {
        var s = New(rows: 4, cols: 3);
        Assert.False(s.IsSingleCell);

        s.Click(1, 1, false, false);
        Assert.True(s.IsSingleCell);

        s.Click(2, 2, shift: true, ctrl: false);
        Assert.False(s.IsSingleCell);

        s.ClickRowHeader(0, false, false);
        Assert.False(s.IsSingleCell);
    }
}
