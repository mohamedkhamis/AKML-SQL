using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 042 (task T136, FR-130 to FR-134). The grid over a PostgreSQL result set: every cell that goes to the engine carries its
/// column's type so the server can read the text as the column's type, the columns to read back are asked for, what the server
/// stored replaces what was typed without running the query again, and a result that cannot be edited says why. The behaviour the
/// SQL Server grid has (Set to NULL, invalid cells, confirmations) is unchanged and covered by its own tests.
/// </summary>
public sealed class ResultSetGridPgApplyTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>public.items: id integer serial PK, name text NOT NULL, price numeric(10,2), added date, tags text[].</summary>
    private static ExecuteResultSet ItemsSet(params string?[][] rows) => new()
    {
        ColumnNames = new[] { "id", "name", "price", "added", "tags" },
        ColumnSqlTypes = new[] { "integer", "text", "numeric(10,2)", "date", "text[]" },
        ClrTypeHints = new[] { ClrTypeHint.ServerNumber, ClrTypeHint.ServerText, ClrTypeHint.ServerNumber, ClrTypeHint.ServerText, ClrTypeHint.ServerText },
        Rows = rows.Length > 0 ? rows : new[]
        {
            new string?[] { "1", "Ada", "9.50", "2026-01-02", "{a,b}" },
            new string?[] { "2", "Alan", null, "2026-01-03", null },
        },
        IsEditable = true,
        BaseSchema = "public",
        BaseTable = "items",
        Provenance = new[]
        {
            new ColumnProvenanceDto { Ordinal = 0, BaseColumnName = "id", IsKey = true, IsTruePrimaryKey = true, IsAutoIncrement = true, AllowDBNull = false },
            new ColumnProvenanceDto { Ordinal = 1, BaseColumnName = "name", AllowDBNull = false },
            new ColumnProvenanceDto { Ordinal = 2, BaseColumnName = "price", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 3, BaseColumnName = "added", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 4, BaseColumnName = "tags", AllowDBNull = true },
        },
    };

    private static void Edit(IRenderedComponent<Shared.ResultSetGrid> cut, int r, int c, string value)
    {
        Cell(cut, r, c).DoubleClick();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input(value);
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static void EditInsert(IRenderedComponent<Shared.ResultSetGrid> cut, int insert, int c, string value)
    {
        cut.Find($"[data-testid=results-insert-cell-{insert}-{c}]").Click();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input(value);
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    // ---- what goes to the engine ------------------------------------------------------------------------------------

    [Fact]
    public void An_update_carries_the_column_type_of_every_set_and_key_cell_and_asks_for_the_row_back()
    {
        var cut = _host.Render(ItemsSet());
        Edit(cut, 0, 2, "10");
        Edit(cut, 0, 4, "{x,\"y z\"}");

        cut.Find("[data-testid=results-apply]").Click();

        var request = Assert.Single(_host.Exec.Applied);
        var edit = Assert.Single(request.Edits);
        Assert.Equal(CrudOp.Update, edit.Op);
        Assert.Equal(new[] { ("price", "numeric(10,2)"), ("tags", "text[]") }, edit.SetCells.Select(c => (c.BaseColumnName, c.TypeName!)).ToArray());
        var key = Assert.Single(edit.KeyCells);
        Assert.Equal(("id", "integer", "1"), (key.BaseColumnName, key.TypeName, key.Value));
        Assert.Equal(new[] { "id", "name", "price", "added", "tags" }, request.ReturnColumns);
        Assert.Equal("public", request.BaseSchema);
        Assert.Equal("items", request.BaseTable);
    }

    [Fact]
    public void An_insert_carries_the_column_types_and_leaves_the_generated_key_out()
    {
        var cut = _host.Render(ItemsSet());
        cut.Find("[data-testid=results-add-row]").Click();
        // Its name and date are typed.
        EditInsert(cut, 0, 1, "Grace");
        EditInsert(cut, 0, 3, "2026-02-03");

        cut.Find("[data-testid=results-apply]").Click();

        var edit = Assert.Single(Assert.Single(_host.Exec.Applied).Edits);
        Assert.Equal(CrudOp.Insert, edit.Op);
        Assert.DoesNotContain(edit.SetCells, c => c.BaseColumnName == "id");
        Assert.Equal("text", edit.SetCells.Single(c => c.BaseColumnName == "name").TypeName);
        Assert.Equal("date", edit.SetCells.Single(c => c.BaseColumnName == "added").TypeName);
        Assert.Equal("Grace", edit.SetCells.Single(c => c.BaseColumnName == "name").Value);
    }

    [Fact]
    public void A_delete_is_keyed_by_the_original_key_with_its_type()
    {
        var cut = _host.Render(ItemsSet());
        cut.Find("[data-testid=results-delete-1]").Click();

        cut.Find("[data-testid=results-apply]").Click();

        var edit = Assert.Single(Assert.Single(_host.Exec.Applied).Edits);
        Assert.Equal(CrudOp.Delete, edit.Op);
        var key = Assert.Single(edit.KeyCells);
        Assert.Equal(("id", "integer", "2"), (key.BaseColumnName, key.TypeName, key.Value));
    }

    // ---- what comes back --------------------------------------------------------------------------------------------

    [Fact]
    public void The_row_the_server_stored_replaces_what_was_typed()
    {
        var cut = _host.Render(ItemsSet());
        Edit(cut, 0, 2, "5");     // typed "5"; numeric(10,2) stores "5.00"
        _host.Exec.ApplyReply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Ok,
            Results = new[] { new CrudEditResult { Index = 0, Ok = true, RowsAffected = 1, Row = new string?[] { "1", "Ada", "5.00", "2026-01-02", "{a,b}" } } },
        };

        cut.Find("[data-testid=results-apply]").Click();

        Assert.Equal("5.00", Cell(cut, 0, 2).TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
    }

    [Fact]
    public void An_inserted_row_shows_the_key_and_defaults_the_server_filled_in()
    {
        var cut = _host.Render(ItemsSet());
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 1, "Grace");
        _host.Exec.ApplyReply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Ok,
            Results = new[] { new CrudEditResult { Index = 0, Ok = true, RowsAffected = 1, Row = new string?[] { "21", "Grace", null, "2026-10-14", null } } },
        };

        cut.Find("[data-testid=results-apply]").Click();

        Assert.Equal("21", Cell(cut, 2, 0).TextContent.Trim());
        Assert.Equal("2026-10-14", Cell(cut, 2, 3).TextContent.Trim());
    }

    [Fact]
    public void Each_returned_row_lands_on_the_row_it_was_written_for_when_edits_and_deletes_are_mixed()
    {
        var cut = _host.Render(ItemsSet());
        cut.Find("[data-testid=results-delete-0]").Click();      // row 0 goes: the delete is edit 0
        Edit(cut, 1, 1, "Alan T");                              // row 1 is renamed: the update is edit 1
        _host.Exec.ApplyReply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Ok,
            Results = new[]
            {
                new CrudEditResult { Index = 0, Ok = true, RowsAffected = 1 },
                new CrudEditResult { Index = 1, Ok = true, RowsAffected = 1, Row = new string?[] { "2", "ALAN T", null, "2026-01-03", null } },
            },
        };

        cut.Find("[data-testid=results-apply]").Click();

        Assert.Single(DisplayOrder(cut));
        Assert.Equal("ALAN T", Cell(cut, 0, 1).TextContent.Trim());      // what a trigger made of it
    }

    [Fact]
    public void A_failed_apply_shows_the_failing_edits_message_keeps_the_pending_changes_and_marks_nothing_applied()
    {
        var cut = _host.Render(ItemsSet());
        Edit(cut, 0, 1, "Ada L");
        Edit(cut, 1, 2, "abc");
        _host.Exec.ApplyReply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Error,
            ErrorMessage = "Edit 1 failed: invalid input syntax for type numeric: \"abc\"",
            Results = new[]
            {
                new CrudEditResult { Index = 0, Ok = false, Error = "Rolled back — edit 1 failed." },
                new CrudEditResult { Index = 1, Ok = false, Error = "invalid input syntax for type numeric: \"abc\"" },
            },
        };

        cut.Find("[data-testid=results-apply]").Click();

        Assert.Contains("invalid input syntax for type numeric", cut.Find("[data-testid=results-apply-message]").TextContent);
        Assert.DoesNotContain("Rolled back", cut.Find("[data-testid=results-apply-message]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=results-applybar]"));      // the edits are still pending: correct and apply again
        Assert.Equal("Ada L", Cell(cut, 0, 1).TextContent.Trim());
    }

    // ---- results that cannot be edited ------------------------------------------------------------------------------

    private static ExecuteResultSet ReadOnlyWithReason(string reason)
    {
        var set = ReadOnlySet();
        set.ReadOnlyReason = reason;
        return set;
    }

    [Theory]
    [InlineData("The result comes from more than one table.")]
    [InlineData("The table has no primary key, so a row cannot be identified.")]
    [InlineData("The result comes from a view, and a view cannot be edited.")]
    public void A_read_only_result_says_why_beside_the_grid(string reason)
    {
        var cut = _host.Render(ReadOnlyWithReason(reason));

        var note = cut.Find("[data-testid=results-readonly-reason]");
        Assert.Contains(reason, note.TextContent);
        Assert.Equal("note", note.GetAttribute("role"));
        Assert.Empty(cut.FindAll("[data-testid=results-add-row]"));
    }

    [Fact]
    public void A_read_only_result_without_a_reason_shows_no_note_as_before()
    {
        var cut = _host.Render(ReadOnlySet());

        Assert.Empty(cut.FindAll("[data-testid=results-readonly-reason]"));
    }

    [Fact]
    public void An_editable_result_shows_no_read_only_note()
    {
        var cut = _host.Render(ItemsSet());

        Assert.Empty(cut.FindAll("[data-testid=results-readonly-reason]"));
    }

    [Fact]
    public void Ctrl_0_on_a_read_only_result_with_a_reason_says_the_reason_and_changes_nothing()
    {
        var reason = "The result comes from more than one table.";
        var cut = _host.Render(ReadOnlyWithReason(reason));
        ClickCell(cut, 0, 1);

        Press(cut, "0", ctrl: true);

        Assert.Contains(_host.Notes.Sent, m => m.Text == reason);
        Assert.Equal("alpha", Cell(cut, 0, 1).TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
    }
}
