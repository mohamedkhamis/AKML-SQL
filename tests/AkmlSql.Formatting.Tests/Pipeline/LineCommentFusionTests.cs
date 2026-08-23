using Xunit;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Regression: a <c>--</c> line comment runs to end-of-line, so the token after it must land on a
/// new line. A <b>leading</b> comment (before the first statement) is the trap — the statement's
/// first token is break-suppressed, so without the emitter's dangling-line-comment guard the token
/// fuses into the comment and is lost. That corrupts the SQL, which stage-6 SemanticValidator then
/// rejects and reverts — surfacing in the Format Styles editor as
/// "Preview unavailable — the current settings produce semantically-different SQL".
/// </summary>
public class LineCommentFusionTests
{
    private static FormatResult Format(string sql)
        => new FormatterPipeline().Format(sql, new FormattingProfile());

    [Fact]
    public void LeadingLineComment_DoesNotFuseWithNextToken()
    {
        var result = Format("-- hello\nSELECT 1;");

        // Formatting was applied (not reverted by the semantic validator) …
        Assert.True(result.ValidationPassed);
        Assert.True(result.WasModified);
        // … and the SELECT is a real statement, not swallowed into the comment.
        Assert.DoesNotContain("helloSELECT", result.FormattedText);
        Assert.Contains("SELECT 1", result.FormattedText);
    }

    [Fact]
    public void LeadingLineComment_LowercaseKeyword_GetsCased()
    {
        // If the SELECT were still fused into the comment, it would stay lowercase (comment text is
        // never cased). Uppercase here proves it is being formatted as code.
        var result = Format("-- daily rollup\nselect 1;");

        Assert.True(result.WasModified);
        Assert.Contains("SELECT 1", result.FormattedText);
    }

    [Fact]
    public void CommentBetweenStatements_StillCorrect()
    {
        // Comments between statements already got a break and must remain unaffected by the guard.
        var result = Format("SELECT 1;\n-- hello\nSELECT 2;");

        Assert.True(result.ValidationPassed);
        Assert.DoesNotContain("helloSELECT", result.FormattedText);
    }
}
