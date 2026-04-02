using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Core.Tests.Refactoring;

/// <summary>
/// Tests that <see cref="ExpandInsertColumnsOperation.BuildColumnListWithTypes"/>
/// produces correct inline metadata comments for INSERT column expansions.
/// </summary>
public class InsertMetadataTests
{
    [Fact]
    public void BuildColumnListWithTypes_BasicColumns_ProducesTypeComments()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "ProductName", TypeName = "nvarchar", MaxLength = 40, IsNullable = false },
            new() { ColumnName = "SupplierID", TypeName = "int", IsNullable = true },
            new() { ColumnName = "UnitPrice", TypeName = "money", IsNullable = true, DefaultValue = "(0)" }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.Contains("ProductName,", result);
        Assert.Contains("-- nvarchar(40), not null", result);
        Assert.Contains("SupplierID,", result);
        Assert.Contains("-- int, null", result);
        Assert.Contains("UnitPrice", result);
        Assert.Contains("-- money, null, default ((0))", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_IdentityColumn_AnnotatedAsIdentity()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "ProductID", TypeName = "int", IsIdentity = true, IsNullable = false },
            new() { ColumnName = "ProductName", TypeName = "nvarchar", MaxLength = 40, IsNullable = false }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.Contains("ProductID", result);
        Assert.Contains("-- IDENTITY, auto-generated", result);
        // ProductName should be a regular column
        Assert.Contains("ProductName", result);
        Assert.Contains("-- nvarchar(40), not null", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_ComputedColumn_Excluded()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "Total", TypeName = "money", IsComputed = true, ComputedDefinition = "Qty * Price" },
            new() { ColumnName = "Qty", TypeName = "int", IsNullable = false }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.DoesNotContain("Total", result);
        Assert.Contains("Qty", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_DecimalType_ShowsPrecisionAndScale()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "Price", TypeName = "decimal", Precision = 10, Scale = 2, IsNullable = false }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.Contains("-- decimal(10,2), not null", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_VarcharMax_ShowsMaxLength()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "Description", TypeName = "nvarchar", MaxLength = -1, IsNullable = true }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.Contains("-- nvarchar(MAX), null", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_LastColumn_NoTrailingComma()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "Col1", TypeName = "int", IsNullable = false },
            new() { ColumnName = "Col2", TypeName = "int", IsNullable = false }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        // The last column line should not have a comma before the comment
        var lines = result.Split('\n');
        var lastColumnLine = lines.Last(l => l.Contains("--"));
        // Verify last column name appears without comma before the --
        var commentIdx = lastColumnLine.IndexOf("--");
        var beforeComment = lastColumnLine[..commentIdx];
        // The last column should NOT have a comma (it's been replaced with a space)
        Assert.DoesNotContain(",", beforeComment.TrimEnd());
    }

    [Fact]
    public void BuildColumnListWithTypes_DefaultValue_IncludedInComment()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "Status", TypeName = "varchar", MaxLength = 20, IsNullable = false, DefaultValue = "'Active'" }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.Contains("default ('Active')", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_EmptyList_ReturnsEmptyParens()
    {
        var columns = new List<Column>();

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        Assert.Contains("(", result);
        Assert.Contains(")", result);
    }

    [Fact]
    public void BuildColumnListWithTypes_OnlyComputedColumns_ReturnsEmptyBody()
    {
        var columns = new List<Column>
        {
            new() { ColumnName = "Computed1", TypeName = "int", IsComputed = true },
            new() { ColumnName = "Computed2", TypeName = "int", IsComputed = true }
        };

        var result = ExpandInsertColumnsOperation.BuildColumnListWithTypes(columns);

        // Should have parens but no column entries
        Assert.DoesNotContain("Computed1", result);
        Assert.DoesNotContain("Computed2", result);
    }
}
