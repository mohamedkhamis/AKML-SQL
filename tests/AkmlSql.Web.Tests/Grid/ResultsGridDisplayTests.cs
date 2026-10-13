using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T086, research R11, FR-026) — values read the way SSMS shows them, through Core's
/// SqlValueDisplay; Apply keeps the engine's full-precision wire text, and the editor shows the grid's text only
/// where it converts back without loss (PR #255); copy and Save write the text the grid shows.
/// </summary>
public sealed class ResultsGridDisplayTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static ExecuteResultSet Typed() => new()
    {
        ColumnNames = new[] { "d", "dt", "dt2", "bin", "price" },
        ColumnSqlTypes = new[] { "date", "datetime", "datetime2(7)", "varbinary(16)", "money" },
        ClrTypeHints = new[] { ClrTypeHint.DateTime, ClrTypeHint.DateTime, ClrTypeHint.DateTime, ClrTypeHint.Binary, ClrTypeHint.Decimal },
        Rows = new[]
        {
            new string?[] { "2025-10-01T00:00:00.0000000", "2025-10-01T23:57:23.8800000", "2025-10-01T23:57:23.1234567", Convert.ToBase64String(new byte[] { 0x0f, 0xab }), "12.5" },
            new string?[] { null, null, null, null, null },
        },
        IsEditable = true,
        BaseTable = "t",
        Provenance = new[]
        {
            new ColumnProvenanceDto { Ordinal = 0, BaseColumnName = "d", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 1, BaseColumnName = "dt", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 2, BaseColumnName = "dt2", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 3, BaseColumnName = "bin", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 4, BaseColumnName = "price", AllowDBNull = true, IsKey = true },
        },
    };

    [Fact]
    public void Dates_binary_and_money_read_as_in_SSMS()
    {
        var cut = _host.Render(Typed());

        Assert.Equal("2025-10-01", Cell(cut, 0, 0).TextContent.Trim());
        Assert.Equal("2025-10-01 23:57:23.880", Cell(cut, 0, 1).TextContent.Trim());
        Assert.Equal("2025-10-01 23:57:23.1234567", Cell(cut, 0, 2).TextContent.Trim());
        Assert.Equal("0x0FAB", Cell(cut, 0, 3).TextContent.Trim());
        Assert.Equal("12.5000", Cell(cut, 0, 4).TextContent.Trim());
        Assert.DoesNotContain("[binary", cut.Markup);
    }

    [Fact]
    public void NULL_is_muted()
    {
        var cut = _host.Render(Typed());

        var cell = Cell(cut, 1, 1);
        Assert.Equal("NULL", cell.TextContent.Trim());
        Assert.Contains("akml-cell-null", cell.ClassName);
    }

    [Fact]
    public void The_editor_starts_from_the_shown_text_where_it_converts_back_without_loss()
    {
        var cut = _host.Render(Typed());

        Cell(cut, 0, 1).DoubleClick();
        Assert.Equal("2025-10-01 23:57:23.880", cut.Find("[data-testid=results-cell-input]").GetAttribute("value"));
    }

    [Fact]
    public void Leaving_an_untouched_editor_changes_nothing()
    {
        var cut = _host.Render(Typed());

        Cell(cut, 0, 3).DoubleClick();
        Assert.Equal("0x0FAB", cut.Find("[data-testid=results-cell-input]").GetAttribute("value"));
        cut.Find("[data-testid=results-cell-input]").Change("0x0FAB");

        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
    }

    [Fact]
    public void Date_and_binary_values_typed_as_the_grid_shows_them_reach_Apply_as_wire_text()
    {
        var cut = _host.Render(Typed());

        Cell(cut, 0, 0).DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("2025-10-02");
        Cell(cut, 0, 3).DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("0x1F");

        Assert.Equal("2025-10-02", Cell(cut, 0, 0).TextContent.Trim());
        Assert.Equal("0x1F", Cell(cut, 0, 3).TextContent.Trim());
        cut.Find("[data-testid=results-apply]").Click();
        var cells = Assert.Single(Assert.Single(_host.Exec.Applied).Edits).SetCells;
        Assert.Equal("2025-10-02T00:00:00.0000000", cells.Single(c => c.BaseColumnName == "d").Value);
        Assert.Equal(Convert.ToBase64String(new byte[] { 0x1f }), cells.Single(c => c.BaseColumnName == "bin").Value);
    }

    [Fact]
    public void Copy_and_Save_write_the_text_the_grid_shows()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var download = _host.Context.JSInterop.SetupModule("./js/akml-download.js");
        download.SetupVoid("downloadText", _ => true).SetVoidResult();
        var cut = _host.Render(Typed());

        Press(cut, "a", ctrl: true);
        Press(cut, "c", ctrl: true);
        Assert.Equal(
            "2025-10-01\t2025-10-01 23:57:23.880\t2025-10-01 23:57:23.1234567\t0x0FAB\t12.5000\r\nNULL\tNULL\tNULL\tNULL\tNULL\r\n",
            (string)ui.Invocations["copyText"].Single().Arguments[0]!);

        OpenCellMenu(cut, 0, 0);
        cut.Find("[data-testid=results-save-csv]").Click();
        Assert.Equal(
            "d,dt,dt2,bin,price\r\n2025-10-01,2025-10-01 23:57:23.880,2025-10-01 23:57:23.1234567,0x0FAB,12.5000\r\nNULL,NULL,NULL,NULL,NULL\r\n",
            (string)download.Invocations["downloadText"].Single().Arguments[2]!);
    }

    [Fact]
    public void A_large_binary_value_shows_a_preview_but_copies_in_full()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var set = Typed();
        set.Rows[0][3] = Convert.ToBase64String(new byte[2_000]);
        var cut = _host.Render(set);

        Assert.EndsWith("…", Cell(cut, 0, 3).TextContent.Trim());

        ClickCell(cut, 0, 3);
        Press(cut, "c", ctrl: true);
        Assert.Equal(2 + 4_000 + 2, ((string)ui.Invocations["copyText"].Single().Arguments[0]!).Length);   // 0x + hex + CRLF
    }
}
