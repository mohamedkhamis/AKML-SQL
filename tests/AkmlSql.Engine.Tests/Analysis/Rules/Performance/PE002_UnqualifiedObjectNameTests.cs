using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

/// <summary>
/// PE002 flags unqualified table/view references — but a CTE reference has no schema BY
/// DEFINITION: 'dbo.LatestPerTerminal' would break the query. The rule previously flagged
/// every CTE use (the user hit "Object 'LatestPerTerminal' has no schema prefix" on a CTE,
/// and the spike corpus even baked the false positive into 04-cte.expected.json).
/// </summary>
public sealed class Pe002UnqualifiedObjectNameTests
{
    [Fact]
    public void FiresOnUnqualifiedTable()
    {
        const string sql = "SELECT Id FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE002");

        Assert.Single(diags);
        Assert.Contains("Orders", diags[0].Message);
    }

    [Fact]
    public void DoesNotFireOnQualifiedTable()
    {
        const string sql = "SELECT Id FROM dbo.Orders";

        Assert.Empty(AnalysisEngineTestHelper.Analyze(sql, "PE002"));
    }

    [Fact]
    public void DoesNotFireOnCteReference()
    {
        const string sql = """
            WITH LatestPerTerminal AS (
                SELECT TerminalId, MAX(EventTime) AS LastSeen
                FROM dbo.TerminalEvents
                GROUP BY TerminalId
            )
            SELECT * FROM LatestPerTerminal;
            """;

        Assert.Empty(AnalysisEngineTestHelper.Analyze(sql, "PE002"));
    }

    [Fact]
    public void DoesNotFireOnRecursiveCteSelfReference()
    {
        const string sql = """
            WITH DirectReports AS (
                SELECT EmployeeId, ManagerId FROM dbo.Employees WHERE ManagerId IS NULL
                UNION ALL
                SELECT e.EmployeeId, e.ManagerId
                FROM dbo.Employees AS e
                INNER JOIN DirectReports AS d ON e.ManagerId = d.EmployeeId
            )
            SELECT * FROM DirectReports;
            """;

        Assert.Empty(AnalysisEngineTestHelper.Analyze(sql, "PE002"));
    }

    [Fact]
    public void StillFlagsARealUnqualifiedTableInsideACteBody()
    {
        const string sql = """
            WITH Latest AS (
                SELECT Id FROM Orders
            )
            SELECT * FROM Latest;
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE002");

        Assert.Single(diags);
        Assert.Contains("'Orders'", diags[0].Message);
    }

    [Fact]
    public void SecondCteMayReferenceTheFirst()
    {
        const string sql = """
            WITH A AS (SELECT Id FROM dbo.T1),
                 B AS (SELECT Id FROM A)
            SELECT * FROM B;
            """;

        Assert.Empty(AnalysisEngineTestHelper.Analyze(sql, "PE002"));
    }

    [Fact]
    public void DoesNotFireOnTempTablesAndTableVariables()
    {
        const string sql = "SELECT Id FROM #staging; SELECT Id FROM @rows;";

        Assert.Empty(AnalysisEngineTestHelper.Analyze(sql, "PE002"));
    }
}
