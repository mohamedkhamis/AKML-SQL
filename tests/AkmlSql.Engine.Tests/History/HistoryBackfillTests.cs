using System;
using System.Globalization;
using System.IO;
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
}
