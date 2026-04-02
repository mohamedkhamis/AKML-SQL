using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class Se007Tests
{
    [Fact]
    public void CrossDatabaseReference_Fires()
    {
        const string sql = "SELECT * FROM OtherDB.dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE007");

        Assert.Single(diags);
        Assert.Equal("SE007", diags[0].RuleId);
    }

    [Fact]
    public void TwoPartName_DoesNotFire()
    {
        const string sql = "SELECT * FROM dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE007");

        Assert.Empty(diags);
    }

    [Fact]
    public void UnqualifiedTableName_DoesNotFire()
    {
        const string sql = "SELECT * FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE007");

        Assert.Empty(diags);
    }

    [Fact]
    public void FourPartLinkedServerReference_Fires()
    {
        const string sql = "SELECT * FROM LinkedSrv.OtherDB.dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE007");

        Assert.Single(diags);
    }
}
