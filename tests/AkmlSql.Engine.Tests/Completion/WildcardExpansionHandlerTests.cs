using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

public class WildcardExpansionHandlerTests
{
    private readonly WildcardExpansionHandler _handler;
    private readonly DatabaseCache _cache;

    public WildcardExpansionHandlerTests()
    {
        var parserService = new TsqlParserService();
        _handler = new WildcardExpansionHandler(parserService);

        // Build a test cache with Orders(OrderId, CustomerName, OrderDate)
        _cache = new DatabaseCache();
        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "OrderId", TypeName = "int", IsPrimaryKey = true },
                new Column { ColumnId = 2, ColumnName = "CustomerName", TypeName = "nvarchar", MaxLength = 100, IsNullable = true },
                new Column { ColumnId = 3, ColumnName = "OrderDate", TypeName = "datetime" }
            ]
        });
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "OrderDetails",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "DetailId", TypeName = "int", IsPrimaryKey = true },
                new Column { ColumnId = 2, ColumnName = "OrderId", TypeName = "int" },
                new Column { ColumnId = 3, ColumnName = "ProductId", TypeName = "int" },
                new Column { ColumnId = 4, ColumnName = "Quantity", TypeName = "int" }
            ]
        });
        _cache.Schemas["dbo"] = schema;
    }

    [Fact]
    public void BareWildcard_SingleTable_ReturnsAllColumns()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("Orders", result.Tables[0].TableName);
        Assert.Equal(3, result.Tables[0].Columns.Length);
        Assert.Equal("OrderId", result.Tables[0].Columns[0].ColumnName);
        Assert.Equal("CustomerName", result.Tables[0].Columns[1].ColumnName);
        Assert.Equal("OrderDate", result.Tables[0].Columns[2].ColumnName);
    }

    [Fact]
    public void BareWildcard_SingleTableNoAlias_QualifierIsTableName()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Equal("Orders", result.Tables[0].Qualifier);
    }

    [Fact]
    public void BareWildcard_AliasedTable_QualifierIsAlias()
    {
        var sql = "SELECT * FROM Orders o";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Equal("o", result.Tables[0].Qualifier);
    }

    [Fact]
    public void BareWildcard_MultipleTables_ReturnsAllTableGroups()
    {
        var sql = "SELECT * FROM Orders o JOIN OrderDetails od ON o.OrderId = od.OrderId";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Equal(2, result.Tables.Length);
    }

    [Fact]
    public void QualifiedWildcard_ReturnsOnlyQualifiedTable()
    {
        var sql = "SELECT o.* FROM Orders o JOIN OrderDetails od ON o.OrderId = od.OrderId";
        var result = _handler.Handle(sql, cursorOffset: 11, qualifier: "o", _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("Orders", result.Tables[0].TableName);
        Assert.Equal("o", result.Tables[0].Qualifier);
    }

    [Fact]
    public void ColumnsNotLoaded_ReturnsFailure()
    {
        var cacheNoColumns = new DatabaseCache();
        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = false
        });
        cacheNoColumns.Schemas["dbo"] = schema;

        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, cacheNoColumns);

        Assert.False(result.Success);
    }

    [Fact]
    public void NullCache_ReturnsFailure()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, cache: null);

        Assert.False(result.Success);
    }

    [Fact]
    public void TableNotInCache_ReturnsFailure()
    {
        var emptyCache = new DatabaseCache();
        emptyCache.Schemas["dbo"] = new SchemaEntry { SchemaName = "dbo" };

        var sql = "SELECT * FROM UnknownTable";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, emptyCache);

        Assert.False(result.Success);
    }

    [Fact]
    public void TypeDisplay_FormatsCorrectly()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        // OrderId: int, NOT NULL, PK
        Assert.Contains("PK", result.Tables[0].Columns[0].TypeDisplay);
        // CustomerName: nvarchar(100), NULL
        Assert.Contains("NULL", result.Tables[0].Columns[1].TypeDisplay);
    }

    [Fact]
    public void PkColumnsFirst_ThenByOrdinal()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.True(result.Tables[0].Columns[0].ColumnName == "OrderId");
    }

    [Fact]
    public void SchemaQualifiedTable_ResolvesCorrectly()
    {
        var sql = "SELECT * FROM dbo.Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("Orders", result.Tables[0].TableName);
    }
}
