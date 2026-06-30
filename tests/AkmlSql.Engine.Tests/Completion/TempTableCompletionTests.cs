using Xunit;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 030 (T029) — wires the built-but-unused TempTableTracker into the completion pipeline so
/// columns of #temp tables created earlier in the script (CREATE TABLE #t / SELECT … INTO #t) are
/// offered, mirroring how CTE columns are handled. Engine-side / dotnet-testable.
/// </summary>
public class TempTableCompletionTests
{
    private readonly TsqlParserService _parserService = new();

    private static DatabaseCache BuildEmptyCache()
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };
        cache.Schemas["dbo"] = new SchemaEntry { SchemaName = "dbo" };
        cache.RebuildFkIndex();
        return cache;
    }

    private List<string> ColumnsAt(string sqlWithMarker)
    {
        var cursorOffset = sqlWithMarker.IndexOf('|');
        Assert.True(cursorOffset >= 0, "test SQL must contain a cursor marker");
        var sql = sqlWithMarker.Replace("|", string.Empty);

        var engine = new CompletionEngine(_parserService);
        var response = engine.GetCompletions(sql, cursorOffset, BuildEmptyCache());
        return response.Items.Select(i => i.DisplayText).ToList();
    }

    [Fact]
    public void DotQualified_TempTable_SuggestsItsColumns()
    {
        var cols = ColumnsAt("CREATE TABLE #t (a INT, b INT);\nSELECT #t.| FROM #t");
        Assert.Contains("a", cols);
        Assert.Contains("b", cols);
    }

    [Fact]
    public void DotQualified_TempTableAlias_InWhere_SuggestsItsColumns()
    {
        var cols = ColumnsAt("CREATE TABLE #t (a INT, b INT);\nSELECT * FROM #t x WHERE x.|");
        Assert.Contains("a", cols);
        Assert.Contains("b", cols);
    }

    [Fact]
    public void BareColumn_WithTempTableInFrom_SuggestsItsColumns()
    {
        var cols = ColumnsAt("CREATE TABLE #t (a INT, b INT);\nSELECT a, | FROM #t");
        Assert.Contains("a", cols);
        Assert.Contains("b", cols);
    }

    [Fact]
    public void SelectInto_TempTable_SuggestsInferredColumns()
    {
        var cols = ColumnsAt("SELECT c1, c2 INTO #t FROM dbo.src;\nSELECT * FROM #t y WHERE y.|");
        Assert.Contains("c1", cols);
        Assert.Contains("c2", cols);
    }
}
