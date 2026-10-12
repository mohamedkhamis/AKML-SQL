using System;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 041 (T078, research R11, FR-026) — how a result value reads in the grid, type by type, the way
/// SSMS shows it. The input is the engine's round-trip wire text (ISO-8601 "o" for dates, TimeSpan "c"
/// for time, Base64 for binary, invariant numbers) plus the column's declared type; editing and Apply
/// keep using the wire text, so nothing here may lose information the user edits.
/// </summary>
public class SqlValueDisplayTests
{
    private static string Show(string? wire, string declared, int hint = ClrTypeHint.String) =>
        SqlValueDisplay.Format(wire, SqlTypeName.Parse(declared), hint).Text;

    [Fact]
    public void Null_reads_NULL_and_is_flagged()
    {
        var v = SqlValueDisplay.Format(null, SqlTypeName.Parse("int"), ClrTypeHint.Int64);
        Assert.Equal("NULL", v.Text);
        Assert.True(v.IsNull);
    }

    [Fact]
    public void An_empty_string_stays_empty_and_is_flagged()
    {
        var v = SqlValueDisplay.Format("", SqlTypeName.Parse("nvarchar(10)"), ClrTypeHint.String);
        Assert.Equal("", v.Text);
        Assert.True(v.IsEmptyString);
        Assert.False(v.IsNull);
    }

    [Fact]
    public void An_engine_cut_value_is_shown_verbatim_and_flagged()
    {
        var v = SqlValueDisplay.Format("[text 70000 chars]", SqlTypeName.Parse("nvarchar(max)"), ClrTypeHint.String);
        Assert.Equal("[text 70000 chars]", v.Text);
        Assert.True(v.IsEngineCut);
    }

    [Fact] public void Date_shows_the_day() => Assert.Equal("2025-10-01", Show("2025-10-01T00:00:00.0000000", "date", ClrTypeHint.DateTime));

    [Fact] public void Smalldatetime_shows_seconds() => Assert.Equal("2025-10-01 23:57:00", Show("2025-10-01T23:57:00.0000000", "smalldatetime", ClrTypeHint.DateTime));

    [Fact] public void Datetime_shows_milliseconds() => Assert.Equal("2025-10-01 23:57:23.880", Show("2025-10-01T23:57:23.8800000", "datetime", ClrTypeHint.DateTime));

    [Theory]
    [InlineData("datetime2(7)", "2025-10-01 23:57:23.8800000")]
    [InlineData("datetime2(3)", "2025-10-01 23:57:23.880")]
    [InlineData("datetime2(0)", "2025-10-01 23:57:23")]
    [InlineData("datetime2", "2025-10-01 23:57:23.8800000")]   // an older engine's bare name: seven digits
    public void Datetime2_shows_its_declared_fraction(string declared, string expected)
    {
        Assert.Equal(expected, Show("2025-10-01T23:57:23.8800000", declared, ClrTypeHint.DateTime));
    }

    [Theory]
    [InlineData("time(7)", "13:45:30.1234567", "13:45:30.1234567")]
    [InlineData("time(0)", "13:45:30.1234567", "13:45:30")]
    [InlineData("time(3)", "13:45:30", "13:45:30.000")]          // "c" omits a zero fraction
    [InlineData("time", "08:05:00.5", "08:05:00.5000000")]
    public void Time_shows_its_declared_fraction(string declared, string wire, string expected)
    {
        Assert.Equal(expected, Show(wire, declared, ClrTypeHint.String));
    }

    [Theory]
    [InlineData("datetimeoffset(7)", "2025-10-01T23:57:23.8800000+02:00", "2025-10-01 23:57:23.8800000 +02:00")]
    [InlineData("datetimeoffset(0)", "2025-10-01T23:57:23.8800000-05:30", "2025-10-01 23:57:23 -05:30")]
    public void Datetimeoffset_adds_a_space_and_its_offset(string declared, string wire, string expected)
    {
        Assert.Equal(expected, Show(wire, declared, ClrTypeHint.DateTimeOffset));
    }

