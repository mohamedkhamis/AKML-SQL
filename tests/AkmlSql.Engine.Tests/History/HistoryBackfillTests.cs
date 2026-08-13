using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class HistoryBackfillTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"akml-bf-{Guid.NewGuid():N}.db");

    /// <summary>Inserts a legacy row directly (session_id left NULL), as a v1 database would have.</summary>
    private static async Task InsertLegacy(
        string cs, string sql, string? tabTitle, DateTime whenLocal, string db = "aqmar")
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(@"
            INSERT INTO history (sql_text, truncated, server, database_name, username,
                                 executed_at, duration_ms, row_count, status, error_msg,
                                 source, tab_title, content_hash, is_favorite)
            VALUES (@sql, 0, '(local)', @db, NULL, @at, 1, 1, 0, NULL, NULL, @title, @hash, 0);", c);
        cmd.Parameters.AddWithValue("@sql", sql);
        cmd.Parameters.AddWithValue("@db", db);
        cmd.Parameters.AddWithValue("@at",
            DateTime.SpecifyKind(whenLocal, DateTimeKind.Local).ToUniversalTime()
                .ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@title", (object?)tabTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hash", HistoryDatabase.ComputeContentHash(sql));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(int Sessions, int Unassigned)> Counts(string cs)
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT (SELECT COUNT(*) FROM query_sessions), " +
            "       (SELECT COUNT(*) FROM history WHERE session_id IS NULL)", c);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    /// <summary>
    /// Best-effort delete: Microsoft.Data.Sqlite pools the native handle per connection string,
    /// so the OS file (and its WAL-mode sidecars) can still be open past our `await using`
    /// disposals. A bare File.Delete throws IOException on Windows when that happens; mirror the
    /// try/catch pattern from QuerySessionStoreTests (same rationale documented there) and also
    /// clean up the -wal/-shm sidecars.
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static void CleanupDb(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }

    [Fact]
    public async Task Backfill_groups_legacy_rows_and_names_them()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();

            var day = DateTime.Today.AddDays(-1);
            // Two scratch-named groups on the same day + one genuinely saved file.
            await InsertLegacy(cs, "SELECT 1", "dwnhdxfq.sql", day.AddHours(9));
            await InsertLegacy(cs, "SELECT 2", "dwnhdxfq.sql", day.AddHours(10));
            await InsertLegacy(cs, "SELECT 3", "othernam.sql", day.AddHours(11));
            await InsertLegacy(cs, "SELECT 4", "MonthlyReport.sql", day.AddHours(12));

            await new HistoryDatabase(path).InitializeAsync();   // triggers backfill

            var (sessions, unassigned) = await Counts(cs);
            Assert.Equal(3, sessions);      // two scratch groups + one file group
            Assert.Equal(0, unassigned);

            await using var c = new SqliteConnection(cs);
            await c.OpenAsync();
            await using var cmd = new SqliteCommand(
                "SELECT name FROM query_sessions ORDER BY ordinal", c);
            await using var r = await cmd.ExecuteReaderAsync();
            var names = new System.Collections.Generic.List<string>();
            while (await r.ReadAsync()) names.Add(r.GetString(0));

            Assert.Contains("query-01", names);
            Assert.Contains("query-02", names);
            Assert.Contains("MonthlyReport.sql", names);
        }
        finally { CleanupDb(path); }
    }

    [Fact]
    public async Task Backfill_is_idempotent()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();
            await InsertLegacy(cs, "SELECT 1", "dwnhdxfq.sql", DateTime.Today.AddDays(-1).AddHours(9));

            await new HistoryDatabase(path).InitializeAsync();
            var first = await Counts(cs);
            await new HistoryDatabase(path).InitializeAsync();
            var second = await Counts(cs);

            Assert.Equal(first.Sessions, second.Sessions);   // no renumbering, no duplicates
            Assert.Equal(0, second.Unassigned);
        }
        finally { CleanupDb(path); }
    }

    /// <summary>
    /// Forces a genuine SQLITE_BUSY on BackfillSessionsAsync's own BEGIN IMMEDIATE by holding the
    /// write lock from a second connection, and asserts InitializeAsync's whole call chain SWALLOWS
    /// that failure (does not throw) and leaves the row exactly as it was (still
    /// <c>session_id IS NULL</c>), ready for the next InitializeAsync to retry.
    ///
    /// <para>
    /// This deliberately invokes the private BackfillSessionsAsync directly (via reflection) rather
    /// than going through the public InitializeAsync entry point on a pre-populated database. An
    /// earlier throwaway probe (run before this fix, then deleted) held the write lock across a
    /// WHOLE InitializeAsync call and found that an EARLIER, unrelated statement throws first: the
    /// unconditional <c>INSERT OR IGNORE INTO metadata (key, value) VALUES ('schema_version', ...)</c>
    /// near the top of InitializeCoreAsync also needs the write lock (SQLite must open a write
    /// context to attempt the insert and detect the conflict, even though the row already exists and
    /// the statement is a no-op) and is NOT wrapped in any busy-aware catch — confirmed via the
    /// thrown exception's stack trace pointing at that exact line (HistoryDatabase.cs, the "Insert
    /// schema_version if not present" step). That is a separate, pre-existing gap outside this fix
    /// round's scope (only BackfillSessionsAsync's own transaction acquisition was flagged for this
    /// fix). Going through InitializeAsync here would make this test pass or fail based on THAT
    /// unrelated statement instead of the one actually being fixed, so instead this test opens its
    /// own connection and invokes BackfillSessionsAsync on it in isolation.
    /// </para>
    ///
    /// <para>
    /// The isolated connection uses a short <c>Default Timeout=1</c> (ADO.NET-level retry ceiling),
    /// NOT the production connection string. A first pass at this test used the production-realistic
    /// setup (plain connection string + the same <c>PRAGMA busy_timeout=5000</c> InitializeCoreAsync
    /// issues) and held the external lock for 5.3 seconds before releasing — that consistently let
    /// BEGIN IMMEDIATE succeed anyway once the lock freed, because Microsoft.Data.Sqlite's own
    /// managed-level retry ceiling (governed by the connection string's "Default Timeout", which
    /// defaults to ~30s when unset) turned out to be the operative bound on wall-clock retry
    /// duration, not the 5000ms PRAGMA value — confirmed empirically: without a "Default Timeout"
    /// override, a held lock survived past 7+ seconds with BEGIN IMMEDIATE still silently retrying;
    /// with "Default Timeout=1", it reliably threw SQLITE_BUSY around 5.6s. "Default Timeout=1" here
    /// is purely a test-time affordance to force the SAME exception type/code deterministically and
    /// quickly (same technique QuerySessionStoreTests' forced-BUSY test uses); it is not a production
    /// behavior change.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Backfill_swallows_a_busy_lock_and_leaves_rows_ready_for_the_next_run()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();
            await InsertLegacy(cs, "SELECT 1", "dwnhdxfq.sql", DateTime.Today.AddDays(-1).AddHours(9));

            // Hold the write lock from a second connection — BEGIN IMMEDIATE acquires it right
            // away, same technique as QuerySessionStoreTests' forced-collision test.
            await using var blocker = new SqliteConnection(cs);
            await blocker.OpenAsync();
            var blockerTx = blocker.BeginTransaction(deferred: false);

            // Isolated connection for the direct BackfillSessionsAsync call — short Default Timeout
            // so the forced SQLITE_BUSY surfaces within a few seconds (see the class doc above).
            await using var conn = new SqliteConnection($"{cs};Default Timeout=1");
            await conn.OpenAsync();
            await using (var pragmaCmd = new SqliteCommand("PRAGMA busy_timeout=5000;", conn))
                await pragmaCmd.ExecuteNonQueryAsync();

            var backfillMethod = typeof(HistoryDatabase).GetMethod(
                "BackfillSessionsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(backfillMethod);

            var db = new HistoryDatabase(path);
            // Task.Run so BeginTransaction's synchronous busy-wait blocks a background thread, not
            // this test method's own thread.
            var backfillTask = Task.Run(() => (Task)backfillMethod!.Invoke(db, new object[] { conn })!);

            await backfillTask;   // must complete WITHOUT throwing, once BEGIN IMMEDIATE gives up

            await blockerTx.RollbackAsync();   // release the lock we no longer need

            var (sessions, unassigned) = await Counts(cs);
            Assert.Equal(0, sessions);     // nothing was created — the attempt was abandoned
            Assert.Equal(1, unassigned);   // row untouched, ready for the next InitializeAsync
        }
        finally { CleanupDb(path); }
    }

    /// <summary>
    /// Minor fix 5 (final review wave): SQLite's <c>date(substr(executed_at, 1, 19), 'localtime')</c>
    /// returns NULL for a malformed/unparseable executed_at (confirmed empirically — it does not
    /// throw at the SQL level). Before this fix, the backfill's group-listing loop called
    /// <c>r.GetString(0)</c> unconditionally on that column, which throws InvalidCastException on a
    /// NULL value — and because the WHOLE backfill runs inside one transaction, that exception
    /// aborted the entire run and left every OTHER, well-formed group ungrouped too, on every future
    /// engine start (the migration always retries from scratch). One bad row must not be able to
    /// permanently block the rest.
    /// </summary>
    [Fact]
    public async Task Backfill_skips_a_group_with_unparseable_executed_at_but_still_groups_the_rest()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();

            // A well-formed legacy row that SHOULD get grouped...
            await InsertLegacy(cs, "SELECT 1", "dwnhdxfq.sql", DateTime.Today.AddDays(-1).AddHours(9));

            // ...alongside one row whose executed_at cannot be parsed as a date/time at all
            // (simulates truncated/corrupted data).
            await using (var c = new SqliteConnection(cs))
            {
                await c.OpenAsync();
                await using var cmd = new SqliteCommand(@"
                    INSERT INTO history (sql_text, truncated, server, database_name, username,
                                         executed_at, duration_ms, row_count, status, error_msg,
                                         source, tab_title, content_hash, is_favorite)
                    VALUES ('SELECT 2', 0, '(local)', 'aqmar', NULL,
                            'not-a-date', 1, 1, 0, NULL, NULL, NULL, @hash, 0);", c);
                cmd.Parameters.AddWithValue("@hash", HistoryDatabase.ComputeContentHash("SELECT 2"));
                await cmd.ExecuteNonQueryAsync();
            }

            await new HistoryDatabase(path).InitializeAsync();   // triggers backfill; must NOT throw

            var (sessions, unassigned) = await Counts(cs);
            Assert.Equal(1, sessions);      // the well-formed row's group WAS created
            Assert.Equal(1, unassigned);    // the malformed row stays ungrouped, not blocking the rest
        }
        finally { CleanupDb(path); }
    }

    /// <summary>Real (non-scratch) legacy tab titles are trimmed for the session's display name,
    /// matching QuerySessionStore.InsertAsync's `tabTitle!.Trim()` — a stray-whitespace tab_title
    /// should not produce a cosmetically inconsistent session name.</summary>
    [Fact]
    public async Task Backfill_trims_whitespace_from_real_filename_group_names()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();
            await InsertLegacy(cs, "SELECT 1", "  MonthlyReport.sql  ", DateTime.Today.AddDays(-1).AddHours(9));

            await new HistoryDatabase(path).InitializeAsync();   // triggers backfill

            var (sessions, unassigned) = await Counts(cs);
            Assert.Equal(1, sessions);
            Assert.Equal(0, unassigned);

            await using var c = new SqliteConnection(cs);
            await c.OpenAsync();
            await using var cmd = new SqliteCommand("SELECT name FROM query_sessions", c);
            var name = (string)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal("MonthlyReport.sql", name);
        }
        finally { CleanupDb(path); }
    }
}
