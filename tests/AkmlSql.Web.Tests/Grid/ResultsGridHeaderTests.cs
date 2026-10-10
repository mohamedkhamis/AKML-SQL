using Bunit;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T052, research R15, FR-011, FR-103) — the column type on hover and on keyboard focus, the
/// optional second header line, and the name of an unnamed column.
/// </summary>
public sealed class ResultsGridHeaderTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void The_header_title_gives_the_declared_type()
    {
        var cut = _host.Render(ReadOnlySet());

        Assert.Equal("name (nvarchar(50))", cut.Find("[data-testid=results-col-1]").GetAttribute("title"));
        Assert.Equal("created (datetime)", cut.Find("[data-testid=results-col-2]").GetAttribute("title"));
    }

    [Fact]
    public void A_column_from_a_table_adds_its_nullability()
    {
        var cut = _host.Render(EditableSet());

        Assert.Equal("qty (int, NOT NULL)", cut.Find("[data-testid=results-col-2]").GetAttribute("title"));
        Assert.Equal("name (nvarchar(50), NULL)", cut.Find("[data-testid=results-col-1]").GetAttribute("title"));
    }

    [Fact]
    public void Keyboard_focus_on_a_header_shows_the_type_in_a_tooltip_that_Escape_dismisses()
    {
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 1);

        Press(cut, "ArrowUp");

        var tooltip = cut.Find("[data-testid=results-cell-tooltip]");
        Assert.Equal("name (nvarchar(50))", tooltip.TextContent);
        Assert.Equal("tooltip", tooltip.GetAttribute("role"));
        Assert.Equal(tooltip.GetAttribute("id"), cut.Find("[data-testid=results-col-1]").GetAttribute("aria-describedby"));

        Press(cut, "Escape");
        Assert.Empty(cut.FindAll("[data-testid=results-cell-tooltip]"));
        Assert.Null(cut.Find("[data-testid=results-col-1]").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void A_pointer_press_on_a_header_shows_no_focus_tooltip()
    {
        var cut = _host.Render(ReadOnlySet());

        MouseDown(cut.Find("[data-testid=results-col-1]"));

        Assert.Empty(cut.FindAll("[data-testid=results-cell-tooltip]"));
    }

    [Fact]
    public void The_type_line_appears_only_with_the_setting_on()
    {
        var off = _host.Render(ReadOnlySet());
        Assert.Empty(off.FindAll("[data-testid=results-col-type-0]"));

        var on = _host.Render(ReadOnlySet(), showColumnTypes: true);
        Assert.Equal("int", on.Find("[data-testid=results-col-type-0]").TextContent);
        Assert.Equal("nvarchar(50)", on.Find("[data-testid=results-col-type-1]").TextContent);
    }

    [Fact]
    public void An_unnamed_column_reads_No_column_name()
    {
        var set = ReadOnlySet();
        set.ColumnNames = new[] { "id", "", "created" };

        var cut = _host.Render(set);

        Assert.Equal("(No column name)", cut.Find("[data-testid=results-col-1] .akml-col-name").TextContent);
    }

    [Fact]
    public void A_focused_cell_the_column_hides_shows_its_value_in_a_tooltip()
    {
        var longText = new string('w', 300);
        var cut = _host.Render(ReadOnlySet(new string?[] { "1", longText, null }, new string?[] { "2", "short", null }));
        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(1, 100));
        ClickCell(cut, 1, 1);

        Press(cut, "ArrowUp");   // keyboard focus on row 0's long value

        Assert.Equal(longText, cut.Find("[data-testid=results-cell-tooltip]").TextContent);
        Assert.Equal(longText, Cell(cut, 0, 1).GetAttribute("title"));

        Press(cut, "ArrowDown");   // "short" fits: no tooltip
        Assert.Empty(cut.FindAll("[data-testid=results-cell-tooltip]"));
    }
}
