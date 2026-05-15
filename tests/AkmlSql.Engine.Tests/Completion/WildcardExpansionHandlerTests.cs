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

    [Fact]
    public void Cte_SelectStarFromCte_ReturnsOnlyProjectedColumns()
    {
        // CTE projects 2 of Orders' 3 columns. Wildcard expansion of `SELECT *
        // FROM cte1` must return only the projected columns, NOT all columns of
        // the underlying Orders table.
        var sql = "WITH cte1 AS (SELECT OrderId, CustomerName FROM Orders) SELECT * FROM cte1";
        int starPos = sql.IndexOf("SELECT *", System.StringComparison.Ordinal) + "SELECT ".Length;
        var result = _handler.Handle(sql, starPos, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("cte1", result.Tables[0].TableName);
        Assert.Equal(2, result.Tables[0].Columns.Length);
        Assert.Equal("OrderId", result.Tables[0].Columns[0].ColumnName);
        Assert.Equal("CustomerName", result.Tables[0].Columns[1].ColumnName);
    }

    [Fact]
    public void Cte_TwoCtes_SelectStarFromFirst_ReturnsOnlyFirstCteColumns()
    {
        // Two CTEs defined; SELECT * FROM cte1 must NOT leak cte2's columns or
        // the underlying Orders/OrderDetails columns. Only cte1's projection.
        var sql =
            "WITH cte1 AS (SELECT OrderId, CustomerName FROM Orders), " +
            "cte2 AS (SELECT DetailId, Quantity FROM OrderDetails) " +
            "SELECT * FROM cte1";
        int starPos = sql.LastIndexOf("SELECT *", System.StringComparison.Ordinal) + "SELECT ".Length;
        var result = _handler.Handle(sql, starPos, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("cte1", result.Tables[0].TableName);
        Assert.Equal(2, result.Tables[0].Columns.Length);
        Assert.Equal("OrderId", result.Tables[0].Columns[0].ColumnName);
        Assert.Equal("CustomerName", result.Tables[0].Columns[1].ColumnName);
    }

    [Fact]
    public void Cte_UserExactRepro_MultiLineSingleCte_ReturnsCte1Projection()
    {
        // Exact format the user pastes — multi-line CTE with leading whitespace.
        var sql =
            "WITH Cte1 AS (\n" +
            "SELECT  OrderId,\n" +
            "        CustomerName\n" +
            "FROM Orders\n" +
            ")\n" +
            "SELECT  *\n" +
            "FROM Cte1";
        int starPos = sql.IndexOf("SELECT  *", System.StringComparison.Ordinal) + "SELECT  ".Length;
        var result = _handler.Handle(sql, starPos, qualifier: null, _cache);

        // Diagnostic: dump what we got
        if (!result.Success || result.Tables == null || result.Tables.Length == 0)
        {
            Assert.Fail($"Expected success with Cte1 group; got Success={result.Success}, " +
                        $"ErrorMessage='{result.ErrorMessage}', Tables.Length={result.Tables?.Length ?? -1}");
        }
        var cte1Group = System.Linq.Enumerable.FirstOrDefault(
            result.Tables, t => string.Equals(t.TableName, "Cte1", System.StringComparison.OrdinalIgnoreCase));
        if (cte1Group == null)
        {
            var groupNames = string.Join(", ",
                System.Linq.Enumerable.Select(result.Tables, t => t.TableName));
            Assert.Fail($"No Cte1 group; got groups: [{groupNames}]");
        }
        Assert.Equal(2, cte1Group!.Columns.Length);
    }

    // SQL Prompt parity: when invalid content after the cursor breaks the full
    // parse, the handler retries by parsing only the prefix up to the cursor.
    // The prefix's WITH clause is syntactically self-contained even when what
    // follows is broken, so CteResolver can extract the column projections from
    // the prefix-parsed script.
    [Fact]
    public void Cte_UserMalformedSqlRepro_ReturnsCte1Projection()
    {
        // Verbatim repro of the user's reported SQL where they have an incomplete
        // construct: a comma after FROM Cte1 followed by what looks like a second
        // CTE definition but in the wrong place (CTE syntax doesn't work inside
        // FROM). The parser's recovery may interpret "Cte2 AS (...)" as a derived
        // table joined to Cte1; the cursor at the outer * must still expand to
        // Cte1's projection only.
        var sql =
            "WITH Cte1 AS (SELECT OrderId, CustomerName FROM Orders)\n" +
            "SELECT *\n" +
            "FROM Cte1\n" +
            ",\n" +
            "Cte2 AS (SELECT DetailId, Quantity FROM OrderDetails)\n" +
            "SELECT DetailId, Quantity FROM Cte2;";
        int starPos = sql.IndexOf("SELECT *", System.StringComparison.Ordinal) + "SELECT ".Length;
        var result = _handler.Handle(sql, starPos, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.NotNull(result.Tables);
        // The Cte1 group must be present and must contain exactly the projected columns.
        var cte1Group = System.Linq.Enumerable.FirstOrDefault(
            result.Tables, t => string.Equals(t.TableName, "Cte1", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(cte1Group);
        Assert.Equal(2, cte1Group!.Columns.Length);
        Assert.Equal("OrderId", cte1Group.Columns[0].ColumnName);
        Assert.Equal("CustomerName", cte1Group.Columns[1].ColumnName);
    }

    [Fact]
    public void Cte_TwoCtes_SelectStarFromSecond_ReturnsOnlySecondCteColumns()
    {
        // Same setup as above, but SELECT * FROM cte2 → only cte2's projection.
        var sql =
            "WITH cte1 AS (SELECT OrderId, CustomerName FROM Orders), " +
            "cte2 AS (SELECT DetailId, Quantity FROM OrderDetails) " +
            "SELECT * FROM cte2";
        int starPos = sql.LastIndexOf("SELECT *", System.StringComparison.Ordinal) + "SELECT ".Length;
        var result = _handler.Handle(sql, starPos, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("cte2", result.Tables[0].TableName);
        Assert.Equal(2, result.Tables[0].Columns.Length);
        Assert.Equal("DetailId", result.Tables[0].Columns[0].ColumnName);
        Assert.Equal("Quantity", result.Tables[0].Columns[1].ColumnName);
    }
}
