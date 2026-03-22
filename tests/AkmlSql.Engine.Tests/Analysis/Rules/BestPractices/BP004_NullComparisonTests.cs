using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP004_NullComparisonTests
{
    [Fact]
    public void FiresOnEqualsNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId = NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
        Assert.Equal("BP004", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnIsNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId IS NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnIsNotNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId IS NOT NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnNotEqualsNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId <> NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
    }

    [Fact]
    public void SeverityIsError()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId = NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
    }

    [Fact]
    public void FixActionProducesIsNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId = NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
        Assert.Single(diags[0].FixActions);
        var fix = diags[0].FixActions[0];
        Assert.Contains("IS NULL", fix.ReplacementText);
        Assert.DoesNotContain("IS NOT NULL", fix.ReplacementText);
    }

    [Fact]
    public void FixActionProducesIsNotNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId <> NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Contains("IS NOT NULL", fix.ReplacementText);
    }

    [Fact]
    public void FixCoversEntireExpression()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE CustomerId = NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        var fix = diags[0].FixActions[0];
        // Replacement should cover "CustomerId = NULL" (from StartOffset to EndOffset of the BooleanComparisonExpression)
        Assert.True(fix.ReplacementEnd > fix.ReplacementStart);
        Assert.Equal("CustomerId IS NULL", fix.ReplacementText);
    }
}
