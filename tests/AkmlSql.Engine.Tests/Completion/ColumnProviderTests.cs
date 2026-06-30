using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

public class ColumnProviderTests
{
    private readonly TsqlParserService _parserService = new();
    private readonly ColumnProvider _provider = new();

    /// <summary>
    /// Builds a minimal in-memory <see cref="DatabaseCache"/> with a single dbo schema
    /// and the supplied tables/columns. Each table is fully populated (ColumnsLoaded = true).
    /// </summary>
    private static DatabaseCache BuildCache(params (string Table, string[] Columns)[] tables)
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };
        var schema = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = schema;

        foreach (var (tableName, cols) in tables)
        {
            var obj = new DatabaseObject
            {
                SchemaName = "dbo",
                ObjectName = tableName,
                ObjectType = DbObjectType.Table,
                ColumnsLoaded = true
            };
            int colId = 1;
            foreach (var c in cols)
            {
                obj.Columns.Add(new Column
                {
                    ColumnId = colId++,
                    ColumnName = c,
                    TypeName = "nvarchar",
                    MaxLength = 50,
                    IsNullable = true
                });
            }
            schema.Objects.Add(obj);
        }
        cache.RebuildFkIndex();
        return cache;
    }

    /// <summary>
    /// Drives the completion engine end-to-end against the supplied SQL (with a '|'
    /// cursor marker), returning only the items produced by ColumnProvider.
    /// </summary>
    private CompletionResponse RunCompletion(string sqlWithMarker, DatabaseCache cache)
    {
        var cursorOffset = sqlWithMarker.IndexOf('|');
        Assert.True(cursorOffset >= 0, "test SQL must contain a cursor marker");
        var sql = sqlWithMarker.Replace("|", string.Empty);

        var engine = new CompletionEngine(_parserService);
        return engine.GetCompletions(sql, cursorOffset, cache);
    }

    /// <summary>As <see cref="RunCompletion"/> but with an explicit column-suggestion scope (T032).</summary>
    private CompletionResponse RunCompletion(string sqlWithMarker, DatabaseCache cache,
        AkmlSql.Core.Config.ColumnSuggestionScope scope)
    {
        var cursorOffset = sqlWithMarker.IndexOf('|');
        Assert.True(cursorOffset >= 0, "test SQL must contain a cursor marker");
        var sql = sqlWithMarker.Replace("|", string.Empty);
        var engine = new CompletionEngine(_parserService) { ColumnScopeMode = scope };
        return engine.GetCompletions(sql, cursorOffset, cache);
    }

    // ── ColumnScope (spec 030 T032 / FR-012): columns before a FROM clause ──

    [Fact]
    public void Select_NoFrom_ScopeAll_ListsColumnsFromAllTables()
    {
        var cache = BuildCache(
            ("Customers", new[] { "CustomerId", "CustomerName" }),
            ("Orders", new[] { "OrderId", "Total" }));

        var response = RunCompletion("SELECT |", cache, AkmlSql.Core.Config.ColumnSuggestionScope.All);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("CustomerName", columns);
        Assert.Contains("OrderId", columns);
        Assert.Contains("Total", columns);
    }

    [Fact]
    public void Select_NoFrom_ScopeReferencedOnly_ListsNoColumns()
    {
        var cache = BuildCache(("Customers", new[] { "CustomerId", "CustomerName" }));

        var response = RunCompletion("SELECT |", cache, AkmlSql.Core.Config.ColumnSuggestionScope.ReferencedOnly);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .ToList();

        Assert.Empty(columns);   // no FROM table referenced → no columns under ReferencedOnly
    }

    // ── Bare-column path: WHERE clause, single table ──

    [Fact]
    public void Where_SingleTable_ShowsBareColumns()
    {
        var cache = BuildCache(("Customers", new[] { "Id", "Name", "Email" }));

        var response = RunCompletion("SELECT * FROM Customers WHERE |", cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Id", columns);
        Assert.Contains("Name", columns);
        Assert.Contains("Email", columns);
        // Single-table queries should NOT qualify the column with the alias
        Assert.DoesNotContain("Customers.Id", columns);
    }

    // ── Bare-column path: WHERE clause, multi-table → qualified ──

    [Fact]
    public void Where_MultiTable_QualifiesColumnsWithAlias()
    {
        var cache = BuildCache(
            ("Customers", new[] { "Id", "Name" }),
            ("Orders", new[] { "Id", "CustomerId", "Amount" }));

        var response = RunCompletion(
            "SELECT * FROM Customers c JOIN Orders o ON c.Id = o.CustomerId WHERE |",
            cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("c.Name", columns);
        Assert.Contains("o.Amount", columns);
        // The two tables both have an "Id" column — both must be qualified
        // so the user can disambiguate.
        Assert.Contains("c.Id", columns);
        Assert.Contains("o.Id", columns);
        // Bare names must NOT appear when more than one table is in scope
        Assert.DoesNotContain("Id", columns);
    }

    // ── Bare-column path: GROUP BY ──

    [Fact]
    public void GroupBy_SingleTable_ShowsBareColumns()
    {
        var cache = BuildCache(("Terminals", new[] { "TID", "Region", "Status" }));

        var response = RunCompletion(
            "SELECT TID, COUNT(*) FROM Terminals GROUP BY |",
            cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("TID", columns);
        Assert.Contains("Region", columns);
        Assert.Contains("Status", columns);
    }

    // ── Bare-column path: ORDER BY ──

    [Fact]
    public void OrderBy_ShowsBareColumns()
    {
        var cache = BuildCache(("Products", new[] { "Sku", "Name", "Price" }));

        var response = RunCompletion("SELECT * FROM Products ORDER BY |", cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Sku", columns);
        Assert.Contains("Name", columns);
        Assert.Contains("Price", columns);
    }

    // ── User-reported bug: ORDER BY after SELECT col,* needs the qualified form
    //    so the user can disambiguate. SQL Server rejects bare references in this
    //    case with "Msg 209: Ambiguous column name". The provider now emits BOTH
    //    forms even in single-table queries when the cursor is in an
    //    ambiguity-prone clause (ORDER BY / GROUP BY / HAVING).
    [Fact]
    public void OrderBy_SingleTable_OffersTableQualifiedColumns_ForAmbiguityResolution()
    {
        var cache = BuildCache(
            ("martyrs", new[] { "id", "name", "created_at", "updated_at" }));

        var response = RunCompletion(
            "SELECT created_at,*\nFROM martyrs\nORDER BY |",
            cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        // Both forms must be present so the user can pick the one that resolves
        // the ambiguity.
        Assert.Contains("created_at", columns);
        Assert.Contains("martyrs.created_at", columns);
        Assert.Contains("name", columns);
        Assert.Contains("martyrs.name", columns);
    }

    [Fact]
    public void GroupBy_SingleTable_OffersTableQualifiedColumns()
    {
        var cache = BuildCache(("orders", new[] { "id", "status", "amount" }));

        var response = RunCompletion(
            "SELECT status, COUNT(*) FROM orders GROUP BY |",
            cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("status", columns);
        Assert.Contains("orders.status", columns);
    }

    // Regression guard: the qualified form must NOT appear in WHERE single-table
    // queries — that would clutter the popup with duplicate suggestions for the
    // common case. SQL Server resolves WHERE references without ambiguity errors.
    [Fact]
    public void Where_SingleTable_DoesNot_DuplicateWithQualifiedForm()
    {
        var cache = BuildCache(("Customers", new[] { "Id", "Name" }));

        var response = RunCompletion("SELECT * FROM Customers WHERE |", cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Id", columns);
        Assert.Contains("Name", columns);
        Assert.DoesNotContain("Customers.Id", columns);
        Assert.DoesNotContain("Customers.Name", columns);
    }

    // ── Dot-qualified path (existing behavior) ──

    [Fact]
    public void DotQualified_ReturnsTableColumnsOnly()
    {
        var cache = BuildCache(
            ("Customers", new[] { "Id", "Name" }),
            ("Orders", new[] { "OrderNumber", "Amount" }));

        var response = RunCompletion("SELECT * FROM Customers c WHERE c.|", cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToList();

        Assert.Contains("Id", columns);
        Assert.Contains("Name", columns);
        // Orders columns must NOT appear under "c." (which resolves to Customers)
        Assert.DoesNotContain("OrderNumber", columns);
        Assert.DoesNotContain("Amount", columns);
    }

    // ── PK ranking ──

    [Fact]
    public void Where_PrimaryKeyColumn_RankedHigherThanRegularColumns()
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };
        var schema = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = schema;
        var obj = new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Customers",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true
        };
        obj.Columns.Add(new Column { ColumnId = 1, ColumnName = "Id", TypeName = "int", IsPrimaryKey = true });
        obj.Columns.Add(new Column { ColumnId = 2, ColumnName = "Name", TypeName = "nvarchar", MaxLength = 50 });
        schema.Objects.Add(obj);
        cache.RebuildFkIndex();

        var response = RunCompletion("SELECT * FROM Customers WHERE |", cache);

        var idItem = response.Items.FirstOrDefault(i => i.DisplayText == "Id");
        var nameItem = response.Items.FirstOrDefault(i => i.DisplayText == "Name");

        Assert.NotNull(idItem);
        Assert.NotNull(nameItem);
        Assert.True(idItem!.SortPriority < nameItem!.SortPriority,
            "PK column should sort before non-PK column (lower priority number = higher rank)");
    }

    // ── Columns not loaded ──

    [Fact]
    public void Where_ColumnsNotLoaded_ReturnsEmpty()
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };
        var schema = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = schema;
        // Object exists but ColumnsLoaded = false (Phase B not complete yet)
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Pending",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = false
        });
        cache.RebuildFkIndex();

        var response = RunCompletion("SELECT * FROM Pending WHERE |", cache);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .ToList();

        Assert.Empty(columns);
    }
}
