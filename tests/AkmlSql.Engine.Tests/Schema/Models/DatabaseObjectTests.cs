using Xunit;
using AkmlSql.Engine.Schema.Models;
using DbIndex = AkmlSql.Engine.Schema.Models.Index;

namespace AkmlSql.Engine.Tests.Schema.Models;

public class DatabaseObjectTests
{
    // ── DatabaseObject ─────────────────────────────────────────────────────

    [Fact]
    public void DatabaseObject_Defaults()
    {
        var obj = new DatabaseObject();
        Assert.Equal(0, obj.ObjectId);
        Assert.Equal(string.Empty, obj.SchemaName);
        Assert.Equal(string.Empty, obj.ObjectName);
        Assert.Equal(DbObjectType.Table, obj.ObjectType);
        Assert.Equal(default(DateTime), obj.ModifyDate);
        Assert.Equal(0L, obj.ApproxRowCount);
        Assert.Null(obj.Description);
        Assert.Empty(obj.Columns);
        Assert.Empty(obj.Parameters);
        Assert.Empty(obj.Indexes);
        Assert.False(obj.ColumnsLoaded);
    }

    [Fact]
    public void DatabaseObject_FullName()
    {
        var obj = new DatabaseObject { SchemaName = "dbo", ObjectName = "Orders" };
        Assert.Equal("dbo.Orders", obj.FullName);
    }

    [Fact]
    public void DatabaseObject_FullName_EmptySchema()
    {
        var obj = new DatabaseObject { SchemaName = "", ObjectName = "Orders" };
        Assert.Equal(".Orders", obj.FullName);
    }

    [Fact]
    public void DatabaseObject_Mutations()
    {
        var obj = new DatabaseObject
        {
            ObjectId = 42,
            SchemaName = "sales",
            ObjectName = "Invoices",
            ObjectType = DbObjectType.View,
            ApproxRowCount = 1000,
            Description = "Invoice view",
            ColumnsLoaded = true
        };
        Assert.Equal(42, obj.ObjectId);
        Assert.Equal("sales", obj.SchemaName);
        Assert.Equal("Invoices", obj.ObjectName);
        Assert.Equal(DbObjectType.View, obj.ObjectType);
        Assert.Equal(1000L, obj.ApproxRowCount);
        Assert.Equal("Invoice view", obj.Description);
        Assert.True(obj.ColumnsLoaded);
    }

    // ── DbObjectType enum ─────────────────────────────────────────────────

    [Fact]
    public void DbObjectType_Values()
    {
        Assert.Equal(0, (int)DbObjectType.Table);
        Assert.Equal(1, (int)DbObjectType.View);
        Assert.Equal(2, (int)DbObjectType.Procedure);
        Assert.Equal(3, (int)DbObjectType.ScalarFunction);
        Assert.Equal(4, (int)DbObjectType.TableFunction);
        Assert.Equal(5, (int)DbObjectType.InlineFunction);
        Assert.Equal(6, (int)DbObjectType.Synonym);
        Assert.Equal(7, (int)DbObjectType.Sequence);
    }

    // ── Column ────────────────────────────────────────────────────────────

    [Fact]
    public void Column_Defaults()
    {
        var col = new Column();
        Assert.Equal(0, col.ColumnId);
        Assert.Equal(string.Empty, col.ColumnName);
        Assert.Equal(string.Empty, col.TypeName);
        Assert.Equal(0, col.MaxLength);
        Assert.Equal(0, col.Precision);
        Assert.Equal(0, col.Scale);
        Assert.False(col.IsNullable);
        Assert.False(col.IsIdentity);
        Assert.False(col.IsComputed);
        Assert.Null(col.ComputedDefinition);
        Assert.Null(col.DefaultValue);
        Assert.False(col.IsPrimaryKey);
        Assert.Null(col.Description);
    }

    // ── Column.TypeDisplay ────────────────────────────────────────────────

