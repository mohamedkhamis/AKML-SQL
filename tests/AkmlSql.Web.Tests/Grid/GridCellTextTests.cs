using AkmlSql.Web.Shared.Grid;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T041, research R8, FR-002) — the words the grid uses about one cell's value: whether the
/// engine cut it, how big it is, and the hover text for a value the column width hides.
/// </summary>
public sealed class GridCellTextTests
{
    [Fact]
    public void Engine_cut_indicators_are_recognised_through_Core()
    {
        Assert.True(GridCellText.IsEngineCutIndicator("[text 70000 chars]"));
        Assert.True(GridCellText.IsEngineCutIndicator("[binary 12 bytes]"));
        Assert.False(GridCellText.IsEngineCutIndicator("[text]"));
        Assert.False(GridCellText.IsEngineCutIndicator(null));
    }

    [Fact]
    public void DescribeSize_reads_the_engine_cut_size()
    {
        Assert.Equal("70,000 characters", GridCellText.DescribeSize("[text 70000 chars]"));
        Assert.Equal("1,048,576 bytes", GridCellText.DescribeSize("[binary 1048576 bytes]"));
    }

    [Fact]
    public void DescribeSize_of_a_normal_value_is_its_length()
    {
        Assert.Equal("5 characters", GridCellText.DescribeSize("hello"));
        Assert.Equal("1 character", GridCellText.DescribeSize("x"));
        Assert.Equal("0 characters", GridCellText.DescribeSize(""));
    }

    [Fact]
    public void DescribeSize_of_null_says_NULL()
    {
        Assert.Equal("NULL", GridCellText.DescribeSize(null));
    }

    [Fact]
    public void The_tooltip_is_the_first_thousand_characters()
    {
        var longText = new string('a', 1_500);

        var tip = GridCellText.Tooltip(longText)!;

        Assert.Equal(1_001, tip.Length);         // 1,000 characters and an ellipsis
        Assert.EndsWith("…", tip);
        Assert.Equal("short", GridCellText.Tooltip("short"));
        Assert.Null(GridCellText.Tooltip(null));
        Assert.Null(GridCellText.Tooltip(""));
    }
}
