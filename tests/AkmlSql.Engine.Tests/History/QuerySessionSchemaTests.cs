using System.IO;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class QuerySessionSchemaTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"akml-hist-{System.Guid.NewGuid():N}.db");

    [Fact]
    public async Task Initialize_creates_query_sessions_and_session_id_column()
    {
        var path = TempDbPath();
        try
        {
            var db = new HistoryDatabase(path);
            await db.InitializeAsync();
            db.Dispose();

            await using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                await conn.OpenAsync();

                Assert.True(await TableExists(conn, "query_sessions"));
                Assert.True(await ColumnExists(conn, "history", "session_id"));
                Assert.True(await IndexExists(conn, "IX_qs_session_key"));
                Assert.True(await IndexExists(conn, "IX_qs_date_ordinal"));
                Assert.True(await IndexExists(conn, "IX_history_session"));

                // Verify UNIQUE constraint on session_key
                Assert.True(await IndexIsUnique(conn, "IX_qs_session_key"));

                // Verify UNIQUE constraint on (local_date, ordinal)
                Assert.True(await IndexIsUnique(conn, "IX_qs_date_ordinal"));
            }
        }
        finally
        {
            // Connection is now disposed, safe to delete
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            SafeDeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        var path = TempDbPath();
        try
        {
            var db = new HistoryDatabase(path);
            await db.InitializeAsync();
            await db.InitializeAsync();   // must not throw on the ALTER TABLE
            db.Dispose();

            await using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                await conn.OpenAsync();
                Assert.True(await ColumnExists(conn, "history", "session_id"));
            }
        }
        finally
        {
            // Connection is now disposed, safe to delete
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            SafeDeleteDatabaseFiles(path);
        }
    }

    private static async Task<bool> TableExists(SqliteConnection c, string name)
    {
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n", c);
        cmd.Parameters.AddWithValue("@n", name);
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> IndexExists(SqliteConnection c, string name)
    {
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@n", c);
        cmd.Parameters.AddWithValue("@n", name);
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ColumnExists(SqliteConnection c, string table, string column)
    {
        await using var cmd = new SqliteCommand($"PRAGMA table_info({table});", c);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            if (string.Equals(r.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static async Task<bool> IndexIsUnique(SqliteConnection c, string indexName)
    {
        // PRAGMA index_list returns: seq, name, unique, origin, partial
        // For query_sessions table, check if the index on it is marked as unique (column 2 = 1)
        await using var cmd = new SqliteCommand("PRAGMA index_list(query_sessions);", c);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.GetString(1);
            var unique = r.GetInt32(2);
            if (string.Equals(name, indexName, System.StringComparison.OrdinalIgnoreCase))
                return unique == 1;
        }
        return false;
    }

    private static void SafeDeleteDatabaseFiles(string path)
    {
        // Delete the main database file
        try { File.Delete(path); } catch { }

        // Delete WAL and SHM files that SQLite creates
        try { File.Delete(path + "-wal"); } catch { }
        try { File.Delete(path + "-shm"); } catch { }
    }
}