    [Theory]
    [InlineData("nvarchar", -1, 0, 0, "nvarchar(MAX)")]
    [InlineData("NVARCHAR", -1, 0, 0, "NVARCHAR(MAX)")]
    [InlineData("nvarchar", 50, 0, 0, "nvarchar(50)")]
    [InlineData("varchar", -1, 0, 0, "varchar(MAX)")]
    [InlineData("varchar", 100, 0, 0, "varchar(100)")]
    [InlineData("char", 10, 0, 0, "char(10)")]
    [InlineData("nchar", 20, 0, 0, "nchar(20)")]
    [InlineData("binary", 8, 0, 0, "binary(8)")]
    [InlineData("varbinary", -1, 0, 0, "varbinary(MAX)")]
    [InlineData("varbinary", 16, 0, 0, "varbinary(16)")]
    [InlineData("decimal", 0, 18, 2, "decimal(18,2)")]
    [InlineData("DECIMAL", 0, 10, 4, "DECIMAL(10,4)")]
    [InlineData("numeric", 0, 12, 3, "numeric(12,3)")]
    [InlineData("int", 0, 0, 0, "int")]
    [InlineData("uniqueidentifier", 0, 0, 0, "uniqueidentifier")]
    [InlineData("datetime2", 0, 0, 0, "datetime2")]
    public void Column_TypeDisplay(string typeName, int maxLength, int precision, int scale, string expected)
    {
        var col = new Column
        {
            TypeName = typeName,
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale
        };
        Assert.Equal(expected, col.TypeDisplay);
    }

    // ── Parameter ─────────────────────────────────────────────────────────

    [Fact]
    public void Parameter_Defaults()
    {
        var p = new Parameter();
        Assert.Equal(0, p.ParameterId);
        Assert.Equal(string.Empty, p.ParameterName);
        Assert.Equal(string.Empty, p.TypeName);
        Assert.Equal(0, p.MaxLength);
        Assert.Equal(0, p.Precision);
        Assert.Equal(0, p.Scale);
        Assert.False(p.IsOutput);
        Assert.False(p.HasDefault);
        Assert.Null(p.DefaultValue);
    }

    [Fact]
    public void Parameter_Mutations()
    {
        var p = new Parameter
        {
            ParameterId = 1,
            ParameterName = "@Id",
            TypeName = "int",
            IsOutput = true,
            HasDefault = true,
            DefaultValue = 0
        };
        Assert.Equal(1, p.ParameterId);
        Assert.Equal("@Id", p.ParameterName);
        Assert.Equal("int", p.TypeName);
        Assert.True(p.IsOutput);
        Assert.True(p.HasDefault);
        Assert.Equal(0, p.DefaultValue);
    }

    // ── Index ─────────────────────────────────────────────────────────────

    [Fact]
    public void Index_Defaults()
    {
        var idx = new DbIndex();
        Assert.Equal(string.Empty, idx.IndexName);
        Assert.Equal(IndexType.Clustered, idx.IndexType);
        Assert.False(idx.IsPrimaryKey);
        Assert.False(idx.IsUnique);
        Assert.Empty(idx.Columns);
    }

    [Fact]
    public void Index_Mutations()
    {
        var idx = new DbIndex
        {
            IndexName = "IX_Orders_CustomerId",
            IndexType = IndexType.Nonclustered,
            IsPrimaryKey = false,
            IsUnique = true
        };
        idx.Columns.Add(new IndexColumn { ColumnName = "CustomerId" });
        Assert.Equal("IX_Orders_CustomerId", idx.IndexName);
        Assert.Equal(IndexType.Nonclustered, idx.IndexType);
        Assert.True(idx.IsUnique);
        Assert.Single(idx.Columns);
    }

    // ── IndexType enum ────────────────────────────────────────────────────

    [Fact]
    public void IndexType_Values()
    {
        Assert.Equal(0, (int)IndexType.Clustered);
        Assert.Equal(1, (int)IndexType.Nonclustered);
        Assert.Equal(2, (int)IndexType.Heap);
        Assert.Equal(3, (int)IndexType.Columnstore);
    }

    // ── IndexColumn ───────────────────────────────────────────────────────

    [Fact]
    public void IndexColumn_Defaults()
    {
        var ic = new IndexColumn();
        Assert.Equal(string.Empty, ic.ColumnName);
        Assert.False(ic.IsDescending);
    }

    [Fact]
    public void IndexColumn_Mutations()
    {
        var ic = new IndexColumn { ColumnName = "CreatedAt", IsDescending = true };
        Assert.Equal("CreatedAt", ic.ColumnName);
        Assert.True(ic.IsDescending);
    }
}
