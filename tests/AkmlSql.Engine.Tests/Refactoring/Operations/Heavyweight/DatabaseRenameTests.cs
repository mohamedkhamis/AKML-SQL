using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// Spec 030 / T060 / US5 / FR-018 / R8 — unit tests for the PURE database-wide Smart Rename
/// script builder (<see cref="DatabaseRenameScriptBuilder.BuildRenameScript"/>). These exercise the
/// reviewable-script generation WITHOUT a live SQL Server: the test feeds synthetic
/// <see cref="DatabaseRenameScriptBuilder.RenameTarget"/> + <see cref="DatabaseRenameScriptBuilder.DependentDefinition"/>
/// rows directly (mirroring <c>FindInvalidObjectsHandlerTests</c> feeding synthetic <c>DependencyRow</c>s).
/// This is what proves the testability split: all live work is isolated in
/// <c>DatabaseRenameDependencyReader</c>, so the builder is deterministic and database-free.
/// </summary>
public class DatabaseRenameTests
{
    private static DatabaseRenameScriptBuilder.RenameTarget ObjectTarget(
        string schema = "dbo", string name = "GetOrders", string newName = "GetCustomerOrders")
        => new(schema, name, newName, IsColumn: false, ParentTable: null);

    private static DatabaseRenameScriptBuilder.RenameTarget ColumnTarget(
        string schema = "dbo", string table = "Orders", string name = "Total", string newName = "OrderTotal")
        => new(schema, name, newName, IsColumn: true, ParentTable: table);

    private static DatabaseRenameScriptBuilder.DependentDefinition Dep(
        string schema, string name, string typeDesc, string definition)
        => new(schema, name, typeDesc, definition);

    // ── Object rename ────────────────────────────────────────────────────────

    [Fact]
    public void ObjectRename_EmitsSpRename_ObjectForm()
    {
        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ObjectTarget(), []);

