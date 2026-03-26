using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class St004Tests
{
    [Fact]
    public void SelectWithoutSemicolon_Fires()
    {
        const string sql = "SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST004");

        Assert.Single(diags);
        Assert.Equal("ST004", diags[0].RuleId);
    }

    [Fact]
    public void SelectWithSemicolon_DoesNotFire()
    {
        const string sql = "SELECT 1;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST004");

        Assert.Empty(diags);
    }

    [Fact]
    public void InsertWithoutSemicolon_Fires()
    {
        const string sql = "INSERT INTO Orders (Name) VALUES ('Test')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST004");

        Assert.Single(diags);
    }

    [Fact]
    public void UpdateWithSemicolon_DoesNotFire()
    {
        const string sql = "UPDATE Orders SET Name = 'Test' WHERE Id = 1;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST004");

        Assert.Empty(diags);
    }

    [Fact]
    public void FixActionInsertsAtEnd()
    {
        const string sql = "SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST004");

        Assert.Single(diags);
        Assert.NotEmpty(diags[0].FixActions);
        Assert.Equal(";", diags[0].FixActions[0].ReplacementText);
    }
}
