using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 032 US4 (T027) — INSERT target/column-list scoping (clusters C1/C2, contract rows P8/P9).
/// Campaign baseline: insert family 42/80 — `INSERT INTO t (|` offered a generic object list.
/// </summary>
public class InsertCompletionTests
{
    private readonly TsqlParserService _parser = new();

    private (string Sql, int Caret) AtMarker(string sqlWithMarker)
    {
        var caret = sqlWithMarker.IndexOf('|');
        Assert.True(caret >= 0, "test SQL must contain a caret marker");
        return (sqlWithMarker.Remove(caret, 1), caret);
    }

    private List<string> Complete(string sqlWithMarker)
    {
        var (sql, caret) = AtMarker(sqlWithMarker);
        var engine = new CompletionEngine(_parser);
        var response = engine.GetCompletions(sql, caret, NorthwindAutoTestCacheFactory.Create());
        return response.Items.Select(i => i.DisplayText).ToList();
    }

    private CursorContext Analyze(string sqlWithMarker)
    {
        var (sql, caret) = AtMarker(sqlWithMarker);
        return new CursorContextAnalyzer().Analyze(_parser.GetTokenStream(sql), caret);
    }

    // ── Analyzer: the three INSERT positions ───────────────────────────────

    [Fact]
    public void InsertInto_bare_is_target_position()
    {
        Assert.Equal(ClauseType.InsertTarget, Analyze("INSERT INTO |").ClauseType);
    }

    [Fact]
    public void InsertInto_column_paren_is_column_list_with_target_injected()
    {
        var context = Analyze("INSERT INTO dbo.Customers (|");

        Assert.Equal(ClauseType.InsertColumnList, context.ClauseType);
        Assert.True(context.AvailableAliases.ContainsKey("Customers"));
        Assert.Equal("dbo.Customers", context.AvailableAliases["Customers"]);
    }

    [Fact]
    public void Insert_bare_is_keyword_position()
    {
        Assert.Equal(ClauseType.InsertColumns, Analyze("INSERT |").ClauseType);
    }

    [Fact]
    public void Insert_after_closed_column_list_is_keyword_position()
    {
        Assert.Equal(ClauseType.InsertColumns, Analyze("INSERT INTO dbo.Customers (CustomerName) |").ClauseType);
    }

    // ── P9: INSERT INTO | offers insertable objects only ───────────────────

    [Fact]
    public void InsertTarget_offers_tables_and_views_not_procs_or_functions()
    {
        var items = Complete("INSERT INTO |");

        Assert.Contains(items, i => i.EndsWith("Customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.EndsWith("vw_CustomerOrders", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Contains("usp_GetCustomerOrders", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Contains("fn_OrderItemCount", StringComparison.OrdinalIgnoreCase));
    }

    // ── P8: INSERT INTO t (| offers t's columns ────────────────────────────

    [Fact]
    public void InsertColumnList_offers_target_columns_only()
    {
        var items = Complete("INSERT INTO dbo.Customers (|");

        Assert.Contains("CustomerName", items);
        Assert.Contains("City", items);
        Assert.DoesNotContain("ProductName", items);
        Assert.DoesNotContain(items, i => i.Contains("usp_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InsertColumnList_excludes_identity_columns()
    {
        var items = Complete("INSERT INTO dbo.Customers (|");

        Assert.DoesNotContain("CustomerID", items);
    }

    [Fact]
    public void InsertColumnList_second_column_still_scoped()
    {
        // MULTI-072 shape: prior statement + partial column list.
        var items = Complete("DELETE FROM Sales.Invoices;\nINSERT INTO dbo.Customers (CustomerName, |");

        Assert.Contains("City", items);
        Assert.Contains("Country", items);
        Assert.DoesNotContain("InvoiceDate", items);
    }

    // ── AfterInsert keywords ───────────────────────────────────────────────

    [Fact]
    public void Insert_keyword_position_offers_INTO()
    {
        var items = Complete("INSERT |");

        Assert.Contains("INTO", items);
    }
}
