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
    public async Task Deduplicate_DisplayName_IsStickyAcrossReExecution()
    {
        // Rename a query (older row gets the name), then re-execute it (newer row has no name).
        // The representative is the newer row, but the display name must persist (latest non-null).
        var named = await SeedAsync("SELECT 2", status: 0, durationMs: 5, rowCount: 0, tabTitle: "My Report");
        var reRun = await SeedAsync("SELECT 2", status: 0, durationMs: 6, rowCount: 0, tabTitle: null);
        await SetExecutedAtAsync(named, DaysAgo(1));
        await SetExecutedAtAsync(reRun, DaysAgo(0));

        var (entries, _) = await _db.SearchAsync(new HistoryFilter { Deduplicate = true });

        var rep = Assert.Single(entries);
        Assert.Equal(reRun, rep.Id);             // representative is the latest execution
        Assert.Equal("My Report", rep.TabTitle); // ...but the name survives the re-execution
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
