using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// T072/T073 (FR-039, R10): version-preserving history retention.
/// A purge must trim OLD version snapshots while keeping each query's LATEST version and
/// ALL execution records (the <c>history</c> rows). A below-window purge trims nothing,
/// and favorites are preserved (their execution rows are never deleted by version-trim).
/// </summary>
public sealed class HistoryRetentionVersionPreservationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly HistoryDatabase _db;

    public HistoryRetentionVersionPreservationTests()
    {
        // Isolate each test run in its own temporary database file.
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            "akmlsql-history-tests",
            $"hist-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_dbPath);
        _db.InitializeAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task PurgeOldVersions_TrimsOldVersions_KeepsLatestAndExecutions()
    {
        // Arrange: one execution (history row) with three versions —
        // two OLD (well beyond the window) and one RECENT (the latest).
        var historyId = await SeedExecutionAsync(sqlText: "SELECT 1");

        // Old versions (saved 30 / 40 days ago) — inserted first so they get the lowest ids.
        await SeedVersionAsync(historyId, "v1 old", DaysAgo(40));
        await SeedVersionAsync(historyId, "v2 old", DaysAgo(30));
        // Latest version: most-recent saved_at AND highest id.
        await SeedVersionAsync(historyId, "v3 latest", DaysAgo(0));

        // A second, independent execution on the same query content keeps an execution row
        // that must survive the trim (executions are never deleted by version-trim).
        var secondExecId = await SeedExecutionAsync(sqlText: "SELECT 1");

        // Act: trim versions older than 7 days.
        var deleted = await _db.PurgeOldVersionsAsync(retentionDays: 7);

        // Assert: exactly the two old versions were trimmed.
        Assert.Equal(2, deleted);

        var versions = await _db.GetVersionsAsync(historyId);
        Assert.Single(versions);
        Assert.Equal("v3 latest", versions[0].SqlText);

        // Both execution (history) rows survive — version-trim never touches the history table.
        var exec1 = await _db.GetFullSqlAsync(historyId);
        var exec2 = await _db.GetFullSqlAsync(secondExecId);
        Assert.NotNull(exec1);
        Assert.NotNull(exec2);
    }

    [Fact]
    public async Task PurgeOldVersions_KeepsSoleVersion_EvenWhenOld()
    {
        // An entry whose ONLY version is old must still keep it: a sole version is its own
        // latest (MAX(id) per history_id), so it is exempt from trimming.
        var historyId = await SeedExecutionAsync(sqlText: "SELECT 2");
        await SeedVersionAsync(historyId, "only old version", DaysAgo(60));

        var deleted = await _db.PurgeOldVersionsAsync(retentionDays: 7);

        Assert.Equal(0, deleted);
        var versions = await _db.GetVersionsAsync(historyId);
        Assert.Single(versions);
        Assert.Equal("only old version", versions[0].SqlText);
    }

    [Fact]
    public async Task PurgeOldVersions_BelowWindow_TrimsNothing()
    {
        // All versions are old relative to a 7-day window, but a very long retention window
        // (100 years) puts the cutoff far in the past → nothing qualifies for trimming.
        var historyId = await SeedExecutionAsync(sqlText: "SELECT 3");
        await SeedVersionAsync(historyId, "v1", DaysAgo(40));
        await SeedVersionAsync(historyId, "v2", DaysAgo(30));
        await SeedVersionAsync(historyId, "v3", DaysAgo(20));

        var deleted = await _db.PurgeOldVersionsAsync(retentionDays: 36500);

        Assert.Equal(0, deleted);
        var versions = await _db.GetVersionsAsync(historyId);
        Assert.Equal(3, versions.Count);
    }

    [Fact]
    public async Task PurgeOldVersions_PreservesFavoriteEntryItsLatestVersionAndExecutions()
    {
        // A favorited execution with old + latest versions: the favorite execution row,
        // its latest version, and the execution record itself must all survive.
        var favoriteId = await SeedExecutionAsync(sqlText: "SELECT 99");
        await _db.ToggleFavoriteAsync(favoriteId); // mark as favorite

        await SeedVersionAsync(favoriteId, "fav old", DaysAgo(50));
        await SeedVersionAsync(favoriteId, "fav latest", DaysAgo(0));

        var deleted = await _db.PurgeOldVersionsAsync(retentionDays: 7);

        Assert.Equal(1, deleted); // only the single old version

        // Favorite execution row survives (favorites preserved).
        var favorites = await SearchFavoritesAsync();
        Assert.Contains(favorites, e => e.Id == favoriteId && e.IsFavorite);

        // Latest version preserved.
        var versions = await _db.GetVersionsAsync(favoriteId);
        Assert.Single(versions);
        Assert.Equal("fav latest", versions[0].SqlText);

        // Execution record preserved.
        Assert.NotNull(await _db.GetFullSqlAsync(favoriteId));
    }

    [Fact]
    public async Task RetentionService_Startup_TrimsOldVersionsViaWiring()
    {
        // Regression guard for T073 wiring: HistoryRetentionService.StartAsync() must invoke
        // the version-preserving trim (not only the entry-level purge).
        var historyId = await SeedExecutionAsync(sqlText: "SELECT 7");
        await SeedVersionAsync(historyId, "old", DaysAgo(40));
        await SeedVersionAsync(historyId, "latest", DaysAgo(0));

        var settings = new HistorySettings { RetentionDays = 7, MaxEntries = 100_000 };
        using (var service = new HistoryRetentionService(_db, settings))
        {
            await service.StartAsync();
        }

        var versions = await _db.GetVersionsAsync(historyId);
        Assert.Single(versions);
        Assert.Equal("latest", versions[0].SqlText);

        // The execution row survives the service's combined purge.
        Assert.NotNull(await _db.GetFullSqlAsync(historyId));
    }

    // ── Seeding helpers ───────────────────────────────────────────────────────

    /// <summary>Seeds one execution (history) row and returns its id.</summary>
    private Task<long> SeedExecutionAsync(string sqlText) =>
        _db.InsertEntryAsync(
            sqlText: sqlText,
            truncated: false,
            server: "localhost",
            database: "TestDb",
            username: "tester",
            durationMs: 12,
            rowCount: 1,
            status: 0,
            errorMessage: null,
            source: "test",
            tabTitle: "Query1.sql");

    /// <summary>
    /// Inserts a version snapshot with an explicit <c>saved_at</c>. The production
    /// <see cref="HistoryDatabase.InsertVersionAsync"/> cannot set saved_at (it relies on the
    /// column default <c>datetime('now')</c>), so seeding aged versions requires a raw write.
    /// The value is written in SQLite's native <c>datetime('now')</c> format
    /// ('YYYY-MM-DD HH:MM:SS') to mirror exactly what production stores.
    /// </summary>
    private async Task SeedVersionAsync(long historyId, string sqlText, DateTime savedAtUtc)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(@"
            INSERT INTO history_versions (history_id, sql_text, saved_at)
            VALUES (@historyId, @sqlText, @savedAt);", conn);
        cmd.Parameters.AddWithValue("@historyId", historyId);
        cmd.Parameters.AddWithValue("@sqlText", sqlText);
        cmd.Parameters.AddWithValue("@savedAt", savedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<HistoryEntryDto>> SearchFavoritesAsync()
    {
        var (entries, _) = await _db.SearchAsync(new HistoryFilter { FavoritesOnly = true });
        return entries;
    }

    private static DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days);

    public void Dispose()
    {
        _db.Dispose();

        // Microsoft.Data.Sqlite pools connections; clear the pool so the temp file unlocks
        // before deletion. Swallow IO errors so cleanup can never fail a passing test.
        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch
        {
            // ignore
        }

        TryDelete(_dbPath);
        TryDelete(_dbPath + "-wal");
        TryDelete(_dbPath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore — temp files are cleaned up by the OS eventually
        }
    }
}
