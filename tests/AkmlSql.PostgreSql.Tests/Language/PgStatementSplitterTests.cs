using AkmlSql.PostgreSql.Language;
using PgSqlParser;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (FR-010, FR-019, research R7): scripts run one statement at a time, split on semicolons outside
/// strings, comments, dollar-quoted bodies and quoted identifiers; there is no batch separator.
/// </summary>
public class PgStatementSplitterTests
{
    private static List<string> Texts(string sql) => PgStatementSplitter.Split(sql).Select(s => s.Text).ToList();

    [Fact]
    public void Statements_split_on_semicolons_and_the_terminator_is_not_part_of_the_text()
    {
        Assert.Equal(new[] { "SELECT 1", "SELECT 2", "SELECT 3" }, Texts("SELECT 1; SELECT 2;\nSELECT 3"));
    }

    [Fact]
    public void Empty_and_comment_only_statements_are_dropped()
    {
        Assert.Equal(new[] { "SELECT 1" }, Texts(";; SELECT 1;\n-- nothing here\n;  /* or here */ "));
        Assert.Empty(Texts("   \n-- only a comment\n"));
        Assert.Empty(Texts(""));
    }

    [Fact]
    public void Semicolons_inside_literals_comments_and_quoted_names_do_not_split()
    {
        const string sql = "SELECT 'a;b', E'c\\';d', \"e;f\" -- g;h\n, 1 /* i; /* j; */ k; */;SELECT 2";
        var statements = Texts(sql);
        Assert.Equal(2, statements.Count);
        Assert.Contains("'a;b'", statements[0]);
        Assert.Equal("SELECT 2", statements[1]);
    }

    [Fact]
    public void A_dollar_quoted_function_body_is_one_statement()
    {
        const string sql = "CREATE FUNCTION f() RETURNS int AS $$ BEGIN PERFORM 1; RETURN 2; END; $$ LANGUAGE plpgsql;\nSELECT f();";
        var statements = Texts(sql);
        Assert.Equal(2, statements.Count);
        Assert.StartsWith("CREATE FUNCTION f()", statements[0]);
        Assert.EndsWith("LANGUAGE plpgsql", statements[0]);
        Assert.Equal("SELECT f()", statements[1]);
    }

    [Fact]
    public void A_do_block_with_a_tagged_dollar_body_is_one_statement()
    {
        const string sql = "DO $body$ BEGIN RAISE NOTICE 'x;y'; END $body$;SELECT 1";
        Assert.Equal(2, Texts(sql).Count);
    }

    [Fact]
    public void A_sql_standard_function_body_with_BEGIN_ATOMIC_is_one_statement()
    {
        const string sql = "CREATE FUNCTION add(a int, b int) RETURNS int LANGUAGE sql BEGIN ATOMIC SELECT a + b; END;\nSELECT add(1, 2);";
        var statements = Texts(sql);
        Assert.Equal(2, statements.Count);
        Assert.Contains("BEGIN ATOMIC", statements[0]);
        Assert.EndsWith("END", statements[0]);
    }

    [Fact]
    public void A_CASE_inside_a_BEGIN_ATOMIC_body_does_not_end_it_early()
    {
        const string sql = "CREATE FUNCTION f(x int) RETURNS int LANGUAGE sql BEGIN ATOMIC SELECT CASE WHEN x > 0 THEN 1 ELSE 0 END; SELECT 2; END; SELECT 3";
        var statements = Texts(sql);
        Assert.Equal(2, statements.Count);
        Assert.EndsWith("END", statements[0]);
        Assert.Equal("SELECT 3", statements[1]);
    }

    [Fact]
    public void A_plain_BEGIN_statement_is_a_transaction_start_not_a_block()
    {
        Assert.Equal(new[] { "BEGIN", "SELECT 1", "COMMIT" }, Texts("BEGIN; SELECT 1; COMMIT;"));
    }

