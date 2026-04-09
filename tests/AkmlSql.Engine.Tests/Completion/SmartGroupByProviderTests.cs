using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

public class SmartGroupByProviderTests
{
    private readonly TsqlParserService _parserService = new();

    /// <summary>
    /// Builds an empty database cache so column suggestions don't interfere with
    /// the smart action item under test.
    /// </summary>
    private static DatabaseCache BuildEmptyCache()
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };
        cache.Schemas["dbo"] = new SchemaEntry { SchemaName = "dbo" };
        cache.RebuildFkIndex();
        return cache;
    }

    /// <summary>
    /// Drives the completion engine end-to-end and returns the smart-action item
    /// (the one with DisplayText starting with "▶") if present.
    /// </summary>
    private CompletionItem? RunAndGetSmartItem(string sqlWithMarker)
    {
        var cursorOffset = sqlWithMarker.IndexOf('|');
        Assert.True(cursorOffset >= 0, "test SQL must contain a cursor marker");
        var sql = sqlWithMarker.Replace("|", string.Empty);

        var engine = new CompletionEngine(_parserService);
        var response = engine.GetCompletions(sql, cursorOffset, BuildEmptyCache());

        return response.Items.FirstOrDefault(i =>
            i.DisplayText.StartsWith("▶", StringComparison.Ordinal));
    }

    // ── Basic case ──

    [Fact]
    public void Basic_SimpleSelectListAndAggregate_ProducesNonAggregatedColumns()
    {
        var item = RunAndGetSmartItem(
            "SELECT a, b, COUNT(*) FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("a, b", item!.InsertText);
    }

    // ── Aggregate functions filtered ──

    [Fact]
    public void Aggregates_AreFiltered()
    {
        var item = RunAndGetSmartItem(
            "SELECT a, COUNT(*), SUM(b), AVG(c), MIN(d), MAX(e) FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("a", item!.InsertText);
    }

    [Fact]
    public void StringAgg_IsFiltered()
    {
        var item = RunAndGetSmartItem(
            "SELECT region, STRING_AGG(name, ',') FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("region", item!.InsertText);
    }

    [Fact]
    public void WindowFunction_IsFiltered()
    {
        // Window functions (LAG, LEAD, ROW_NUMBER, etc.) are also incompatible with
        // bare GROUP BY because they reference rows individually.
        var item = RunAndGetSmartItem(
            "SELECT region, ROW_NUMBER() OVER (ORDER BY id) FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("region", item!.InsertText);
    }

    // ── AS alias stripping ──

    [Fact]
    public void Aliases_AreStripped()
    {
        var item = RunAndGetSmartItem(
            "SELECT a AS alpha, b AS beta, COUNT(*) AS total FROM tbl GROUP BY |");

        Assert.NotNull(item);
        // The "AS alpha" and "AS beta" suffixes must be removed; only the
        // expressions themselves are inserted.
        Assert.Equal("a, b", item!.InsertText);
    }

    [Fact]
    public void QualifiedColumns_ArePreserved()
    {
        var item = RunAndGetSmartItem(
            "SELECT c.Name, c.Region, COUNT(*) FROM Customers c JOIN Orders o ON c.Id = o.CustomerId GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("c.Name, c.Region", item!.InsertText);
    }

    // ── Wildcards ──

    [Fact]
    public void Wildcard_SuppressesSmartItem()
    {
        // SELECT * cannot be expanded by the token-based extractor — better to
        // produce no smart item than a misleading one.
        var item = RunAndGetSmartItem(
            "SELECT *, COUNT(*) FROM tbl GROUP BY |");

        Assert.Null(item);
    }

    [Fact]
    public void OnlyAggregates_ProducesNoSmartItem()
    {
        // No non-aggregated columns at all → nothing to add.
        var item = RunAndGetSmartItem(
            "SELECT COUNT(*), SUM(amount) FROM tbl GROUP BY |");

        Assert.Null(item);
    }

    // ── DISTINCT / TOP modifiers ──

    [Fact]
    public void DistinctModifier_IsSkipped()
    {
        var item = RunAndGetSmartItem(
            "SELECT DISTINCT a, b FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("a, b", item!.InsertText);
    }

    [Fact]
    public void TopModifier_IsSkipped()
    {
        var item = RunAndGetSmartItem(
            "SELECT TOP 10 a, b, COUNT(*) FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("a, b", item!.InsertText);
    }

    // ── Sort priority ──

    [Fact]
    public void SmartItem_SortsAtTop()
    {
        var cursorOffset = "SELECT a, b, COUNT(*) FROM tbl GROUP BY ".Length;
        var sql = "SELECT a, b, COUNT(*) FROM tbl GROUP BY ";
        var engine = new CompletionEngine(_parserService);
        var response = engine.GetCompletions(sql, cursorOffset, BuildEmptyCache());

        var smart = response.Items.FirstOrDefault(i =>
            i.DisplayText.StartsWith("▶", StringComparison.Ordinal));
        Assert.NotNull(smart);

        var others = response.Items
            .Where(i => !i.DisplayText.StartsWith("▶", StringComparison.Ordinal))
            .ToList();
        if (others.Count > 0)
        {
            Assert.True(smart!.SortPriority < others.Min(i => i.SortPriority),
                "smart action must sort above all other completion items");
        }

        Assert.Equal((int)CompletionObjectType.Snippet, smart!.ObjectType);
    }

    // ── PartialText guard ──

    [Fact]
    public void PartialTextPresent_SuppressesSmartItem()
    {
        // Once the user starts typing a column name, they want fuzzy matching,
        // not the smart action.
        var item = RunAndGetSmartItem(
            "SELECT a, b, COUNT(*) FROM tbl GROUP BY a|");

        Assert.Null(item);
    }

    // ── Multi-statement scoping ──

    [Fact]
    public void PreviousStatement_DoesNotLeak()
    {
        // The smart item should reflect ONLY the SELECT list of the statement
        // containing the cursor, not the previous statement.
        var item = RunAndGetSmartItem(
            "SELECT x, y FROM other; SELECT a, b, COUNT(*) FROM tbl GROUP BY |");

        Assert.NotNull(item);
        Assert.Equal("a, b", item!.InsertText);
    }

    // ── Non-GroupBy contexts ──

    [Fact]
    public void NotInGroupBy_ProducesNoSmartItem()
    {
        var item = RunAndGetSmartItem("SELECT a, b FROM tbl WHERE |");

        Assert.Null(item);
    }

    // ── Internal helper test for ExtractNonAggregatedSelectExpressions ──

    [Fact]
    public void ExtractNonAggregated_HandlesNestedParensCorrectly()
    {
        // Commas INSIDE function calls / parens must not split expressions.
        var sql = "SELECT a, ISNULL(b, 0), COUNT(*) FROM tbl GROUP BY ";
        var tokens = _parserService.GetTokenStream(sql);
        var cursor = sql.Length;

        var result = SmartGroupByProvider.ExtractNonAggregatedSelectExpressions(tokens, cursor);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0]);
        Assert.Equal("ISNULL(b, 0)", result[1]);
    }
}
