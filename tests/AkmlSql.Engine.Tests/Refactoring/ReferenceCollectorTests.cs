using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring;

/// <summary>
/// Unit tests for <see cref="ReferenceCollector"/>.
/// </summary>
public class ReferenceCollectorTests
{
    private static readonly TsqlParserService ParserService;

    static ReferenceCollectorTests()
    {
        ParserService = new TsqlParserService();
        ParserService.SetServerVersion(160);
    }

    private static IReadOnlyList<ReferenceMatch> Collect(string sql, string targetName)
    {
        var script = ParserService.Parse(sql, out _) ?? new TSqlScript();
        var collector = new ReferenceCollector(targetName, "test.sql", sql);
        script.Accept(collector);
        return collector.Matches;
    }

    [Fact]
    public void Collect_SimpleColumn_FindsMatch()
    {
        // "col" appears as a column reference in the SELECT list
        var matches = Collect("SELECT col FROM t", "col");
        Assert.Single(matches);
        Assert.Equal("col", matches[0].MatchedText);
    }

    [Fact]
    public void Collect_QualifiedColumn_FindsObjectName()
    {
        // "t" appears both as the table alias in FROM and in "t.col"
        var matches = Collect("SELECT t.col FROM dbo.t", "t");
        // Should find at least one occurrence of "t"
        Assert.True(matches.Count >= 1);
        Assert.All(matches, m => Assert.Equal("t", m.MatchedText, ignoreCase: true));
    }

    [Fact]
    public void Collect_Variable_FindsMatch()
    {
        // @x declared and then selected
        var matches = Collect("DECLARE @x INT = 1; SELECT @x", "@x");
        // Should find at least one reference (the SELECT @x)
        Assert.True(matches.Count >= 1);
        Assert.All(matches, m => Assert.Contains("x", m.MatchedText, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Collect_CaseInsensitive_FindsMatch()
    {
        // Target is "col" (lowercase), document has "COL" (uppercase)
        var matches = Collect("SELECT COL FROM t", "col");
        Assert.Single(matches);
        Assert.Equal("COL", matches[0].MatchedText);
    }

    [Fact]
    public void Collect_NoMatch_ReturnsEmpty()
    {
        // "xyz" does not appear anywhere in the SQL
        var matches = Collect("SELECT a FROM b", "xyz");
        Assert.Empty(matches);
    }

    [Fact]
    public void Collect_ColumnReference_LineAndColumnArePositive()
    {
        var matches = Collect("SELECT col FROM t", "col");
        Assert.Single(matches);
        Assert.True(matches[0].Line >= 1);
        Assert.True(matches[0].Column >= 1);
    }

    [Fact]
    public void Collect_ColumnReference_StartOffsetIsNonNegative()
    {
        var sql = "SELECT col FROM t";
        var matches = Collect(sql, "col");
        Assert.Single(matches);
        Assert.True(matches[0].StartOffset >= 0);
        Assert.True(matches[0].EndOffset > matches[0].StartOffset);
    }

    [Fact]
    public void Collect_ColumnReference_MatchedTextExtractedFromSql()
    {
        var sql = "SELECT col FROM t";
        var matches = Collect(sql, "col");
        Assert.Single(matches);
        var extracted = sql.Substring(matches[0].StartOffset, matches[0].EndOffset - matches[0].StartOffset);
        Assert.Equal("col", extracted, ignoreCase: true);
    }

    [Fact]
    public void Collect_TableReference_FindsTableName()
    {
        var matches = Collect("SELECT * FROM Orders", "Orders");
        // NamedTableReference visit should find it
        Assert.True(matches.Count >= 1);
    }

    [Fact]
    public void Collect_EmptySql_ReturnsEmpty()
    {
        var matches = Collect(string.Empty, "col");
        Assert.Empty(matches);
    }

    [Fact]
    public void Collect_ContextSnippet_IsNotEmpty()
    {
        var matches = Collect("SELECT col FROM t", "col");
        Assert.Single(matches);
        Assert.False(string.IsNullOrEmpty(matches[0].ContextSnippet));
    }

    [Fact]
    public void Collect_FilePath_MatchesProvided()
    {
        var script = ParserService.Parse("SELECT col FROM t", out _) ?? new TSqlScript();
        var collector = new ReferenceCollector("col", "/some/path.sql", "SELECT col FROM t");
        script.Accept(collector);
        Assert.True(collector.Matches.Count >= 1);
        Assert.Equal("/some/path.sql", collector.Matches[0].FilePath);
    }
}