    [Fact]
    public void Binary_shows_0x_and_upper_case_hex()
    {
        var wire = Convert.ToBase64String(new byte[] { 0x0f, 0xab, 0x00 });
        Assert.Equal("0x0FAB00", Show(wire, "varbinary(16)", ClrTypeHint.Binary));
        Assert.Equal("0x", Show(Convert.ToBase64String(Array.Empty<byte>()), "varbinary(max)", ClrTypeHint.Binary));
    }

    [Fact]
    public void Large_binary_shows_a_preview_unless_the_full_text_is_asked_for()
    {
        var bytes = new byte[2_000];
        bytes[0] = 0xAB;
        var wire = Convert.ToBase64String(bytes);

        var preview = SqlValueDisplay.Format(wire, SqlTypeName.Parse("varbinary(max)"), ClrTypeHint.Binary);
        Assert.True(preview.IsPreview);
        Assert.StartsWith("0xAB00", preview.Text);
        Assert.EndsWith("…", preview.Text);
        Assert.Equal(2 + 512 * 2 + 1, preview.Text.Length);

        var full = SqlValueDisplay.Format(wire, SqlTypeName.Parse("varbinary(max)"), ClrTypeHint.Binary, fullText: true);
        Assert.False(full.IsPreview);
        Assert.Equal(2 + 2_000 * 2, full.Text.Length);
    }

    [Fact] public void Uniqueidentifier_is_upper_case() => Assert.Equal("6F9619FF-8B86-D011-B42D-00C04FC964FF", Show("6f9619ff-8b86-d011-b42d-00c04fc964ff", "uniqueidentifier", ClrTypeHint.Guid));

    [Fact] public void Decimal_keeps_the_declared_scale_the_engine_sent() => Assert.Equal("12.50", Show("12.50", "decimal(10,2)", ClrTypeHint.Decimal));

    [Theory]
    [InlineData("money", "12.5", "12.5000")]
    [InlineData("money", "-3", "-3.0000")]
    [InlineData("smallmoney", "0.1", "0.1000")]
    public void Money_shows_four_decimals(string declared, string wire, string expected)
    {
        Assert.Equal(expected, Show(wire, declared, ClrTypeHint.Decimal));
    }

    [Fact] public void Bit_shows_1_or_0() => Assert.Equal("1", Show("1", "bit", ClrTypeHint.Bool));

    [Fact] public void Other_types_show_the_wire_text() => Assert.Equal("hello", Show("hello", "nvarchar(50)"));

    [Fact]
    public void Without_a_declared_type_the_hint_decides()
    {
        Assert.Equal("2025-10-01 23:57:23.8800000", Show("2025-10-01T23:57:23.8800000", "", ClrTypeHint.DateTime));
        Assert.Equal("0x01", Show(Convert.ToBase64String(new byte[] { 1 }), "", ClrTypeHint.Binary));
        Assert.Equal("ABC", Show("abc", "", ClrTypeHint.Guid));
    }

    [Fact]
    public void A_value_that_does_not_parse_is_shown_as_it_came()
    {
        Assert.Equal("not a date", Show("not a date", "datetime", ClrTypeHint.DateTime));
        Assert.Equal("%%%", Show("%%%", "varbinary(10)", ClrTypeHint.Binary));
    }

    // ── PR #255: a value typed the way the grid shows it becomes the wire text Apply binds ───────────────

    private static string? ToWire(string? typed, string declared, int hint = ClrTypeHint.String) =>
        SqlValueDisplay.ToWire(typed, SqlTypeName.Parse(declared), hint);

