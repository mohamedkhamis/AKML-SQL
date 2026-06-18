using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 030 loop fix — guards the post-Apply auto-refresh classifier. The critical invariant is that
/// it NEVER green-lights re-running a mutating or multi-statement batch (which would duplicate INSERTs
/// or error on DDL against the persistent connection).
/// </summary>
public sealed class QueryRefreshSafetyTests
{
    [Theory]
    [InlineData("SELECT id, val FROM dbo.Customers")]
    [InlineData("select * from t")]
    [InlineData("SELECT id, val FROM #t ORDER BY id;")]      // single trailing ';' tolerated
    [InlineData("   SELECT 1   ")]                            // leading/trailing whitespace
    public void Allows_single_readonly_select(string sql)
        => Assert.True(QueryRefreshSafety.IsSingleReadOnlySelect(sql));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("CREATE TABLE #t(id int PRIMARY KEY, val varchar(10)); INSERT #t VALUES(1,'a'); SELECT val FROM #t;")]
    [InlineData("INSERT INTO t VALUES (1); SELECT * FROM t;")] // multi-statement with a mutation
    [InlineData("INSERT INTO t VALUES (1)")]                   // mutation, not a SELECT
    [InlineData("UPDATE t SET x = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("SELECT * INTO newtable FROM t")]              // SELECT … INTO creates a table
    [InlineData("WITH c AS (SELECT 1 AS n) SELECT n FROM c")]  // CTE — conservatively excluded
    [InlineData("SELECT 1; SELECT 2")]                          // two SELECTs is still multi-statement
    [InlineData("EXEC dbo.DoThing")]
    public void Rejects_unsafe_or_multistatement(string? sql)
        => Assert.False(QueryRefreshSafety.IsSingleReadOnlySelect(sql));
}
