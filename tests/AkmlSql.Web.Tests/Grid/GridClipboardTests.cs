using AkmlSql.Core.Text;
using AkmlSql.Web.Shared.Grid;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T044, research R5, FR-009/FR-031) — what Copy, Copy with headers and Copy as put on the
/// clipboard: the text the grid shows, in grid order, NULL as NULL, a value the column width hides in
/// full, a value the engine cut as its indicator (and counted), and one line per row.
/// </summary>
public sealed class GridClipboardTests
{
    private static readonly string[] Headers = { "id", "name", "note" };

    // Fetched rows; the display order below may differ.
    private static readonly string?[][] Data =
    {
        new string?[] { "1", "alpha", null },
        new string?[] { "2", "line\r\nbreak", "[text 70000 chars]" },
        new string?[] { "3", new string('x', 600), "ok" },
    };

    private static string? TextAt(int fetchedRow, int col) => Data[fetchedRow][col];

    private static GridCopyResult Copy(GridSelection s, CopyFormat format = CopyFormat.Tsv, bool header = false, bool retain = false, int[]? order = null)
        => GridClipboard.Build(s, Headers, order ?? new[] { 0, 1, 2 }, TextAt, format, header, retain);

    [Fact]
    public void Copy_is_tab_separated_one_line_per_row_with_NULL_as_NULL()
    {
        var s = new GridSelection(3, 3);
        s.ClickRowHeader(0, false, false);

        Assert.Equal("1\talpha\tNULL\r\n", Copy(s).Text);
    }

    [Fact]
    public void Copy_with_headers_adds_only_the_copied_columns_headers_in_grid_order()
    {
        var s = new GridSelection(3, 3);
        s.ClickColumnHeader(2, false, false);
        s.ClickColumnHeader(0, false, ctrl: true);

        var result = Copy(s, header: true);

        Assert.Equal("id\tnote\r\n1\tNULL\r\n2\t[text 70000 chars]\r\n3\tok\r\n", result.Text);
    }

    [Fact]
    public void A_value_hidden_by_the_column_width_is_copied_in_full()
    {
        var s = new GridSelection(3, 3);
        s.Click(2, 1, false, false);

        Assert.Equal(new string('x', 600) + "\r\n", Copy(s).Text);
    }

    [Fact]
    public void An_engine_cut_value_is_copied_as_its_indicator_and_counted()
    {
        var s = new GridSelection(3, 3);
        s.SelectAll();

        var result = Copy(s);

        Assert.Contains("[text 70000 chars]", result.Text);
        Assert.Equal(1, result.EngineCutCount);
        Assert.Equal(3, result.RowCount);
    }

    [Fact]
    public void Line_breaks_become_a_space_unless_kept()
    {
        var s = new GridSelection(3, 3);
        s.Click(1, 1, false, false);

        Assert.Equal("line break\r\n", Copy(s).Text);
        Assert.Equal("line\r\nbreak\r\n", Copy(s, retain: true).Text);
    }

    [Fact]
    public void Rows_follow_the_display_order()
    {
        var s = new GridSelection(3, 3);
        s.ClickColumnHeader(0, false, false);

        Assert.Equal("3\r\n1\r\n2\r\n", Copy(s, order: new[] { 2, 0, 1 }).Text);
    }

    [Fact]
    public void Duplicate_column_names_are_copied_by_position()
    {
        var headers = new[] { "name", "name" };
        var s = new GridSelection(1, 2);
        s.SelectAll();

        var result = GridClipboard.Build(s, headers, new[] { 0 }, (r, c) => c == 0 ? "a" : "b", CopyFormat.Tsv, includeHeader: true, retainLineBreaks: false);

        Assert.Equal("name\tname\r\na\tb\r\n", result.Text);
    }

    [Fact]
    public void A_disjoint_selection_copies_the_union_in_grid_order_with_blank_unselected_cells()
    {
        var s = new GridSelection(3, 3);
        s.Click(0, 0, false, false);
        s.Click(2, 2, false, ctrl: true);

        Assert.Equal("1\t\r\n\tok\r\n", Copy(s).Text);
    }

    [Fact]
    public void Copy_as_formats_go_through_the_shared_formatters()
    {
        var s = new GridSelection(3, 3);
        s.ClickRowHeader(0, false, false);

        Assert.Equal("id,name,note\r\n1,alpha,NULL\r\n", Copy(s, CopyFormat.Csv).Text);
        Assert.Equal("[\r\n  {\"id\": 1, \"name\": \"alpha\", \"note\": null}\r\n]", Copy(s, CopyFormat.Json).Text);
        Assert.StartsWith("| id | name | note |", Copy(s, CopyFormat.Markdown).Text);
        Assert.Equal("INSERT INTO [TableName] ([id], [name], [note]) VALUES (1, N'alpha', NULL);\r\n", Copy(s, CopyFormat.Insert).Text);
    }

    [Fact]
    public void A_copy_over_the_limit_is_cut_to_a_bounded_prefix()
    {
        var s = new GridSelection(3, 3);
        s.SelectAll();

        var result = GridClipboard.Build(s, Headers, new[] { 0, 1, 2 }, TextAt, CopyFormat.Tsv, false, false, maxChars: 10);

        Assert.True(result.Truncated);
        Assert.Equal(10, result.Text.Length);
    }

    [Fact]
    public void Nothing_selected_copies_nothing()
    {
        var result = Copy(new GridSelection(3, 3));
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.RowCount);
    }

    // ── PR #255: JSON and INSERT decide numbers by the column's type ────────────────────────────────────

    private static readonly string[] TypedHeaders = { "zip", "amount", "label", "qty", "blob" };

    private static readonly string?[][] TypedData =
    {
        new string?[] { "00501", "1,234", "NaN", "12.5000", "0x0F" },
        new string?[] { "12345", "Infinity", "7", "-3", null },
    };

    private static readonly GridValueKind[] Kinds =
    {
        GridValueKind.Text, GridValueKind.Text, GridValueKind.Text, GridValueKind.Number, GridValueKind.Binary,
    };

    private static GridCopyResult CopyTyped(CopyFormat format)
    {
        var s = new GridSelection(2, 5);
        s.SelectAll();
        return GridClipboard.Build(s, TypedHeaders, new[] { 0, 1 }, (r, c) => TypedData[r][c], format, false, false, columnKinds: Kinds);
    }

    [Fact]
    public void Copy_as_INSERT_quotes_character_values_that_look_like_numbers()
    {
        var text = CopyTyped(CopyFormat.Insert).Text;

        Assert.Contains("VALUES (N'00501', N'1,234', N'NaN', 12.5000, 0x0F);", text);
        Assert.Contains("VALUES (N'12345', N'Infinity', N'7', -3, NULL);", text);
    }

    [Fact]
    public void Copy_as_JSON_keeps_character_values_as_strings_and_writes_valid_JSON()
    {
        var text = CopyTyped(CopyFormat.Json).Text;

        Assert.Contains("\"zip\": \"00501\"", text);
        Assert.Contains("\"label\": \"NaN\"", text);
        Assert.Contains("\"qty\": 12.5000", text);
        Assert.Contains("\"blob\": \"0x0F\"", text);
        using var doc = System.Text.Json.JsonDocument.Parse(text);   // throws on a bare NaN / Infinity
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }
}
