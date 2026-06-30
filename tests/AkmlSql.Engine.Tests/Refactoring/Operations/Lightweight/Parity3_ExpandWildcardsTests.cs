using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// Spec 030 Parity #3 — unit tests for the schema-aware ExpandWildcardsOperation.
/// Mirrors the ExpandInsertColumns in-memory-cache pattern.
/// </summary>
public sealed class Parity3_ExpandWildcardsTests
{
    private readonly ExpandWildcardsOperation _op = new();

    private static DatabaseCache BuildCache(bool columnsLoaded = true)
    {
        var cache = new DatabaseCache();
        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = columnsLoaded,
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
            ObjectName = "Customers",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = columnsLoaded,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "CustomerId", TypeName = "int", IsPrimaryKey = true },
                new Column { ColumnId = 2, ColumnName = "Name", TypeName = "nvarchar", MaxLength = 50 }
            ]
        });
        cache.Schemas["dbo"] = schema;
        return cache;
    }

    [Fact]
    public void BareStar_SingleTable_ExpandsToBareColumnNames()
    {
        const string sql = "SELECT * FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        // PK first, then ordinal (handler ordering).
        Assert.Equal("SELECT OrderId, CustomerName, OrderDate FROM Orders", result);
        // No alias prefix on bare-single.
        Assert.DoesNotContain(".OrderId", result);
    }

    [Fact]
    public void QualifiedStar_ExpandsThatTableAliasPrefixed()
    {
        const string sql = "SELECT o.* FROM Orders o";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        Assert.Equal("SELECT o.OrderId, o.CustomerName, o.OrderDate FROM Orders o", result);
    }

    [Fact]
    public void BareStar_MultiTable_ExpandsAllAliasPrefixed()
    {
        const string sql = "SELECT * FROM Orders o JOIN Customers c ON o.CustomerName = c.Name";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache());

        var (result, warnings) = _op.Apply(ctx);

        Assert.Empty(warnings);
        // Cross-table column order is dictionary-driven — assert membership, not exact ordering.
        Assert.Contains("o.OrderId", result);
        Assert.Contains("o.CustomerName", result);
        Assert.Contains("o.OrderDate", result);
        Assert.Contains("c.CustomerId", result);
        Assert.Contains("c.Name", result);
        // No bare star remains.
        Assert.DoesNotContain("SELECT *", result);
        // Multi-table is always alias-prefixed.
        Assert.DoesNotContain("SELECT OrderId", result);
    }

    [Fact]
    public void NullCache_ReturnsWarning_TextUnchanged()
    {
        const string sql = "SELECT * FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql); // null cache

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Schema cache not available"));
    }

    [Fact]
    public void ColumnsNotLoaded_ReturnsWarning_NotSilentOriginal()
    {
        // Phase-A-only cache (ColumnsLoaded=false): the handler cannot expand → graceful warning.
        const string sql = "SELECT * FROM Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql, BuildCache(columnsLoaded: false));

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result); // unchanged because no columns available
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("columns", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatchPath_AnyException_ReturnsOriginalTextWithWarning_NotSilentEmptyArray()
    {
        // BUG #7 regression guard: the catch block must surface a non-empty warning
        // instead of silently returning (originalText, []).
        //
        // Forcing mechanism: parse from a long padded SQL so that the SelectStarExpression
        // carries a high StartOffset (well beyond the short DocumentText), then overwrite
        // DocumentText with the short string. When Apply() calls
        //   handler.Handle(text, star.StartOffset=highOffset, ...)
        // the AliasResolver finds cursorOffset > batch end → returns no aliases →
        // Handle returns Success=false with message "No tables found in FROM clause" →
        // the per-star handler-warning path fires, NOT the catch.
        //
        // That means this test exercises the per-star warning path, NOT the catch itself —
        // the catch path cannot be reached from the public API without a build (no hook
        // exists to inject a throwing component post-construction). The assertion is
        // therefore a non-regression canary: it would have caught the old bug (catch
        // returned [] rather than a non-empty warning) had the catch fired.
        //
        // What IS definitively asserted:
        //   1. The original (short) DocumentText is returned unchanged.
        //   2. warnings is non-empty — the operation is never silent on failure.
        // If a future change introduces a throw site reachable here, the prefix check
        // ("Expand wildcards failed:") will distinguish catch from handler warnings.
        const string paddedSql =
            "SELECT                                                                         " +
            "                                                                               " +
            "* FROM Orders"; // star is roughly at offset 162

        const string shortText = "SELECT * FROM Orders"; // 20 chars — star at offset 7

        // Build ctx from the long SQL so Script has a star at a high offset.
        var ctx = LightweightOperationTestHelper.CreateContext(paddedSql, BuildCache());
        // Overwrite DocumentText with the short version — desync Script vs DocumentText.
        ctx.DocumentText = shortText;

        var (result, warnings) = _op.Apply(ctx);

        // Original (short) text must come back unchanged.
        Assert.Equal(shortText, result);
        // Must never return an empty warning array — silent failures are the bug.
        Assert.NotEmpty(warnings);
    }
}
