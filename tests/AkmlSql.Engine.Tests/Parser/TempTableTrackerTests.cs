using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class TempTableTrackerTests
{
    private readonly TempTableTracker _tracker = new();

    private static TSqlScript ParseSql(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out _) as TSqlScript;
        return script!;
    }

    // ── CREATE TABLE #temp ────────────────────────────────────────────────

    [Fact]
    public void TrackTempTables_CreateTempTable_Found()
    {
        var sql = "CREATE TABLE #t (id INT, name VARCHAR(50));\nSELECT * FROM #t;";
        var script = ParseSql(sql);

        var tables = _tracker.TrackTempTables(script, sql.Length);

        Assert.True(tables.ContainsKey("#t"));
    }

    [Fact]
    public void TrackTempTables_CreateTempTable_ColumnsExtracted()
    {
        var sql = "CREATE TABLE #t (id INT, name VARCHAR(50));\nSELECT * FROM #t;";
        var script = ParseSql(sql);

        var tables = _tracker.TrackTempTables(script, sql.Length);

        Assert.Contains("id", tables["#t"]);
        Assert.Contains("name", tables["#t"]);
    }

    // ── SELECT INTO #temp ─────────────────────────────────────────────────

    [Fact]
    public void TrackTempTables_SelectInto_Found()
    {
        var sql = "SELECT col1, col2 INTO #result FROM src;\nSELECT * FROM #result;";
        var script = ParseSql(sql);

        var tables = _tracker.TrackTempTables(script, sql.Length);

        Assert.True(tables.ContainsKey("#result"));
    }

    [Fact]
    public void TrackTempTables_SelectInto_ColumnNamesInferred()
    {
        var sql = "SELECT col1, col2 INTO #result FROM src;\nSELECT * FROM #result;";
        var script = ParseSql(sql);

        var tables = _tracker.TrackTempTables(script, sql.Length);

        Assert.Contains("col1", tables["#result"]);
        Assert.Contains("col2", tables["#result"]);
    }

    // ── No temp tables ────────────────────────────────────────────────────

    [Fact]
    public void TrackTempTables_NoTempTables_ReturnsEmpty()
    {
        var sql = "SELECT 1;";
        var script = ParseSql(sql);

        var tables = _tracker.TrackTempTables(script, sql.Length);

        Assert.Empty(tables);
    }

    // ── Null script ───────────────────────────────────────────────────────

    [Fact]
    public void TrackTempTables_NullScript_ReturnsEmpty()
    {
        var tables = _tracker.TrackTempTables(null, 0);

        Assert.Empty(tables);
    }

    // ── Cursor offset before declaration ─────────────────────────────────

    [Fact]
    public void TrackTempTables_CursorBeforeDeclaration_NotIncluded()
    {
        var sql = "SELECT 1;\nCREATE TABLE #t (id INT);";
        var script = ParseSql(sql);

        // Cursor at offset 5 (before the CREATE TABLE)
        var tables = _tracker.TrackTempTables(script, 5);

        Assert.DoesNotContain("#t", tables.Keys);
    }

    // ── Regular tables not included ───────────────────────────────────────

    [Fact]
    public void TrackTempTables_RegularTable_NotIncluded()
    {
        var sql = "CREATE TABLE dbo.Orders (id INT);";
        var script = ParseSql(sql);

        var tables = _tracker.TrackTempTables(script, sql.Length);

        // Regular tables (no # prefix) should not be included
        Assert.DoesNotContain("Orders", tables.Keys);
    }
}
