using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// UI scan 2026-11-03 (W6): the same style must put the same number of empty lines between two statements whichever
/// dialect lays them out. The built-in Khamis Style asks for two; the PostgreSQL formatter gives two.
/// </summary>
public class StatementSpacingTests
{
    [Theory]
    [InlineData("khamis-style", 2)]
    [InlineData("default", 1)]
    public void Empty_lines_between_statements_are_the_styles(string styleName, int expected)
    {
        var path = Path.Combine(SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", styleName + ".akmlstyle");
        var profile = ProfileSerializer.Deserialize(File.ReadAllText(path));
        var result = new FormatterPipeline().Format("select 1;\nselect 2;", profile);

        Assert.True(result.Success, "format failed");
        var actual = EmptyLinesBetweenStatements(result.FormattedText);
        Assert.True(expected == actual, $"expected {expected} empty lines, got {actual} in:\n{result.FormattedText.Replace("\n", "<LF>\n")}");
    }

    /// <summary>A statement the parser could not read is kept as written (S2) and still sits the style's empty lines
    /// below the statement before it.</summary>
    [Fact]
    public void A_statement_kept_as_written_sits_the_styles_empty_lines_below_the_one_before()
    {
        var path = Path.Combine(SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "khamis-style.akmlstyle");
        var profile = ProfileSerializer.Deserialize(File.ReadAllText(path));
        var result = new FormatterPipeline().Format("select 1;\nselect * from dbo.Orders.CustomerIDtry = 'UK';", profile);

        Assert.True(result.Success, "format failed");
        var actual = EmptyLinesBetweenStatements(result.FormattedText);
        Assert.True(actual == 2, $"expected 2 empty lines, got {actual} in:\n{result.FormattedText.Replace("\n", "<LF>\n")}");
    }

    /// <summary>Review of W6: the comments written directly above a statement introduce it (a Leading comment). The style's
    /// empty lines go above the first of them and the statement follows its comments on the next line — the first fix put the
    /// count between the comments and their statement, which pushed the comments onto the statement before. Khamis Style
    /// also strips every empty line it did not ask for (preserveEmptyLines off): the hoisted gap must survive that pass.</summary>
    [Theory]
    [InlineData("khamis-style", 2)]
    [InlineData("default", 1)]
    public void The_styles_empty_lines_sit_above_a_statements_leading_comments(string styleName, int expected)
    {
        var result = new FormatterPipeline().Format("select 1;\n-- about two\n/* more */\nselect 2;", BuiltIn(styleName));

        Assert.True(result.Success, "format failed");
        var expectedLines = new List<string> { "SELECT 1;" };
        expectedLines.AddRange(Enumerable.Repeat(string.Empty, expected));
        expectedLines.AddRange(new[] { "-- about two", "/* more */", "SELECT 2;" });
        Assert.Equal(expectedLines, Lines(result.FormattedText));
    }

    /// <summary>A comment the author set apart with empty lines on both sides (Standalone) is not a statement's preamble: it keeps
    /// its own empty line, and the statement after it gets the style's count.</summary>
    [Fact]
    public void A_standalone_comment_keeps_its_own_empty_lines()
    {
        var result = new FormatterPipeline().Format("select 1;\n\n-- note\n\nselect 2;", BuiltIn("default"));

        Assert.True(result.Success, "format failed");
        Assert.Equal(new[] { "SELECT 1;", "", "-- note", "", "SELECT 2;" }, Lines(result.FormattedText));
    }

    private static FormattingProfile BuiltIn(string styleName)
    {
        var path = Path.Combine(SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", styleName + ".akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(path));
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static int EmptyLinesBetweenStatements(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var selects = lines.Select((l, i) => (l, i)).Where(x => x.l.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
        Assert.True(selects.Count >= 2, "two SELECT statements expected in:\n" + text);
        var empty = 0;
        for (var i = selects[1] - 1; i > selects[0] && lines[i].Trim().Length == 0; i--) empty++;
        return empty;
    }
}
