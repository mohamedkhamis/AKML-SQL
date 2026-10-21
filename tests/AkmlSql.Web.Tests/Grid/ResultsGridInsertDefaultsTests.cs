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
        set.DefaultsKnown = true;                       // the engine read the table's columns
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

    /// <summary>Re-review REV-06: with the defaults unknown (a cold schema cache, a temp table) the grid does not assume "no
    /// default" — it leaves the NOT NULL column to the server and says the cell is not set.</summary>
    [Fact]
    public void With_unknown_defaults_a_NOT_NULL_column_is_left_to_the_server()
    {
        var set = EditableSet();
        set.DefaultsKnown = false;                      // e.g. the schema's columns had not loaded
        var cut = _host.Render(set);
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "new name");

        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));
        Assert.Equal("(not set)", cut.Find("[data-testid=results-insert-cell-0-2]").TextContent.Trim());
        cut.Find("[data-testid=results-apply]").Click();
        Assert.Equal(new[] { "name" }, Assert.Single(Assert.Single(_host.Exec.Applied).Edits).SetCells.Select(c => c.BaseColumnName).ToArray());
    }

    /// <summary>Re-review REV-06 (the reviewer's probe, assertions kept): a SQL Server insert that returns only its identity
    /// (a table with a trigger) still gives the new row its key, without running the batch again.</summary>
    [Fact]
    public void Inserted_SQL_Server_row_must_merge_returned_identity_without_rerunning_batch()
    {
        var set = EditableSet();
        set.Provenance[2].HasDefault = true;
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] {
            new CrudEditResult { Index = 0, Ok = true, NewIdentity = "4" },
        } };
        var grid = _host.Render(set);
        grid.Find("[data-testid=results-add-row]").Click();
        grid.Find("[data-testid=results-apply]").Click();
        grid.WaitForAssertion(() => Assert.Equal(4, set.Rows.Length));
        Assert.Equal("4", set.Rows[3][0]);
    }

    /// <summary>Re-review REV-06: the stored row a SQL Server insert returns (OUTPUT INSERTED) is what the grid shows —
    /// identity and defaults included — and the new row can be edited at once by its key.</summary>
    [Fact]
    public void The_stored_row_from_OUTPUT_INSERTED_is_shown_and_the_row_is_editable_by_its_key()
    {
        var set = WithDefaults();
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] {
            // ReturnColumns are id, name, qty, flag, blob: the defaults the server filled in come back.
            new CrudEditResult { Index = 0, Ok = true, NewIdentity = "4", Row = new string?[] { "4", "new name", "0", "1", null } },
        } };
        var cut = _host.Render(set);
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "new name");
        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal(4, set.Rows.Length));
        Assert.Equal(new string?[] { "4", "new name", "0", "1", null }, set.Rows[3]);

        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok };
        Cell(cut, 3, 1).DoubleClick();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input("renamed");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid=results-apply]").Click();

        var update = Assert.Single(_host.Exec.Applied[^1].Edits);
        Assert.Equal(CrudOp.Update, update.Op);
        Assert.Equal("4", Assert.Single(update.KeyCells).Value);
    }

    /// <summary>Review round 3 (REV-06): an insert the server committed but did not return shows only what is known — the typed
    /// cell and the identity — marks the rest as not read back, and refuses edits and deletes until the query runs again: its
    /// other values, and so possibly its key, would be guesses.</summary>
    [Fact]
    public void An_insert_that_was_not_read_back_shows_its_unknown_cells_and_cannot_be_edited_or_deleted()
    {
        var set = WithDefaults();
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] {
            new CrudEditResult { Index = 0, Ok = true, NewIdentity = "4", Row = null },
        } };
        var cut = _host.Render(set);
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "new name");
        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal(4, set.Rows.Length));
        Assert.Contains("could not be read back", cut.Find("[data-testid=results-apply-message]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=results-unconfirmed-3]"));
        Assert.Equal("4", Cell(cut, 3, 0).TextContent.Trim());
        Assert.Equal("new name", Cell(cut, 3, 1).TextContent.Trim());
        foreach (var c in new[] { 2, 3, 4 })   // qty and flag took defaults the grid was not told; not shown as NULL
        {
            Assert.Equal("true", Cell(cut, 3, c).GetAttribute("data-unknown"));
            Assert.DoesNotContain("NULL", Cell(cut, 3, c).TextContent);
        }

        Assert.DoesNotContain("akml-cell-editable", Cell(cut, 3, 1).ClassList);
        ClickCell(cut, 3, 1);
        Press(cut, "F2");                                  // the keyboard way in is refused too
        Assert.Empty(cut.FindAll("[data-testid=results-cell-input]"));
        Assert.True(cut.Find("[data-testid=results-delete-3]").HasAttribute("disabled"));
        cut.Find("[data-testid=results-delete-3]").Click();
        Assert.Empty(cut.FindAll("[data-testid=results-apply]"));   // nothing pending: the click was refused

        // The other rows stay editable, and the marker survives the next Apply, which deletes a row before it.
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] { new CrudEditResult { Index = 0, Ok = true, RowsAffected = 1 } } };
        cut.Find("[data-testid=results-delete-0]").Click();
        cut.Find("[data-testid=results-apply]").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, set.Rows.Length));
        Assert.NotNull(cut.Find("[data-testid=results-unconfirmed-2]"));
        Assert.Equal("true", Cell(cut, 2, 2).GetAttribute("data-unknown"));
        Assert.Empty(cut.FindAll("[data-testid=results-unconfirmed-0]"));
    }

    [Fact]
    public void An_unconfirmed_row_stays_locked_when_its_result_grid_is_recreated()
    {
        var set = WithDefaults();
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] {
            new CrudEditResult { Index = 0, Ok = true, NewIdentity = "4", Row = null },
        } };
        var cut = _host.Render(set);
        cut.Find("[data-testid=results-add-row]").Click();
        cut.Find("[data-testid=results-apply]").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, set.Rows.Length));
        cut.Dispose();

        var reopened = _host.Render(set);
        Assert.True(reopened.Find("[data-testid=results-delete-3]").HasAttribute("disabled"));
        Assert.NotNull(reopened.Find("[data-testid=results-unconfirmed-3]"));
        Assert.Equal("true", Cell(reopened, 3, 2).GetAttribute("data-unknown"));
        ClickCell(reopened, 3, 1);
        Press(reopened, "F2");
        Assert.Empty(reopened.FindAll("[data-testid=results-cell-input]"));

        // A new query result has no unresolved inserts, even if it contains the same values.
        var refreshed = WithDefaults();
        refreshed.Rows = set.Rows;
        var fresh = _host.Render(refreshed);
        Assert.False(fresh.Find("[data-testid=results-delete-3]").HasAttribute("disabled"));
        Assert.Empty(fresh.FindAll("[data-testid=results-unconfirmed-3]"));
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("results-save-csv")]
    [InlineData("results-save-tsv")]
    public void Unconfirmed_rows_cannot_be_exported_as_fabricated_NULLs(string action)
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var download = _host.Context.JSInterop.SetupModule("./js/akml-download.js");
        download.SetupVoid("downloadText", _ => true).SetVoidResult();
        var set = WithDefaults();
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] {
            new CrudEditResult { Index = 0, Ok = true, NewIdentity = "4", Row = null },
        } };
        var cut = _host.Render(set);
        cut.Find("[data-testid=results-add-row]").Click();
        cut.Find("[data-testid=results-apply]").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, set.Rows.Length));
        ClickCell(cut, 3, 2);
        if (action == "copy") Press(cut, "c", ctrl: true);
        else
        {
            OpenCellMenu(cut, 3, 2);
            cut.Find($"[data-testid={action}]").Click();
        }

        cut.WaitForAssertion(() => Assert.Contains(_host.Notes.Sent, n => n.Text.Contains("read back") && n.Text.Contains("SELECT")));
        Assert.Empty(ui.Invocations["copyText"]);
        Assert.Empty(download.Invocations["downloadText"]);

        // Confirmed rows remain available for copying.
        ClickCell(cut, 0, 1);
        Press(cut, "c", ctrl: true);
        cut.WaitForAssertion(() => Assert.Single(ui.Invocations["copyText"]));
    }

    /// <summary>Review round 3: the stored row from the server carries no not-read-back marker.</summary>
    [Fact]
    public void A_read_back_insert_has_no_unknown_cells()
    {
        var set = WithDefaults();
        _host.Exec.ApplyReply = new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = new[] {
            new CrudEditResult { Index = 0, Ok = true, NewIdentity = "4", Row = new string?[] { "4", "new name", "0", "1", null } },
        } };
        var cut = _host.Render(set);
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "new name");
        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal(4, set.Rows.Length));
        Assert.Empty(cut.FindAll("[data-testid^=results-unconfirmed-]"));
        Assert.Empty(cut.FindAll("[data-unknown]"));
        Assert.DoesNotContain("read back", cut.Find("[data-testid=results-apply-message]").TextContent);
        Assert.False(cut.Find("[data-testid=results-delete-3]").HasAttribute("disabled"));
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
