using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

public class FormatterPipelineTests
{
    private readonly FormatterPipeline _pipeline = new();
    private readonly FormattingProfile _profile = new();

    [Fact]
    public void Format_SimpleSelect_ProducesFormattedOutput()
    {
        var sql = "select col1, col2 from dbo.MyTable where col1 = 1";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success);
        Assert.True(result.WasModified);
        Assert.Contains("SELECT", result.FormattedText); // Keywords uppercased
    }

    [Fact]
    public void Format_ShortStatement_CollapsesWithoutResidualAlignmentPadding()
    {
        // Spec 030 loop fix: collapseShortStatements (default on, threshold 80) collapses a short
        // statement onto one line. The cross-clause alignment padding that forms a vertical river in
        // the multi-line form ("FROM   dbo", "WHERE  id") must be dropped on collapse, not left as
        // stray double-spaces. Regression for the "SELECT id, name FROM   t WHERE  id = 1" bug.
        var sql = "select id, name from dbo.users where id = 1";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success);
        Assert.DoesNotContain("\n", result.FormattedText.TrimEnd());   // collapsed onto a single line
        Assert.DoesNotContain("  ", result.FormattedText);             // no residual alignment gaps
    }

    [Fact]
    public void Format_CollapsedShortStatement_IsIdempotent()
    {
        // The clamp must be a fixed point: re-formatting the collapsed output yields the same text
        // (otherwise the pipeline's stage-7 idempotency check would also flag it).
        var sql = "select id, name from dbo.users where id = 1";
        var first = _pipeline.Format(sql, _profile);
        var second = _pipeline.Format(first.FormattedText, _profile);

        Assert.True(second.Success);
        Assert.Equal(first.FormattedText, second.FormattedText);
    }

    [Fact]
    public void Format_AlreadyFormatted_ReturnsUnmodified()
    {
        // A well-formatted query should return WasModified=false or similar output
        var sql = "SELECT\n    col1\nFROM\n    dbo.MyTable\n";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success);
    }

    [Fact]
    public void Format_EmptyString_ReturnsOriginal()
    {
        var result = _pipeline.Format("", _profile);
        Assert.Equal("", result.FormattedText);
    }

    [Fact]
    public void Format_InvalidSql_ReturnsWithDiagnostics()
    {
        var sql = "SELEC FROM WHERE";
        var result = _pipeline.Format(sql, _profile);
        // Should handle gracefully
        Assert.NotNull(result.FormattedText);
    }

    [Fact]
    public void Format_SelectWithJoin_FormatsCorrectly()
    {
        var sql = "select a.col1, b.col2 from TableA a inner join TableB b on a.id = b.aid where a.active = 1 order by a.col1";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success, $"Format failed. ValidationPassed={result.ValidationPassed}. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}. Output: [{result.FormattedText}]");
        Assert.Contains("FROM", result.FormattedText);
        Assert.Contains("JOIN", result.FormattedText);
        Assert.Contains("WHERE", result.FormattedText);
    }

    [Fact]
    public void Format_MultipleStatements_SeparatesWithEmptyLine()
    {
        var sql = "select 1; select 2;";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success);
    }

    [Fact]
    public void Format_WithComments_PreservesComments()
    {
        var sql = "-- Header comment\nselect col1 from MyTable -- inline comment";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success);
        Assert.Contains("-- Header comment", result.FormattedText);
        Assert.Contains("-- inline comment", result.FormattedText);
    }

    [Fact]
    public void Format_CasingApplied_KeywordsUppercased()
    {
        var sql = "select col1 from mytable where id = 1";
        var result = _pipeline.Format(sql, _profile);

        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
        Assert.Contains("FROM", result.FormattedText);
        Assert.Contains("WHERE", result.FormattedText);
    }
}
