using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Covers <c>HistoryDatabase.CorrectMisclassifiedScratchNamesAsync</c> — the one-time repair for
/// <c>query_sessions</c> rows that were misclassified as genuine filenames (<c>name_source = 1</c>)
/// by the PRE-FIX single-dot scratch-name regex, before it was widened to accept one-or-more dots
/// (see <see cref="QuerySessionNamerTests"/>). <see cref="HistoryDatabase.BackfillSessionsAsync"/>
/// cannot repair these itself — it only ever considers <c>history</c> rows with
/// <c>session_id IS NULL</c>, and every affected row already has a session assigned.
///
/// <para>
/// TEST-SETUP NOTE: the corrective pass is guarded by a <c>metadata</c> flag row
/// ('scratch_name_correction_v1') written UNCONDITIONALLY on its first run — including a run
/// against a brand-new, empty database (0 candidates is still "done": a fresh install can never
/// have pre-fix legacy data, so there's nothing to repair, and it must never re-scan on every
/// later startup). The very first <see cref="HistoryDatabase.InitializeAsync"/> call in each test
/// below (used only to create the schema) therefore also sets that flag. Tests that need the pass
/// to actually run against SEEDED bad data delete that flag row afterward — this reproduces the
/// real deployment shape exactly: an existing database, upgraded from a build that predates the
/// flag concept entirely, with legacy query_sessions rows already sitting in it.
/// </para>
/// </summary>
public class ScratchNameCorrectionTests
{
    private const string FlagKey = "scratch_name_correction_v1";

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"akml-snc-{Guid.NewGuid():N}.db");

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

    private static async Task DeleteFlagAsync(string cs)
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand("DELETE FROM metadata WHERE key = @k;", c);
        cmd.Parameters.AddWithValue("@k", FlagKey);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> FlagPresentAsync(string cs)
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM metadata WHERE key = @k;", c);
        cmd.Parameters.AddWithValue("@k", FlagKey);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task InsertSessionAsync(
        string cs, string sessionKey, string localDate, int ordinal,
        string name, int nameSource)
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(@"
            INSERT INTO query_sessions
                (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
            VALUES (@key, @d, @ord, @name, @src, NULL, NULL, @created);", c);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        cmd.Parameters.AddWithValue("@d", localDate);
        cmd.Parameters.AddWithValue("@ord", ordinal);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@src", nameSource);
        cmd.Parameters.AddWithValue("@created",
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(string Name, int NameSource)> ReadSessionAsync(string cs, string sessionKey)
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT name, name_source FROM query_sessions WHERE session_key = @key;", c);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.GetString(0), r.GetInt32(1));
    }

    [Fact]
    public async Task Corrective_pass_renames_only_the_misclassified_scratch_sessions()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            // Schema-only init. This also sets the flag against an empty database (see class doc);
            // delete it below to simulate a pre-existing (never-yet-corrected) database.
            await new HistoryDatabase(path).InitializeAsync();
            await DeleteFlagAsync(cs);

            // Four sessions, same local day, distinct ordinals (IX_qs_date_ordinal is UNIQUE).
            const string day = "2026-08-01";

            // A: already auto-named (name_source = 0) — untouched either way, not a candidate.
            await InsertSessionAsync(cs, "sess-A", day, 5, "query-05", 0);

            // B: THE BUG — misclassified as a real filename under the old single-dot regex.
            // Its own ordinal is 7, so a correct repair renames it to "query-07".
            await InsertSessionAsync(cs, "sess-B", day, 7, "epxoezf5..sql", 1);

            // C: a genuine saved filename, also name_source = 1 — must stay exactly as-is.
            await InsertSessionAsync(cs, "sess-C", day, 9, "MonthlyReport.sql", 1);

            // D: a user's MANUAL rename (name_source = 2) whose name, by unlucky coincidence,
            // also matches the scratch pattern. Manual renames are final regardless of what the
            // stored name looks like — the corrective pass's WHERE clause must never select
            // name_source = 2 at all.
            await InsertSessionAsync(cs, "sess-D", day, 11, "km1kjagk..sql", 2);

            await new HistoryDatabase(path).InitializeAsync();   // runs the corrective pass

            var a = await ReadSessionAsync(cs, "sess-A");
            var b = await ReadSessionAsync(cs, "sess-B");
            var c = await ReadSessionAsync(cs, "sess-C");
            var d = await ReadSessionAsync(cs, "sess-D");

            Assert.Equal(("query-05", 0), a);              // unaffected
            Assert.Equal(("query-07", 0), b);               // FIXED: own ordinal, now auto
            Assert.Equal(("MonthlyReport.sql", 1), c);      // untouched — genuine filename
            Assert.Equal(("km1kjagk..sql", 2), d);          // untouched — manual rename is final

            Assert.True(await FlagPresentAsync(cs));
        }
        finally { CleanupDb(path); }
    }

    /// <summary>
    /// Guard check: once the flag is set, the pass must never scan again, even if a row looks
    /// exactly like an unfixed candidate. Directly reverts the already-corrected session B back to
    /// its pre-fix (name_source = 1, scratch name) shape after the first successful run, then
    /// re-initializes — if the flag did NOT hold, this second run would "fix" it right back to
    /// query-07, so this is a real (falsifiable), not merely a repeat-of-a-no-op, assertion.
    /// </summary>
    [Fact]
    public async Task Corrective_pass_runs_at_most_once_even_if_a_later_row_looks_uncorrected()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();
            await DeleteFlagAsync(cs);

            const string day = "2026-08-01";
            await InsertSessionAsync(cs, "sess-B", day, 7, "epxoezf5..sql", 1);

            await new HistoryDatabase(path).InitializeAsync();   // first real run: fixes sess-B
            Assert.Equal(("query-07", 0), await ReadSessionAsync(cs, "sess-B"));
            Assert.True(await FlagPresentAsync(cs));

            // Revert sess-B to its pre-fix shape WITHOUT touching the flag.
            await using (var c = new SqliteConnection(cs))
            {
                await c.OpenAsync();
                await using var cmd = new SqliteCommand(
                    "UPDATE query_sessions SET name = 'epxoezf5..sql', name_source = 1 " +
                    "WHERE session_key = 'sess-B';", c);
                await cmd.ExecuteNonQueryAsync();
            }

            await new HistoryDatabase(path).InitializeAsync();   // must be a no-op: flag still set

            Assert.Equal(("epxoezf5..sql", 1), await ReadSessionAsync(cs, "sess-B"));
        }
        finally { CleanupDb(path); }
    }
}
