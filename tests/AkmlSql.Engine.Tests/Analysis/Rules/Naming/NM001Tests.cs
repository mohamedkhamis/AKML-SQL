using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Naming;

public sealed class NM001Tests
{
    [Fact]
    public void ReservedWordAsColumnName_Fires()
    {
        // "Key" and "Order" are both in the reserved word list
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T ([Key] INT, [Order] INT)", "NM001");
        Assert.Equal(2, diags.Count);
        Assert.All(diags, d => Assert.Equal("NM001", d.RuleId));
    }

    [Fact]
    public void NonReservedColumnNames_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (OrderId INT, ProductName VARCHAR(100))", "NM001");
        Assert.Empty(diags);
    }
}
