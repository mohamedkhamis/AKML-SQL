using Xunit;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Schema;

public class DatabaseCacheTests
{
    private static DatabaseCache CreateCacheWithObjects()
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };
        cache.Schemas["dbo"] = new SchemaEntry
        {
            SchemaName = "dbo",
            Objects =
            [
                new DatabaseObject { ObjectName = "Orders", SchemaName = "dbo" },
                new DatabaseObject { ObjectName = "Customers", SchemaName = "dbo" }
            ]
        };
        cache.Schemas["hr"] = new SchemaEntry
        {
            SchemaName = "hr",
            Objects =
            [
                new DatabaseObject { ObjectName = "Employees", SchemaName = "hr" }
            ]
        };
        return cache;
    }

    // ── FindObject ────────────────────────────────────────────────────────

    [Fact]
    public void FindObject_ExactMatch_ReturnsObject()
    {
        var cache = CreateCacheWithObjects();

        var result = cache.FindObject("dbo", "Orders");

        Assert.NotNull(result);
        Assert.Equal("Orders", result.ObjectName);
    }

    [Fact]
    public void FindObject_CaseInsensitive_ReturnsObject()
    {
        var cache = CreateCacheWithObjects();

        var result = cache.FindObject("DBO", "ORDERS");

        Assert.NotNull(result);
    }

    [Fact]
    public void FindObject_NotFound_ReturnsNull()
    {
        var cache = CreateCacheWithObjects();

        var result = cache.FindObject("dbo", "NonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void FindObject_UnknownSchema_ReturnsNull()
    {
        var cache = CreateCacheWithObjects();

        var result = cache.FindObject("unknown", "Orders");

        Assert.Null(result);
    }

    // ── GetAllObjects ─────────────────────────────────────────────────────

    [Fact]
    public void GetAllObjects_ReturnsAllObjects()
    {
        var cache = CreateCacheWithObjects();

        var all = cache.GetAllObjects().ToList();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetAllObjects_EmptyCache_ReturnsEmpty()
    {
        var cache = new DatabaseCache();

        var all = cache.GetAllObjects().ToList();

        Assert.Empty(all);
    }

    // ── GetObjectsInSchema ────────────────────────────────────────────────

    [Fact]
    public void GetObjectsInSchema_FiltersBySchema()
    {
        var cache = CreateCacheWithObjects();

        var dboObjects = cache.GetObjectsInSchema("dbo").ToList();

        Assert.Equal(2, dboObjects.Count);
        Assert.All(dboObjects, o => Assert.Equal("dbo", o.SchemaName));
    }

    [Fact]
    public void GetObjectsInSchema_UnknownSchema_ReturnsEmpty()
    {
        var cache = CreateCacheWithObjects();

        var objects = cache.GetObjectsInSchema("nonexistent").ToList();

        Assert.Empty(objects);
    }

    // ── GetSchemaNames ────────────────────────────────────────────────────

    [Fact]
    public void GetSchemaNames_ReturnsDistinctSchemas()
    {
        var cache = CreateCacheWithObjects();

        var names = cache.GetSchemaNames().ToList();

        Assert.Contains("dbo", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hr", names, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, names.Count);
    }

    // ── AreColumnsLoaded ──────────────────────────────────────────────────

    [Fact]
    public void AreColumnsLoaded_FalseByDefault()
    {
        var cache = CreateCacheWithObjects();

        bool loaded = cache.AreColumnsLoaded("dbo", "Orders");

        Assert.False(loaded);
    }

    [Fact]
    public void AreColumnsLoaded_TrueAfterSetting()
    {
        var cache = CreateCacheWithObjects();
        var obj = cache.FindObject("dbo", "Orders")!;
        obj.ColumnsLoaded = true;

        bool loaded = cache.AreColumnsLoaded("dbo", "Orders");

        Assert.True(loaded);
    }

    // ── GetForeignKeysForTable ────────────────────────────────────────────

    [Fact]
    public void GetForeignKeysForTable_ReturnsRelevantFks()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(new ForeignKey
        {
            ParentSchema = "dbo",
            ParentTable = "Orders",
            ReferencedSchema = "dbo",
            ReferencedTable = "Customers"
        });

        var fks = cache.GetForeignKeysForTable("dbo", "Orders");

        Assert.Single(fks);
    }

    [Fact]
    public void GetForeignKeysForTable_MatchesReferencedTable()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(new ForeignKey
        {
            ParentSchema = "dbo",
            ParentTable = "Orders",
            ReferencedSchema = "dbo",
            ReferencedTable = "Customers"
        });

        var fks = cache.GetForeignKeysForTable("dbo", "Customers");

        Assert.Single(fks);
    }

    [Fact]
    public void GetForeignKeysForTable_NoFks_ReturnsEmpty()
    {
        var cache = new DatabaseCache();

        var fks = cache.GetForeignKeysForTable("dbo", "NoFkTable");

        Assert.Empty(fks);
    }

    // ── IsStale ───────────────────────────────────────────────────────────

    [Fact]
    public void IsStale_FalseAfterConstruct()
    {
        var cache = new DatabaseCache();

        Assert.False(cache.IsStale);
    }
}
