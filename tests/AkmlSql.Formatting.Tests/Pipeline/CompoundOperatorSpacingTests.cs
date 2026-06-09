using System;
using System.IO;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T010 — base-pipeline fix. ScriptDom tokenises the compound comparison operators
/// (<c>&gt;=</c>, <c>&lt;=</c>, <c>&lt;&gt;</c>, <c>!=</c>, <c>!&lt;</c>, <c>!&gt;</c>) as two
/// adjacent single-char operator tokens, and the LayoutEngine's operator-spacing used to insert a
/// space between the halves (<c>x &gt;= y</c> → <c>x &gt; = y</c>, <c>x &lt;&gt; y</c> →
/// <c>x &lt; &gt; y</c>). These lock the re-joined output through the full pipeline.
/// </summary>
public class CompoundOperatorSpacingTests
{
    [Theory]
    [InlineData("select 1 where a >= 1;", ">=")]
    [InlineData("select 1 where a <= 1;", "<=")]
    [InlineData("select 1 where a <> 1;", "<>")]
    [InlineData("select 1 where a != 1;", "!=")]
    [InlineData("select 1 where a !< 1;", "!<")]
    [InlineData("select 1 where a !> 1;", "!>")]
    public void CompoundOperator_StaysJoined(string sql, string op)
    {
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.Contains(op, result.FormattedText);
        // The split form (one space inside the operator) must not appear.
        var split = op[0] + " " + op[1];
        Assert.DoesNotContain(split, result.FormattedText);
    }

    [Theory]
    [InlineData("select 1 where a = 1;", "a = 1")]   // single operators keep their spaces
    [InlineData("select 1 where a > 1;", "a > 1")]
    [InlineData("select 1 where a < 1;", "a < 1")]
    public void SingleOperator_KeepsSpaces(string sql, string expected)
    {
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());
        Assert.Contains(expected, result.FormattedText);
    }

    [Fact]
    public void CompoundOperators_AreIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format("select 1 where a >= 1 and b <> 2 and c <= 3;", profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
        Assert.DoesNotContain("> =", once.FormattedText);
        Assert.DoesNotContain("< >", once.FormattedText);
        Assert.DoesNotContain("< =", once.FormattedText);
    }

    private static FormattingProfile LoadDefaultStyle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found");
        var stylePath = Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "default.akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(stylePath));
    }
}
