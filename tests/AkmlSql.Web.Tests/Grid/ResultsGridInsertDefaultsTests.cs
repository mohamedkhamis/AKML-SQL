using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Review finding WEB-20: a new row's unset cell is left out of the INSERT, so the column's default applies — it is no longer
/// sent as NULL (which suppressed a nullable column's default), and a NOT NULL column with a default no longer has to be typed.
/// A NOT NULL column without one still must be. dbo.demo here: qty NOT NULL with a default, flag NULL with a default.
/// </summary>
public sealed class ResultsGridInsertDefaultsTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static ExecuteResultSet WithDefaults(bool qtyHasDefault = true)
    {
        var set = EditableSet();
        set.Provenance[2].HasDefault = qtyHasDefault;   // qty int NOT NULL DEFAULT 0
        set.Provenance[3].HasDefault = true;            // flag bit NULL DEFAULT 1
        return set;
    }

    private static void EditInsert(IRenderedComponent<Shared.ResultSetGrid> cut, int insert, int c, string value)
    {
        cut.Find($"[data-testid=results-insert-cell-{insert}-{c}]").Click();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input(value);
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    [Fact]
    public void Unset_cells_are_left_out_so_their_defaults_apply()
    {
        var cut = _host.Render(WithDefaults());
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "new name");

        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));     // qty has a default: nothing to type
        Assert.Equal("(default)", cut.Find("[data-testid=results-insert-cell-0-2]").TextContent.Trim());
        cut.Find("[data-testid=results-apply]").Click();

        var insert = Assert.Single(Assert.Single(_host.Exec.Applied).Edits);
        Assert.Equal(CrudOp.Insert, insert.Op);
        Assert.Equal(new[] { "name" }, insert.SetCells.Select(c => c.BaseColumnName).ToArray());   // no NULL for flag, blob or qty
    }

    [Fact]
    public void A_NOT_NULL_column_without_a_default_still_needs_a_value()
    {
        var cut = _host.Render(WithDefaults(qtyHasDefault: false));
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "new name");

        Assert.NotEmpty(cut.FindAll("[data-testid=results-apply-blocked]"));
        Assert.True(cut.Find("[data-testid=results-apply]").HasAttribute("disabled"));
    }

    [Fact]
    public void A_row_with_nothing_set_is_sent_as_all_defaults()
    {
        var cut = _host.Render(WithDefaults());
        cut.Find("[data-testid=results-add-row]").Click();

        cut.Find("[data-testid=results-apply]").Click();

        var insert = Assert.Single(Assert.Single(_host.Exec.Applied).Edits);
        Assert.Equal(CrudOp.Insert, insert.Op);
        Assert.Empty(insert.SetCells);
    }
}
