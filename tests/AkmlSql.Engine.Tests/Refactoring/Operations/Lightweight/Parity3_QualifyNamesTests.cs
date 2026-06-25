using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// Spec 030 Parity #3 — unit tests for the schema-aware QualifyObjectNamesOperation (table qualify).
/// Covers Bug #2 isolation (unconditional qualify) and the ColumnsLoaded asymmetry (Phase-A-only
/// cache still qualifies).
/// </summary>
public sealed class Parity3_QualifyNamesTests
{
    private readonly QualifyObjectNamesOperation _op = new();

    /// <param name="columnsLoaded">When false, the cache is Phase-A-only (object names but no columns).</param>
    /// <param name="ambiguous">When true, adds a second schema 'sales' that also owns 'Orders'.</param>
    private static DatabaseCache BuildCache(bool columnsLoaded = true, bool ambiguous = false)
    {
        var cache = new DatabaseCache();

        var dbo = new SchemaEntry { SchemaName = "dbo" };
        dbo.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = columnsLoaded,
            Columns = columnsLoaded
                ? [new Column { ColumnId = 1, ColumnName = "OrderId", TypeName = "int", IsPrimaryKey = true }]
                : []
        });
        cache.Schemas["dbo"] = dbo;

        if (ambiguous)
        {
            var sales = new SchemaEntry { SchemaName = "sales" };
            sales.Objects.Add(new DatabaseObject
            {
                SchemaName = "sales",
                ObjectName = "Orders",
                ObjectType = DbObjectType.Table,
                ColumnsLoaded = columnsLoaded
            });
            cache.Schemas["sales"] = sales;
        }

        return cache;
    }

    private static DatabaseCache BuildSingleSchemaCache()
    {
        // Object owned by exactly one non-dbo schema → resolves to that schema.
        var cache = new DatabaseCache();
        var sales = new SchemaEntry { SchemaName = "sales" };
        sales.Objects.Add(new DatabaseObject
        {
            SchemaName = "sales",
            ObjectName = "Invoices",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true
        });
        cache.Schemas["sales"] = sales;
        return cache;
    }

    [Fact]
    public void Unqualified_QualifiesToDbo()
    {
        const string sql = "SELECT OrderId FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        Assert.Equal("SELECT OrderId FROM dbo.Orders", result);
    }

    [Fact]
    public void AlreadyQualified_LeftIntact()
    {
        const string sql = "SELECT OrderId FROM dbo.Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        Assert.Equal(sql, result); // no double-qualify
    }

    [Fact]
    public void Ambiguous_SkipsWithWarning()
    {
        // Orders exists in both dbo and sales — but dbo is preferred, so it is NOT ambiguous.
        // Use a non-dbo-only ambiguity: drop dbo and have two non-dbo schemas.
        var cache = new DatabaseCache();
        foreach (var s in new[] { "sales", "hr" })
        {
            var entry = new SchemaEntry { SchemaName = s };
            entry.Objects.Add(new DatabaseObject { SchemaName = s, ObjectName = "Staff", ObjectType = DbObjectType.Table });
            cache.Schemas[s] = entry;
        }

        const string sql = "SELECT * FROM Staff";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, cache);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result); // skipped — unchanged
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("ambiguous", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DboAndOtherSchema_NowAmbiguous_SkipsWithWarning()
    {
        // Bug #2: dbo + sales both own Orders. There is NO reachable default/active schema,
        // so we must NOT guess. Previously this preferred dbo; the correct conservative
        // behavior is to skip + warn ambiguous (qualify only when EXACTLY one schema owns it).
        const string sql = "SELECT OrderId FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache(ambiguous: true));

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result); // unchanged — never guess dbo over sales
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("ambiguous", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CteName_CollidingWithRealTable_IsNotRewritten()
    {
        // Bug #1: a CTE named like a real cached table must NOT be qualified — doing so
        // silently bypasses the CTE and points at the base table (WRONG SEMANTICS).
        // dbo.Orders is in the cache; the WITH defines a CTE also named Orders.
        const string sql = "WITH Orders AS (SELECT 1 AS OrderId) SELECT OrderId FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        // The FROM Orders reference resolves to the CTE — left intact, no warning.
        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void TempTableReference_SkippedSilently_NoWarning()
    {
        // Bug #1: #temp references are never in the schema cache. They must be skipped
        // for qualification AND must NOT emit a spurious "not found in schema cache" warning.
        const string sql = "SELECT OrderId FROM #TempOrders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result); // unchanged — never qualify a temp table
        Assert.Empty(warnings);    // no spurious "not found" warning
    }

    [Fact]
    public void Apply_WhenInsertOffsetIsInvalid_SurfacesWarning_TextUnchanged()
    {
        // Bug #7: the Apply() catch must not be a silent no-op. Force an exception by
        // pointing the context at parsed offsets that exceed the (mutated) document text,
        // so the text.Insert(offset, ...) throws. The catch must return the ORIGINAL text
        // plus a warning describing the failure.
        const string sql = "SELECT OrderId FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());
        // Parsed Script offsets reference the original SQL (offset ~20 for 'Orders'),
        // but the document text is now far shorter → Insert throws ArgumentOutOfRange.
        ctx.DocumentText = "x";

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal("x", result); // original (mutated) text returned, not corrupted
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Qualify failed", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SingleNonDboSchema_QualifiesToThatSchema()
    {
        const string sql = "SELECT * FROM Invoices";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildSingleSchemaCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        Assert.Equal("SELECT * FROM sales.Invoices", result);
    }

    [Fact]
    public void BracketStyle_Preserved()
    {
        const string sql = "SELECT OrderId FROM [Orders]";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        // Original bracketed → schema injected bracketed too; original table bracket preserved.
        Assert.Equal("SELECT OrderId FROM [dbo].[Orders]", result);
    }

    [Fact]
    public void PhaseAOnlyCache_StillQualifies()
    {
        // ColumnsLoaded=false (Phase-A-only) MUST NOT block qualification — the asymmetry trap.
        const string sql = "SELECT OrderId FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache(columnsLoaded: false));

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        Assert.Equal("SELECT OrderId FROM dbo.Orders", result);
    }

    [Fact]
    public void NullCache_ReturnsWarning_TextUnchanged()
    {
        const string sql = "SELECT OrderId FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql); // null cache

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Schema cache not available"));
    }
}
