using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Tests.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// A string literal or quoted identifier that spans lines holds its line breaks, and the spaces before them, as its value.
/// The classic layout's trailing-whitespace pass ran over the whole text: it dropped the CR of a CRLF and the spaces at a
/// line's end inside them, validation saw the literal change, and the whole script came back unformatted. In SSMS, whose
/// buffers are CRLF, that was every script with a string over several lines (found in the review of PR #261). The SQL
/// Prompt layout never had the problem.
/// </summary>
public sealed class MultiLineLiteralTests
{
    /// <summary>Every built-in style (the classic layout reads them all).</summary>
    public static IEnumerable<object[]> BuiltInStyles() =>
        Directory.GetFiles(BuiltInDir(), "*.akmlstyle").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    private static string BuiltInDir() =>
        Path.Combine(SqlPromptLayoutSafetyTests.RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn");

    private static FormattingProfile BuiltIn(string name) =>
        ProfileSerializer.Deserialize(File.ReadAllText(Path.Combine(BuiltInDir(), name + ".akmlstyle")));

    private static string Why(FormatResult result) => string.Join(" | ", result.Diagnostics.Select(d => d.Message));

    [Theory]
    [MemberData(nameof(BuiltInStyles))]
    public void A_CRLF_script_with_a_string_over_two_lines_is_formatted_and_keeps_it(string style)
    {
        var result = new FormatterPipeline().Format("select N'first line\r\nsecond line' as note\r\nselect 2\r\n", BuiltIn(style));

        Assert.True(result.ValidationPassed, Why(result));
        Assert.True(result.WasModified, "the script came back unformatted");
        Assert.Contains("N'first line\r\nsecond line'", result.FormattedText);
        Assert.DoesNotMatch(@"(?<!\r)\n", result.FormattedText);   // CRLF throughout, as the script was written
    }

    [Theory]
    [MemberData(nameof(BuiltInStyles))]
    public void Spaces_at_a_line_end_inside_a_literal_are_kept_and_the_others_removed(string style)
    {
        var profile = BuiltIn(style);
        var result = new FormatterPipeline().Format("select 1   \nselect 'x  \ny' as v   \n", profile);

        Assert.True(result.ValidationPassed, Why(result));
        Assert.Contains("'x  \ny'", result.FormattedText);
        if (profile.Whitespace.TrailingWhitespace == "remove")
            Assert.DoesNotMatch(@"[ \t]\n", result.FormattedText.Replace("'x  \ny'", "''"));
    }

    [Fact]
    public void A_quoted_identifier_over_two_lines_keeps_its_CRLF()
    {
        var result = new FormatterPipeline().Format("select 1 as [two\r\nlines]\r\n", new FormattingProfile());

        Assert.True(result.ValidationPassed, Why(result));
        Assert.Contains("[two\r\nlines]", result.FormattedText);
    }
}
