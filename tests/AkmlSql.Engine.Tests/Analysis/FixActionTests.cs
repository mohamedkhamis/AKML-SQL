using AkmlSql.Core.Models.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// Verifies that rules produce the correct fix replacement text.
/// These tests run purely through the engine — no shell/VS SDK required.
/// </summary>
public sealed class FixActionTests
{
    // ─── PE010: SELECT * → SELECT 1 in EXISTS ────────────────────────────────

    [Fact]
    public void PE010_FixReplacesSelectorWithOne()
    {
        const string sql = "SELECT Id FROM dbo.Orders WHERE EXISTS (SELECT * FROM dbo.Items WHERE OrderId = Orders.Id)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE010");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal("1", fix.ReplacementText);
    }

    [Fact]
    public void PE010_FixTypeIsTransform()
    {
        const string sql = "SELECT Id FROM dbo.Orders WHERE EXISTS (SELECT * FROM dbo.Items WHERE OrderId = Orders.Id)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE010");

        var fix = diags[0].FixActions[0];
        Assert.Equal(FixType.Transform, fix.FixType);
    }

    // ─── BP004: = NULL → IS NULL ─────────────────────────────────────────────

    [Fact]
    public void BP004_FixProducesIsNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE Col = NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal("Col IS NULL", fix.ReplacementText);
    }

    [Fact]
    public void BP004_FixProducesIsNotNull()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE Col <> NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP004");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal("Col IS NOT NULL", fix.ReplacementText);
    }

    // ─── PE001: Suppress fix action ─────────────────────────────────────────

    [Fact]
    public void PE001_SuppressFixHasCorrectRuleId()
    {
        const string sql = """
            CREATE PROCEDURE dbo.GetAll AS
            BEGIN
                SELECT * FROM dbo.Orders
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE001");

        Assert.Single(diags);
        var fix = diags[0].FixActions.FirstOrDefault(f => f.FixType == FixType.Suppress);
        Assert.NotNull(fix);
        Assert.Equal("PE001", fix.SuppressRuleId);
    }

    // ─── DEP001: text → varchar(max) ─────────────────────────────────────────

    [Fact]
    public void DEP001_FixReplacesTextWithVarcharMax()
    {
        const string sql = "CREATE TABLE dbo.Docs (Id INT, Body text)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP001");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal("varchar(max)", fix.ReplacementText);
    }

    [Fact]
    public void DEP001_FixReplacesNtextWithNvarcharMax()
    {
        const string sql = "CREATE TABLE dbo.Docs (Id INT, Body ntext)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP001");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal("nvarchar(max)", fix.ReplacementText);
    }

    // ─── PE009: Insert SET NOCOUNT ON ────────────────────────────────────────

    [Fact]
    public void PE009_FixTypeIsInsert()
    {
        const string sql = """
            CREATE PROCEDURE dbo.MyProc AS
            BEGIN
                SELECT 1
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE009");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal(FixType.Insert, fix.FixType);
        Assert.Contains("SET NOCOUNT ON", fix.ReplacementText);
    }

    // ─── BP001: @@IDENTITY → SCOPE_IDENTITY() ────────────────────────────────

    [Fact]
    public void BP001_FixReplacesWith_ScopeIdentity()
    {
        const string sql = "SELECT @@IDENTITY AS NewId";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP001");

        Assert.Single(diags);
        var fix = diags[0].FixActions[0];
        Assert.Equal("SCOPE_IDENTITY()", fix.ReplacementText);
    }
}
