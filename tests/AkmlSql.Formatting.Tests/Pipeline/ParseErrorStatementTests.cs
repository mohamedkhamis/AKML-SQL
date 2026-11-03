using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// UI scan 2026-11-03 (S2). ScriptDom leaves a statement it cannot read out of the tree, so
/// neither layout knew where that statement was: the SQL Prompt layout flushed its tokens with
/// default spacing (one line, the next statement appended to it) and the rule-based layout
/// reflowed it by keyword. A statement with a parse error must stay exactly as written; the
/// statements around it are formatted as usual, each starting its own line with the style's
/// empty lines between statements.
/// </summary>
public class ParseErrorStatementTests
{
    // The scan's input: a three-part name followed by "= …" is a syntax error.
    private const string Broken =
        "select * from dbo.Orders.CustomerIDtry = 'UK' and (c.City in ('London'))\n order by [City];";

    private const string Second = "select count(*) from dbo.Orders.Orders o where (o.CustomerID = 1)";

    private const string Script = Broken + "\n " + Second;

    /// <summary>The SSMS "Khamis Style" (a style in SQL Prompt's model), the shipped built-in Khamis Style (AKML's own
    /// model, two empty lines between statements) and the default AKML style.</summary>
    public static TheoryData<string> Profiles => new() { "Khamis Style", "Khamis Style (built-in)", "default" };

