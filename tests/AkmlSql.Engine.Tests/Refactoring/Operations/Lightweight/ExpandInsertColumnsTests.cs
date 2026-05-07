using AkmlSql.Core.Config;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T036 — Unit tests for ExpandInsertColumnsOperation.
/// </summary>
public sealed class ExpandInsertColumnsTests
{
    private readonly ExpandInsertColumnsOperation _op = new();

    [Fact]
    public void ExpandInsertColumns_CacheMiss_ReturnsWarning()
    {
        // No schema cache → should warn but not throw
        const string sql = "INSERT INTO dbo.Orders VALUES (1, 'Test', GETDATE())";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // Text unchanged because we can't expand without schema info
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Could not resolve columns"));
    }

    [Fact]
    public void ExpandInsertColumns_NoInsertStatement_ReturnsUnchanged()
    {
        const string sql = "SELECT Id, Name FROM dbo.Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandInsertColumns_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandInsertColumns_InsertWithExistingColumns_ReturnsUnchanged()
    {
        // INSERT with a column list already present — no expansion needed
        const string sql = "INSERT INTO dbo.Orders (Id, Name) VALUES (1, 'Test')";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandInsertColumns_NullSchemaCache_WarnForEachInsert()
    {
        const string sql =
            "INSERT INTO dbo.Orders VALUES (1);\r\n" +
            "INSERT INTO dbo.Products VALUES (2)";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // One warning per unresolvable INSERT
        Assert.Equal(2, warnings.Length);
    }

    // Phase 2 A.3: InsertOptions.IncludeColumns = false short-circuits the operation
    // before cache lookup, so no warning is raised even when the cache would be missing.
    [Fact]
    public void ExpandInsertColumns_IncludeColumnsFalse_ReturnsUnchangedNoWarning()
    {
        const string sql = "INSERT INTO dbo.Orders VALUES (1, 'Test', GETDATE())";
        var settings = new IntelliSenseSettings();
        settings.InsertOptions.IncludeColumns = false;
        var ctx = LightweightOperationTestHelper.CreateContext(sql, settings);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    // Phase 2 A.3: InsertOptions.IncludeDefaultsAsComments = false omits the
    // "default (...)" suffix from per-column comments produced by BuildColumnListWithTypes.
    [Fact]
    public void BuildColumnListWithTypes_IncludeDefaultsFalse_OmitsDefaultSegment()
    {
        var columns = new List<Column>
        {
            new() { ColumnId = 1, ColumnName = "Id", TypeName = "int", IsNullable = false, DefaultValue = "((0))" },
            new() { ColumnId = 2, ColumnName = "Name", TypeName = "nvarchar", MaxLength = 100, IsNullable = true }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns, includeDefaults: false);

        Assert.DoesNotContain("default", result, System.StringComparison.OrdinalIgnoreCase);
        // Other comment metadata (type + nullability) must still be present.
        Assert.Contains("int", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not null", result, System.StringComparison.OrdinalIgnoreCase);
    }
}
