using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>Spec 042 (FR-141, FR-146, research R1/R2): the thin wrapper over the PostgreSQL parser.</summary>
[Trait("Category", "Parse")]
public class PgParserTests
{
    [Fact]
    public void A_valid_script_parses_into_one_tree_per_statement()
    {
        var result = PgParser.Parse("SELECT 1; INSERT INTO t VALUES (1)");

        Assert.True(result.Success);
        Assert.Equal(2, result.Tree!.Stmts.Count);
        Assert.Null(result.Error);
    }

    [Fact]
    public void A_syntax_error_reports_message_offset_line_and_column_in_utf16()
    {
        const string sql = "SELECT '😀é' AS a;\nSELEC 1";

        var result = PgParser.Parse(sql);

        Assert.False(result.Success);
        var error = result.Error!;
        Assert.Contains("syntax error", error.Message);
        Assert.Equal(sql.IndexOf("SELEC ", StringComparison.Ordinal), error.Offset);
        Assert.Equal(2, error.Line);
        Assert.Equal(1, error.Column);
        Assert.Equal("42601", error.SqlState);
    }

    [Fact]
    public void Tokenize_returns_keyword_and_literal_tokens_with_utf16_offsets()
    {
        const string sql = "SELECT 'é€' , 42";

        var tokens = PgParser.Tokenize(sql);

        Assert.Equal("SELECT", sql.Substring(tokens[0].Start, tokens[0].End - tokens[0].Start));
        Assert.Contains(tokens, t => t.Kind == PgSqlParser.SqlTokenKind.StringLiteral);
        Assert.Contains(tokens, t => t.Kind == PgSqlParser.SqlTokenKind.NumericLiteral);
    }

    [Theory]
    [InlineData("select  a ,b\nfrom t where x=1", "SELECT a, b FROM t WHERE x = 1", true)]
    [InlineData("SELECT a FROM t -- note", "SELECT a FROM t", true)]
    [InlineData("SELECT a FROM t WHERE x = 1", "SELECT a FROM t WHERE x = 2", false)]
    [InlineData("SELECT \"A\" FROM t", "SELECT a FROM t", false)]
    [InlineData("SELECT 1; SELECT 2", "SELECT 1", false)]
    public void TreesEquivalent_ignores_layout_and_case_but_not_meaning(string left, string right, bool equivalent)
    {
        var a = PgParser.Parse(left).Tree!;
        var b = PgParser.Parse(right).Tree!;

        Assert.Equal(equivalent, PgParser.TreesEquivalent(a, b));
    }

    [Fact]
    public void Classify_names_the_statement_kinds()
    {
        var kinds = PgParser.Classify("SELECT 1; BEGIN; INSERT INTO t VALUES (1); COPY t FROM STDIN; DROP TABLE t")
            .Select(i => i.Kind.ToString()).ToArray();

        Assert.Equal(new[] { "Select", "Transaction", "Insert", "Copy", "Ddl" }, kinds);
    }
}
