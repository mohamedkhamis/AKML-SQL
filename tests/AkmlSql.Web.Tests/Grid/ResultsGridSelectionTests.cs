using AkmlSql.Web.Services;
using Bunit;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T047, research R4/R5, FR-008–FR-010) — spreadsheet selection, keyboard focus and copy. The grid
/// root is the only tab stop; the focused cell is its aria-activedescendant.
/// </summary>
public sealed class ResultsGridSelectionTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static bool Selected(IRenderedComponent<Shared.ResultSetGrid> cut, int r, int c) =>
        Cell(cut, r, c).GetAttribute("aria-selected") == "true";

    [Fact]
    public void Clicking_a_cell_selects_it_and_makes_it_the_active_descendant()
    {
        var cut = _host.Render(ReadOnlySet());

        ClickCell(cut, 1, 2);

        Assert.True(Selected(cut, 1, 2));
        Assert.False(Selected(cut, 1, 1));
        Assert.Equal("akml-grid-0-1-2", Root(cut).GetAttribute("aria-activedescendant"));
        Assert.Equal("akml-grid-0-1-2", Cell(cut, 1, 2).GetAttribute("id"));
    }

    [Fact]
    public void A_row_number_selects_the_row_a_header_the_column_and_the_corner_everything()
    {
        var cut = _host.Render(ReadOnlySet());

        MouseDown(cut.Find("[data-testid=results-rownum-2]"));
        Assert.All(Enumerable.Range(0, 3), c => Assert.True(Selected(cut, 2, c)));
        Assert.False(Selected(cut, 1, 0));

        MouseDown(cut.Find("[data-testid=results-col-1]"));
        Assert.All(Enumerable.Range(0, 5), r => Assert.True(Selected(cut, r, 1)));
        Assert.False(Selected(cut, 2, 0));

        MouseDown(cut.Find("[data-testid=results-corner]"));
        Assert.All(Enumerable.Range(0, 5), r => Assert.All(Enumerable.Range(0, 3), c => Assert.True(Selected(cut, r, c))));
    }

    [Fact]
    public void Shift_extends_from_the_anchor_and_Ctrl_adds_or_removes()
    {
        var cut = _host.Render(ReadOnlySet());

        ClickCell(cut, 1, 0);
        ClickCell(cut, 3, 1, shift: true);
        Assert.True(Selected(cut, 2, 1));
        Assert.False(Selected(cut, 2, 2));

        ClickCell(cut, 0, 2, ctrl: true);
        Assert.True(Selected(cut, 0, 2));
        Assert.True(Selected(cut, 3, 1));

        ClickCell(cut, 2, 1, ctrl: true);
        Assert.False(Selected(cut, 2, 1));

        MouseDown(cut.Find("[data-testid=results-rownum-4]"), ctrl: true);
        Assert.True(Selected(cut, 4, 0));
        Assert.True(Selected(cut, 1, 0));
    }

    [Fact]
    public void Ctrl_A_selects_everything_and_arrows_move_the_focus()
    {
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 0);

        Press(cut, "ArrowDown");
        Press(cut, "ArrowRight");
        Assert.Equal("akml-grid-0-1-1", Root(cut).GetAttribute("aria-activedescendant"));
        Assert.True(Selected(cut, 1, 1));
        Assert.False(Selected(cut, 0, 0));

        Press(cut, "ArrowDown", shift: true);
        Assert.True(Selected(cut, 1, 1));
        Assert.True(Selected(cut, 2, 1));

        Press(cut, "End", ctrl: true);
        Assert.Equal("akml-grid-0-4-2", Root(cut).GetAttribute("aria-activedescendant"));

        Press(cut, "a", ctrl: true);
        Assert.True(Selected(cut, 0, 0));
        Assert.True(Selected(cut, 4, 2));
    }

    [Fact]
    public void Ctrl_C_copies_tab_separated_text_and_Ctrl_Shift_C_adds_the_header_line()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var cut = _host.Render(ReadOnlySet());

        ClickCell(cut, 0, 0);
        ClickCell(cut, 1, 1, shift: true);
        Press(cut, "c", ctrl: true);
        Press(cut, "C", ctrl: true, shift: true);

        var copies = ui.Invocations["copyText"].Select(i => (string)i.Arguments[0]!).ToArray();
        Assert.Equal("1\talpha\r\n2\tbravo\r\n", copies[0]);
        Assert.Equal("id\tname\r\n1\talpha\r\n2\tbravo\r\n", copies[1]);
        Assert.Contains(_host.Notes.Sent, n => n.Text == "Copied 2 rows." && n.Kind == NotificationKind.Success);
    }

    [Fact]
    public void A_copy_holding_engine_cut_values_says_how_many()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var cut = _host.Render(ReadOnlySet(
            new string?[] { "1", "[text 70000 chars]", null },
            new string?[] { "2", "[text 90000 chars]", null }));

        Press(cut, "a", ctrl: true);
        Press(cut, "c", ctrl: true);

        Assert.Equal("1\t[text 70000 chars]\tNULL\r\n2\t[text 90000 chars]\tNULL\r\n", (string)ui.Invocations["copyText"].Single().Arguments[0]!);
        Assert.Contains(_host.Notes.Sent, n => n.Text.Contains("2 values the engine cut") && n.Kind == NotificationKind.Warning);
    }

    [Fact]
    public void A_copy_the_browser_refuses_is_reported()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Setup<bool>("copyText", _ => true).SetResult(false);
        var cut = _host.Render(ReadOnlySet());

        ClickCell(cut, 0, 0);
        Press(cut, "c", ctrl: true);

        Assert.Contains(_host.Notes.Sent, n => n.Kind == NotificationKind.Error);
    }

    [Fact]
    public void The_grid_root_is_the_only_tab_stop()
    {
        var cut = _host.Render(EditableSet());

        var stops = cut.FindAll("[tabindex]").Where(e => e.GetAttribute("tabindex") != "-1").ToList();

        var stop = Assert.Single(stops);
        Assert.Equal("results-grid", stop.GetAttribute("data-testid"));
        Assert.Equal("0", stop.GetAttribute("tabindex"));
        Assert.Equal("grid", stop.GetAttribute("role"));
        Assert.Equal("true", stop.GetAttribute("aria-multiselectable"));
        Assert.Equal("-1", cut.Find("[data-testid=results-delete-0]").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("[data-testid=results-sort-0]").GetAttribute("tabindex"));
    }

    [Fact]
    public void A_new_result_clears_the_selection()
    {
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 2, 2);

        cut.Render(p => p.Add(x => x.Set, ReadOnlySet()));

        Assert.False(Selected(cut, 2, 2));
        Assert.Null(Root(cut).GetAttribute("aria-activedescendant"));
    }

    [Fact]
    public void A_drag_selects_the_rectangle_from_the_press()
    {
        var cut = _host.Render(ReadOnlySet());

        cut.InvokeAsync(() => cut.Instance.OnCellPointerDownFromJs(1, 0, false, false));
        cut.InvokeAsync(() => cut.Instance.OnDragSelectFromJs(3, 1));

        Assert.True(Selected(cut, 1, 0));
        Assert.True(Selected(cut, 3, 1));
        Assert.True(Selected(cut, 2, 0));
        Assert.False(Selected(cut, 3, 2));
    }

    [Fact]
    public void Up_from_the_top_row_focuses_the_header_and_Left_from_the_first_column_the_row_number()
    {
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 1);

        Press(cut, "ArrowUp");
        Assert.Equal("akml-grid-0-h-1", Root(cut).GetAttribute("aria-activedescendant"));

        Press(cut, "ArrowDown");
        Press(cut, "ArrowLeft");
        Press(cut, "ArrowLeft");
        Assert.Equal("akml-grid-0-0-rn", Root(cut).GetAttribute("aria-activedescendant"));
    }
}
