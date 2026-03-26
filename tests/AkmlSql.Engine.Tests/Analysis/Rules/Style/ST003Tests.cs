using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class St003Tests
{
    [Fact]
    public void CommaJoin_Fires()
    {
        const string sql = "SELECT * FROM Orders, Customers WHERE Orders.CustomerId = Customers.Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST003");

        Assert.Single(diags);
        Assert.Equal("ST003", diags[0].RuleId);
    }

    [Fact]
    public void ExplicitInnerJoin_DoesNotFire()
    {
        const string sql = "SELECT * FROM Orders INNER JOIN Customers ON Orders.CustomerId = Customers.Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST003");

        Assert.Empty(diags);
    }

    [Fact]
    public void SingleTable_DoesNotFire()
    {
        const string sql = "SELECT * FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST003");

        Assert.Empty(diags);
    }

    [Fact]
    public void ExplicitLeftJoin_DoesNotFire()
    {
        const string sql = "SELECT * FROM Orders LEFT JOIN Customers ON Orders.CustomerId = Customers.Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST003");

        Assert.Empty(diags);
    }
}
