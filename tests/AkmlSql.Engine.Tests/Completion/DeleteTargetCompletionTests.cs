using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// FROM-less DML targets: <c>DELETE t</c> and <c>UPDATE t</c> are valid T-SQL and must offer
/// table names, exactly like <c>DELETE FROM t</c>.
///
/// <para>Every case in the delete corpus (f07-delete.json) uses the FROM form, so the family
/// reported 100% while the FROM-less form was never exercised. The engine turned out to handle
/// it correctly all along — the reported bug was the shell's own object-expecting keyword gate
/// (see ObjectExpectingKeywordTests) — but nothing pinned the engine side either, so these lock
/// it down at the layer the corpus gate measures.</para>
/// </summary>
public class DeleteTargetCompletionTests
{
    private readonly TsqlParserService _parser = new();

    private List<string> Complete(string sqlWithMarker)
    {
        var caret = sqlWithMarker.IndexOf('|');
        Assert.True(caret >= 0, "test SQL must contain a caret marker");
        var sql = sqlWithMarker.Remove(caret, 1);
        var response = new CompletionEngine(_parser)
            .GetCompletions(sql, caret, NorthwindAutoTestCacheFactory.Create());
        return response.Items.Select(i => i.DisplayText).ToList();
    }

    [Theory]
    [InlineData("DELETE |")]
    [InlineData("DELETE Cus|")]
    [InlineData("UPDATE |")]
    [InlineData("UPDATE Cus|")]
    // The reported script shape: semicolon-less USE header, commented-out WHERE, prior statement.
    [InlineData("USE [Northwind]\n\nSELECT * FROM dbo.Customers \n--WHERE flag = 0 \nORDER BY CustomerID DESC;\n\nDELETE Cus|")]
    [InlineData("USE [Northwind]\n\nDELETE Cus|")]
    [InlineData("USE [Northwind]\nGO\n\nDELETE Cus|")]
    [InlineData("SELECT * FROM dbo.Customers\n\nUPDATE Cus|")]
    public void FromLessDmlTarget_offersTables(string sqlWithMarker)
    {
        // Qualified ("dbo.Customers") vs bare ("Customers") varies with statement context — a
        // missing semicolon before the DML keyword flips it. That is a separate presentation
        // question; what must hold here is that the target table is offered at all.
        Assert.Contains(Complete(sqlWithMarker),
            item => item == "Customers" || item == "dbo.Customers");
    }

    [Theory]
    [InlineData("DELETE dbo.|")]
    [InlineData("UPDATE dbo.|")]
    public void FromLessDmlTarget_schemaQualified_offersBareNames(string sqlWithMarker)
    {
        Assert.Contains("Customers", Complete(sqlWithMarker));
    }

    /// <summary>
    /// SSMS re-filters with the VS matcher, which keys on FilterText. A schema-qualified
    /// DisplayText with no FilterText would make a bare prefix like `cus` fail to match
    /// `dbo.Customers` in the IDE even though the engine returned the row.
    /// </summary>
    [Theory]
    [InlineData("DELETE |")]
    [InlineData("UPDATE |")]
    [InlineData("DELETE FROM |")]
    public void QualifiedTargets_carryABareFilterText(string sqlWithMarker)
    {
        var caret = sqlWithMarker.IndexOf('|');
        var sql = sqlWithMarker.Remove(caret, 1);
        var response = new CompletionEngine(_parser)
            .GetCompletions(sql, caret, NorthwindAutoTestCacheFactory.Create());

        var customers = response.Items.First(i => i.DisplayText == "dbo.Customers");
        Assert.Equal("Customers", customers.FilterText);
    }
}
