using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class SE003Tests
{
    [Fact]
    public void GrantToPublic_Fires()
    {
        const string sql = "GRANT SELECT ON Orders TO PUBLIC";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE003");

        Assert.Single(diags);
        Assert.Equal("SE003", diags[0].RuleId);
    }

    [Fact]
    public void GrantToNamedUser_DoesNotFire()
    {
        const string sql = "GRANT SELECT ON Orders TO AppUser";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE003");

        Assert.Empty(diags);
    }
}
