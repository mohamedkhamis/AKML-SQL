using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// UI scan 2026-11-03 (W3) — a committed value that cannot be a value of its column's type is invalid the way an emptied
/// NOT NULL cell is (spec 041 FR-014): a glyph with a type-specific title, the typed text kept so it can be corrected, and
/// Apply blocked until it is fixed or set to NULL. What the servers accept is accepted — SQL Server bit 0/1/true/false,
/// PostgreSQL boolean t/f/true/false/yes/no/on/off/1/0, invariant numbers (a sign, a decimal point, an exponent; no
/// thousands separator), dates as the grid shows them — and what only the server can judge is left to it. A failure the
/// server still reports names the row and column and marks that cell, without blocking a second Apply.
/// </summary>
public sealed class ResultsGridCommitValidationTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    // ── Sets ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// dbo.typed (SQL Server): 0 id int PK identity, 1 amount money, 2 price decimal(10,2), 3 ratio float, 4 qty int NOT NULL,
    /// 5 small tinyint, 6 flag bit, 7 d date, 8 dt datetime2(3), 9 t time(0), 10 dto datetimeoffset(7), 11 uid uniqueidentifier,
    /// 12 blob varbinary(16), 13 name nvarchar(50). Row 0 holds a value in every column; row 1 is NULL but for id and qty.
    /// </summary>
    private static ExecuteResultSet SqlServerSet() => new()
    {
        ColumnNames = new[] { "id", "amount", "price", "ratio", "qty", "small", "flag", "d", "dt", "t", "dto", "uid", "blob", "name" },
        ColumnSqlTypes = new[] { "int", "money", "decimal(10,2)", "float", "int", "tinyint", "bit", "date", "datetime2(3)", "time(0)", "datetimeoffset(7)", "uniqueidentifier", "varbinary(16)", "nvarchar(50)" },
        ClrTypeHints = new[]
        {
            ClrTypeHint.Int64, ClrTypeHint.Decimal, ClrTypeHint.Decimal, ClrTypeHint.Double, ClrTypeHint.Int64, ClrTypeHint.Int64, ClrTypeHint.Bool,
            ClrTypeHint.DateTime, ClrTypeHint.DateTime, ClrTypeHint.String, ClrTypeHint.DateTimeOffset, ClrTypeHint.Guid, ClrTypeHint.Binary, ClrTypeHint.String,
        },
        Rows = new[]
        {
            new string?[] { "1", "1.5", "9.99", "0.25", "10", "7", "1", "2025-01-01T00:00:00.0000000", "2025-01-01T08:00:00.0000000", "08:00:00", "2025-01-01T08:00:00.0000000+02:00", "0f8fad5b-d9cb-469f-a165-70867728950e", "AQI=", "alpha" },
            new string?[] { "2", null, null, null, "2", null, null, null, null, null, null, null, null, null },
        },
        IsEditable = true,
        DefaultsKnown = true,
        BaseSchema = "dbo",
        BaseTable = "typed",
        Provenance = new[]
        {
            new ColumnProvenanceDto { Ordinal = 0, BaseColumnName = "id", IsKey = true, IsTruePrimaryKey = true, IsAutoIncrement = true },
            Column(1, "amount"), Column(2, "price"), Column(3, "ratio"), Column(4, "qty", allowNull: false), Column(5, "small"), Column(6, "flag"),
            Column(7, "d"), Column(8, "dt"), Column(9, "t"), Column(10, "dto"), Column(11, "uid"), Column(12, "blob"), Column(13, "name"),
        },
    };

    /// <summary>
    /// public.typed (PostgreSQL, read as server text): 0 id integer serial PK, 1 flag boolean, 2 cnt integer NOT NULL, 3 big bigint,
    /// 4 price numeric(10,2), 5 ratio double precision, 6 added date, 7 uid uuid, 8 cash money, 9 ts timestamp without time zone,
    /// 10 name text. Row 0 holds a value in every column; row 1 is NULL but for id and cnt.
    /// </summary>
    private static ExecuteResultSet PostgresSet() => new()
    {
        ColumnNames = new[] { "id", "flag", "cnt", "big", "price", "ratio", "added", "uid", "cash", "ts", "name" },
        ColumnSqlTypes = new[] { "integer", "boolean", "integer", "bigint", "numeric(10,2)", "double precision", "date", "uuid", "money", "timestamp without time zone", "text" },
        ClrTypeHints = new[]
        {
            ClrTypeHint.ServerNumber, ClrTypeHint.ServerText, ClrTypeHint.ServerNumber, ClrTypeHint.ServerNumber, ClrTypeHint.ServerNumber, ClrTypeHint.ServerNumber,
            ClrTypeHint.ServerText, ClrTypeHint.ServerText, ClrTypeHint.ServerText, ClrTypeHint.ServerText, ClrTypeHint.ServerText,
        },
        Rows = new[]
        {
            new string?[] { "1", "t", "10", "5", "9.50", "0.25", "2026-01-02", "0f8fad5b-d9cb-469f-a165-70867728950e", "$1.50", "2026-01-02 08:00:00", "Ada" },
            new string?[] { "2", null, "2", null, null, null, null, null, null, null, null },
        },
        IsEditable = true,
        DefaultsKnown = true,
        BaseSchema = "public",
        BaseTable = "typed",
        Provenance = new[]
        {
            new ColumnProvenanceDto { Ordinal = 0, BaseColumnName = "id", IsKey = true, IsTruePrimaryKey = true, IsAutoIncrement = true },
            Column(1, "flag"), Column(2, "cnt", allowNull: false), Column(3, "big"), Column(4, "price"), Column(5, "ratio"),
            Column(6, "added"), Column(7, "uid"), Column(8, "cash"), Column(9, "ts"), Column(10, "name"),
        },
    };

    private static ColumnProvenanceDto Column(int ordinal, string name, bool allowNull = true) =>
        new() { Ordinal = ordinal, BaseColumnName = name, AllowDBNull = allowNull };

    // ── Driving ────────────────────────────────────────────────────────────────────────────────────

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

    private static string ApplyMessage(IRenderedComponent<Shared.ResultSetGrid> cut) =>
        cut.Find("[data-testid=results-apply-message]").TextContent.Trim();

    private static bool ApplyDisabled(IRenderedComponent<Shared.ResultSetGrid> cut) =>
        cut.Find("[data-testid=results-apply]").HasAttribute("disabled");

    /// <summary>The cell is invalid with <paramref name="title"/>, still shows what was typed, and blocks Apply.</summary>
    private void AssertInvalid(IRenderedComponent<Shared.ResultSetGrid> cut, int col, string typed, string title)
    {
        var glyph = cut.Find($"[data-testid=results-cell-invalid-0-{col}]");
        Assert.Equal(title, glyph.GetAttribute("title"));
        var cell = Cell(cut, 0, col);
        Assert.Contains("akml-cell-invalid", cell.ClassName);
        Assert.Equal("true", cell.GetAttribute("aria-invalid"));
        Assert.Equal(title, cell.GetAttribute("title"));
        Assert.Equal(typed, cell.TextContent.Trim());
        Assert.Contains("valid value", cut.Find("[data-testid=results-apply-blocked]").TextContent);
        Assert.True(ApplyDisabled(cut));
        cut.Find("[data-testid=results-apply]").Click();
        Assert.Empty(_host.Exec.Applied);
    }

    /// <summary>An apply reply that failed on edit <paramref name="index"/> of <paramref name="edits"/>, as the engine words it.</summary>
    private static ApplyChangesResult Rejection(int index, string error, string? column, int edits = 1)
    {
        var results = new CrudEditResult[edits];
        for (int i = 0; i < edits; i++)
        {
            results[i] = i == index
                ? new CrudEditResult { Index = i, Ok = false, Error = error, Column = column }
                : new CrudEditResult { Index = i, Ok = false, Error = $"Rolled back — edit {index} failed." };
        }
        return new ApplyChangesResult { Status = ExecuteStatus.Error, ErrorMessage = $"Edit {index} failed: {error}", Results = results };
    }

    // ── SQL Server: what commits ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "abc", "Not a valid money value (a number such as 12.50)", "12.5")]
    [InlineData(2, "1,234.50", "Not a valid decimal value (a number such as 12.50)", "1234.50")]      // no thousands separator
    [InlineData(3, "fast", "Not a valid float value (a number)", "1.5")]
    [InlineData(4, "12.5", "Not a valid int value (a whole number)", "12")]
    [InlineData(5, "300", "Out of range for tinyint (0 to 255)", "255")]
    [InlineData(6, "maybe", "Not a valid bit value (0/1/true/false)", "0")]
    [InlineData(7, "yesterday", "Not a valid date value (yyyy-MM-dd)", "2025-10-02")]
    [InlineData(8, "01/02/2025", "Not a valid datetime2 value (yyyy-MM-dd HH:mm:ss)", "2025-01-02 08:00:00")]
    [InlineData(9, "25:00", "Not a valid time value (HH:mm:ss)", "23:00")]
    [InlineData(10, "2025-01-01 08:00:00 +25:00", "Not a valid datetimeoffset value (yyyy-MM-dd HH:mm:ss +HH:mm)", "2025-01-01 08:00:00 +01:00")]
    [InlineData(11, "not-a-guid", "Not a valid uniqueidentifier value (8-4-4-4-12 hex digits)", "0F8FAD5B-D9CB-469F-A165-70867728950F")]
    [InlineData(12, "0xZZ", "Not a valid binary value (0x and hex digits)", "0x1F")]
    [InlineData(12, "test", "Not a valid binary value (0x and hex digits)", "0x1F")]   // review of W3: a Base64-shaped word is not a value
    [InlineData(12, "Hw==", "Not a valid binary value (0x and hex digits)", "0x1f")]
    public void SqlServer_a_value_the_column_cannot_take_is_invalid_until_corrected(int col, string typed, string title, string fix)
    {
        var cut = _host.Render(SqlServerSet());

        Edit(cut, 0, col, typed);
        AssertInvalid(cut, col, typed, title);

        Edit(cut, 0, col, fix);
        Assert.Empty(cut.FindAll($"[data-testid=results-cell-invalid-0-{col}]"));
        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));
        Assert.False(ApplyDisabled(cut));
    }

    [Theory]
    [InlineData(1, "12.5", "12.5")]                                                   // money
    [InlineData(1, "-3", "-3")]
    [InlineData(2, "1e2", "1e2")]                                                     // decimal: an exponent is a number
    [InlineData(3, "-1.5e3", "-1.5e3")]                                               // float
    [InlineData(4, "+5", "+5")]                                                       // int
    [InlineData(5, "255", "255")]                                                     // tinyint, at its edge
    [InlineData(6, "true", "1")]                                                      // bit: true / false travel as 1 / 0
    [InlineData(6, "FALSE", "0")]
    [InlineData(6, "1", "1")]
    [InlineData(7, "2025-10-02", "2025-10-02T00:00:00.0000000")]                      // dates as the grid shows them (PR #255)
    [InlineData(8, "2025-10-01 23:57:23.880", "2025-10-01T23:57:23.8800000")]
    [InlineData(9, "23:57:23", "23:57:23")]
    [InlineData(10, "2025-10-01 23:57:23 +02:00", "2025-10-01T23:57:23.0000000+02:00")]
    [InlineData(11, "0F8FAD5B-D9CB-469F-A165-70867728950E", "0F8FAD5B-D9CB-469F-A165-70867728950E")]
    [InlineData(12, "0x1F", "Hw==")]
    [InlineData(13, "anything at all", "anything at all")]
    public void SqlServer_a_value_the_server_accepts_commits_and_is_sent_in_wire_form(int col, string typed, string wire)
    {
        var cut = _host.Render(SqlServerSet());

        Edit(cut, 1, col, typed);

        Assert.Empty(cut.FindAll($"[data-testid=results-cell-invalid-1-{col}]"));
        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));
        Assert.False(ApplyDisabled(cut));
        cut.Find("[data-testid=results-apply]").Click();
        Assert.Equal(wire, Assert.Single(Assert.Single(_host.Exec.Applied).Edits).SetCells.Single().Value);
    }

    // ── PostgreSQL: what commits ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "maybe", "Not a valid boolean value (true/false)", "no")]        // "no" → f; "yes" → t would equal the fetched t and be no edit
    [InlineData(2, "1.0", "Not a valid integer value (a whole number)", "1")]
    [InlineData(3, "9223372036854775808", "Out of range for bigint (-9223372036854775808 to 9223372036854775807)", "1")]
    [InlineData(4, "abc", "Not a valid numeric value (a number such as 12.50)", "1.25")]
    [InlineData(5, "fast", "Not a valid double precision value (a number)", "1.5")]
    [InlineData(4, "1,234.50", "Not a valid numeric value (a number such as 12.50)", "1234.50")]   // no thousands separator on PostgreSQL either
    [InlineData(7, "xyz", "Not a valid uuid value (8-4-4-4-12 hex digits)", "0f8fad5b-d9cb-469f-a165-70867728950f")]
    public void Postgres_a_value_the_column_cannot_take_is_invalid_until_corrected(int col, string typed, string title, string fix)
    {
        var cut = _host.Render(PostgresSet());

        Edit(cut, 0, col, typed);
        AssertInvalid(cut, col, typed, title);

        Edit(cut, 0, col, fix);
        Assert.Empty(cut.FindAll($"[data-testid=results-cell-invalid-0-{col}]"));
        Assert.False(ApplyDisabled(cut));
    }

    [Theory]
    [InlineData(1, "yes", "t")]                                   // boolean: every form the server reads, stored as t / f
    [InlineData(1, "OFF", "f")]
    [InlineData(1, "1", "t")]
    [InlineData(1, "0", "f")]
    [InlineData(1, "T", "t")]
    [InlineData(1, "No", "f")]
    [InlineData(1, "on", "t")]
    [InlineData(1, "true", "t")]
    [InlineData(2, "+42", "+42")]                                 // integer
    [InlineData(3, "-1", "-1")]                                   // bigint
    [InlineData(4, "NaN", "NaN")]                                 // numeric holds NaN on PostgreSQL
    [InlineData(4, "1e30", "1e30")]                               // review of W3: numeric has no fixed size — judged by shape
    [InlineData(5, "Infinity", "Infinity")]                       // double precision too
    [InlineData(6, "2026-02-03", "2026-02-03T00:00:00.0000000")]
    [InlineData(6, "today", "today")]                             // review of W3: date reads what timestamp does — the server judges it
    [InlineData(6, "infinity", "infinity")]
    [InlineData(7, "0F8FAD5B-D9CB-469F-A165-70867728950E", "0F8FAD5B-D9CB-469F-A165-70867728950E")]
    [InlineData(8, "$1,234.56", "$1,234.56")]                     // money reads lc_monetary forms: the server judges it
    [InlineData(9, "tomorrow", "tomorrow")]                       // a timestamp has many input forms: the server judges it
    [InlineData(10, "Grace", "Grace")]                            // text: never judged
    public void Postgres_a_value_the_server_accepts_or_alone_can_judge_commits(int col, string typed, string wire)
    {
        var cut = _host.Render(PostgresSet());

        Edit(cut, 1, col, typed);

        Assert.Empty(cut.FindAll($"[data-testid=results-cell-invalid-1-{col}]"));
        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));
        cut.Find("[data-testid=results-apply]").Click();
        Assert.Equal(wire, Assert.Single(Assert.Single(_host.Exec.Applied).Edits).SetCells.Single().Value);
    }

    // ── The invalid cell ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Set_to_NULL_clears_a_type_invalid_cell()
    {
        var cut = _host.Render(SqlServerSet());
        Edit(cut, 0, 1, "abc");
        ClickCell(cut, 0, 1);

        Press(cut, "0", ctrl: true);

        Assert.Empty(cut.FindAll("[data-testid=results-cell-invalid-0-1]"));
        Assert.Equal("NULL", Cell(cut, 0, 1).TextContent.Trim());
        Assert.False(ApplyDisabled(cut));
    }

    [Fact]
    public void Reopening_an_invalid_cell_edits_the_typed_text()
    {
        var cut = _host.Render(SqlServerSet());
        Edit(cut, 0, 8, "01/02/2025");   // reads as a loose date, but is not in the grid's form (and is ambiguous)

        Assert.Equal("01/02/2025", Cell(cut, 0, 8).TextContent.Trim());
        Cell(cut, 0, 8).DoubleClick();
        Assert.Equal("01/02/2025", cut.Find("[data-testid=results-cell-input]").GetAttribute("value"));
    }

    /// <summary>Full-cycle check after the review of PR #259: a Base64-shaped word typed into a binary column is invalid, yet it
    /// decodes — the editor reopened on the bytes it stands for (0x1F), Enter changed nothing, and the mark stayed. An invalid
    /// pending value reopens as the text the cell shows, so correcting it is one edit.</summary>
    [Fact]
    public void Reopening_an_invalid_binary_cell_edits_the_typed_text_not_its_decoded_form()
    {
        var cut = _host.Render(SqlServerSet());
        Edit(cut, 0, 12, "Hw==");
        Assert.Equal("Hw==", Cell(cut, 0, 12).TextContent.Trim());

        Cell(cut, 0, 12).DoubleClick();
        var input = cut.Find("[data-testid=results-cell-input]");
        Assert.Equal("Hw==", input.GetAttribute("value"));

        input.Input("0x1F");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Empty(cut.FindAll("[data-testid=results-cell-invalid-0-12]"));
        Assert.Equal("0x1F", Cell(cut, 0, 12).TextContent.Trim());
        Assert.False(ApplyDisabled(cut));
    }

    [Fact]
    public void The_apply_bar_counts_type_invalid_cells_with_emptied_ones()
    {
        var cut = _host.Render(SqlServerSet());

        Edit(cut, 0, 1, "abc");
        Edit(cut, 0, 4, "");

        Assert.Contains("2 cells need a valid value", cut.Find("[data-testid=results-apply-blocked]").TextContent);
        Assert.Equal("Enter a value or set NULL", cut.Find("[data-testid=results-cell-invalid-0-4]").GetAttribute("title"));
    }

    [Fact]
    public void A_new_rows_cell_is_validated_too()
    {
        var cut = _host.Render(SqlServerSet());
        cut.Find("[data-testid=results-add-row]").Click();

        EditInsert(cut, 0, 6, "maybe");

        Assert.Equal("Not a valid bit value (0/1/true/false)", cut.Find("[data-testid=results-insert-invalid-0-6]").GetAttribute("title"));
        Assert.True(ApplyDisabled(cut));
    }

    // ── A failure the server still reports ────────────────────────────────────────────────────────

    [Fact]
    public void A_server_rejection_names_the_row_and_the_column_the_engine_reported_and_marks_the_cell()
    {
        const string error = "Arithmetic overflow error converting expression to data type int.";
        _host.Exec.ApplyReply = Rejection(0, error, "qty");
        var cut = _host.Render(EditableSet());
        Edit(cut, 1, 1, "x");
        Edit(cut, 1, 2, "99");

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Row 2, qty: " + error, ApplyMessage(cut)));
        Assert.Equal(error, cut.Find("[data-testid=results-cell-invalid-1-2]").GetAttribute("title"));
        Assert.Equal(error, Cell(cut, 1, 2).GetAttribute("title"));
        Assert.Contains("akml-cell-invalid", Cell(cut, 1, 2).ClassName);
        Assert.Empty(cut.FindAll("[data-testid=results-cell-invalid-1-1]"));
        // The pending edits stay and may be applied again as they are: the cause can lie elsewhere.
        Assert.NotNull(cut.Find("[data-testid=results-applybar]"));
        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));
        Assert.False(ApplyDisabled(cut));

        Edit(cut, 1, 2, "5");
        Assert.Empty(cut.FindAll("[data-testid=results-cell-invalid-1-2]"));
    }

    [Fact]
    public void A_server_rejection_without_a_column_names_the_one_cell_the_edit_set()
    {
        const string error = "The input string '1e400' was not in a correct format.";
        _host.Exec.ApplyReply = Rejection(0, error, null);
        var cut = _host.Render(EditableSet());
        Edit(cut, 0, 2, "99");

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Row 1, qty: " + error, ApplyMessage(cut)));
        Assert.Equal(error, cut.Find("[data-testid=results-cell-invalid-0-2]").GetAttribute("title"));
    }

    [Fact]
    public void A_server_rejection_without_a_column_on_a_multi_cell_edit_names_the_row_only()
    {
        const string error = "Transaction (Process ID 52) was deadlocked on lock resources with another process.";
        _host.Exec.ApplyReply = Rejection(0, error, null);
        var cut = _host.Render(EditableSet());
        Edit(cut, 0, 1, "x");
        Edit(cut, 0, 2, "99");

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Row 1: " + error, ApplyMessage(cut)));
        Assert.Empty(cut.FindAll("[data-testid^=results-cell-invalid-]"));
    }

    [Fact]
    public void A_server_rejection_is_matched_to_its_edit_by_the_index_in_the_request()
    {
        // Deletes go first in the request, so the update is edit 1; edit 0's rolled-back note must not be the message.
        const string error = "The UPDATE statement conflicted with the CHECK constraint \"ck_qty\".";
        _host.Exec.ApplyReply = Rejection(1, error, "qty", edits: 2);
        var cut = _host.Render(EditableSet());
        cut.Find("[data-testid=results-delete-0]").Click();
        Edit(cut, 2, 2, "-1");

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Row 3, qty: " + error, ApplyMessage(cut)));
        Assert.Equal(error, cut.Find("[data-testid=results-cell-invalid-2-2]").GetAttribute("title"));
    }

    [Fact]
    public void A_server_rejection_of_a_new_row_names_it()
    {
        const string error = "Cannot insert duplicate key row in object 'dbo.demo' with unique index 'ux_qty'.";
        _host.Exec.ApplyReply = Rejection(0, error, "qty");
        var cut = _host.Render(EditableSet());
        cut.Find("[data-testid=results-add-row]").Click();
        EditInsert(cut, 0, 2, "5");

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal("New row 1, qty: " + error, ApplyMessage(cut)));
        Assert.Equal(error, cut.Find("[data-testid=results-insert-invalid-0-2]").GetAttribute("title"));
        Assert.False(ApplyDisabled(cut));
    }

    [Fact]
    public void The_next_Apply_clears_the_marks_of_the_last_rejection()
    {
        const string error = "boom";
        _host.Exec.ApplyReply = Rejection(0, error, "qty");
        var cut = _host.Render(EditableSet());
        Edit(cut, 0, 2, "99");
        cut.Find("[data-testid=results-apply]").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=results-cell-invalid-0-2]")));

        _host.Exec.ApplyReply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Ok,
            Results = new[] { new CrudEditResult { Index = 0, Ok = true, RowsAffected = 1 } },
        };
        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Applied 1 change(s)", ApplyMessage(cut)));
        Assert.Empty(cut.FindAll("[data-testid=results-cell-invalid-0-2]"));
    }
}
