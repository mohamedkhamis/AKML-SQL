using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T050, research R6, N23, FR-012, FR-016) — sorting changes only the order rows are shown in.
/// Pending edits, deletes and Apply stay on the fetched row they were made on.
/// </summary>
public sealed class ResultsGridSortTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static ExecuteResultSet Numbers() => ReadOnlySet(
        new string?[] { "10", "b", null },
        new string?[] { "2", "a", null },
        new string?[] { "33", "c", null },
        new string?[] { null, "d", null });

    [Fact]
    public void The_sort_control_cycles_ascending_descending_and_none()
    {
        var cut = _host.Render(Numbers());
        var header = () => cut.Find("[data-testid=results-col-0]");

        cut.Find("[data-testid=results-sort-0]").Click();
        Assert.Equal("ascending", header().GetAttribute("aria-sort"));
        Assert.Equal(new[] { 3, 1, 0, 2 }, DisplayOrder(cut));   // NULL first, then 2 < 10 < 33

        cut.Find("[data-testid=results-sort-0]").Click();
        Assert.Equal("descending", header().GetAttribute("aria-sort"));
        Assert.Equal(new[] { 2, 0, 1, 3 }, DisplayOrder(cut));

        cut.Find("[data-testid=results-sort-0]").Click();
        Assert.Null(header().GetAttribute("aria-sort"));
        Assert.Equal(new[] { 0, 1, 2, 3 }, DisplayOrder(cut));
        Assert.Equal("Sort", cut.Find("[data-testid=results-sort-0]").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_header_click_selects_the_column_and_does_not_sort()
    {
        var cut = _host.Render(Numbers());

        MouseDown(cut.Find("[data-testid=results-col-0]"));

        Assert.Equal(new[] { 0, 1, 2, 3 }, DisplayOrder(cut));
        Assert.Equal("true", Cell(cut, 2, 0).GetAttribute("aria-selected"));
        Assert.Null(cut.Find("[data-testid=results-col-0]").GetAttribute("aria-sort"));
    }

    [Fact]
    public void Row_numbers_stay_in_screen_order_after_a_sort()
    {
        var cut = _host.Render(Numbers());

        cut.Find("[data-testid=results-sort-0]").Click();

        Assert.Equal("1", cut.Find("[data-testid=results-rownum-3]").QuerySelector(".akml-rownum")!.TextContent);
        Assert.Equal("0", Cell(cut, 3, 0).GetAttribute("data-r"));
    }

    [Fact]
    public void Pending_edits_stay_on_their_rows_and_Apply_targets_the_edited_row()
    {
        var cut = _host.Render(EditableSet());
        Cell(cut, 2, 1).DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("zulu");

        cut.Find("[data-testid=results-sort-2]").Click();   // by qty: 2 (row 1), 10 (row 0), 33 (row 2)
        Assert.Equal(new[] { 1, 0, 2 }, DisplayOrder(cut));
        Assert.Equal("zulu", Cell(cut, 2, 1).TextContent.Trim());
        Assert.Contains("akml-row-edited", cut.Find("[data-testid=results-row-2]").ClassName);

        cut.Find("[data-testid=results-apply]").Click();
        var edit = Assert.Single(Assert.Single(_host.Exec.Applied).Edits);
        Assert.Equal("zulu", edit.SetCells.Single().Value);
        Assert.Equal("3", edit.KeyCells.Single().Value);
    }

    [Fact]
    public void The_sort_uses_the_edited_value()
    {
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 2).DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("99");

        cut.Find("[data-testid=results-sort-2]").Click();

        Assert.Equal(new[] { 1, 2, 0 }, DisplayOrder(cut));
    }

    [Fact]
    public void After_an_Apply_the_page_does_not_re_run_and_the_sort_is_applied_to_the_committed_rows()
    {
        var cut = _host.Render(EditableSet());
        cut.Find("[data-testid=results-sort-2]").Click();          // qty asc: rows 1 (2), 0 (10), 2 (33)
        Cell(cut, 1, 2).DoubleClick();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input("50");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(new[] { 1, 0, 2 }, DisplayOrder(cut));       // editing does not move the row

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { 0, 2, 1 }, DisplayOrder(cut)));   // 10, 33, 50
        Assert.Equal("ascending", cut.Find("[data-testid=results-col-2]").GetAttribute("aria-sort"));
        Assert.Equal("50", Cell(cut, 1, 2).TextContent.Trim());
    }

    [Fact]
    public void A_new_result_clears_the_sort()
    {
        var cut = _host.Render(Numbers());
        cut.Find("[data-testid=results-sort-0]").Click();

        cut.Render(p => p.Add(x => x.Set, Numbers()));

        Assert.Null(cut.Find("[data-testid=results-col-0]").GetAttribute("aria-sort"));
        Assert.Equal(new[] { 0, 1, 2, 3 }, DisplayOrder(cut));
    }

    [Fact]
    public void The_header_menu_sorts_and_clears_the_sort()
    {
        var cut = _host.Render(Numbers());

        OpenHeaderMenu(cut, 0);
        cut.Find("[data-testid=results-menu-sort-desc]").Click();
        cut.WaitForAssertion(() => Assert.Equal(new[] { 2, 0, 1, 3 }, DisplayOrder(cut)));

        OpenHeaderMenu(cut, 0);
        cut.Find("[data-testid=results-menu-sort-clear]").Click();
        cut.WaitForAssertion(() => Assert.Equal(new[] { 0, 1, 2, 3 }, DisplayOrder(cut)));
    }
}
