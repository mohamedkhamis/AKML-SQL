using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Formatting;
using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// Spec 042 (task T145, FR-141 to FR-144). The formatter either formats or leaves the text exactly as it was and says why: a script that
/// does not parse, one it cannot lay out, one whose layout would not mean the same, or whose layout is not stable. It keeps the script's
/// own line ends and final newline, formats a selection by whole statements, and offers case-only and terminator-only passes.
/// </summary>
public class PgFormatterSafetyTests
{
    private static readonly SqlPromptStyle Style = PgFormatTestStyles.Load("default");

    // ── a script that does not parse ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_syntax_error_returns_the_original_and_points_at_the_first_error()
    {
        const string sql = "select 1;\nselec 2;\nselect (3";

        var result = PgFormatter.Format(sql, Style);

        Assert.False(result.Success);
        Assert.Equal(sql, result.Text);
        Assert.False(result.WasModified);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(sql.IndexOf("selec ", StringComparison.Ordinal), diagnostic.Offset);
        Assert.Contains("syntax error", diagnostic.Message);
    }

    [Theory]
    [InlineData("\\c otherdb")]
    [InlineData("select 'unterminated")]
    [InlineData("select $$ never closed")]
    public void Text_the_server_would_reject_is_not_touched(string sql)
    {
        var result = PgFormatter.Format(sql, Style);

        Assert.False(result.Success);
        Assert.Equal(sql, result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Nothing_to_format_is_a_success_that_changes_nothing(string sql)
    {
        var result = PgFormatter.Format(sql, Style);

        Assert.True(result.Success);
        Assert.Equal(sql, result.Text);
        Assert.False(result.WasModified);
    }

    [Fact]
    public void A_script_of_comments_only_is_left_as_it_is()
    {
        const string sql = "-- nothing here\n/* at all */";

        var result = PgFormatter.Format(sql, Style);

        Assert.Equal(sql, result.Text);
    }

    // ── the original's shape ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_final_newline_is_kept_when_there_was_one_and_not_added_when_there_was_not()
    {
        Assert.EndsWith("\n", PgFormatter.Format("select 1\n", Style).Text);
        Assert.False(PgFormatter.Format("select 1", Style).Text.EndsWith('\n'));
    }

    [Fact]
    public void A_script_with_Windows_line_ends_keeps_them_everywhere()
    {
        var result = PgFormatter.Format("select a,\r\n b from t\r\nwhere x = 1 and\r\n y = 2;\r\n", Style);

        Assert.True(result.Success);
        Assert.DoesNotContain("\n", result.Text.Replace("\r\n", string.Empty));
        Assert.Contains("\r\n", result.Text);
    }

    [Fact]
    public void A_dollar_quoted_body_with_its_own_line_ends_is_untouched()
    {
        const string body = "$$\r\nbegin\r\n    return 1;\r\nend\r\n$$";
        var sql = "create function f() returns int as " + body + " language plpgsql;";

        var result = PgFormatter.Format(sql, Style);

        Assert.True(result.Success);
        Assert.Contains(body, result.Text);
    }

    // ── what is kept ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Comments_are_kept_in_order_and_an_end_of_line_comment_stays_at_the_end_of_its_line()
    {
        const string sql = "-- first\nselect a, -- about a\n b /* about b */ from t; -- last\n/* the end */\n";

        var result = PgFormatter.Format(sql, Style);

        Assert.True(result.Success, string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        var texts = new[] { "-- first", "-- about a", "/* about b */", "-- last", "/* the end */" };
        var at = -1;
        foreach (var text in texts)
        {
            var next = result.Text.IndexOf(text, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"'{text}' is missing or out of order in:\n{result.Text}");
            at = next;
        }
        Assert.Matches(@"a,?\s*-- about a", result.Text.Replace("\n", " "));
        var lineOfA = result.Text.Split('\n').First(l => l.Contains("-- about a"));
        Assert.Contains("a", lineOfA.Split("--")[0]);
    }

    [Fact]
    public void Quoted_names_and_their_case_are_never_touched_whatever_the_casing_options_say()
    {
        var style = StyleWithCasing("uppercase", "uppercase", "uppercase");
        const string sql = "select \"MixedCase\", \"select\", t.\"Weird Name\" from \"Sales\".\"Order Details\" t where \"x\" = 1;";

        var result = PgFormatter.Format(sql, style);

        Assert.True(result.Success);
        foreach (var name in new[] { "\"MixedCase\"", "\"select\"", "t.\"Weird Name\"", "\"Sales\".\"Order Details\"", "\"x\"" })
            Assert.Contains(name, result.Text);
    }

    // ── terminators and case, without layout ─────────────────────────────────────────────────────────

    [Fact]
    public void Terminators_can_be_added_or_removed_without_touching_the_layout()
    {
        const string sql = "select  1 ;  select 2";

        var added = PgFormatter.Format(sql, Style, new PgFormatOptions(ApplyLayout: false, ApplyCasing: false, Semicolons: 1));
        // PostgreSQL needs the terminators between statements: "remove" takes the last one's.
        var removed = PgFormatter.Format("select  1 ;  select 2 ;", Style, new PgFormatOptions(ApplyLayout: false, ApplyCasing: false, Semicolons: 2));

        Assert.True(added.Success && removed.Success);
        Assert.Equal("select  1 ;  select 2;", added.Text);
        Assert.Equal("select  1 ;  select 2 ", removed.Text);
    }

    [Fact]
    public void Case_only_changes_the_case_of_keywords_functions_and_types()
    {
        var style = StyleWithCasing("uppercase", "uppercase", "uppercase");
        const string sql = "select  count(*)  ,  x::integer   from   t   where  a   is  null";

        var result = PgFormatter.Format(sql, style, new PgFormatOptions(ApplyLayout: false));

        Assert.Equal("SELECT  COUNT(*)  ,  x::INTEGER   FROM   t   WHERE  a   IS  NULL", result.Text);
    }

    // ── a selection ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_selection_formats_the_statements_it_touches_and_nothing_else()
    {
        const string sql = "select  1 ;\nselect   a,b from  t where x=1 ;\nselect  3 ;";
        var start = sql.IndexOf("a,b", StringComparison.Ordinal);

        var result = PgFormatter.FormatSelection(sql, start, start + 3, Style);

        Assert.True(result.Success);
        Assert.Equal(sql.IndexOf("select   a,b", StringComparison.Ordinal), result.OriginalStart);
        Assert.Equal(sql.IndexOf(" ;\nselect  3", StringComparison.Ordinal), result.OriginalEnd);
        Assert.Contains("FROM", result.Text);
        Assert.Contains("x = 1", result.Text);
        var replaced = sql.Substring(0, result.OriginalStart) + result.Text + sql.Substring(result.OriginalEnd);
        Assert.StartsWith("select  1 ;\n", replaced);
        Assert.EndsWith("select  3 ;", replaced);
    }

    [Fact]
    public void An_empty_selection_formats_the_statement_the_caret_is_in()
    {
        const string sql = "select 1;select   a  from t;";
        var caret = sql.IndexOf("a  from", StringComparison.Ordinal);

        var result = PgFormatter.FormatSelection(sql, caret, caret, Style);

        Assert.True(result.Success);
        Assert.Equal(sql.IndexOf("select   a", StringComparison.Ordinal), result.OriginalStart);
        Assert.Contains("FROM", result.Text);
    }

    [Fact]
    public void A_selection_with_no_statement_in_it_says_so()
    {
        var result = PgFormatter.FormatSelection("select 1;\n\n-- note\n", 11, 14, Style);

        Assert.False(result.Success);
        Assert.Contains("no statement", result.Diagnostics.Single().Message);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static SqlPromptStyle StyleWithCasing(string keywords, string functions, string types)
    {
        var document = SqlPromptStyleDocument.CreateDefault("casing", "casing");
        document.Set("casing.reservedKeywords", keywords);
        document.Set("casing.builtInFunctions", functions);
        document.Set("casing.builtInDataTypes", types);
        return new SqlPromptStyle(document);
    }

    [Fact]
    public async Task The_awaitable_form_gives_the_result_of_the_blocking_one_and_reports_a_syntax_error_as_a_diagnostic()
    {
        const string sql = "select a,b from t where c=1";

        var blocking = PgFormatter.Format(sql, Style);
        var awaited = await PgFormatter.FormatAsync(sql, Style);
        Assert.True(awaited.Success);
        Assert.Equal(blocking.Text, awaited.Text);

        var broken = await PgFormatter.FormatAsync("select from where", Style);
        Assert.False(broken.Success);
        Assert.StartsWith("Not formatted:", Assert.Single(broken.Diagnostics).Message);
    }
}