        Assert.Contains("sp_rename", script);
        // OBJECT form: 'schema.obj','new' — no ,'COLUMN' object-type argument.
        Assert.Contains("[dbo].[GetOrders]", script);
        Assert.Contains("GetCustomerOrders", script);
        Assert.DoesNotContain("'COLUMN'", script);
    }

    [Fact]
    public void ObjectRename_ZeroDependents_StillRenamesObject()
    {
        // The US5 / zero-dependents case: sp_rename only, no ALTER.
        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ObjectTarget(), []);

        Assert.Contains("sp_rename", script);
        Assert.DoesNotContain("ALTER", script);
        Assert.DoesNotContain("CREATE", script); // no leftover CREATE blocks when there are no dependents
    }

    [Fact]
    public void ObjectRename_WithDependents_AltersEachDependent()
    {
        // US5.1: renaming an OBJECT must also rewrite all referencing objects, because sp_rename does
        // NOT touch dependent module text — they break until each is ALTERed.
        var dependents = new[]
        {
            Dep("dbo", "ReportProc", "SQL_STORED_PROCEDURE",
                "CREATE PROCEDURE dbo.ReportProc AS BEGIN EXEC dbo.GetOrders; END"),
            Dep("dbo", "OrdersView", "VIEW",
                "CREATE VIEW dbo.OrdersView AS SELECT * FROM dbo.GetOrders()"),
        };

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ObjectTarget(), dependents);

        Assert.Contains("sp_rename", script);
        // Each dependent is re-issued as ALTER (not CREATE) with the new name substituted.
        Assert.Contains("ALTER PROCEDURE dbo.ReportProc", script);
        Assert.Contains("ALTER VIEW dbo.OrdersView", script);
        Assert.Contains("dbo.GetCustomerOrders", script);
        Assert.DoesNotContain("CREATE PROCEDURE", script);
        Assert.DoesNotContain("CREATE VIEW", script);
    }

    [Fact]
    public void ObjectRename_DependentBody_RewrittenCleanly_NoDoubleApply()
    {
        // Regression: ReferenceCollector reports an object identifier TWICE at the same offset span
        // (parent NamedTableReference + child SchemaObjectName). Without de-dup, both Remove+Insert
        // edits landed on the same span and double-rewrote the text (observed "EXEC
        // dbo.GetCustomeGetCustomerOrders"). The builder must de-dup by span → exactly-once clean rewrite.
        // NOTE: the earlier WithDependents test couldn't catch this — the corrupt string still *contains*
        // "GetCustomerOrders" as a substring, so a Contains() assert passes. This asserts the EXACT body.
        var dependents = new[]
        {
            Dep("dbo", "ReportProc", "SQL_STORED_PROCEDURE",
                "CREATE PROCEDURE dbo.ReportProc AS BEGIN EXEC dbo.GetOrders; END"),
        };

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ObjectTarget(), dependents);

        Assert.Contains("ALTER PROCEDURE dbo.ReportProc AS BEGIN EXEC dbo.GetCustomerOrders; END", script);
        // The old reference must not survive anywhere as a body EXEC (the sp_rename literal uses brackets).
        Assert.DoesNotContain("EXEC dbo.GetOrders", script);
    }

    // ── Column rename (the literal US5 acceptance, spec.md:104/108) ────────────

    [Fact]
    public void ColumnRename_EmitsSpRename_ColumnForm()
    {
        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ColumnTarget(), []);

        Assert.Contains("sp_rename", script);
        // COLUMN form: 'schema.table.oldcol','new','COLUMN'
        Assert.Contains("[dbo].[Orders].[Total]", script);
        Assert.Contains("'COLUMN'", script);
        Assert.Contains("OrderTotal", script);
    }

    [Fact]
    public void ColumnRename_AltersEachDependentWithOldToNew()
    {
        // sp_rename on a COLUMN does NOT rewrite dependent module bodies — each proc/view/function
        // that references the old column name must be ALTERed with old → new. This is the literal
        // US5 column-rename acceptance (spec.md:104/108).
        var dependents = new[]
        {
            Dep("dbo", "OrderSummary", "VIEW",
                "CREATE VIEW dbo.OrderSummary AS SELECT Total FROM dbo.Orders"),
            Dep("dbo", "GetTotal", "SQL_SCALAR_FUNCTION",
                "CREATE FUNCTION dbo.GetTotal() RETURNS money AS BEGIN RETURN (SELECT SUM(Total) FROM dbo.Orders) END"),
        };

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ColumnTarget(), dependents);

        // sp_rename COLUMN first.
        int renameIdx = script.IndexOf("sp_rename", System.StringComparison.Ordinal);
        Assert.True(renameIdx >= 0);
        Assert.Contains("'COLUMN'", script);

        // Then a per-dependent ALTER with the old column name rewritten to the new one in the body.
        Assert.Contains("ALTER VIEW dbo.OrderSummary", script);
        Assert.Contains("ALTER FUNCTION dbo.GetTotal", script);
        Assert.Contains("SELECT OrderTotal FROM dbo.Orders", script);
        Assert.Contains("SUM(OrderTotal)", script);

        // The old column name must no longer appear inside the rewritten dependent bodies
        // (it may still appear in the sp_rename literal and comment header).
        int firstAlter = script.IndexOf("ALTER VIEW", System.StringComparison.Ordinal);
        var afterRename = script.Substring(firstAlter);
        Assert.DoesNotContain("SELECT Total FROM", afterRename);
        Assert.DoesNotContain("SUM(Total)", afterRename);
    }

    [Fact]
    public void Rename_IsDependencyOrdered_RenameBeforeAlters()
    {
        var dependents = new[]
        {
            Dep("dbo", "OrderSummary", "VIEW",
                "CREATE VIEW dbo.OrderSummary AS SELECT Total FROM dbo.Orders"),
        };

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ColumnTarget(), dependents);

        int renameIdx = script.IndexOf("sp_rename", System.StringComparison.Ordinal);
        int alterIdx = script.IndexOf("ALTER VIEW", System.StringComparison.Ordinal);

        Assert.True(renameIdx >= 0, "sp_rename must be present");
        Assert.True(alterIdx > renameIdx, "the rename must come before the dependent ALTERs");
    }

    [Fact]
    public void Rename_IsTransactionWrapped()
    {
        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ObjectTarget(), []);

        Assert.Contains("SET XACT_ABORT ON", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Contains("COMMIT TRANSACTION", script);

        // BEGIN must precede COMMIT.
        Assert.True(
            script.IndexOf("BEGIN TRANSACTION", System.StringComparison.Ordinal) <
            script.IndexOf("COMMIT TRANSACTION", System.StringComparison.Ordinal));
    }

    // ── Identifier quoting / escaping ─────────────────────────────────────────

    [Fact]
    public void Rename_QuotesIdentifiers_AndDoublesClosingBracket()
    {
        // A name containing a ']' must have it doubled inside the bracket-quoted sp_rename literal.
        var target = new DatabaseRenameScriptBuilder.RenameTarget(
            Schema: "dbo", Name: "Weird]Name", NewName: "CleanName", IsColumn: false, ParentTable: null);

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(target, []);

        // ']' doubled to ']]' inside the bracket quote.
        Assert.Contains("[dbo].[Weird]]Name]", script);
    }

    [Fact]
    public void ColumnRename_QuotesAllThreeParts()
    {
        var script = DatabaseRenameScriptBuilder.BuildRenameScript(ColumnTarget(), []);

        // The column sp_rename literal must bracket-quote schema, table, and column.
        Assert.Contains("[dbo].[Orders].[Total]", script);
    }

    [Fact]
    public void Rename_NewNameIsBareInSpRename_NotBracketedOrQualified()
    {
        // sp_rename's @newname must be the bare new name (sp_rename rejects a qualified/bracketed new name).
        var target = new DatabaseRenameScriptBuilder.RenameTarget(
            Schema: "dbo", Name: "Old", NewName: "[New]", IsColumn: false, ParentTable: null);

        var script = DatabaseRenameScriptBuilder.BuildRenameScript(target, []);

        Assert.Contains("@newname = N'New'", script);
        Assert.DoesNotContain("@newname = N'[New]'", script);
    }
}
