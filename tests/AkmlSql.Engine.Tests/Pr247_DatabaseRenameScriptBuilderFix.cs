using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// PR #247 regression: bracket-quoted identifiers in dependent module bodies were being rewritten
/// to the bare (unquoted) new name because <c>m.MatchedText</c> (which equals
/// <c>Identifier.Value</c> from ScriptDom — already stripped of brackets) was used to detect
/// bracket quoting. The fix reads the original source character at <c>m.StartOffset</c> instead.
/// </summary>
public class Pr247_DatabaseRenameScriptBuilderFix
{
    private static DatabaseRenameScriptBuilder.RenameTarget ObjectTarget(
        string schema, string name, string newName)
        => new(schema, name, newName, IsColumn: false, ParentTable: null);

    private static DatabaseRenameScriptBuilder.DependentDefinition Dep(
        string schema, string name, string typeDesc, string definition)
        => new(schema, name, typeDesc, definition);

    /// <summary>
    /// A dependent whose body uses bracket-quoted [My Table] must be rewritten to [My New Table],
    /// NOT to the bare unquoted "My New Table". Before the fix the bracket check always evaluated
    /// to false and the replacement was emitted unquoted — corrupting the identifier for any name
    /// that requires quoting (spaces, reserved words, special chars).
    /// </summary>
    [Fact]
    public void RewriteDependent_BracketQuotedIdentifier_PreservesBrackets()
    {
        var target = ObjectTarget(
            schema:  "dbo",
            name:    "My Table",      // bare name (as resolved from sys.objects)
            newName: "My New Table"); // new bare name

        var dependents = new[]
        {
            Dep("dbo", "UseMyTable", "SQL_STORED_PROCEDURE",
                "CREATE PROCEDURE dbo.UseMyTable AS SELECT * FROM dbo.[My Table]"),
        };

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(target, dependents);

        // The rewritten body must bracket-quote the new name because the original was bracketed.
        Assert.Contains("[My New Table]", script);

        // The bare (unquoted) replacement must NOT appear in the rewritten body section.
        // Find where ALTER starts so we only check the dependent body, not the header comment.
        int alterIdx = script.IndexOf("ALTER PROCEDURE", StringComparison.Ordinal);
        Assert.True(alterIdx >= 0, "Expected an ALTER PROCEDURE block in the script");

        var alterBody = script.Substring(alterIdx);
        Assert.DoesNotContain("FROM dbo.My New Table", alterBody);
        Assert.Contains("[My New Table]", alterBody);
    }

    /// <summary>
    /// An unquoted identifier in the dependent body must still be replaced without adding brackets —
    /// the fix must not over-correct plain identifiers.
    /// </summary>
    [Fact]
    public void RewriteDependent_UnquotedIdentifier_NoBracketsAdded()
    {
        var target = ObjectTarget(
            schema:  "dbo",
            name:    "GetOrders",
            newName: "GetCustomerOrders");

        var dependents = new[]
        {
            Dep("dbo", "ReportProc", "SQL_STORED_PROCEDURE",
                "CREATE PROCEDURE dbo.ReportProc AS EXEC dbo.GetOrders"),
        };

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(target, dependents);

        int alterIdx = script.IndexOf("ALTER PROCEDURE", StringComparison.Ordinal);
        Assert.True(alterIdx >= 0);
        var alterBody = script.Substring(alterIdx);

        // Must contain the plain (unquoted) new name, not a bracket-wrapped version.
        Assert.Contains("dbo.GetCustomerOrders", alterBody);
        Assert.DoesNotContain("[GetCustomerOrders]", alterBody);
    }
}
