using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T044, FR-021): Parse checks a PostgreSQL script with the server's own grammar and reports every
/// statement that does not parse, each at its line and column. It takes the script and nothing else - no connection, no
/// server - so these tests need no database.
/// </summary>
[Trait("Category", "Parse")]
public class PgParseRunnerTests
{
    private static ExecuteMessageDto[] Errors(ExecuteQueryResult r) =>
        r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.Error).ToArray();

    [Fact]
    public void Twenty_seeded_syntax_errors_are_twenty_messages_each_at_its_line_and_column()
    {
        // One statement per line, each with a second FROM where a table name should be.
        var lines = Enumerable.Range(1, 20).Select(k => $"SELECT {k} FROM FROM t{k};");
        var script = string.Join("\n", lines);

        var result = PgParseRunner.Run(script);

        Assert.Equal(ExecuteStatus.Error, result.Status);
        var errors = Errors(result);
        Assert.Equal(20, errors.Length);
        for (var k = 1; k <= 20; k++)
        {
            var error = errors[k - 1];
            Assert.Equal("42601", error.SqlState);
            Assert.Equal(k, error.Line);
            // "SELECT k FROM " is 12 characters for one digit and 13 for two; the second FROM follows.
            Assert.Equal(k < 10 ? 15 : 16, error.Column);
            Assert.Equal(PgParseRunner.NotChecked, error.Hint);
            Assert.Contains("syntax error", error.Text);
        }
        Assert.Equal(20, result.Batches.Length);
        Assert.All(result.Batches, b => Assert.Equal(ExecuteStatus.Error, b.Status));
    }

    [Fact]
    public void Statements_that_parse_are_not_reported_and_a_clean_script_says_so()
    {
        var clean = PgParseRunner.Run("SELECT 1;\nUPDATE t SET a = 1 WHERE b = 2;\nCREATE TABLE x (id int);");

        Assert.Equal(ExecuteStatus.Ok, clean.Status);
        Assert.Empty(Errors(clean));
        Assert.Contains(clean.MessageDetails, m => m.Kind == ExecuteMessageKind.System && m.Text.Contains("No syntax errors in 3 statement(s)."));
        Assert.Contains(clean.MessageDetails, m => m.Text.Contains(PgParseRunner.NotChecked));

        var mixed = PgParseRunner.Run("SELECT 1;\nSELEC 2;\nSELECT 3;");
        var error = Assert.Single(Errors(mixed));
        Assert.Equal(2, error.Line);
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.Error, ExecuteStatus.Ok }, mixed.Batches.Select(b => b.Status).ToArray());
    }

    [Fact]
    public void An_error_on_the_second_line_of_a_statement_maps_into_the_whole_script()
    {
        var result = PgParseRunner.Run("SELECT 1;\n\nSELECT a,\n       b FROM FROM t;");

        var error = Assert.Single(Errors(result));
        Assert.Equal(4, error.Line);
        // Seven spaces, b, a space, FROM and a space come first: the second FROM is at column 15 of its own line.
        Assert.Equal(15, error.Column);
    }

    [Fact]
    public void A_character_outside_the_basic_plane_before_the_error_keeps_the_column_right()
    {
        var result = PgParseRunner.Run("SELECT 1;\nSELECT '\U0001F642' FROM FROM t;");

        var error = Assert.Single(Errors(result));
        Assert.Equal(2, error.Line);
        // SELECT and a space (7), the quote (8), the emoji (two UTF-16 units, 9 and 10), the quote (11), a space (12),
        // FROM (13 to 16) and a space (17): the second FROM is at column 18. Counted in code points it would be 17.
        Assert.Equal(18, error.Column);
    }

    [Fact]
    public void A_GO_line_gets_the_SQL_Server_hint_beside_the_not_checked_text()
    {
        var result = PgParseRunner.Run("SELECT 1\nGO\nSELECT 2");

        var error = Assert.Single(Errors(result));
        Assert.Contains("SQL Server", error.Hint);
        Assert.Contains(PgParseRunner.NotChecked, error.Hint);
    }

    [Fact]
    public void Meta_commands_and_client_copy_are_not_checked_and_are_not_errors()
    {
        var result = PgParseRunner.Run("SELECT 1;\n\\dt\nCOPY t FROM STDIN;");

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.Skipped, ExecuteStatus.Skipped }, result.Batches.Select(b => b.Status).ToArray());
        Assert.Contains(result.MessageDetails, m => m.Text.Contains("psql client command"));
        Assert.Contains(result.MessageDetails, m => m.Text.Contains("STDIN"));
    }

    [Fact]
    public void An_empty_script_has_nothing_to_check()
    {
        var result = PgParseRunner.Run("  \n-- only a comment\n");

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Empty(result.Batches);
        Assert.Contains(result.MessageDetails, m => m.Text.StartsWith("There was nothing to check."));
    }

    [Fact]
    public void Dollar_quoted_bodies_and_comments_do_not_split_or_confuse_the_check()
    {
        var script = "CREATE FUNCTION f() RETURNS int AS $$ SELECT 1; SELECT 2; $$ LANGUAGE sql; /* a; b */ SELECT 'x;y';";

        var result = PgParseRunner.Run(script);

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal(2, result.Batches.Length);
    }
}