    [Theory]
    [InlineData("2025-10-02", "date", "2025-10-02T00:00:00.0000000")]
    [InlineData("2025-10-01 23:57:23.880", "datetime", "2025-10-01T23:57:23.8800000")]
    [InlineData("2025-10-01 23:57", "smalldatetime", "2025-10-01T23:57:00.0000000")]
    [InlineData("2025-10-01T23:57:23.1234567", "datetime2(7)", "2025-10-01T23:57:23.1234567")]
    [InlineData("2025-10-01T23:57:23.1234567", "datetime2", "2025-10-01T23:57:23.1234567")]
    [InlineData("2025-10-01 23:57:23.8800000 +02:00", "datetimeoffset(7)", "2025-10-01T23:57:23.8800000+02:00")]
    [InlineData("23:57:23", "time(7)", "23:57:23")]
    [InlineData("23:57:23.880", "time(3)", "23:57:23.8800000")]
    [InlineData("08:30", "time", "08:30:00")]
    [InlineData("0x1F", "varbinary(10)", "Hw==")]
    [InlineData("0x1", "binary(1)", "AQ==")]
    [InlineData("0x", "varbinary(max)", "")]
    public void A_date_time_or_binary_value_typed_as_the_grid_shows_it_becomes_wire_text(string typed, string declared, string wire)
    {
        Assert.Equal(wire, ToWire(typed, declared, declared.StartsWith("varbinary") || declared.StartsWith("binary") ? ClrTypeHint.Binary : ClrTypeHint.String));
    }

    [Theory]
    [InlineData("not a date", "date")]
    [InlineData("2025-13-40", "datetime")]
    [InlineData("25:00:00", "time")]
    [InlineData("0xZZ", "varbinary(10)")]
    [InlineData("00501", "varchar(10)")]
    [InlineData("  padded  ", "nvarchar(20)")]
    public void Anything_else_is_kept_as_typed(string typed, string declared)
    {
        Assert.Equal(typed, ToWire(typed, declared));
    }

    [Fact]
    public void Without_a_declared_type_the_CLR_hint_decides()
    {
        Assert.Equal("2025-10-02T00:00:00.0000000", ToWire("2025-10-02", "", ClrTypeHint.DateTime));
        Assert.Equal("Hw==", ToWire("0x1F", "", ClrTypeHint.Binary));
        Assert.Equal("2025-10-02", ToWire("2025-10-02", "", ClrTypeHint.String));
        Assert.Null(ToWire(null, "date"));
    }

    [Theory]
    [InlineData("2025-10-01T00:00:00.0000000", "date", "2025-10-01")]
    [InlineData("2025-10-01T23:57:23.8800000", "datetime", "2025-10-01 23:57:23.880")]
    [InlineData("Hw==", "varbinary(10)", "0x1F")]
    [InlineData("23:57:23.8800000", "time(7)", "23:57:23.8800000")]
    [InlineData("2025-10-01T23:57:23.8800000+02:00", "datetimeoffset(7)", "2025-10-01 23:57:23.8800000 +02:00")]
    public void The_editor_starts_with_the_shown_text_when_it_converts_back_without_loss(string wire, string declared, string edit)
    {
        int hint = declared.StartsWith("varbinary") ? ClrTypeHint.Binary : ClrTypeHint.String;
        Assert.Equal(edit, SqlValueDisplay.EditText(wire, SqlTypeName.Parse(declared), hint));
    }

    [Theory]
    [InlineData("2025-10-01T23:57:23.1234567", "datetime2(3)")]   // shown to 3 digits: editing it would round
    [InlineData("12.5", "money")]                                  // shown as 12.5000; the wire stays
    [InlineData("d1b2c3d4-0000-0000-0000-000000000000", "uniqueidentifier")]
    public void The_editor_keeps_the_wire_text_where_the_shown_text_would_lose_or_change_it(string wire, string declared)
    {
        Assert.Equal(wire, SqlValueDisplay.EditText(wire, SqlTypeName.Parse(declared), ClrTypeHint.String));
    }
}
