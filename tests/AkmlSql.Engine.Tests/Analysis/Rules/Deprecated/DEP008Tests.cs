using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Deprecated;

public sealed class Dep008Tests
{
    /// <summary>
    /// DEP008 is a stub rule that always returns an empty diagnostic list.
    /// These tests verify it does not crash and never produces false positives.
    /// </summary>
    [Fact]
    public void WithNolock_DoesNotFire()
    {
        const string sql = "SELECT * FROM Orders WITH (NOLOCK)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP008");

        Assert.Empty(diags);
    }

    [Fact]
    public void AnyValidSql_DoesNotFire()
    {
        const string sql = "SELECT Id, Name FROM dbo.Orders WHERE Id = 1;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP008");

        Assert.Empty(diags);
    }
}
