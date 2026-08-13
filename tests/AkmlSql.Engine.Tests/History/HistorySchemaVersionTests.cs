using System;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Minor fix 1 (final review wave): <c>metadata.schema_version</c> was bumped to '2' in code but
/// only ever written via <c>INSERT OR IGNORE</c>, so a database first created under an older schema
/// version kept its ORIGINAL stamped value forever, no matter how many times a newer engine build
/// reopened it. Inert today (nothing reads the value), but a future version-gated migration keyed on
/// it would misfire. The write is now an UPSERT that always reflects the CURRENT build's
/// <c>SchemaVersion</c> on every <c>InitializeAsync</c>.
/// </summary>
public class HistorySchemaVersionTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"akml-schemaver-{Guid.NewGuid():N}.db");

    private static void CleanupDb(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Reopening_an_older_stamped_database_upgrades_the_stored_version()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();

            // Simulate a database first created under an older engine build (schema_version='1'),
            // the way INSERT OR IGNORE would have left it stamped forever pre-fix.
            await using (var c = new SqliteConnection(cs))
            {
                await c.OpenAsync();
                await using var cmd = new SqliteCommand(
                    "UPDATE metadata SET value = '1' WHERE key = 'schema_version';", c);
                await cmd.ExecuteNonQueryAsync();
            }

            await new HistoryDatabase(path).InitializeAsync();   // current build reopens it

            await using var conn = new SqliteConnection(cs);
            await conn.OpenAsync();
            await using var readCmd = new SqliteCommand(
                "SELECT value FROM metadata WHERE key = 'schema_version';", conn);
            var value = (string)(await readCmd.ExecuteScalarAsync())!;

            Assert.Equal("2", value);   // current SchemaVersion, not the stale '1'
        }
        finally { CleanupDb(path); }
    }
}