    [Fact]
    public void Start_is_the_first_character_of_the_statement_and_lines_are_one_based()
    {
        const string sql = "SELECT 1;\n\n   SELECT 'é😀' AS x,\n  y;\nSELECT 3";
        var statements = PgStatementSplitter.Split(sql);

        Assert.Equal(3, statements.Count);
        Assert.Equal(0, statements[0].Start);
        Assert.Equal(1, statements[0].StartLine);
        Assert.Equal(1, statements[0].StartColumn);

        Assert.Equal(sql.IndexOf("SELECT 'é", StringComparison.Ordinal), statements[1].Start);
        Assert.Equal(3, statements[1].StartLine);
        Assert.Equal(4, statements[1].StartColumn);
        Assert.Equal(sql.Substring(statements[1].Start, statements[1].End - statements[1].Start), statements[1].Text);

        Assert.Equal(5, statements[2].StartLine);
    }

    [Fact]
    public void The_last_statement_may_lack_a_terminator_and_unfinished_text_does_not_throw()
    {
        var statements = PgStatementSplitter.Split("SELECT 1; SELECT 'open");
        Assert.Equal(2, statements.Count);
        Assert.False(statements[0].Unterminated);
        Assert.True(statements[1].Unterminated);
        Assert.Equal("SELECT 'open", statements[1].Text);
    }

    [Fact]
    public void Meta_commands_are_flagged_and_never_merged_into_a_neighbouring_statement()
    {
        var statements = PgStatementSplitter.Split("SELECT 1;\n\\dt public.*\nSELECT 2;");

        Assert.Equal(3, statements.Count);
        Assert.False(statements[0].IsMetaCommand);
        Assert.True(statements[1].IsMetaCommand);
        Assert.Equal("\\dt public.*", statements[1].Text);
        Assert.False(statements[2].IsMetaCommand);
    }

    [Fact]
    public void A_meta_command_in_the_middle_of_a_statement_flags_that_statement()
    {
        var statements = PgStatementSplitter.Split("SELECT 1 \\g\nSELECT 2");

        Assert.True(statements[0].IsMetaCommand);
        Assert.Equal(2, statements.Count);
    }

    [Theory]
    [InlineData("COPY t FROM STDIN;", true)]
    [InlineData("copy t (a, b) from stdin with (format csv)", true)]
    [InlineData("COPY t TO STDOUT", true)]
    [InlineData("COPY (SELECT 1) TO STDOUT", true)]
    [InlineData("COPY t FROM '/tmp/file.csv'", false)]
    [InlineData("COPY t TO PROGRAM 'gzip'", false)]
    [InlineData("SELECT 'copy t from stdin'", false)]
    public void COPY_with_STDIN_or_STDOUT_is_recognised_without_a_server(string sql, bool expected)
    {
        var statement = PgStatementSplitter.Split(sql).Single();
        Assert.Equal(expected, PgStatementInspector.UsesClientCopy(statement.Text));
    }

    [Fact]
    public void A_leading_comment_stays_in_the_statement_so_a_planner_hint_reaches_the_server()
    {
        var statements = Texts("/*+ SeqScan(t) */ SELECT * FROM t;\n-- note\nSELECT 2");

        Assert.Equal(2, statements.Count);
        Assert.StartsWith("/*+ SeqScan(t) */", statements[0]);
        Assert.Equal("-- note\nSELECT 2", statements[1]);
    }

    [Fact]
    public void GO_is_not_a_separator_so_it_stays_part_of_the_text()
    {
        var statements = Texts("SELECT 1\nGO\nSELECT 2");
        Assert.Single(statements);
        Assert.Contains("GO", statements[0]);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 'a;b'; SELECT $$x;y$$; SELECT \"c;d\" FROM t")]
    [InlineData("CREATE TABLE t (a int);\nINSERT INTO t VALUES (1), (2);\nUPDATE t SET a = a + 1 WHERE a > 1;")]
    [InlineData("DO $$ BEGIN PERFORM 1; END $$; SELECT E'it\\'s;';")]
    [InlineData("CREATE FUNCTION f(a int) RETURNS int LANGUAGE sql BEGIN ATOMIC SELECT a; END; SELECT f(1);")]
    [InlineData("SELECT 1 /* ; */;\nSELECT 2; /* trailing */")]
    public void The_managed_split_agrees_with_the_server_parser_on_valid_scripts(string sql)
    {
        var native = Parser.SplitWithParser(sql);
        Assert.True(native.IsSuccess, native.Error?.Message);

        var expected = native.Value.Statements.Select(s => s.Text.Trim().TrimEnd(';').Trim()).Where(t => t.Length > 0).ToList();
        Assert.Equal(expected, Texts(sql));
    }
}
