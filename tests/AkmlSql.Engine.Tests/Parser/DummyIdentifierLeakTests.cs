using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

/// <summary>
/// The caret-repair helpers inject a synthetic <c>__akml_dummy__</c> identifier so that
/// half-typed SQL still produces an AST. That token is an internal parsing artifact and must
/// never reach the user as a suggestion.
///
/// <para><see cref="SuffixCompletionHelper.IsDummyIdentifier"/> was filtered in exactly one
/// place — the TOKEN-based alias path — while the AST-based column-inference path
/// (<c>CteResolver.InferColumnsFromQuery</c>, reached from both
/// <see cref="AliasResolver.ResolveDerivedTableProjections"/> and
/// <see cref="CteResolver.ResolveCtes"/>) added select-list names verbatim. Typing
/// <c>SELECT | FROM T</c> inside a derived table or CTE therefore offered
/// <c>__akml_dummy__</c> as a column.</para>
/// </summary>
public class DummyIdentifierLeakTests
{
    private static TSqlScript ParseRepaired(string sql, int cursorOffset)
    {
        var service = new TsqlParserService();
        return service.ParseWithSuffix(sql, cursorOffset, out _)!;
    }

    [Fact]
    public void DerivedTableProjection_OmitsRepairDummy()
    {
        // Caret sits in the derived table's empty select list — RepairAtCursor injects the dummy.
        const string prefix = "SELECT * FROM (SELECT ";
        const string sql = prefix + " FROM Products p) d";
        var script = ParseRepaired(sql, prefix.Length);

        var projections = new AliasResolver().ResolveDerivedTableProjections(script, prefix.Length);

        Assert.True(projections.ContainsKey("d"), "derived alias 'd' should resolve");
        Assert.DoesNotContain(projections["d"], SuffixCompletionHelper.IsDummyIdentifier);
    }

    /// <summary>
    /// A caret inside a CTE's own body normally leaves that CTE unregistered (it is not in scope
    /// there), so the only CTE path that publishes repaired columns is the recursive arm, which
    /// infers the CTE's shape from the ANCHOR member. A caret in the anchor puts the dummy
    /// straight into the published column list.
    /// </summary>
    [Fact]
    public void RecursiveCteAnchorColumns_OmitRepairDummy()
    {
        const string prefix = "WITH c AS (SELECT ";
        const string sql = prefix + " FROM dbo.Orders o UNION ALL SELECT n FROM c) SELECT * FROM c";
        var script = ParseRepaired(sql, prefix.Length);

        var ctes = new CteResolver().ResolveCtes(script, prefix.Length);

        Assert.True(ctes.ContainsKey("c"), "recursive CTE 'c' should publish its anchor columns");
        Assert.DoesNotContain(ctes["c"], SuffixCompletionHelper.IsDummyIdentifier);
    }
}
