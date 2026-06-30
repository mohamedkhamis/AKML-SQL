using System;
using System.IO;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 (#1) — base-pipeline fix. A leading <c>-</c>/<c>+</c> that acts as a sign
/// (negation / explicit positive) must hug its operand (<c>-1</c>, not <c>- 1</c>). The
/// LineBreakDecider's default single-space rule used to separate them, so
/// <c>DATEADD(YEAR, -1, GETDATE())</c> formatted as <c>DATEADD(YEAR, - 1, GETDATE())</c>.
/// These lock the hugging output AND guard that binary subtraction is left alone.
/// </summary>
public class UnarySignSpacingTests
{
    // --- Unary sign hugs its operand (the bug) ---------------------------------------------------

    [Theory]
    [InlineData("select dateadd(year, -1, getdate()) as x;", "-1", "- 1")]   // after comma (corpus 13)
    [InlineData("select dateadd(month, -6, getdate()) as x;", "-6", "- 6")]  // after comma (corpus 04)
    public void UnaryMinus_AfterComma_Hugs(string sql, string hugged, string split)
    {
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        // Assert the formatter actually ran — a validation failure returns the ORIGINAL (which still
        // contains "-1"), which would pass the substring checks spuriously.
        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.True(result.WasModified, $"output was not modified: {result.FormattedText}");
        Assert.Contains(hugged, result.FormattedText);
        Assert.DoesNotContain(split, result.FormattedText);
    }

    [Fact]
    public void UnaryMinus_AtExpressionStart_Hugs()
    {
        var result = new FormatterPipeline().Format("select -1 as x;", LoadDefaultStyle());
        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.Contains("-1", result.FormattedText);
        Assert.DoesNotContain("- 1", result.FormattedText);
    }

    [Fact]
    public void UnaryMinus_AfterComparisonOperator_Hugs()
    {
        var result = new FormatterPipeline().Format("select 1 where a = -5;", LoadDefaultStyle());
        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.Contains("-5", result.FormattedText);
        Assert.DoesNotContain("- 5", result.FormattedText);
    }

    // --- Binary subtraction keeps its spaces (over-suppression guard) -----------------------------

    [Fact]
    public void BinaryMinus_AfterInteger_KeepsSpaces()
    {
        var result = new FormatterPipeline().Format("select 5 - 3 as x;", LoadDefaultStyle());
        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.Contains("5 - 3", result.FormattedText);
    }

    [Fact]
    public void BinaryMinus_AfterIdentifier_KeepsSpaces()
    {
        var result = new FormatterPipeline().Format("select a - b as x from t;", LoadDefaultStyle());
        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.Contains("a - b", result.FormattedText);
    }

    [Fact]
    public void BinaryMinus_AfterCloseParen_KeepsSpaces()
    {
        // prevPrev for the "1" operand is ")" — the most common real binary minus (after a
        // function call / subexpression). This confirms RightParenthesis is in the value-ending set.
        var result = new FormatterPipeline().Format("select count(*) - 1 as x from t;", LoadDefaultStyle());
        Assert.True(result.ValidationPassed, $"validation failed: {result.FormattedText}");
        Assert.Contains(") - 1", result.FormattedText);
    }

    [Fact]
    public void UnaryAndBinary_AreIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(
            "select dateadd(year, -1, getdate()) as a, 5 - 3 as b from t;", profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
        Assert.Contains("-1", once.FormattedText);
        Assert.DoesNotContain("- 1", once.FormattedText);
        Assert.Contains("5 - 3", once.FormattedText);
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
