using PgSqlParser;
using PgSqlParser.Utils;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spike S1 (spec 042, research R27): pins the behaviour of the pgsqlparser wrapper the design leans
/// on - the shapes of Parse / Scan / SplitWithScanner, which offset unit each one uses, and the
/// error position. These tests describe the wrapper; they are the measurement, not TDD for our code.
/// </summary>
[Trait("Category", "Parse")]
public class PgParserSmokeTests
{
    // 😀 is one code point but two UTF-16 units and four UTF-8 bytes; é is one code point, two UTF-8 bytes.
    private const string NonAscii = "SELECT '😀é' AS a;\nSELEC 1";

    [Fact]
    public void Parse_returns_one_RawStmt_per_statement_with_locations()
    {
        var result = Parser.Parse("SELECT 1; UPDATE t SET a = 2");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Stmts.Count);
        Assert.Equal(0, result.Value.Stmts[0].StmtLocation);
        Assert.True(result.Value.Stmts[1].StmtLocation > 0);
    }

    [Fact]
    public void Parse_error_carries_message_and_a_one_based_cursor()
    {
        var result = Parser.Parse("SELEC 1");

        Assert.False(result.IsSuccess);
        Assert.Contains("syntax error", result.Error!.Message);
        Assert.Equal(1, result.Error.CursorPos);
    }

    [Fact]
    public void Error_cursor_counts_code_points_and_the_wrapper_converts_to_utf16()
    {
        var result = Parser.Parse(NonAscii);

        Assert.False(result.IsSuccess);
        var error = result.Error!;

        // 18 code points precede SELEC, so the 1-based code-point position is 19 ...
        Assert.Equal(19, error.CursorPos);

        // ... but the emoji occupies two UTF-16 units, so the 0-based UTF-16 offset is 19 as well only
        // because the wrapper converts: code point 18 (0-based) + 1 extra unit for the emoji.
        var charOffset = error.GetCursorCharOffset(NonAscii);
        Assert.StartsWith("SELEC", NonAscii.Substring(charOffset));
    }

    [Fact]
    public void Error_converts_to_line_and_column()
    {
        var result = Parser.Parse(NonAscii);

        var lineAndColumn = result.Error!.GetLineAndColumn(NonAscii);

        Assert.NotNull(lineAndColumn);
        Assert.Equal(2, lineAndColumn!.Value.Item1);
        Assert.Equal(1, lineAndColumn.Value.Item2);
    }

    [Fact]
    public void Scan_offsets_are_utf16_and_keywords_carry_a_kind()
    {
        var tokens = Parser.Tokenize(NonAscii.Replace("SELEC 1", "SELECT 1")).Value;

        var select = tokens[0];
        Assert.Equal(SqlTokenKind.Keyword, select.Kind);
        Assert.Equal(0, select.Start);
        Assert.Equal(6, select.End);

        var literal = tokens.First(t => t.Kind == SqlTokenKind.StringLiteral);
        var text = NonAscii.Replace("SELEC 1", "SELECT 1");
        Assert.Equal("'😀é'", text.Substring(literal.Start, literal.End - literal.Start));
    }

    [Fact]
    public void SplitWithScanner_respects_dollar_quoting_and_gives_utf16_spans()
    {
        const string script = "SELECT 'é';\nCREATE FUNCTION f() RETURNS int AS $$ BEGIN RETURN 1; END; $$ LANGUAGE plpgsql;\nSELECT 2";

        var split = Parser.SplitWithScanner(script);

        Assert.True(split.IsSuccess);
        Assert.Equal(3, split.Value.Statements.Count);
        var function = split.Value.Statements[1];
        Assert.Contains("$$ BEGIN RETURN 1; END; $$", function.Text);
        // Text is the raw slice of the script, leading white space included: a statement starts at the first
        // non-white-space character after Location, which is what error positions must be added to.
        Assert.Equal(function.Text, script.Substring(function.Location, function.Length));
        Assert.StartsWith("\n", function.Text);
    }

    [Fact]
    public void Parse_locations_are_utf8_bytes_and_the_mapper_converts_them()
    {
        const string script = "SELECT 'é€😀';\nSELECT 2";
        var parsed = Parser.Parse(script).Value;
        var mapper = new Utf8OffsetMapper(script);

        var second = parsed.Stmts[1];
        // The byte location is larger than the character index because é, € and 😀 are multi-byte.
        Assert.True(second.StmtLocation > script.IndexOf("SELECT 2", StringComparison.Ordinal));
        Assert.Equal(script.IndexOf("SELECT 2", StringComparison.Ordinal), mapper.ToCharOffset(second.StmtLocation));
    }

    [Fact]
    public void EqualsIgnoringLocations_ignores_whitespace_and_case_but_not_meaning()
    {
        var a = Parser.Parse("select  a ,b\nfrom t where x=1").Value;
        var b = Parser.Parse("SELECT a, b FROM t WHERE x = 1").Value;
        var different = Parser.Parse("SELECT a, b FROM t WHERE x = 2").Value;

        Assert.True(a.Stmts[0].Stmt.EqualsIgnoringLocations(b.Stmts[0].Stmt));
        Assert.False(a.Stmts[0].Stmt.EqualsIgnoringLocations(different.Stmts[0].Stmt));
    }

    [Fact]
    public void Classify_reports_statement_kind_and_read_only()
    {
        var infos = Parser.Classify("SELECT 1; INSERT INTO t VALUES (1); BEGIN").Value;

        Assert.Equal(3, infos.Count);
        Assert.True(infos[0].IsReadOnly);
        Assert.False(infos[1].IsReadOnly);
    }
}
