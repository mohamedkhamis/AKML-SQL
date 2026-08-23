using System.Globalization;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 030 bug fix — deduplicated history search must return a DETERMINISTIC representative row
/// per content_hash: the most-recent execution. The previous query did
/// <c>GROUP BY content_hash</c> with bare (non-aggregated) columns alongside several <c>MAX()</c>
/// aggregates; SQLite only pins bare columns to the min/max row when there is exactly ONE MIN/MAX,
/// so name / status / row-count / duration were taken from an arbitrary row in the group.
///
/// These tests insert known multi-row groups and assert the representative's fields come from the
/// latest execution (with a sticky latest-non-null display name and "any version" favourite/open).
/// They are the deterministic evidence for the fix — the UI cannot prove it, because the old bug
/// was non-deterministic and a green UI pass is indistinguishable from luck.
/// </summary>
public sealed class HistoryDedupRepresentativeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly HistoryDatabase _db;

    public HistoryDedupRepresentativeTests()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            "akmlsql-history-tests",
            $"dedup-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_dbPath);
        _db.InitializeAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Deduplicate_RepresentativeFieldsComeFromLatestExecution()
    {
        // Two executions of the SAME sql (→ same content_hash → one deduped group).
        // The OLDER one failed with a stale row-count and no name; the NEWER one succeeded,
        // is named, and has its own duration/row-count. The representative must reflect the NEWER.
        var older = await SeedAsync("SELECT 1", status: 1, durationMs: 999, rowCount: 7, tabTitle: null);
        var newer = await SeedAsync("SELECT 1", status: 0, durationMs: 11, rowCount: 1, tabTitle: "Renamed");
        await SetExecutedAtAsync(older, DaysAgo(2));
        await SetExecutedAtAsync(newer, DaysAgo(0));

        var (entries, total) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true });

        var rep = Assert.Single(entries);
        Assert.Equal(1, total);
        Assert.Equal(newer, rep.Id);          // latest row's id (actions target this)
        Assert.Equal(0, rep.Status);          // latest run's status, not the older failure
        Assert.Equal(11, rep.DurationMs);     // latest run's duration
        Assert.Equal(1, rep.RowCount);        // latest run's row-count
        Assert.Equal("Renamed", rep.TabTitle);
        Assert.Equal(2, rep.ExecutionCount);  // both executions counted
    }

    [Fact]
    public async Task Deduplicate_DisplayName_ReflectsLatestExecutionsTabTitle_NotStickyWithoutASession()
    {
        // Task 6 (history-session-grouping) behaviour change: the FIRST_VALUE(h.tab_title) window
        // that made a name "sticky" across re-executions of the same content_hash was deliberately
        // REMOVED — the display name now comes from the joined query_sessions row for grouped rows,
        // with a per-row h.tab_title fallback for legacy/ungrouped rows (no session_id). These two
        // inserts pass no sessionKey, so they fall back to the old per-content-hash partition with
        // NO session to carry a name: the older row's tab_title ("My Report") no longer bleeds into
        // the newer, unnamed row. The representative (latest execution) reports its OWN (empty)
        // name instead of inheriting the older row's.
        var named = await SeedAsync("SELECT 2", status: 0, durationMs: 5, rowCount: 0, tabTitle: "My Report");
        var reRun = await SeedAsync("SELECT 2", status: 0, durationMs: 6, rowCount: 0, tabTitle: null);
        await SetExecutedAtAsync(named, DaysAgo(1));
        await SetExecutedAtAsync(reRun, DaysAgo(0));

        var (entries, _) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true });

        var rep = Assert.Single(entries);
        Assert.Equal(reRun, rep.Id);        // representative is still the latest execution
        Assert.Equal(string.Empty, rep.TabTitle); // no longer inherits the older row's name
        Assert.Equal(2, rep.ExecutionCount);
    }

    [Fact]
    public async Task Deduplicate_IsFavorite_TrueWhenAnyVersionFavorited()
    {
        // Favourite the OLDER execution, then re-run (newer row not favourited).
        // The deduped representative must read as favourited — consistent with the FavoritesOnly
        // filter, which matches a group when ANY of its rows is favourited.
        var older = await SeedAsync("SELECT 3", status: 0, durationMs: 1, rowCount: 0, tabTitle: null);
        var newer = await SeedAsync("SELECT 3", status: 0, durationMs: 1, rowCount: 0, tabTitle: null);
        await SetExecutedAtAsync(older, DaysAgo(1));
        await SetExecutedAtAsync(newer, DaysAgo(0));
        await _db.ToggleFavoriteAsync(older);

        var (entries, _) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true });

        var rep = Assert.Single(entries);
        Assert.Equal(newer, rep.Id);
        Assert.True(rep.IsFavorite);
    }

    [Fact]
    public async Task Deduplicate_StickyName_RespectsFilter_NotBledFromOtherPartitionMember()
    {
        // Same sql executed on two servers (→ same content_hash → one deduped group across servers).
        // The OLDER row is on server 'A' (name 'KeepMe'); the NEWER row is on server 'B' with a
        // DIFFERENT name 'Bled'. When the search is FILTERED to server 'A', only the A row is in the
        // partition, so the display name must be 'KeepMe'. This is adversarial: the OLD correlated
        // subquery scanned the bare table ignoring the filter and would return the latest non-null
        // across ALL servers ('Bled', from the filtered-out B row) — so it FAILS on the old code and
        // PASSES only because tab_title is now a window column over the FILTERED partition.
        var older = await SeedAsync("SELECT 9", status: 0, durationMs: 1, rowCount: 0, tabTitle: "KeepMe");
        var newer = await SeedAsync("SELECT 9", status: 0, durationMs: 1, rowCount: 0, tabTitle: "Bled");
        await SetServerAsync(older, "A");
        await SetServerAsync(newer, "B");
        await SetExecutedAtAsync(older, DaysAgo(2));
        await SetExecutedAtAsync(newer, DaysAgo(0));

        var (entries, _) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Server = "A" });

        var rep = Assert.Single(entries);
        Assert.Equal(older, rep.Id);              // only the A row survives the filter
        Assert.Equal("KeepMe", rep.TabTitle);     // name from the A partition, NOT 'Bled' from server B
    }

    [Fact]
    public async Task Deduplicate_Paging_IsStableViaIdTiebreak()
    {
        // Two DISTINCT sql texts (→ two content_hashes → two groups) with the SAME executed_at.
        // Without an id tiebreak in the outer ORDER BY, equal executed_at groups can reorder across
        // LIMIT/OFFSET pages, duplicating/skipping rows on "Load more". Page 0 and page 1 (size 1)
        // must return the two distinct ids with no overlap.
        var a = await SeedAsync("SELECT 100", status: 0, durationMs: 1, rowCount: 0, tabTitle: null);
        var b = await SeedAsync("SELECT 200", status: 0, durationMs: 1, rowCount: 0, tabTitle: null);
        var sameTime = DaysAgo(0);
        await SetExecutedAtAsync(a, sameTime);
        await SetExecutedAtAsync(b, sameTime);

        var (page0, total) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 1, Offset = 0 });
        var (page1, _) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Limit = 1, Offset = 1 });

        Assert.Equal(2, total);
        var id0 = Assert.Single(page0).Id;
        var id1 = Assert.Single(page1).Id;
        Assert.NotEqual(id0, id1);                                  // no overlap across pages
        Assert.Equal(new[] { a, b }.OrderBy(x => x), new[] { id0, id1 }.OrderBy(x => x)); // both ids, once each
    }

    [Fact]
    public async Task Rename_AppliesToWholeContentHashGroup_AndSurvivesFilter()
    {
        // Same sql executed twice (→ same content_hash → one deduped group across servers), both
        // initially unnamed. Older row is on server 'A'; newer row is on server 'B'. Rename targets
        // the NEWER (server-B) row's id. Because the rename now stamps tab_title on EVERY row of the
        // content_hash group, a search FILTERED to server 'A' — which excludes the renamed row — must
        // still show the new name. Under the old per-row rename (WHERE id = @id) the A row's tab_title
        // stayed null and the deduped (filtered) window would surface no name, so this FAILS on the
        // old code and PASSES only because the rename propagated to the whole group.
        var older = await SeedAsync("SELECT 42", status: 0, durationMs: 1, rowCount: 0, tabTitle: null);
        var newer = await SeedAsync("SELECT 42", status: 0, durationMs: 1, rowCount: 0, tabTitle: null);
        await SetServerAsync(older, "A");
        await SetServerAsync(newer, "B");
        await SetExecutedAtAsync(older, DaysAgo(2));
        await SetExecutedAtAsync(newer, DaysAgo(0));

        await _db.UpdateTabTitleAsync(newer, "MyReport"); // rename the server-B row...

        var (entries, _) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, Server = "A" });

        var rep = Assert.Single(entries);
        Assert.Equal(older, rep.Id);            // only the A row survives the filter
        Assert.Equal("MyReport", rep.TabTitle); // ...yet the rename propagated to it (query-level label)
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private Task<long> SeedAsync(string sqlText, int status, long durationMs, long rowCount, string? tabTitle) =>
        _db.InsertEntryAsync(
            sqlText: sqlText,
            truncated: false,
            server: "localhost",
            database: "TestDb",
            username: "tester",
            durationMs: durationMs,
            rowCount: rowCount,
            status: status,
            errorMessage: null,
            source: "test",
            tabTitle: tabTitle);

    private async Task SetExecutedAtAsync(long id, DateTime executedAtUtc)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand(
            "UPDATE history SET executed_at = @executedAt WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("@executedAt", executedAtUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SetServerAsync(long id, string server)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand(
            "UPDATE history SET server = @server WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("@server", server);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days);

    public void Dispose()
    {
        // NB: deliberately NOT calling SqliteConnection.ClearAllPools() — it is process-global and
        // would close pooled connections held by other engine tests running in parallel. TryDelete
        // is best-effort; a still-pooled temp file is harmless (the OS reclaims %TEMP% eventually).
        _db.Dispose();
        TryDelete(_dbPath);
        TryDelete(_dbPath + "-wal");
        TryDelete(_dbPath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