    private static FormattingProfile Profile(string name) => name switch
    {
        "Khamis Style" => SqlPromptStyles.ToProfile(SqlPromptStyleDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json")))),
        "Khamis Style (built-in)" => ProfileSerializer.Deserialize(File.ReadAllText(Path.Combine(
            AkmlSql.Formatting.Tests.SqlPrompt.SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "khamis-style.akmlstyle"))),
        _ => new FormattingProfile(),
    };

    /// <summary>Line breaks a style puts between two statements: one, plus its empty lines (W6: the count, not a yes/no).</summary>
    private static int LineBreaksBetweenStatements(FormattingProfile profile) =>
        profile.SqlPrompt is { } document
            ? 1 + new SqlPromptStyle(SqlPromptStyleDocument.FromNode(document)).Whitespace.EmptyLinesBetweenStatements
            : 1 + Math.Max(0, profile.Whitespace.EmptyLineBetweenStatements);

    /// <summary>Review of the S2 fix: a comment on the broken statement's last line was dropped by the rule-based layout
    /// (the run's last node carried it as a trailing comment, and the kept-as-written branch skipped trailing comments).</summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_comment_on_the_broken_statements_last_line_is_kept(string name)
    {
        var profile = Profile(name);
        var pipeline = new FormatterPipeline();
        foreach (var comment in new[] { "-- broken here", "/* note */" })
        {
            var result = pipeline.Format(Broken + " " + comment + "\n" + Second, profile);

            Assert.True(result.Success, comment);
            Assert.True(result.FormattedText.Contains(comment), $"'{comment}' lost in:\n{result.FormattedText}");
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(result.FormattedText, @"SELECT\s+COUNT", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                $"second statement lost in:\n{result.FormattedText}");
        }
    }

    /// <summary>Review of W6: a comment written directly above the broken statement introduces it — it stays above it, below
    /// the style's empty lines, as it does for a statement the parser reads.</summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_comment_above_the_broken_statement_stays_above_it(string name)
    {
        var profile = Profile(name);

        var result = new FormatterPipeline().Format("select 1;\n-- broken below\n" + Broken, profile);

        Assert.True(result.Success);
        var separator = new string('\n', LineBreaksBetweenStatements(profile));
        Assert.True(result.FormattedText.Contains(separator + "-- broken below\n" + Broken),
            $"expected {separator.Length} line breaks, the comment, then the statement as written, in:\n{result.FormattedText.Replace("\n", "<LF>\n")}");
    }

    /// <summary>Review of S2: the run's own line breaks are copied from the document. An SSMS document has CRLF ones and the
    /// rest of the output is written with LF; the kept text must not bring mixed line endings into it. (The default
    /// trailing-whitespace pass happens to strip a CR before each LF; the layout must not rely on it.)</summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_broken_statements_own_line_breaks_are_written_like_the_rest(string name)
    {
        var profile = Profile(name);
        profile.Whitespace.TrailingWhitespace = "keep";

        var result = new FormatterPipeline().Format(Script.Replace("\n", "\r\n"), profile);

        Assert.True(result.Success);
        Assert.DoesNotContain("\r", result.FormattedText);
        Assert.Contains(Broken, result.FormattedText);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Statement_with_a_parse_error_stays_as_written_and_the_next_one_starts_its_own_line(string name)
    {
        var profile = Profile(name);
        var pipeline = new FormatterPipeline();

        var result = pipeline.Format(Script, profile);

        Assert.True(result.Success);
        Assert.True(result.ValidationPassed);
        // (a) the statement the parser could not read: its text, casing and line breaks as written
        Assert.StartsWith(Broken, result.FormattedText);
        // (b) the next statement starts on a new line, after the style's empty lines between statements
        var rest = result.FormattedText[Broken.Length..];
        var separator = new string('\n', LineBreaksBetweenStatements(profile));
        Assert.StartsWith(separator, rest);
        Assert.False(rest[separator.Length..].StartsWith('\n'), "too many empty lines before the next statement");
        // and is laid out exactly as it would be on its own
        var alone = pipeline.Format(Second, profile).FormattedText;
        Assert.Equal(alone.TrimEnd(), rest.TrimStart('\n').TrimEnd());
    }

    /// <summary>
    /// The scan saw "… ORDER BY [City]; SELECT COUNT ( * )" on one line, then FROM / WHERE on their
    /// own lines. The broken statement must come back as written, and the second statement is the
    /// style's own layout of it: Khamis Style collapses a statement shorter than 75 characters, which
    /// the flushed tokens of the broken statement used to defeat.
    /// </summary>
    [Fact]
    public void Khamis_Style_gives_the_scan_input_the_expected_text()
    {
        var profile = Profile("Khamis Style");

        var result = new FormatterPipeline().Format(Script, profile);

        var separator = new string('\n', LineBreaksBetweenStatements(profile));
        Assert.Equal(
            Broken + separator + "SELECT COUNT ( * ) FROM dbo.Orders.Orders o WHERE ( o.CustomerID = 1 )",
            result.FormattedText);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_script_that_is_only_a_broken_statement_comes_back_unchanged(string name)
    {
        var result = new FormatterPipeline().Format(Broken, Profile(name));

        Assert.Equal(Broken, result.FormattedText);
        Assert.False(result.WasModified);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_broken_last_statement_stays_as_written_on_its_own_lines(string name)
    {
        var profile = Profile(name);
        var pipeline = new FormatterPipeline();
        const string first = "select 1 as a from dbo.T;";

        var result = pipeline.Format(first + "\n" + Broken, profile);

        Assert.True(result.Success);
        Assert.True(result.ValidationPassed);
        var text = result.FormattedText.TrimEnd();
        Assert.EndsWith(Broken, text);
        var head = text[..^Broken.Length];
        Assert.EndsWith(new string('\n', LineBreaksBetweenStatements(profile)), head);
        Assert.Equal(pipeline.Format(first, profile).FormattedText.TrimEnd(), head.TrimEnd());
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_broken_statement_before_GO_keeps_GO_on_its_own_line(string name)
    {
        var profile = Profile(name);
        var pipeline = new FormatterPipeline();

        var result = pipeline.Format(Broken + "\nGO\n" + Second, profile);

        Assert.True(result.ValidationPassed);
        Assert.True(result.WasModified);
        Assert.StartsWith(Broken + "\n", result.FormattedText);
        Assert.Contains("\nGO\n", result.FormattedText);
        Assert.EndsWith(pipeline.Format(Second, profile).FormattedText.TrimEnd(), result.FormattedText.TrimEnd());
    }

    /// <summary>
    /// ScriptDom reports "GO 2" as an error and drops the batch after it from the tree, exactly as
    /// it drops a broken statement — but that batch is valid, and both layouts have always laid it
    /// out from its tokens. It must not be mistaken for unread text and kept as written.
    /// </summary>
    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_batch_after_GO_with_a_count_is_still_formatted(string name)
    {
        var result = new FormatterPipeline().Format("select 1 as a from dbo.T\nGO 2\nselect 2 from dbo.U", Profile(name));

        Assert.True(result.ValidationPassed);
        // The batch is laid out by the style (the built-in Khamis Style puts FROM on its own line), so only the shape is pinned:
        // GO 2 on its own line, the SELECT cased and started on the next one.
        Assert.Matches(@"\nGO 2\nSELECT 2\b", result.FormattedText);
        Assert.Contains("dbo.U", result.FormattedText[result.FormattedText.IndexOf("GO 2", StringComparison.Ordinal)..]);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Formatting_a_script_with_a_broken_statement_is_idempotent(string name)
    {
        var profile = Profile(name);
        var pipeline = new FormatterPipeline();

        var once = pipeline.Format(Script, profile).FormattedText;

        Assert.Equal(once, pipeline.Format(once, profile).FormattedText);
    }
}
