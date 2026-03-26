using System.Linq;
using System.Threading;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>Integration tests — multiple rules running through AnalysisEngineTestHelper.</summary>
public sealed class AnalysisEngineTests
{
    [Fact]
    public void EmptyDocumentReturnsNoDiagnostics()
    {
        var diags = AnalysisEngineTestHelper.Analyze(string.Empty);
        Assert.Empty(diags);
    }

    [Fact]
    public void WhitespaceOnlyDocumentReturnsNoDiagnostics()
    {
        var diags = AnalysisEngineTestHelper.Analyze("   \r\n   ");
        Assert.Empty(diags);
    }

    [Fact]
    public void MultipleRulesFiredOnSingleStatement()
    {
        // This DELETE has no WHERE (PE003=Error) and no schema on Orders (PE002=Warning)
        const string sql = "DELETE FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql);

        // Must contain at least PE003 and PE002
        Assert.Contains(diags, d => d.RuleId == "PE003");
        Assert.Contains(diags, d => d.RuleId == "PE002");
    }

    [Fact]
    public void ProcedureBodyTriggersPE001AndPE009()
    {
        const string sql = """
            CREATE PROCEDURE dbo.GetAll AS
            BEGIN
                SELECT * FROM dbo.Orders
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql);

        Assert.Contains(diags, d => d.RuleId == "PE001"); // SELECT * in proc
        Assert.Contains(diags, d => d.RuleId == "PE009"); // Missing SET NOCOUNT ON
    }

    [Fact]
    public void BP004FiredAlongsidePE002()
    {
        const string sql = "SELECT * FROM Orders WHERE Col = NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql);

        Assert.Contains(diags, d => d.RuleId == "BP004");
        Assert.Contains(diags, d => d.RuleId == "PE002");
    }

    [Fact]
    public void DeprecatedDataTypeFiredForTextColumn()
    {
        const string sql = """
            CREATE TABLE dbo.Documents (
                Id    INT PRIMARY KEY,
                Body  text
            )
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP001");

        Assert.Single(diags);
        Assert.Equal("DEP001", diags[0].RuleId);
        Assert.Equal(DiagnosticSeverity.Warning, diags[0].Severity);
    }

    [Fact]
    public void DiagnosticOffsetsAreWithinDocumentBounds()
    {
        const string sql = "DELETE FROM dbo.Orders";
        var len = sql.Length;

        var diags = AnalysisEngineTestHelper.Analyze(sql);

        foreach (var d in diags)
        {
            Assert.True(d.StartOffset >= 0,            $"StartOffset < 0 for {d.RuleId}");
            Assert.True(d.EndOffset   <= len,           $"EndOffset > document length for {d.RuleId}");
            Assert.True(d.StartOffset <= d.EndOffset,   $"StartOffset > EndOffset for {d.RuleId}");
        }
    }

    [Fact]
    public void RuleIdFilterReturnsOnlyMatchingRule()
    {
        const string sql = "DELETE FROM Orders WHERE Id = NULL";

        var all      = AnalysisEngineTestHelper.Analyze(sql);
        var onlyBP04 = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.True(all.Count >= onlyBP04.Count);
        Assert.All(onlyBP04, d => Assert.Equal("BP004", d.RuleId));
    }
}
