using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// T065 — Unit tests for InsertToUpdateOperation (FR-021).
/// Convert an INSERT statement into an equivalent UPDATE:
///   SET   = non-PK, non-identity, non-computed columns
///   WHERE = PRIMARY KEY columns (looked up in the schema cache)
/// </summary>
public sealed class InsertToUpdateTests
{
    private static InsertToUpdateOperation Op => new();

    // The operation has a single direction, so it ignores OperationType — leave it default
    // (and crucially, do NOT reference RefactorOperationType.InsertToUpdate, which the
    // orchestrator adds to the enum after this task merges).
    private static RefactorPreviewRequest MakeRequest() => new();

    // ─── Schema cache builder ────────────────────────────────────────────────
    // Mirrors ColumnProviderTests/ConnectionScopeTests BuildCache, but lets each
    // column flag IsPrimaryKey / IsIdentity / IsComputed.

    private static DatabaseCache BuildCache(string table, params Column[] columns)
    {
        var cache  = new DatabaseCache { CacheKey = "srv:db" };
        var schema = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = schema;

        var obj = new DatabaseObject
        {
            SchemaName    = "dbo",
            ObjectName    = table,
            ObjectType    = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns       = [.. columns]
        };
        schema.Objects.Add(obj);
        cache.RebuildFkIndex();
        return cache;
    }

    private static Column Col(string name, bool pk = false, bool identity = false, bool computed = false)
        => new()
        {
            ColumnName   = name,
            TypeName     = "int",
            IsPrimaryKey = pk,
            IsIdentity   = identity,
            IsComputed   = computed
        };

    private static RefactoringContext ContextWithCache(string sql, DatabaseCache cache)
    {
        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        ctx.SchemaCache = cache;
        return ctx;
    }

    // ─── Simple INSERT with explicit columns + a PK in the cache ──────────────

    [Fact]
    public async Task InsertToUpdate_ExplicitColumns_WithPk_GeneratesSetAndWhere()
    {
        const string sql = "INSERT INTO dbo.Orders (OrderId, CustomerId, Total) VALUES (1, 42, 99)";
        var cache = BuildCache("Orders",
            Col("OrderId", pk: true),
            Col("CustomerId"),
            Col("Total"));

        var ctx      = ContextWithCache(sql, cache);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        var change = Assert.Single(response.Changes);

        var update = change.NewText;
        Assert.StartsWith("UPDATE", update.TrimStart());

        // PK column belongs in WHERE, NOT in SET.
        var (setPart, wherePart) = SplitOnWhere(update);
        Assert.DoesNotContain("OrderId =", setPart);
        Assert.Contains("OrderId = 1", wherePart);

        // Non-PK columns belong in SET.
        Assert.Contains("CustomerId = 42", setPart);
        Assert.Contains("Total = 99", setPart);

        // The change replaces the original INSERT span.
        Assert.Contains("INSERT", change.OldText);
    }

    // ─── Identity column is skipped from SET ──────────────────────────────────
    // Identity column here is NOT the PK, so it is excluded from SET *and* absent
    // from WHERE (only the separate PK column drives the WHERE).

    [Fact]
    public async Task InsertToUpdate_IdentityColumn_SkippedFromSet()
    {
        const string sql = "INSERT INTO dbo.Orders (RowVer, OrderKey, Total) VALUES (7, 1001, 99)";
        var cache = BuildCache("Orders",
            Col("RowVer", identity: true),   // identity, not PK -> excluded from SET, not in WHERE
            Col("OrderKey", pk: true),        // PK -> WHERE only
            Col("Total"));                    // plain -> SET

        var ctx      = ContextWithCache(sql, cache);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        var change = Assert.Single(response.Changes);
        var (setPart, wherePart) = SplitOnWhere(change.NewText);

        // Identity column must not appear in SET, and (not being PK) not in WHERE.
        Assert.DoesNotContain("RowVer", setPart);
        Assert.DoesNotContain("RowVer", wherePart);

        // PK in WHERE, plain column in SET.
        Assert.Contains("OrderKey = 1001", wherePart);
        Assert.Contains("Total = 99", setPart);
    }

    // ─── Missing PK -> placeholder + warning, CanApply still true ──────────────

    [Fact]
    public async Task InsertToUpdate_NoPrimaryKey_EmitsPlaceholderAndWarning()
    {
        const string sql = "INSERT INTO dbo.Orders (CustomerId, Total) VALUES (42, 99)";
        var cache = BuildCache("Orders",
            Col("CustomerId"),
            Col("Total"));

        var ctx      = ContextWithCache(sql, cache);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        Assert.NotEmpty(response.Changes);

        var change = Assert.Single(response.Changes);
        Assert.Contains("-- TODO", change.NewText);
        Assert.Contains("WHERE", change.NewText);

        // Both non-PK columns still land in SET.
        Assert.Contains("CustomerId = 42", change.NewText);
        Assert.Contains("Total = 99", change.NewText);

        Assert.NotEmpty(response.Warnings);
    }

    // ─── INSERT ... SELECT -> CanApply = false ────────────────────────────────

    [Fact]
    public async Task InsertToUpdate_InsertSelect_CanApplyFalse()
    {
        const string sql = "INSERT INTO dbo.Orders (CustomerId) SELECT CustomerId FROM dbo.Staging";
        var cache = BuildCache("Orders", Col("OrderId", pk: true), Col("CustomerId"));

        var ctx      = ContextWithCache(sql, cache);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
        Assert.Empty(response.Changes);
    }

    // ─── Multi-row VALUES -> CanApply = false ─────────────────────────────────

    [Fact]
    public async Task InsertToUpdate_MultiRowValues_CanApplyFalse()
    {
        const string sql = "INSERT INTO dbo.Orders (OrderId, Total) VALUES (1, 99), (2, 88)";
        var cache = BuildCache("Orders", Col("OrderId", pk: true), Col("Total"));

        var ctx      = ContextWithCache(sql, cache);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
        Assert.Empty(response.Changes);
    }

    // ─── No INSERT in scope -> empty changes, no errors ───────────────────────

    [Fact]
    public async Task InsertToUpdate_NoInsert_EmptyChanges()
    {
        const string sql = "SELECT * FROM dbo.Orders";
        var cache = BuildCache("Orders", Col("OrderId", pk: true));

        var ctx      = ContextWithCache(sql, cache);
        var response = await Op.PreviewAsync(MakeRequest(), ctx, default);

        Assert.True(response.CanApply);
        Assert.Empty(response.Changes);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Splits the generated UPDATE into the SET segment and the WHERE segment.</summary>
    private static (string setPart, string wherePart) SplitOnWhere(string update)
    {
        var idx = update.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        return idx < 0
            ? (update, string.Empty)
            : (update.Substring(0, idx), update.Substring(idx));
    }
}
