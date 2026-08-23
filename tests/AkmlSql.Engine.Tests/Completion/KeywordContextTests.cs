using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 032 US5 (T031) — context-correct keywords + built-in functions (clusters B2–B7, D).
/// Contract rows P10–P15. Campaign repro IDs referenced per test.
/// </summary>
public class KeywordContextTests
{
    private readonly TsqlParserService _parser = new();

    private List<string> Complete(string sqlWithMarker)
    {
        var caret = sqlWithMarker.IndexOf('|');
        Assert.True(caret >= 0);
        var sql = sqlWithMarker.Remove(caret, 1);
        var engine = new CompletionEngine(_parser);
        return engine.GetCompletions(sql, caret, NorthwindAutoTestCacheFactory.Create())
            .Items.Select(i => i.DisplayText).ToList();
    }

    // ── B2: ORDER / GROUP → BY (KW-023, KW-039) ────────────────────────────

    [Fact]
    public void Order_offers_BY_not_tables_or_having()
    {
        var items = Complete("SELECT * FROM dbo.Categories ORDER |");

        Assert.Contains("BY", items);
        Assert.DoesNotContain("HAVING", items);
        Assert.DoesNotContain(items, i => i.EndsWith("Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Group_offers_BY()
    {
        var items = Complete("SELECT c.CustomerName FROM dbo.Customers c GROUP |");

        Assert.Contains("BY", items);
        Assert.DoesNotContain("ORDER", items);
    }

    // ── B3: join qualifiers (KW-026…031) ───────────────────────────────────

    [Fact]
    public void Inner_offers_JOIN_only()
    {
        var items = Complete("SELECT * FROM dbo.Orders o INNER |");

        Assert.Contains("JOIN", items);
        Assert.DoesNotContain("OUTER JOIN", items);
        Assert.DoesNotContain("ON", items);
    }

    [Fact]
    public void Left_offers_JOIN_and_OUTER_JOIN()
    {
        var items = Complete("SELECT * FROM dbo.Orders o LEFT |");

        Assert.Contains("JOIN", items);
        Assert.Contains("OUTER JOIN", items);
    }

    [Fact]
    public void Cross_offers_JOIN_and_APPLY()
    {
        var items = Complete("SELECT * FROM dbo.Orders o CROSS |");

        Assert.Contains("JOIN", items);
        Assert.Contains("APPLY", items);
        Assert.DoesNotContain("OUTER JOIN", items);
    }

    [Fact]
    public void LeftOuter_offers_JOIN_only()
    {
        var items = Complete("SELECT * FROM dbo.Orders o LEFT OUTER |");

        Assert.Contains("JOIN", items);
        Assert.DoesNotContain("APPLY", items);
    }

    [Fact]
    public void Left_function_call_not_treated_as_join_qualifier()
    {
        // LEFT( is the string function — the caret inside it is an expression position.
        var items = Complete("SELECT LEFT(CustomerName, 2), | FROM dbo.Customers");

        Assert.Contains("CustomerName", items);
    }

    // ── B4: set operators (KW-050) ─────────────────────────────────────────

    [Fact]
    public void Union_offers_SELECT_and_ALL()
    {
        var items = Complete("SELECT CustomerID FROM dbo.Customers\nUNION |");

        Assert.Contains("SELECT", items);
        Assert.Contains("ALL", items);
    }

    // ── B5: DELETE → FROM (KW-049) ─────────────────────────────────────────

    [Fact]
    public void Delete_offers_FROM_not_set()
    {
        var items = Complete("DELETE |");

        Assert.Contains("FROM", items);
        Assert.DoesNotContain("SET", items);
        Assert.DoesNotContain("VALUES", items);
    }

    // ── B6: CASE states (KW-043, KW-044) ───────────────────────────────────

    [Fact]
    public void CaseWhen_condition_offers_THEN()
    {
        var items = Complete("SELECT CASE WHEN Price > 20 | FROM dbo.Products");

        Assert.Contains("THEN", items);
        Assert.DoesNotContain("ELSE", items);
    }

    [Fact]
    public void CaseThen_value_offers_ELSE_and_WHEN()
    {
        var items = Complete("SELECT CASE WHEN Price > 20 THEN 'High' | END AS Band FROM dbo.Products");

        Assert.Contains("ELSE", items);
        Assert.Contains("WHEN", items);
        Assert.DoesNotContain("THEN", items);
    }

    [Fact]
    public void MergeWhen_not_misclassified_as_case()
    {
        var items = Complete("MERGE dbo.Orders AS tgt USING dbo.OrderDetails AS src ON tgt.OrderID = src.OrderID WHEN |");

        Assert.DoesNotContain("THEN", items);
    }

    // ── B7: UPDATE TOP (n) (UPD-012 / P15) ─────────────────────────────────

    [Fact]
    public void UpdateTop_offers_tables()
    {
        var items = Complete("UPDATE TOP (10) |");

        Assert.Contains(items, i => i.EndsWith("Orders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.EndsWith("Products", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateTopTable_Set_offers_assignable_columns_not_set_options()
    {
        var items = Complete("UPDATE TOP (5) dbo.Orders SET |");

        Assert.Contains("OrderDate", items);
        Assert.DoesNotContain("ANSI_NULLS", items);
    }

    // ── D: built-in functions in expression positions (P14, INS-038) ───────

    [Fact]
    public void Where_comparison_offers_builtins()
    {
        var items = Complete("SELECT * FROM dbo.Orders WHERE OrderDate >= |");

        Assert.Contains("GETDATE", items);
        Assert.Contains("DATEADD", items);
    }

    [Fact]
    public void UpdateSet_value_offers_builtins()
    {
        var items = Complete("UPDATE dbo.Products SET Price = |");

        Assert.Contains("ROUND", items);
        Assert.Contains("ISNULL", items);
    }

    [Fact]
    public void InsertValues_offers_builtins()
    {
        var items = Complete("INSERT INTO dbo.Orders (CustomerID, OrderDate) VALUES (1, |");

        Assert.Contains("GETDATE", items);
    }

    [Fact]
    public void JoinOn_dot_qualified_offers_scalar_udfs()
    {
        var items = Complete("SELECT * FROM dbo.Orders o JOIN dbo.OrderDetails od ON dbo.|");

        Assert.Contains(items, i => i.Contains("fn_OrderItemCount", StringComparison.OrdinalIgnoreCase));
    }

    // ── Spec 032 US7 (T046) — H2/H3: ranking & suppression fidelity ────────

    [Fact]
    public void UpdateSet_target_excludes_identity_and_computed()
    {
        // UPD-020 / P22 — IDENTITY columns are not assignable.
        var items = Complete("UPDATE dbo.Categories SET |");

        Assert.DoesNotContain("CategoryID", items);
        Assert.Contains("CategoryName", items);
    }

    [Fact]
    public void UpdateSet_value_side_still_offers_identity()
    {
        // READING an identity column in the assigned expression is legal.
        var items = Complete("UPDATE dbo.Categories SET Description = |");

        Assert.Contains("CategoryID", items);
    }

    [Fact]
    public void CrossApply_partial_offers_table_valued_function()
    {
        // P23 — APPLY lexes as an Identifier and used to suppress everything.
        var items = Complete("SELECT * FROM dbo.Orders o CROSS APPLY fn_|");

        Assert.Contains(items, i => i.Contains("fn_OrdersByCustomer", StringComparison.OrdinalIgnoreCase));
    }
}
