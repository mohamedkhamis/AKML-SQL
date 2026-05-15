using Xunit;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Tests that <c>MatchByColumnName</c> on <see cref="JoinOnFkProvider"/> gates Pass 3 —
/// the CTE name-match fallback that emits ON-clause suggestions when at least one join
/// participant is a CTE with matching column names but no FK relationship.
///
/// Table-to-table name-match does not exist in this provider (by design; FK is
/// authoritative for real tables). Only Pass 3 (CTE name-match) is gated.
/// </summary>
public class JoinOnFkProviderTests
{
    private readonly JoinOnFkProvider _provider = new();
    // An empty DatabaseCache satisfies CanHandle's non-null requirement while
    // contributing zero FK relationships — so only the name-match pass can fire.
    private static readonly DatabaseCache EmptyCache = new() { CacheKey = "test:db" };

    // ── Helper: build a minimal CursorContext with two CTE aliases and shared column ──

    /// <summary>
    /// Builds a CursorContext that looks like: two CTEs in scope (C1, C2) both exposing
    /// an "Id" column, with the cursor inside the ON clause. No DatabaseCache is needed
    /// since the name-match pass resolves columns from AvailableCtes directly.
    /// </summary>
    private static CursorContext MakeCtxWithTwoCtesSharingColumn()
    {
        var ctx = new CursorContext
        {
            ClauseType = ClauseType.JoinOn,
            PrecedingDot = false
        };

        // Two CTEs in scope with a shared column "Id"
        ctx.AvailableCtes["C1"] = ["Id", "Name"];
        ctx.AvailableCtes["C2"] = ["Id", "Total"];

        // Aliases pointing to the CTEs (AvailableAliases drives the outer loop in JoinOnFkProvider)
        ctx.AvailableAliases["C1"] = "C1";
        ctx.AvailableAliases["C2"] = "C2";

        return ctx;
    }

    // ── Test 1: MatchByColumnName = true (default) ──────────────────────────

    /// <summary>
    /// When MatchByColumnName is true (default), Pass 3 must fire and yield at least
    /// one name-match suggestion referencing the shared "Id" column (e.g. C1.Id = C2.Id).
    /// </summary>
    [Fact]
    public void MatchByColumnName_True_FallsBackToColumnNameMatchForCtes()
    {
        _provider.MatchByColumnName = true;

        var ctx = MakeCtxWithTwoCtesSharingColumn();
        Assert.True(_provider.CanHandle(ctx, EmptyCache));

        var items = _provider.GetCompletions(ctx, EmptyCache).ToList();

        // At least one suggestion must be a CTE name-match for "Id"
        Assert.Contains(items, i =>
            i.InsertText.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0 &&
            i.SecondaryText != null &&
            i.SecondaryText.IndexOf("Name match", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // ── Test 2: MatchByColumnName = false ───────────────────────────────────

    /// <summary>
    /// When MatchByColumnName is false, Pass 3 must be entirely skipped so no
    /// name-match suggestions appear. With no cache (no FKs), the result must be empty.
    /// </summary>
    [Fact]
    public void MatchByColumnName_False_NoNameMatchSuggestionsWhenNoFk()
    {
        _provider.MatchByColumnName = false;

        var ctx = MakeCtxWithTwoCtesSharingColumn();
        Assert.True(_provider.CanHandle(ctx, EmptyCache));

        var items = _provider.GetCompletions(ctx, EmptyCache).ToList();

        // Must not contain any name-match suggestions
        Assert.DoesNotContain(items, i =>
            i.SecondaryText != null &&
            i.SecondaryText.IndexOf("Name match", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
