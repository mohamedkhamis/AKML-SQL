using System;
using System.Linq;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Core.Tests.Schema;

public class DatabaseCacheTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DatabaseCache BuildCache(params (string Schema, string Object)[] objects)
    {
        var cache = new DatabaseCache { CacheKey = "server:db" };
        foreach (var (schema, obj) in objects)
        {
            if (!cache.Schemas.TryGetValue(schema, out var entry))
            {
                entry = new SchemaEntry { SchemaName = schema };
                cache.Schemas[schema] = entry;
            }
            entry.Objects.Add(new DatabaseObject
            {
                SchemaName = schema,
                ObjectName = obj,
                ObjectType = DbObjectType.Table
            });
        }
        return cache;
    }

    private static ForeignKey MakeFk(string parentSchema, string parentTable,
                                      string refSchema, string refTable,
                                      string fkName = "FK_Test") =>
        new()
        {
            FkName = fkName,
            ParentSchema = parentSchema,
            ParentTable = parentTable,
            ParentColumns = ["Id"],
            ReferencedSchema = refSchema,
            ReferencedTable = refTable,
            ReferencedColumns = ["Id"]
        };

    // ── FindObject ────────────────────────────────────────────────────────────

    [Fact]
    public void FindObject_ReturnsObject_WhenExists()
    {
        var cache = BuildCache(("dbo", "Orders"));
        var result = cache.FindObject("dbo", "Orders");
        Assert.NotNull(result);
        Assert.Equal("Orders", result.ObjectName);
    }

    [Fact]
    public void FindObject_ReturnsNull_WhenSchemaNotFound()
    {
        var cache = BuildCache(("dbo", "Orders"));
        Assert.Null(cache.FindObject("sales", "Orders"));
    }

    [Fact]
    public void FindObject_ReturnsNull_WhenObjectNotInSchema()
    {
        var cache = BuildCache(("dbo", "Orders"));
        Assert.Null(cache.FindObject("dbo", "Customers"));
    }

    [Fact]
    public void FindObject_IsCaseInsensitive_ForSchemaAndObject()
    {
        var cache = BuildCache(("DBO", "Orders"));
        Assert.NotNull(cache.FindObject("dbo", "orders"));
        Assert.NotNull(cache.FindObject("DBO", "ORDERS"));
    }

    // ── GetAllObjects ─────────────────────────────────────────────────────────

    [Fact]
    public void GetAllObjects_ReturnsObjectsFromAllSchemas()
    {
        var cache = BuildCache(("dbo", "Orders"), ("dbo", "Customers"), ("sales", "Invoices"));
        var all = cache.GetAllObjects().ToList();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetAllObjects_ReturnsEmpty_WhenNoSchemas()
    {
        var cache = new DatabaseCache();
        Assert.Empty(cache.GetAllObjects());
    }

    // ── GetObjectsInSchema ────────────────────────────────────────────────────

    [Fact]
    public void GetObjectsInSchema_ReturnsOnlySchemaObjects()
    {
        var cache = BuildCache(("dbo", "Orders"), ("dbo", "Customers"), ("sales", "Invoices"));
        var dboObjs = cache.GetObjectsInSchema("dbo").ToList();
        Assert.Equal(2, dboObjs.Count);
        Assert.All(dboObjs, o => Assert.Equal("dbo", o.SchemaName));
    }

    [Fact]
    public void GetObjectsInSchema_ReturnsEmpty_WhenSchemaNotFound()
    {
        var cache = BuildCache(("dbo", "Orders"));
        Assert.Empty(cache.GetObjectsInSchema("nonexistent"));
    }

    [Fact]
    public void GetObjectsInSchema_IsCaseInsensitive()
    {
        var cache = BuildCache(("DBO", "Orders"));
        Assert.Single(cache.GetObjectsInSchema("dbo"));
    }

    // ── GetSchemaNames ────────────────────────────────────────────────────────

    [Fact]
    public void GetSchemaNames_ReturnsAllSchemas()
    {
        var cache = BuildCache(("dbo", "T1"), ("sales", "T2"), ("hr", "T3"));
        var names = cache.GetSchemaNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dbo", names);
        Assert.Contains("sales", names);
        Assert.Contains("hr", names);
    }

    // ── AreColumnsLoaded ──────────────────────────────────────────────────────

    [Fact]
    public void AreColumnsLoaded_ReturnsFalse_WhenObjectNotFound()
    {
        var cache = new DatabaseCache();
        Assert.False(cache.AreColumnsLoaded("dbo", "Missing"));
    }

    [Fact]
    public void AreColumnsLoaded_ReturnsFalse_WhenColumnsLoadedIsFalse()
    {
        var cache = BuildCache(("dbo", "Orders"));
        Assert.False(cache.AreColumnsLoaded("dbo", "Orders"));
    }

    [Fact]
    public void AreColumnsLoaded_ReturnsTrue_WhenColumnsLoadedIsTrue()
    {
        var cache = BuildCache(("dbo", "Orders"));
        cache.FindObject("dbo", "Orders")!.ColumnsLoaded = true;
        Assert.True(cache.AreColumnsLoaded("dbo", "Orders"));
    }

    // ── RebuildFkIndex ────────────────────────────────────────────────────────

    [Fact]
    public void RebuildFkIndex_PopulatesParentSide()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("dbo", "Orders", "dbo", "Customers"));
        cache.RebuildFkIndex();

        var fks = cache.GetForeignKeysForTable("dbo", "Orders");
        Assert.Single(fks);
        Assert.Equal("FK_Test", fks[0].FkName);
    }

    [Fact]
    public void RebuildFkIndex_PopulatesReferencedSide()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("dbo", "Orders", "dbo", "Customers"));
        cache.RebuildFkIndex();

        var fks = cache.GetForeignKeysForTable("dbo", "Customers");
        Assert.Single(fks);
    }

    [Fact]
    public void RebuildFkIndex_DoesNotDuplicate_WhenParentAndReferencedAreSameTable()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("dbo", "Category", "dbo", "Category", "FK_Self"));
        cache.RebuildFkIndex();

        var fks = cache.GetForeignKeysForTable("dbo", "Category");
        Assert.Single(fks); // should not appear twice
    }

    [Fact]
    public void RebuildFkIndex_CaseInsensitiveKeyLookup()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("DBO", "ORDERS", "dbo", "Customers"));
        cache.RebuildFkIndex();

        Assert.Single(cache.GetForeignKeysForTable("dbo", "orders"));
    }

    [Fact]
    public void GetForeignKeysForTable_ReturnsEmpty_WhenNoFKsForTable()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("dbo", "Orders", "dbo", "Customers"));
        cache.RebuildFkIndex();

        Assert.Empty(cache.GetForeignKeysForTable("dbo", "Products"));
    }

    [Fact]
    public void GetForeignKeysForTable_FallsBackToLinearScan_WhenIndexNotBuilt()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("dbo", "Orders", "dbo", "Customers"));
        // RebuildFkIndex NOT called

        var fks = cache.GetForeignKeysForTable("dbo", "Orders");
        Assert.Single(fks);
    }

    [Fact]
    public void RebuildFkIndex_MultipleFksForSameTable()
    {
        var cache = new DatabaseCache();
        cache.ForeignKeys.Add(MakeFk("dbo", "Orders", "dbo", "Customers", "FK1"));
        cache.ForeignKeys.Add(MakeFk("dbo", "Orders", "dbo", "Products", "FK2"));
        cache.RebuildFkIndex();

        var fks = cache.GetForeignKeysForTable("dbo", "Orders");
        Assert.Equal(2, fks.Count);
    }

    // ── Phase default ─────────────────────────────────────────────────────────

    [Fact]
    public void NewCache_HasNotLoadedPhase()
    {
        var cache = new DatabaseCache();
        Assert.Equal(PopulationPhase.NotLoaded, cache.Phase);
        Assert.False(cache.IsStale);
    }
}
