using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Shared.Grid;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T046, research R1–R3, FR-001–FR-007) — column widths of one result set. Every row shares the
/// one --akml-grid-cols track list on the grid root (row-number track first), so widths never reflow; a new
/// result is fitted afresh, and the user's widths survive re-renders of the same result.
/// </summary>
public sealed class ResultsGridLayoutTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void The_root_carries_the_track_list_with_the_row_number_track_first()
    {
        var set = ReadOnlySet();
        var cut = _host.Render(set);

        var tracks = Tracks(cut);

        Assert.Equal(set.ColumnNames.Length + 1, tracks.Length);
        Assert.Equal(GridColumnLayout.RowNumberWidth(set.Rows.Length), tracks[0]);
    }

    [Fact]
    public void Without_measurement_the_estimate_fits_each_column()
    {
        var cut = _host.Render(ReadOnlySet());

        var tracks = Tracks(cut);

        Assert.Equal(EstimatedFit("id", "1", "2", "3"), tracks[1]);
        Assert.Equal(EstimatedFit("name", "charlie", "alpha", "bravo"), tracks[2]);
        // datetime shows milliseconds: "2025-10-01 08:00:00.000".
        Assert.Equal(EstimatedFit("created", "2025-10-01 08:00:00.000"), tracks[3]);
    }

    [Fact]
    public void Measurement_runs_once_per_new_result_with_at_most_four_strings_a_column()
    {
        var module = _host.Context.JSInterop.SetupModule("./js/akml-results-grid.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<MeasuredWidth[]?>("measureColumns", _ => true)
            .SetResult(new[] { new MeasuredWidth(20, 100), new MeasuredWidth(30, 200), new MeasuredWidth(40, 50) });

        var set = ReadOnlySet();
        var cut = _host.Render(set);

        cut.WaitForAssertion(() => Assert.Single(module.Invocations["measureColumns"]));
        var call = module.Invocations["measureColumns"].Single();
        var headers = Assert.IsType<string[]>(call.Arguments[1]);
        var values = Assert.IsType<string[][]>(call.Arguments[2]);
        Assert.Equal(new[] { "id", "name", "created" }, headers);
        Assert.All(values, v => Assert.True(v.Length <= GridColumnLayout.MaxCandidates));
        cut.WaitForAssertion(() => Assert.Equal(GridColumnLayout.Fit(30, 200), Tracks(cut)[2]));

        // A re-render of the same result measures nothing more.
        cut.Render(p => p.Add(x => x.Set, set).Add(x => x.ShowColumnTypes, true));
        Assert.Single(module.Invocations["measureColumns"]);

        // A new result is measured afresh.
        cut.Render(p => p.Add(x => x.Set, ReadOnlySet()));
        cut.WaitForAssertion(() => Assert.Equal(2, module.Invocations["measureColumns"].Count));
    }

    [Fact]
    public void A_drag_resize_changes_only_its_own_track()
    {
        var cut = _host.Render(ReadOnlySet());
        var before = Tracks(cut);

        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(2, 300));

        var after = Tracks(cut);
        Assert.Equal(300, after[3]);
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[1], after[1]);
        Assert.Equal(before[2], after[2]);
    }

    [Fact]
    public void Widths_survive_a_re_render_of_the_same_result_and_a_new_result_is_fitted_afresh()
    {
        var set = ReadOnlySet();
        var cut = _host.Render(set);
        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(1, 333));

        cut.Render(p => p.Add(x => x.Set, set).Add(x => x.ShowColumnTypes, true));
        Assert.Equal(333, Tracks(cut)[2]);

        cut.Render(p => p.Add(x => x.Set, ReadOnlySet()));
        Assert.Equal(EstimatedFit("name", "charlie"), Tracks(cut)[2]);
    }

    [Fact]
    public void Shift_Alt_Right_on_a_focused_header_widens_that_column_by_16px()
    {
        var cut = _host.Render(ReadOnlySet());
        int before = Tracks(cut)[2];

        MouseDown(cut.Find("[data-testid=results-col-1]"));
        Press(cut, "ArrowRight", shift: true, alt: true);
        Assert.Equal(before + ResultSetGrid.ResizeStepPx, Tracks(cut)[2]);

        Press(cut, "ArrowLeft", shift: true, alt: true);
        Press(cut, "ArrowLeft", shift: true, alt: true);
        Assert.Equal(before - ResultSetGrid.ResizeStepPx, Tracks(cut)[2]);
    }

    [Fact]
    public void Double_clicking_a_column_edge_fits_it_again()
    {
        var cut = _host.Render(ReadOnlySet());
        int fitted = Tracks(cut)[2];
        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(1, 400));

        cut.Find("[data-testid=results-col-resizer-1]").DoubleClick();

        Assert.Equal(fitted, Tracks(cut)[2]);
    }

    [Fact]
    public void The_header_menu_fits_all_columns_and_resets_widths()
    {
        var cut = _host.Render(ReadOnlySet());
        var fitted = Tracks(cut);
        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(0, 200));
        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(2, 300));

        OpenHeaderMenu(cut, 0);
        cut.Find("[data-testid=results-menu-autofit-all]").Click();
        cut.WaitForAssertion(() => Assert.Equal(fitted, Tracks(cut)));

        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(1, 250));
        OpenHeaderMenu(cut, 1);
        cut.Find("[data-testid=results-menu-reset-widths]").Click();
        cut.WaitForAssertion(() => Assert.Equal(fitted, Tracks(cut)));
    }

    [Fact]
    public void The_header_menu_fits_one_column()
    {
        var cut = _host.Render(ReadOnlySet());
        int fitted = Tracks(cut)[1];
        cut.InvokeAsync(() => cut.Instance.OnColumnResizedFromJs(0, 222));

        OpenHeaderMenu(cut, 0);
        cut.Find("[data-testid=results-menu-autofit]").Click();

        cut.WaitForAssertion(() => Assert.Equal(fitted, Tracks(cut)[1]));
    }

    [Fact]
    public void A_zero_row_result_sizes_its_columns_to_the_headers()
    {
        var set = ReadOnlySet();
        set.Rows = Array.Empty<string?[]>();

        var cut = _host.Render(set);

        var tracks = Tracks(cut);
        Assert.Equal(EstimatedFit("id"), tracks[1]);
        Assert.Equal(EstimatedFit("name"), tracks[2]);
        Assert.Equal(EstimatedFit("created"), tracks[3]);
    }

    [Fact]
    public void Every_cell_names_its_display_row_and_column()
    {
        var cut = _host.Render(ReadOnlySet());

        var cell = Cell(cut, 2, 1);

        Assert.Equal("2", cell.GetAttribute("data-r"));
        Assert.Equal("1", cell.GetAttribute("data-c"));
        Assert.Equal("1", cut.Find("[data-testid=results-col-resizer-1]").GetAttribute("data-col"));
    }
}
