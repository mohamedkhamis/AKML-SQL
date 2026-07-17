using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 032 US3 (T022) — EXEC procedure/parameter/variable completion (clusters B1, C3, C4).
/// Contract rows P5–P7. Campaign baseline: exec-procs 25%, clause=Exec fired 7× in 1,500 requests.
/// </summary>
public class ParameterProviderTests
{
    private readonly TsqlParserService _parser = new();

    private (string Sql, int Caret) AtMarker(string sqlWithMarker)
    {
        var caret = sqlWithMarker.IndexOf('|');
        Assert.True(caret >= 0, "test SQL must contain a caret marker");
        return (sqlWithMarker.Remove(caret, 1), caret);
    }

    private CompletionResponseItems Complete(string sqlWithMarker)
    {
        var (sql, caret) = AtMarker(sqlWithMarker);
        var engine = new CompletionEngine(_parser);
        var response = engine.GetCompletions(sql, caret, NorthwindAutoTestCacheFactory.Create());
        return new CompletionResponseItems(response.Items.Select(i => i.DisplayText).ToList());
    }

    private sealed record CompletionResponseItems(List<string> Items);

    // ── B1: EXEC dedicated token → ClauseType.Exec ────────────────────────

    [Fact]
    public void Analyzer_detects_exec_dedicated_token()
    {
        var (sql, caret) = AtMarker("EXEC |");
        var tokens = _parser.GetTokenStream(sql);
        var context = new CursorContextAnalyzer().Analyze(tokens, caret);

        Assert.Equal(ClauseType.Exec, context.ClauseType);
    }

    [Fact]
    public void Exec_position_offers_stored_procedures()
    {
        var items = Complete("EXEC |").Items;

        Assert.Contains(items, i => i.EndsWith("usp_GetCustomerOrders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.EndsWith("usp_MarkInvoicePaid", StringComparison.OrdinalIgnoreCase));
    }

    // ── C3: proc parameters in EXEC argument position ─────────────────────

    [Fact]
    public void Exec_argument_position_offers_parameters()
    {
        var items = Complete("EXEC dbo.usp_GetCustomerOrders |").Items;

        Assert.Contains("@CustomerID", items);
        Assert.Contains("@FromDate", items);
        Assert.Contains("@ToDate", items);
    }

    [Fact]
    public void Exec_argument_partial_offers_matching_parameter()
    {
        var items = Complete("EXEC dbo.usp_GetCustomerOrders @Cust|").Items;

        Assert.Contains("@CustomerID", items);
    }

    [Fact]
    public void Exec_schema_qualified_proc_offers_its_parameters()
    {
        var items = Complete("EXEC Sales.usp_MarkInvoicePaid |").Items;

        Assert.Contains("@InvoiceID", items);
    }

    [Fact]
    public void Exec_already_supplied_parameter_not_offered_again()
    {
        var items = Complete("EXEC dbo.usp_GetCustomerOrders @CustomerID = 1, |").Items;

        Assert.DoesNotContain("@CustomerID", items);
        Assert.Contains("@FromDate", items);
    }

    // ── C4: declared variables complete after '@' ─────────────────────────

    [Fact]
    public void Analyzer_extracts_variable_partial_text()
    {
        var (sql, caret) = AtMarker("SELECT @Cu|");
        var tokens = _parser.GetTokenStream(sql);
        var context = new CursorContextAnalyzer().Analyze(tokens, caret);

        Assert.Equal("@Cu", context.PartialText);
    }

    [Fact]
    public void Declared_variable_offered_on_at_prefix()
    {
        var items = Complete("DECLARE @CustomerID INT;\nSELECT @Cust|").Items;

        Assert.Contains("@CustomerID", items);
    }

    [Fact]
    public void Declared_variable_after_cursor_not_offered()
    {
        var items = Complete("SELECT @Cust|;\nDECLARE @CustomerID INT;").Items;

        Assert.DoesNotContain("@CustomerID", items);
    }
}
