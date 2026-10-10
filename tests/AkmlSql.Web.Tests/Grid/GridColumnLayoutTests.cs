using AkmlSql.Web.Shared.Grid;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T038, research R1/R2, FR-001–FR-007) — the widths of one result set's columns. Each
/// column is as wide as the wider of its header and its widest value, up to a maximum, never below a
/// readable minimum; a width the user dragged stays until they reset it; every row shares one CSS
/// track list, so widths never reflow.
/// </summary>
public sealed class GridColumnLayoutTests
{
    [Fact]
    public void The_planning_constants()
    {
        Assert.Equal(48, GridColumnLayout.MinColumnPx);
        Assert.Equal(480, GridColumnLayout.MaxColumnPx);
        Assert.Equal(120, GridColumnLayout.DefaultColumnPx);
    }

    [Fact]
    public void A_new_layout_uses_the_default_width_and_no_user_widths()
    {
        var layout = new GridColumnLayout(columnCount: 3, rowCount: 10);

        Assert.Equal(new[] { 120, 120, 120 }, layout.WidthsPx);
        Assert.False(layout.IsUserSet(0));
    }

    [Fact]
    public void SelectCandidates_keeps_the_header_and_the_three_longest_values_scanning_every_row()
    {
        var values = new[] { "a", "abcdef", "ab", null, "abc", "abcdefghij", "abcd" };

        var c = GridColumnLayout.SelectCandidates("id", values);

        Assert.Equal("id", c.Header);
        Assert.Equal(new[] { "abcdefghij", "abcdef", "abcd" }, c.Values);
    }

    [Fact]
    public void SelectCandidates_reads_a_long_value_far_down_the_result()
    {
        var values = Enumerable.Repeat("x", 5_000).Append(new string('y', 300)).ToArray();

        var c = GridColumnLayout.SelectCandidates("v", values);

        Assert.Equal(300, c.Values[0].Length);
    }

    [Fact]
    public void Fit_adds_padding_to_the_widest_value()
    {
        // 80 px of text + cell padding.
        Assert.Equal(80 + GridColumnLayout.CellPaddingPx, GridColumnLayout.Fit(headerPx: 20, valuePx: 80));
    }

    [Fact]
    public void A_header_longer_than_every_value_sets_the_width()
    {
        int width = GridColumnLayout.Fit(headerPx: 150, valuePx: 30);

        Assert.Equal(150 + GridColumnLayout.CellPaddingPx + GridColumnLayout.HeaderAllowancePx, width);
    }

    [Fact]
    public void Fit_never_goes_below_the_minimum()
    {
        Assert.Equal(GridColumnLayout.MinColumnPx, GridColumnLayout.Fit(headerPx: 2, valuePx: 3));
    }

    [Fact]
    public void Fit_stops_at_the_maximum()
    {
        Assert.Equal(GridColumnLayout.MaxColumnPx, GridColumnLayout.Fit(headerPx: 40, valuePx: 3_000));
        Assert.Equal(GridColumnLayout.MaxColumnPx, GridColumnLayout.Fit(headerPx: 900, valuePx: 10));
    }

    [Fact]
    public void The_fallback_estimate_is_seven_pixels_per_character()
    {
        Assert.Equal(70, GridColumnLayout.EstimatePx("0123456789"));
        Assert.Equal(0, GridColumnLayout.EstimatePx(null));
    }

    [Theory]
    [InlineData(0, 36)]
    [InlineData(9, 36)]
    [InlineData(99, 36)]
    [InlineData(999, 44)]
    [InlineData(1_000, 52)]
    [InlineData(100_000, 68)]
    public void The_row_number_track_follows_the_digit_count(int rows, int expected)
    {
        Assert.Equal(expected, GridColumnLayout.RowNumberWidth(rows));
    }

    [Fact]
    public void ToCssTracks_puts_the_row_number_track_first()
    {
        var layout = new GridColumnLayout(columnCount: 3, rowCount: 5);
        layout.ApplyFit(0, 72);
        layout.ApplyFit(1, 236);
        layout.ApplyFit(2, 480);

        Assert.Equal("36px 72px 236px 480px", layout.ToCssTracks());
    }

    [Fact]
    public void A_user_width_survives_a_later_fit_until_reset()
    {
        var layout = new GridColumnLayout(2, 5);
        layout.ApplyFit(0, 100);
        layout.SetUserWidth(0, 300);

        layout.ApplyFit(0, 90);   // e.g. auto-fit of all columns after more rows arrived
        Assert.Equal(300, layout.WidthsPx[0]);
        Assert.True(layout.IsUserSet(0));

        layout.ResetWidths();
        Assert.False(layout.IsUserSet(0));
        Assert.Equal(90, layout.WidthsPx[0]);   // back to the fitted width
    }

    [Fact]
    public void Auto_fitting_one_column_replaces_its_user_width()
    {
        var layout = new GridColumnLayout(2, 5);
        layout.SetUserWidth(1, 400);

        layout.AutoFit(1, 140);

        Assert.False(layout.IsUserSet(1));
        Assert.Equal(140, layout.WidthsPx[1]);
    }

    [Fact]
    public void A_user_width_may_exceed_the_maximum_but_not_go_below_the_minimum()
    {
        var layout = new GridColumnLayout(1, 5);

        layout.SetUserWidth(0, 900);
        Assert.Equal(900, layout.WidthsPx[0]);

        layout.SetUserWidth(0, 10);
        Assert.Equal(GridColumnLayout.MinColumnPx, layout.WidthsPx[0]);
    }

    [Fact]
    public void Out_of_range_columns_are_ignored()
    {
        var layout = new GridColumnLayout(1, 5);

        layout.SetUserWidth(5, 200);
        layout.ApplyFit(-1, 200);

        Assert.Equal(new[] { 120 }, layout.WidthsPx);
    }

    [Fact]
    public void The_row_number_track_follows_a_new_row_count_and_keeps_the_column_widths()
    {
        var layout = new GridColumnLayout(2, 99);
        layout.SetUserWidth(1, 300);

        layout.SetRowCount(100);

        Assert.Equal(GridColumnLayout.RowNumberWidth(100), layout.RowNumberWidthPx);
        Assert.Equal($"{GridColumnLayout.RowNumberWidth(100)}px 120px 300px", layout.ToCssTracks());
    }
}
