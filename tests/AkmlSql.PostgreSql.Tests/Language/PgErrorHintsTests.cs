using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (FR-010, task T058): <c>GO</c> is a SQL Server batch separator with no meaning in PostgreSQL. It is not split on
/// and not stripped; when the server rejects a statement that holds a <c>GO</c> line the message gets a hint saying why.
/// </summary>
public class PgErrorHintsTests
{
    [Theory]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT 1\r\ngo\r\nSELECT 2")]
    [InlineData("SELECT 1\n  GO  \nSELECT 2")]
    [InlineData("SELECT 1\nGO 5\nSELECT 2")]
    [InlineData("GO")]
    public void A_line_that_is_only_GO_is_recognised(string text)
    {
        var hint = PgErrorHints.GoSeparator(text);

        Assert.NotNull(hint);
        Assert.Contains("GO", hint);
        Assert.Contains("SQL Server", hint);
        Assert.Contains("semicolon", hint);
    }

    [Theory]
    [InlineData("SELECT 'GO' AS x")]
    [InlineData("SELECT go FROM t")]
    [InlineData("SELECT 1 -- GO\nSELECT 2")]
    [InlineData("SELECT 1 AS go,\n 2")]
    [InlineData("SELECT 1\nGOTO label")]
    [InlineData("")]
    public void GO_inside_other_text_is_not_a_separator_line(string text)
    {
        Assert.Null(PgErrorHints.GoSeparator(text));
    }

    [Fact]
    public void A_GO_line_inside_a_string_or_comment_does_not_count()
    {
        Assert.Null(PgErrorHints.GoSeparator("SELECT 'a\nGO\nb'"));
        Assert.Null(PgErrorHints.GoSeparator("/*\nGO\n*/ SELECT 1"));
        Assert.Null(PgErrorHints.GoSeparator("SELECT $$\nGO\n$$"));
    }

    [Fact]
    public void The_servers_own_hint_comes_first_and_ours_follows()
    {
        var combined = PgErrorHints.Combine("Perhaps you meant X.", "GO is a SQL Server separator.");

        Assert.Equal("Perhaps you meant X.\nGO is a SQL Server separator.", combined);
        Assert.Equal("ours", PgErrorHints.Combine(null, "ours"));
        Assert.Equal("ours", PgErrorHints.Combine("", "ours"));
        Assert.Equal("theirs", PgErrorHints.Combine("theirs", null));
        Assert.Null(PgErrorHints.Combine(null, null));
    }

    [Fact]
    public void Meta_command_and_client_copy_texts_name_the_alternative()
    {
        Assert.Contains("\\dt", PgErrorHints.MetaCommandSkipped("\\dt public.*"));
        Assert.Contains("Schema explorer", PgErrorHints.MetaCommandSkipped("\\dt"));
        Assert.Contains("STDIN", PgErrorHints.ClientCopySkipped());
        Assert.Contains("INSERT", PgErrorHints.ClientCopySkipped());
    }
}
