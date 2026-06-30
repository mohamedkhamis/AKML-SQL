using AkmlSql.Engine.Execution;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// PR #247 regression guard: <see cref="CrudWriteGenerator.BuildQualifiedTable"/> must emit the
/// double-dot (<c>[catalog]..[table]</c>) T-SQL form when a catalog is supplied but schema is absent.
/// A single dot would produce an invalid two-part name that SQL Server cannot resolve cross-DB.
/// </summary>
public sealed class Pr247_CrudWriteGeneratorFix
{
    // ── BuildQualifiedTable — double-dot form ─────────────────────────────

    [Fact]
    public void BuildQualifiedTable_CatalogPresentSchemaEmpty_EmitsDoubleDot()
    {
        var result = CrudWriteGenerator.BuildQualifiedTable("AdventureWorks", "", "MyTable");
        Assert.Equal("[AdventureWorks]..[MyTable]", result);
    }

    [Fact]
    public void BuildQualifiedTable_CatalogPresentSchemaNullString_EmitsDoubleDot()
    {
        var result = CrudWriteGenerator.BuildQualifiedTable("AdventureWorks", null!, "MyTable");
        Assert.Equal("[AdventureWorks]..[MyTable]", result);
    }

    // ── Regression: existing valid forms must still work ──────────────────

    [Fact]
    public void BuildQualifiedTable_ThreePart_CatalogAndSchemaPresent_StillWorks()
    {
        var result = CrudWriteGenerator.BuildQualifiedTable("AdventureWorks", "HumanResources", "Employee");
        Assert.Equal("[AdventureWorks].[HumanResources].[Employee]", result);
    }

    [Fact]
    public void BuildQualifiedTable_TwoPart_SchemaOnlyNoCatalog_StillWorks()
    {
        var result = CrudWriteGenerator.BuildQualifiedTable(null, "dbo", "Orders");
        Assert.Equal("[dbo].[Orders]", result);
    }

    [Fact]
    public void BuildQualifiedTable_OnePartOnly_NoCatalogNoSchema_StillWorks()
    {
        var result = CrudWriteGenerator.BuildQualifiedTable(null, "", "Orders");
        Assert.Equal("[Orders]", result);
    }
}
