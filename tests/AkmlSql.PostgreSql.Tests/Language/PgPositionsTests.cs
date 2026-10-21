using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (FR-014, research R7, spike S3): PostgreSQL reports an error position as a 1-based count of
/// characters (Unicode code points), editors count UTF-16 units. A character outside the BMP, such as an
/// emoji, is the difference.
/// </summary>
public class PgPositionsTests
{
    [Theory]
    [InlineData("SELECT x", 8, 7)]
    [InlineData("SELECT x", 1, 0)]
    [InlineData("abc", 4, 3)]    // one past the end is the end
    [InlineData("abc", 99, 3)]   // clamped
    [InlineData("abc", 0, 0)]    // below the first position is the start
    public void ASCII_text_maps_one_to_one(string text, int position, int expectedOffset)
    {
        Assert.Equal(expectedOffset, PgPositions.ToUtf16Offset(text, position));
    }

    [Fact]
    public void A_character_outside_the_BMP_counts_once_in_the_server_position_and_twice_in_UTF16()
    {
        const string text = "SELECT '😀' AS a, bad";

        var codePointPosition = text.EnumerateRunes().Count() - 3 + 1; // 1-based position of "bad" in code points
        var offset = PgPositions.ToUtf16Offset(text, codePointPosition);

        Assert.Equal(text.IndexOf("bad", StringComparison.Ordinal), offset);
    }

    [Fact]
    public void Lone_surrogates_do_not_throw()
    {
        var text = "a\uD83Db";
        Assert.InRange(PgPositions.ToUtf16Offset(text, 3), 0, text.Length);
    }

    [Theory]
    [InlineData("a\nb\nc", 0, 1, 1)]
    [InlineData("a\nb\nc", 2, 2, 1)]
    [InlineData("a\r\nb", 3, 2, 1)]
    [InlineData("a\rb", 2, 2, 1)]
    [InlineData("ab😀cd", 5, 1, 6)]   // columns are UTF-16 units, as an editor counts them
    [InlineData("", 0, 1, 1)]
    public void Offsets_map_to_one_based_line_and_column(string text, int offset, int line, int column)
    {
        Assert.Equal((line, column), PgPositions.ToLineColumn(text, offset));
    }

    [Fact]
    public void An_offset_past_the_end_is_the_end_of_the_text()
    {
        Assert.Equal((2, 2), PgPositions.ToLineColumn("a\nb", 99));
    }

    [Fact]
    public void A_statement_relative_position_adds_to_the_statements_start()
    {
        const string script = "SELECT 1;\nSELECT 😀, nope;";
        var statement = PgStatementSplitter.Split(script)[1];
        var relativeCodePointPosition = statement.Text.IndexOf("nope", StringComparison.Ordinal) - 1 + 1; // 😀 is one code point, two units

        var documentOffset = PgPositions.ToDocumentOffset(statement, relativeCodePointPosition);

        Assert.Equal(script.IndexOf("nope", StringComparison.Ordinal), documentOffset);
    }
}
