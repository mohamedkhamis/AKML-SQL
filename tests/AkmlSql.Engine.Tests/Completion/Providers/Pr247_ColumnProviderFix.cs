using Xunit;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion.Providers;

/// <summary>
/// PR #247 regression tests for ColumnProvider:
/// (291) GetAllTableColumns must iterate a snapshot so Phase B concurrent Add()s don't throw.
/// (294) ColumnScope=All path must honour ScopeSchemas (FR-016), the same restriction
///       ObjectProvider enforces.
/// </summary>
public class Pr247_ColumnProviderFix
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DatabaseCache BuildTwoSchemaCache()
    {
        var cache = new DatabaseCache { CacheKey = "srv:db" };

        // dbo schema — Orders table with two columns
        var orders = new DatabaseObject
        {
            ObjectName = "Orders",
            SchemaName = "dbo",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnName = "OrderId",    TypeName = "int",      IsPrimaryKey = true },
                new Column { ColumnName = "CustomerId", TypeName = "int",      IsPrimaryKey = false },
            ]
        };
        cache.Schemas["dbo"] = new SchemaEntry
        {
            SchemaName = "dbo",
            Objects    = [orders]
        };

        // secret schema — Salaries table; must be excluded when ScopeSchemas = {"dbo"}
        var salaries = new DatabaseObject
        {
            ObjectName = "Salaries",
            SchemaName = "secret",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnName = "Amount", TypeName = "decimal" }
            ]
        };
        cache.Schemas["secret"] = new SchemaEntry
        {
            SchemaName = "secret",
            Objects    = [salaries]
        };

        return cache;
    }

    private static CursorContext SelectContextNoAliases() =>
        new CursorContext
        {
            ClauseType      = ClauseType.Select,
            PrecedingDot    = false,
            // AvailableAliases is empty by default — triggers ColumnScope=All path
        };

    // ── FR-016: ScopeSchemas filters out-of-scope schemas ────────────────────

    [Fact]
    public void GetCompletions_ScopeAll_ScopeSchemasDbo_ExcludesSecretSchema()
    {
        var provider = new ColumnProvider
        {
            ColumnScopeMode = ColumnSuggestionScope.All,
            ScopeSchemas    = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dbo" }
        };
        var cache = BuildTwoSchemaCache();
        var ctx   = SelectContextNoAliases();

        var items = provider.GetCompletions(ctx, cache).ToList();

        Assert.NotEmpty(items);
        // All returned items must originate from the dbo schema
        Assert.All(items, i => Assert.StartsWith("dbo.", i.SourceObject, StringComparison.OrdinalIgnoreCase));
        // The secret.Salaries.Amount column must not appear
        Assert.DoesNotContain(items, i => i.SourceObject!.StartsWith("secret.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.DisplayText == "Amount");
    }

    [Fact]
    public void GetCompletions_ScopeAll_ScopeSchemasDbo_IncludesDboColumns()
    {
        var provider = new ColumnProvider
        {
            ColumnScopeMode = ColumnSuggestionScope.All,
            ScopeSchemas    = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dbo" }
        };
        var cache = BuildTwoSchemaCache();
        var ctx   = SelectContextNoAliases();

        var items = provider.GetCompletions(ctx, cache).ToList();

        Assert.Contains(items, i => i.DisplayText == "OrderId");
        Assert.Contains(items, i => i.DisplayText == "CustomerId");
    }

    // ── FR-016: empty ScopeSchemas means "no restriction" ────────────────────

    [Fact]
    public void GetCompletions_ScopeAll_EmptyScopeSchemas_IncludesAllSchemas()
    {
        var provider = new ColumnProvider
        {
            ColumnScopeMode = ColumnSuggestionScope.All,
            ScopeSchemas    = new HashSet<string>(StringComparer.OrdinalIgnoreCase) // empty = all
        };
        var cache = BuildTwoSchemaCache();
        var ctx   = SelectContextNoAliases();

        var items = provider.GetCompletions(ctx, cache).ToList();

        // Both dbo and secret columns must be present
        Assert.Contains(items, i => i.DisplayText == "OrderId");
        Assert.Contains(items, i => i.DisplayText == "Amount");
    }

    // ── Snapshot safety: columns with ColumnsLoaded=false are skipped ─────────
    // (The ToArray() snapshot fix is verified indirectly — if we didn't snapshot and
    //  a background thread was mutating the list, the enumerator would throw; the
    //  central build's race detector or stress-test harness covers that path.
    //  This test confirms the ColumnsLoaded guard still works after the snapshot change.)

    [Fact]
    public void GetCompletions_ScopeAll_ColumnsNotLoaded_TableSkipped()
    {
        var provider = new ColumnProvider { ColumnScopeMode = ColumnSuggestionScope.All };
        var cache    = new DatabaseCache { CacheKey = "srv:db" };

        var unloaded = new DatabaseObject
        {
            ObjectName    = "Pending",
            SchemaName    = "dbo",
            ObjectType    = DbObjectType.Table,
            ColumnsLoaded = false,       // Phase B not yet complete
            Columns       = [new Column { ColumnName = "Foo", TypeName = "int" }]
        };
        cache.Schemas["dbo"] = new SchemaEntry
        {
            SchemaName = "dbo",
            Objects    = [unloaded]
        };

        var ctx   = SelectContextNoAliases();
        var items = provider.GetCompletions(ctx, cache).ToList();

        // Table skipped because columns aren't loaded → no items from it
        Assert.DoesNotContain(items, i => i.DisplayText == "Foo");
    }
}
