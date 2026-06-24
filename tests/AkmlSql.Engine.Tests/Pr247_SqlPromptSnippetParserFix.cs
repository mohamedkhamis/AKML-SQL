using AkmlSql.Engine.Snippets;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// PR #247 regression test — <see cref="SqlPromptSnippetParser"/> <c>SplitBody</c> used to strip
/// ALL leading/trailing blank lines with a <c>while</c> loop, discarding intentional blank lines
/// the author placed at the start or end of a snippet body. The fix changes each loop to a single
/// conditional <c>if</c> so at most ONE leading and ONE trailing blank line is removed (matching
/// the CDATA-artifact described in the comment).
/// </summary>
public sealed class Pr247_SqlPromptSnippetParserFix
{
    // Helper: parse a flat-shape snippet XML and return the Body array.
    private static string[] ParseBody(string codeContent)
    {
        var xml = $"""
<Snippet>
  <Title>test</Title>
  <Shortcut>t</Shortcut>
  <Code>{codeContent}</Code>
</Snippet>
""";
        var snippets = SqlPromptSnippetParser.ParseXml(xml);
        Assert.Single(snippets);
        return snippets[0].Body;
    }

    [Fact]
    public void SplitBody_TwoLeadingAndTwoTrailingBlanks_StripsOnlyOne_Each()
    {
        // Body has 2 leading blank lines, some content, and 2 trailing blank lines.
        // After the fix, exactly 1 leading and 1 trailing blank line are removed;
        // the second leading and second trailing blank lines are preserved as intentional content.
        const string bodyWithExtraBlanks = "\n\nSELECT 1\n\n";

        var body = ParseBody(bodyWithExtraBlanks);

        // The split produces: ["", "", "SELECT 1", "", ""]
        // After stripping one leading ("") and one trailing (""):
        // result = ["", "SELECT 1", ""]  — 3 elements
        Assert.Equal(3, body.Length);
        Assert.Equal("", body[0]);          // second leading blank — preserved
        Assert.Equal("SELECT 1", body[1]);
        Assert.Equal("", body[2]);          // second trailing blank — preserved
    }

    [Fact]
    public void SplitBody_SingleLeadingAndTrailingBlank_StripsAll_CdataArtifact()
    {
        // Exactly one leading and one trailing blank line — the common CDATA artifact.
        // Both are removed, leaving only the actual content lines.
        const string bodyWithSingleBlanks = "\nSELECT 1\n";

        var body = ParseBody(bodyWithSingleBlanks);

        Assert.Single(body);
        Assert.Equal("SELECT 1", body[0]);
    }

    [Fact]
    public void SplitBody_InteriorBlankLines_AlwaysPreserved()
    {
        // Interior blank lines are never touched regardless of leading/trailing state.
        const string bodyWithInteriorBlanks = "\nSELECT 1\n\nSELECT 2\n";

        var body = ParseBody(bodyWithInteriorBlanks);

        Assert.Equal(3, body.Length);
        Assert.Equal("SELECT 1", body[0]);
        Assert.Equal("", body[1]);          // interior blank — preserved
        Assert.Equal("SELECT 2", body[2]);
    }

    [Fact]
    public void SplitBody_NoLeadingOrTrailingBlanks_BodyUnchanged()
    {
        // When there are no leading/trailing blanks, nothing should be stripped.
        const string bodyNoBlanks = "SELECT 1\nSELECT 2";

        var body = ParseBody(bodyNoBlanks);

        Assert.Equal(2, body.Length);
        Assert.Equal("SELECT 1", body[0]);
        Assert.Equal("SELECT 2", body[1]);
    }
}
