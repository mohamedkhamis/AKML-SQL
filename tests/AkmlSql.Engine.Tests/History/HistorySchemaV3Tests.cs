using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T049, HIS-05, data-model §2.1) — upgrading a schema-v2 history database: timestamps
/// normalised to ISO "o", orphan versions removed, the full-text index rebuilt and kept in step
/// from then on (the new update trigger), and the <c>open_pid</c> column added. Idempotent.
/// </summary>
public sealed class HistorySchemaV3Tests : IAsyncLifetime
{
    private static readonly Regex IsoO = new Regex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$");
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = HistoryTestDb.Uninitialised("akml-v3");
        await CreateV2DatabaseAsync(_db.ConnectionString);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>The v2 schema exactly as the previous engine created it (no open_pid, no update trigger).</summary>
    private static async Task CreateV2DatabaseAsync(string connectionString)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            // This SQLite build enforces foreign keys by default; a v2 database could still hold
            // orphans (rows deleted by clients that never turned enforcement on).
            "PRAGMA foreign_keys = OFF;",
            "CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);",
            "INSERT INTO metadata (key, value) VALUES ('schema_version', '2');",
            @"CREATE TABLE history (
                id INTEGER PRIMARY KEY AUTOINCREMENT, sql_text TEXT NOT NULL, truncated INTEGER NOT NULL DEFAULT 0,
                server TEXT, database_name TEXT, username TEXT, executed_at TEXT NOT NULL, duration_ms INTEGER NOT NULL,
                row_count INTEGER NOT NULL DEFAULT 0, status INTEGER NOT NULL, error_msg TEXT, source TEXT, tab_title TEXT,
                content_hash TEXT NOT NULL, is_favorite INTEGER NOT NULL DEFAULT 0, is_open INTEGER NOT NULL DEFAULT 0);",
            @"CREATE TABLE query_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT, session_key TEXT NOT NULL, local_date TEXT NOT NULL,
                ordinal INTEGER NOT NULL, name TEXT NOT NULL, name_source INTEGER NOT NULL, server TEXT,
                database_name TEXT, created_at TEXT NOT NULL);",
            "ALTER TABLE history ADD COLUMN session_id INTEGER REFERENCES query_sessions(id);",
            "CREATE VIRTUAL TABLE history_fts USING fts5(sql_text, content='history', content_rowid='id');",
            @"CREATE TRIGGER history_ai AFTER INSERT ON history BEGIN
                INSERT INTO history_fts(rowid, sql_text) VALUES (new.id, new.sql_text); END;",
            @"CREATE TRIGGER history_ad AFTER DELETE ON history BEGIN
                INSERT INTO history_fts(history_fts, rowid, sql_text) VALUES ('delete', old.id, old.sql_text); END;",
            @"CREATE TABLE history_versions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                history_id INTEGER NOT NULL REFERENCES history(id) ON DELETE CASCADE,
                sql_text TEXT NOT NULL, saved_at TEXT NOT NULL DEFAULT (datetime('now')));",
            // A v2 snapshot rewrote executed_at in the space format and changed sql_text with no FTS update.
            @"INSERT INTO history (sql_text, server, database_name, executed_at, duration_ms, status, content_hash)
                VALUES ('SELECT alpha', 'srv1', 'Northwind', '2026-09-20 10:15:30', 5, 0, 'h1');",
            @"INSERT INTO history (sql_text, server, database_name, executed_at, duration_ms, status, content_hash)
                VALUES ('SELECT beta', 'srv1', 'Northwind', '2026-09-21T08:00:00.0000000Z', 5, 0, 'h2');",
            "UPDATE history SET sql_text = 'SELECT gamma' WHERE id = 1;",
            "INSERT INTO history_versions (history_id, sql_text, saved_at) VALUES (1, 'SELECT gamma', '2026-09-20 10:15:30');",
            // An orphan: its history row no longer exists.
            "INSERT INTO history_versions (history_id, sql_text, saved_at) VALUES (999, 'SELECT orphan', '2026-09-19 09:00:00');",
        })
        {
            await using var cmd = new SqliteCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<string>> ColumnAsync(string sql)
    {
        var values = new List<string>();
        await using var conn = new SqliteConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) values.Add(r.IsDBNull(0) ? "<null>" : Convert.ToString(r.GetValue(0))!);
        return values;
    }

    [Fact]
    public async Task Upgrade_repairs_timestamps_orphans_and_the_search_index()
    {
        await _db.Database.InitializeAsync();

        Assert.All(await ColumnAsync("SELECT executed_at FROM history"), v => Assert.Matches(IsoO, v));
        Assert.All(await ColumnAsync("SELECT saved_at FROM history_versions"), v => Assert.Matches(IsoO, v));
        Assert.Equal("2026-09-20T10:15:30.0000000Z", (await ColumnAsync("SELECT executed_at FROM history WHERE id = 1"))[0]);

        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history_versions WHERE history_id = 999"));

        await _db.ExecAsync("INSERT INTO history_fts(history_fts) VALUES('integrity-check');");
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history_fts WHERE history_fts MATCH 'gamma'"));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history_fts WHERE history_fts MATCH 'alpha'"));

        Assert.Equal("1", (await ColumnAsync("SELECT value FROM metadata WHERE key = 'history_v3'"))[0]);
        Assert.Equal("3", (await ColumnAsync("SELECT value FROM metadata WHERE key = 'schema_version'"))[0]);
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM pragma_table_info('history') WHERE name = 'open_pid'"));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'history_au'"));
    }

    [Fact]
    public async Task The_update_trigger_keeps_the_search_index_in_step()
    {
        await _db.Database.InitializeAsync();

        await _db.ExecAsync("UPDATE history SET sql_text = 'SELECT delta' WHERE id = 2;");

        await _db.ExecAsync("INSERT INTO history_fts(history_fts) VALUES('integrity-check');");
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history_fts WHERE history_fts MATCH 'delta'"));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history_fts WHERE history_fts MATCH 'beta'"));
    }

    [Fact]
    public async Task A_second_initialisation_changes_nothing()
    {
        await _db.Database.InitializeAsync();
        var history = await ColumnAsync("SELECT id || '|' || executed_at || '|' || sql_text FROM history ORDER BY id");
        var versions = await ColumnAsync("SELECT id || '|' || saved_at || '|' || sql_text FROM history_versions ORDER BY id");

        await _db.Database.InitializeAsync();

        Assert.Equal(history, await ColumnAsync("SELECT id || '|' || executed_at || '|' || sql_text FROM history ORDER BY id"));
        Assert.Equal(versions, await ColumnAsync("SELECT id || '|' || saved_at || '|' || sql_text FROM history_versions ORDER BY id"));
    }
}
