using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (research R5): the small, tolerant, managed scanner. It must understand every PostgreSQL lexical form
/// that can hide a semicolon or a quote, and it must never throw on unfinished text.
/// </summary>
public class PgLexicalScannerTests
{
    private static List<(PgTokenKind Kind, string Text)> Scan(string sql) =>
        PgLexicalScanner.Scan(sql).Where(t => t.Kind != PgTokenKind.Whitespace)
            .Select(t => (t.Kind, sql.Substring(t.Start, t.End - t.Start))).ToList();

    [Fact]
    public void Tokens_cover_the_whole_text_without_gaps_or_overlap()
    {
        const string sql = "SELECT 'a;b', \"C d\" /* x */ FROM t -- end\n;";
        var tokens = PgLexicalScanner.Scan(sql);

        var position = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(position, token.Start);
            Assert.True(token.End > token.Start);
            position = token.End;
        }
        Assert.Equal(sql.Length, position);
    }

    [Fact]
    public void Standard_strings_double_their_quote_and_have_no_backslash_escape()
    {
        var tokens = Scan(@"SELECT 'it''s', 'a\'");
        Assert.Contains((PgTokenKind.String, "'it''s'"), tokens);
        Assert.Contains((PgTokenKind.String, @"'a\'"), tokens); // the backslash is an ordinary character here
    }

    [Fact]
    public void E_strings_honour_backslash_escapes()
    {
        var tokens = Scan(@"SELECT E'it\'s; fine', 1");
        Assert.Contains((PgTokenKind.String, @"E'it\'s; fine'"), tokens);
        Assert.Contains((PgTokenKind.Number, "1"), tokens);
    }

    [Theory]
    [InlineData("U&'d\\0061t\\+000061'")]
    [InlineData("B'1010'")]
    [InlineData("X'1FF'")]
    [InlineData("N'text'")]
    public void Prefixed_string_forms_are_one_string_token(string literal)
    {
        var tokens = Scan("SELECT " + literal);
        Assert.Equal(PgTokenKind.String, tokens[1].Kind);
        Assert.Equal(literal, tokens[1].Text);
    }

    [Fact]
    public void Quoted_identifiers_double_their_quote_and_may_hold_semicolons()
    {
        var tokens = Scan("SELECT \"a;b\", \"say \"\"hi\"\"\", U&\"d\\0061t\"");
        Assert.Contains((PgTokenKind.QuotedIdentifier, "\"a;b\""), tokens);
        Assert.Contains((PgTokenKind.QuotedIdentifier, "\"say \"\"hi\"\"\""), tokens);
        Assert.Contains(tokens, t => t.Kind == PgTokenKind.QuotedIdentifier && t.Text.StartsWith("U&\""));
    }

    [Theory]
    [InlineData("$$ a; b $$")]
    [InlineData("$body$ a; $$ b; $body$")]
    [InlineData("$_t1$ x $_t1$")]
    public void Dollar_quoted_strings_run_to_the_matching_tag(string literal)
    {
        var tokens = Scan("SELECT " + literal + ", 2");
        Assert.Equal(PgTokenKind.DollarString, tokens[1].Kind);
        Assert.Equal(literal, tokens[1].Text);
        Assert.Contains((PgTokenKind.Number, "2"), tokens);
    }

    [Fact]
    public void A_dollar_tag_does_not_close_on_a_different_tag()
    {
        var tokens = Scan("$a$ x $b$ y $a$ z");
        Assert.Equal(PgTokenKind.DollarString, tokens[0].Kind);
        Assert.Equal("$a$ x $b$ y $a$", tokens[0].Text);
    }

    [Fact]
    public void Positional_parameters_and_identifiers_with_dollars_are_not_dollar_strings()
    {
        var tokens = Scan("SELECT $1, $23, cost$center, a$$b");
        Assert.Contains((PgTokenKind.Parameter, "$1"), tokens);
        Assert.Contains((PgTokenKind.Parameter, "$23"), tokens);
        Assert.Contains((PgTokenKind.Identifier, "cost$center"), tokens);
        Assert.Contains((PgTokenKind.Identifier, "a$$b"), tokens);
    }

    [Fact]
    public void Comments_line_and_nested_block()
    {
        var tokens = Scan("SELECT 1 -- a; 'b\n/* outer /* inner; */ still; */ ;");
        Assert.Equal(PgTokenKind.LineComment, tokens[2].Kind);
        Assert.Equal(PgTokenKind.BlockComment, tokens[3].Kind);
        Assert.Equal("/* outer /* inner; */ still; */", tokens[3].Text);
        Assert.Equal(PgTokenKind.Semicolon, tokens[4].Kind);
    }

    [Fact]
    public void Casts_and_operators_are_not_confused_with_comments()
    {
        var tokens = Scan("SELECT a::int, b->>'k', c--d\n, e/*f*/");
        Assert.Contains((PgTokenKind.Operator, "::"), tokens);
        Assert.Contains((PgTokenKind.Operator, "->>"), tokens);
        Assert.Contains(tokens, t => t.Kind == PgTokenKind.LineComment && t.Text == "--d");
        Assert.Contains(tokens, t => t.Kind == PgTokenKind.BlockComment && t.Text == "/*f*/");
    }

    [Fact]
    public void A_backslash_outside_literals_starts_a_meta_command_to_the_end_of_the_line()
    {
        var tokens = Scan("SELECT 1;\n\\dt public.*\nSELECT 2");
        var meta = Assert.Single(tokens, t => t.Kind == PgTokenKind.MetaCommand);
        Assert.Equal("\\dt public.*", meta.Text);
        Assert.Contains((PgTokenKind.Number, "2"), tokens);
    }

    [Fact]
    public void A_backslash_inside_a_string_or_comment_is_not_a_meta_command()
    {
        Assert.DoesNotContain(Scan(@"SELECT 'a\dt', E'b\n' -- \q"), t => t.Kind == PgTokenKind.MetaCommand);
        Assert.DoesNotContain(Scan(@"/* \q */ SELECT $$\d$$"), t => t.Kind == PgTokenKind.MetaCommand);
    }

    [Theory]
    [InlineData("SELECT 'unterminated", PgTokenKind.String)]
    [InlineData("SELECT \"unterminated", PgTokenKind.QuotedIdentifier)]
    [InlineData("SELECT $$ unterminated", PgTokenKind.DollarString)]
    [InlineData("SELECT /* unterminated", PgTokenKind.BlockComment)]
    public void Unfinished_text_yields_an_unterminated_token_and_never_throws(string sql, PgTokenKind kind)
    {
        var tokens = PgLexicalScanner.Scan(sql);
        var last = tokens[^1];
        Assert.Equal(kind, last.Kind);
        Assert.False(last.Terminated);
        Assert.Equal(sql.Length, last.End);
    }

    [Fact]
    public void Non_ASCII_text_keeps_utf16_offsets_and_letters_are_identifiers()
    {
        const string sql = "SELECT café, '😀' ; naïve";
        var tokens = PgLexicalScanner.Scan(sql);
        Assert.Contains(tokens, t => t.Kind == PgTokenKind.Identifier && sql.Substring(t.Start, t.End - t.Start) == "café");
        var emoji = tokens.Single(t => t.Kind == PgTokenKind.String);
        Assert.Equal("'😀'", sql.Substring(emoji.Start, emoji.End - emoji.Start));
        Assert.Equal(sql.Length, tokens[^1].End);
    }

    [Fact]
    public void Empty_text_has_no_tokens()
    {
        Assert.Empty(PgLexicalScanner.Scan(""));
    }
}
