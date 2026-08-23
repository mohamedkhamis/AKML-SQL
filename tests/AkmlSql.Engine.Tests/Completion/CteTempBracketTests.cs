using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 032 US6 (T036–T038) — CTE resolution (E1/E3/E4/E5/E6), temp tables (F1–F3),
/// bracketed/quoted identifiers (G1–G4). Contract rows P16–P20.
/// </summary>
public class CteTempBracketTests
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

    // ── E1: alias over a CTE (P16) ─────────────────────────────────────────

    [Fact]
    public void Alias_over_cte_resolves_to_cte_columns()
    {
        var items = Complete("WITH cte AS (SELECT OrderID, OrderDate FROM dbo.Orders) SELECT x.| FROM cte x");

        Assert.Contains("OrderID", items);
        Assert.Contains("OrderDate", items);
    }

    // ── E3: CTEs are statement-scoped (P17, MULTI-082 shape) ───────────────

    [Fact]
    public void Cte_not_visible_past_its_statement()
    {
        var items = Complete("WITH cte AS (SELECT CustomerID FROM dbo.Customers) SELECT * FROM cte;\nSELECT * FROM |");

        Assert.DoesNotContain("cte", items);
    }

    // ── E4: SELECT * CTE body star-expands via sources ─────────────────────

    [Fact]
    public void Cte_with_star_body_exposes_source_columns()
    {
        var items = Complete("WITH cte AS (SELECT * FROM dbo.Products) SELECT cte.| FROM cte");

        Assert.Contains("ProductID", items);
        Assert.Contains("ProductName", items);
    }

    [Fact]
    public void Derived_table_with_star_body_exposes_source_columns()
    {
        // SUBQ-026 — the one remaining subqueries zero-item case.
        var items = Complete("SELECT d.| FROM (SELECT * FROM dbo.Products) d");

        Assert.Contains("ProductID", items);
        Assert.Contains("Price", items);
    }

    // ── E5: recursive CTE self-reference ───────────────────────────────────

    [Fact]
    public void Recursive_cte_sees_itself_inside_body()
    {
        var items = Complete(
            "WITH nums AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM | WHERE n < 10) SELECT * FROM nums");

        Assert.Contains("nums", items);
    }

    // ── E6: explicit column lists survive the token fallback ───────────────

    [Fact]
    public void Later_cte_body_keeps_earlier_ctes_explicit_columns()
    {
        // Caret inside the SECOND CTE body (unbalanced parens → token fallback).
        var items = Complete(
            "WITH x (OID, CID) AS (SELECT OrderID, CustomerID FROM dbo.Orders), y AS (SELECT x.| FROM x");

        Assert.Contains("OID", items);
        Assert.Contains("CID", items);
    }

    // ── F1: temp-table names offered (P18) ─────────────────────────────────

    [Fact]
    public void Temp_table_name_offered_after_from()
    {
        var items = Complete("CREATE TABLE #t (a INT, b INT);\nSELECT * FROM #|");

        Assert.Contains("#t", items);
    }

    // ── F2: tracker survives an unparsable trailing statement ──────────────

    [Fact]
    public void Tracker_keeps_definitions_when_cursor_is_past_parsed_extent()
    {
        // Simulates the shrunken-parse case: the trailing statement is mid-edit, so the
        // parsed extent ends before the caret — the old batch-containment gate dropped
        // every definition; the last-batch rule keeps them.
        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader("CREATE TABLE #t (a INT, b INT);");
        var script = parser.Parse(reader, out _) as Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript;

        var tracked = new TempTableTracker().TrackTempTables(script, cursorOffset: 500);

        Assert.True(tracked.ContainsKey("#t"));
        Assert.Contains("b", tracked["#t"]);
    }

    // ── F3: SELECT * INTO #t records usable columns (P19) ──────────────────

    [Fact]
    public void Select_star_into_temp_exposes_source_columns()
    {
        var items = Complete("SELECT * INTO #t FROM dbo.Orders;\nSELECT #t.| FROM #t");

        Assert.Contains("OrderID", items);
        Assert.Contains("OrderDate", items);
    }

    // ── G1/G2: unterminated bracket at caret (P20, INS-011 shape) ──────────

    [Fact]
    public void Unterminated_bracket_partial_filters_correctly()
    {
        var items = Complete("SELECT * FROM [Cust|");

        Assert.Contains(items, i => i.EndsWith("Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bracketed_schema_and_partial_scopes_to_schema()
    {
        var items = Complete("INSERT INTO [dbo].[Cust|");

        Assert.Contains(items, i => i.EndsWith("Customers", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.EndsWith(".Orders", StringComparison.OrdinalIgnoreCase) || i == "Orders");
    }

    // ── G3: double-quoted dot-scoping ──────────────────────────────────────

    [Fact]
    public void DoubleQuoted_schema_dot_scopes_to_schema_objects()
    {
        var items = Complete("SELECT * FROM \"dbo\".\"|");

        Assert.Contains(items, i => i.Contains("Customers", StringComparison.OrdinalIgnoreCase));
    }

    // ── G4: JOIN respects the typed schema qualifier ───────────────────────

    [Fact]
    public void Join_with_schema_qualifier_stays_in_schema()
    {
        var items = Complete("SELECT * FROM dbo.Orders o JOIN [Sales].[|");

        Assert.Contains(items, i => i.Contains("Invoices", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Contains(" ON ", StringComparison.OrdinalIgnoreCase));
    }
}
