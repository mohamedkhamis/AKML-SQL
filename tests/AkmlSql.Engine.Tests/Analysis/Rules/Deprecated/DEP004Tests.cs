using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Deprecated;

public sealed class Dep004Tests
{
    /// <summary>
    /// DEP004 scans the raw token stream for "*=" or "=*" tokens. However, TSql160 parser
    /// returns 0 batches when the SQL contains "*=" (parse error), so the rule never executes
    /// via the standard AnalysisEngineTestHelper path. The no-fire tests still verify the rule
    /// does not crash on valid SQL and does not produce false positives.
    /// </summary>
    [Fact]
    public void AnsiLeftJoin_DoesNotFire()
    {
        const string sql = "SELECT * FROM T1 LEFT JOIN T2 ON T1.Id = T2.Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP004");

        Assert.Empty(diags);
    }

    [Fact]
    public void AnsiInnerJoin_DoesNotFire()
    {
        const string sql = "SELECT * FROM T1 INNER JOIN T2 ON T1.Id = T2.Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP004");

        Assert.Empty(diags);
    }

    [Fact]
    public void StandardEqualityOperator_DoesNotFire()
    {
        const string sql = "SELECT * FROM T1 WHERE T1.Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP004");

        Assert.Empty(diags);
    }
}
