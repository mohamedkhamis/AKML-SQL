using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AkmlSql.Engine.History;

/// <summary>
/// Manages the SQLite database that stores SQL execution history.
/// Uses WAL mode for concurrent read/write performance and FTS5 for full-text search.
/// </summary>
public sealed class HistoryDatabase : IDisposable
{
    private const int SchemaVersion = 3;   // v2: query_sessions + history.session_id; v3: open_pid, FTS update trigger, ISO timestamps (spec 040)
    private const int MaxSqlTextChars = 1_048_576; // 1 MB

    private readonly string _connectionString;
    private bool _disposed;

    public HistoryDatabase()
    {
        // Constants.AppDataPath honours AKML_APP_DATA_ROOT, so an engine built by a test never
        // opens (and migrates or trims) the user's real history database.
        var dbDir = Path.Combine(AkmlSql.Core.Constants.AppDataPath, "history");
        Directory.CreateDirectory(dbDir);

        var dbPath = Path.Combine(dbDir, "sqlhistory.db");
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Test-only constructor that targets a caller-supplied database file path instead of
    /// the per-user AppData location. Used by the engine test project (via InternalsVisibleTo)
    /// to isolate each test run in a temporary database.
    /// </summary>
    internal HistoryDatabase(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path must be provided.", nameof(dbPath));

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Initializes the database schema (tables, indexes, FTS5 virtual table, triggers).
    /// Safe to call multiple times — uses IF NOT EXISTS throughout.
    /// If the database is corrupted, renames the corrupted file and creates a fresh database.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await InitializeCoreAsync();
        }
        catch (SqliteException ex) when (IsCorruptionError(ex))
        {
            Log.Warning(ex, "HistoryDatabase: corruption detected, recovering by creating a fresh database");
            await HandleCorruptionAsync();
            // Retry with a fresh database
            await InitializeCoreAsync();
        }
    }

    /// <summary>
    /// Core initialization logic — opens the database and creates schema.
    /// Separated from <see cref="InitializeAsync"/> to allow retry after corruption recovery.
    /// </summary>
    private async Task InitializeCoreAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Run an integrity check on first access to detect corruption early
        await using (var integrityCmd = new SqliteCommand("PRAGMA integrity_check(1);", conn))
        {
            var result = (await integrityCmd.ExecuteScalarAsync())?.ToString();
            if (result != null && !result.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new SqliteException("Database integrity check failed: " + result, 11 /* SQLITE_CORRUPT */);
            }
        }

        // Set pragmas for performance and safety
        await ExecuteNonQueryAsync(conn, "PRAGMA journal_mode=WAL;");
        await ExecuteNonQueryAsync(conn, "PRAGMA busy_timeout=5000;");
        await ExecuteNonQueryAsync(conn, "PRAGMA foreign_keys=ON;");

        // Create metadata table for schema versioning
        await ExecuteNonQueryAsync(conn, @"
            CREATE TABLE IF NOT EXISTS metadata (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );");

        // Upsert schema_version so an upgraded database always reflects the CURRENT SchemaVersion.
        // INSERT OR IGNORE (the prior form) only ever wrote the version once, on first creation —
        // a database created under schema v1 and later opened by this v2 build would keep the
        // literal string '1' in metadata forever, which would misfire the first time some future
        // change actually gates behaviour on this value.
        await ExecuteNonQueryAsync(conn, $@"
            INSERT INTO metadata (key, value) VALUES ('schema_version', '{SchemaVersion}')
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;");

        // Create main history table
        await ExecuteNonQueryAsync(conn, @"
            CREATE TABLE IF NOT EXISTS history (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                sql_text      TEXT    NOT NULL,
                truncated     INTEGER NOT NULL DEFAULT 0,
                server        TEXT,
                database_name TEXT,
                username      TEXT,
                executed_at   TEXT    NOT NULL,
                duration_ms   INTEGER NOT NULL,
                row_count     INTEGER NOT NULL DEFAULT 0,
                status        INTEGER NOT NULL,
                error_msg     TEXT,
                source        TEXT,
                tab_title     TEXT,
                content_hash  TEXT    NOT NULL,
                is_favorite   INTEGER NOT NULL DEFAULT 0,
                is_open       INTEGER NOT NULL DEFAULT 0
            );");

        // Create indexes for common query patterns
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_executed_at ON history (executed_at);");
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_content_hash ON history (content_hash);");
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_server_db ON history (server, database_name);");
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_status ON history (status);");
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_is_open ON history (is_open);");

        // Schema migration: add is_open column for existing databases
        try
        {
            await ExecuteNonQueryAsync(conn,
                "ALTER TABLE history ADD COLUMN is_open INTEGER NOT NULL DEFAULT 0;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Column already exists — expected for fresh databases or re-runs
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "History: is_open column migration failed (non-fatal)");
        }

        // ── Schema v2: query-session grouping ────────────────────────────────
        // One row per editor-tab query session. The display name lives HERE, not on the
        // history rows, so a rename is a single UPDATE and survives every later execution.
        await ExecuteNonQueryAsync(conn, @"
            CREATE TABLE IF NOT EXISTS query_sessions (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                session_key   TEXT    NOT NULL,
                local_date    TEXT    NOT NULL,
                ordinal       INTEGER NOT NULL,
                name          TEXT    NOT NULL,
                name_source   INTEGER NOT NULL,
                server        TEXT,
                database_name TEXT,
                created_at    TEXT    NOT NULL
            );");

        await ExecuteNonQueryAsync(conn,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_qs_session_key ON query_sessions (session_key);");
        // Backstop for the ordinal race: two shell windows can read the same MAX(ordinal).
        await ExecuteNonQueryAsync(conn,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_qs_date_ordinal ON query_sessions (local_date, ordinal);");

        // Column BEFORE its index — an index cannot reference a column that does not exist yet.
        try
        {
            await ExecuteNonQueryAsync(conn,
                "ALTER TABLE history ADD COLUMN session_id INTEGER REFERENCES query_sessions(id);");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Already migrated — expected on every start after the first.
        }

        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_session ON history (session_id);");
        // Spec 040 (SC-009): the grouped view's key, so counting and paging groups stay fast at scale.
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_group ON history " +
            "(COALESCE(CAST(session_id AS TEXT), 'hash:' || content_hash), executed_at DESC, id DESC);");

        // Create FTS5 virtual table for full-text search on SQL text
        await ExecuteNonQueryAsync(conn, @"
            CREATE VIRTUAL TABLE IF NOT EXISTS history_fts
            USING fts5(sql_text, content='history', content_rowid='id');");

        // Create triggers to keep FTS index in sync with the history table
        await ExecuteNonQueryAsync(conn, @"
            CREATE TRIGGER IF NOT EXISTS history_ai AFTER INSERT ON history BEGIN
                INSERT INTO history_fts(rowid, sql_text) VALUES (new.id, new.sql_text);
            END;");

        await ExecuteNonQueryAsync(conn, @"
            CREATE TRIGGER IF NOT EXISTS history_ad AFTER DELETE ON history BEGIN
                INSERT INTO history_fts(history_fts, rowid, sql_text) VALUES ('delete', old.id, old.sql_text);
            END;");

        // Create version history table for tracking SQL edits over time
        await ExecuteNonQueryAsync(conn, @"
            CREATE TABLE IF NOT EXISTS history_versions (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                history_id INTEGER NOT NULL REFERENCES history(id) ON DELETE CASCADE,
                sql_text   TEXT    NOT NULL,
                saved_at   TEXT    NOT NULL DEFAULT (datetime('now'))
            );");
        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_versions_history_id ON history_versions(history_id);");

        // ── Schema v3 (spec 040): open state per owning shell, FTS kept in step on updates ──
        // open_pid: the shell process whose editor holds the query open. NULL for web rows and
        // for rows no shell has claimed. Added with the same duplicate-column guard as v1/v2.
        await AddColumnIfMissingAsync(conn, "ALTER TABLE history ADD COLUMN open_pid INTEGER NULL;");
        // content_hash on snapshots, so a grouped row's version count can include them (the
        // Versions panel lists runs AND snapshots, de-duplicated by content).
        await AddColumnIfMissingAsync(conn, "ALTER TABLE history_versions ADD COLUMN content_hash TEXT NULL;");

        // A snapshot rewrites history.sql_text; without this trigger the FTS index kept the OLD
        // text, so search found replaced text and missed the current one (HIS-05).
        await ExecuteNonQueryAsync(conn, @"
            CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE OF sql_text ON history BEGIN
                INSERT INTO history_fts(history_fts, rowid, sql_text) VALUES ('delete', old.id, old.sql_text);
                INSERT INTO history_fts(rowid, sql_text) VALUES (new.id, new.sql_text);
            END;");

        Log.Information("History database initialized at {ConnectionString}", _connectionString);

        await RepairV3Async(conn);
        await BackfillSessionsAsync(conn);
        await CorrectMisclassifiedScratchNamesAsync(conn);
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection conn, string alterSql)
    {
        try
        {
            await ExecuteNonQueryAsync(conn, alterSql);
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Already migrated — expected on every start after the first.
        }
    }

    /// <summary>Metadata key guarding <see cref="RepairV3Async"/>.</summary>
    private const string V3RepairFlagKey = "history_v3";

    /// <summary>
    /// Spec 040 (HIS-05, data-model §2.1) — one-time repair of data written by v2 engines:
    /// <list type="number">
    /// <item><c>executed_at</c> and <c>history_versions.saved_at</c> written by SQLite's
    ///   <c>datetime('now')</c> ('YYYY-MM-DD HH:MM:SS') become the insert path's ISO "o" form, so
    ///   every comparison and sort on them is by real time;</item>
    /// <item>orphan <c>history_versions</c> (their row was deleted) are removed;</item>
    /// <item>snapshots get their <c>content_hash</c>;</item>
    /// <item>the full-text index is rebuilt, since v2 snapshots changed text without updating it.</item>
    /// </list>
    /// Runs under BEGIN IMMEDIATE with the flag written in the same transaction — the database is
    /// shared with the web engine and other SSMS windows, so a concurrent start either waits or
    /// fails this pass and retries it next start (the same self-healing story as the backfill).
    /// </summary>
    private async Task RepairV3Async(SqliteConnection conn)
    {
        await using (var probe = new SqliteCommand("SELECT COUNT(*) FROM metadata WHERE key = @k;", conn))
        {
            probe.Parameters.AddWithValue("@k", V3RepairFlagKey);
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) return;
        }

        try
        {
            await using var tx = conn.BeginTransaction(deferred: false);

            async Task RunAsync(string sql)
            {
                await using var cmd = new SqliteCommand(sql, conn, (SqliteTransaction)tx);
                await cmd.ExecuteNonQueryAsync();
            }

            await RunAsync(@"
                UPDATE history
                   SET executed_at = substr(executed_at, 1, 10) || 'T' || substr(executed_at, 12, 8) || '.0000000Z'
                 WHERE substr(executed_at, 11, 1) = ' ';");
            await RunAsync(@"
                UPDATE history_versions
                   SET saved_at = substr(saved_at, 1, 10) || 'T' || substr(saved_at, 12, 8) || '.0000000Z'
                 WHERE substr(saved_at, 11, 1) = ' ';");
            await RunAsync("DELETE FROM history_versions WHERE history_id NOT IN (SELECT id FROM history);");

            var unhashed = new List<(long Id, string Sql)>();
            await using (var cmd = new SqliteCommand(
                "SELECT id, sql_text FROM history_versions WHERE content_hash IS NULL;", conn, (SqliteTransaction)tx))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) unhashed.Add((r.GetInt64(0), r.GetString(1)));
            }
            foreach (var (id, sql) in unhashed)
            {
                await using var upd = new SqliteCommand(
                    "UPDATE history_versions SET content_hash = @h WHERE id = @id;", conn, (SqliteTransaction)tx);
                upd.Parameters.AddWithValue("@h", ComputeContentHash(sql));
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync();
            }

            await RunAsync("INSERT INTO history_fts(history_fts) VALUES('rebuild');");

            await using (var flag = new SqliteCommand(
                "INSERT INTO metadata (key, value) VALUES (@k, '1') ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                conn, (SqliteTransaction)tx))
            {
                flag.Parameters.AddWithValue("@k", V3RepairFlagKey);
                await flag.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            Log.Information("History: v3 repair done ({Snapshots} snapshot hashes computed, search index rebuilt)", unhashed.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "History: v3 repair failed; will retry on next start");
        }
    }

    /// <summary>
    /// One-time regrouping of rows written before session tracking existed. Those rows carry no
    /// tab identity, so a session is INFERRED from (local date, tab_title, server, database).
    /// Nothing is deleted and no column other than session_id is touched.
    ///
    /// <para>Idempotent: only rows with session_id IS NULL are considered, so a second run is a
    /// no-op and never renumbers an existing session.</para>
    ///
    /// <para>executed_at is stored as UTC ISO-8601 with 7 fractional digits, which SQLite's date
    /// functions will not parse; substr(...,1,19) trims it to 'YYYY-MM-DDTHH:MM:SS', which SQLite
    /// treats as UTC-naive, and 'localtime' then converts it to the user's day.</para>
    /// </summary>
    private async Task BackfillSessionsAsync(SqliteConnection conn)
    {
        await using (var probe = new SqliteCommand(
            "SELECT COUNT(*) FROM history WHERE session_id IS NULL", conn))
        {
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) == 0) return;
        }

        Log.Information("History: backfilling query sessions for legacy rows…");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // BEGIN IMMEDIATE for the same reason as QuerySessionStore.InsertAsync: this transaction
            // reads (the GROUP BY scan) before it writes, and the history database is SHARED — the
            // SSMS-paired engine and the web engine both open %AppData%/AKML SQL/history/sqlhistory.db.
            // A deferred transaction promoted after a concurrent commit fails with BUSY_SNAPSHOT, which
            // busy_timeout does not retry. Added 2026-08-13 after the Task 3 review surfaced the same
            // defect one task earlier.
            //
            // Acquired INSIDE this try, not before it: BEGIN IMMEDIATE takes the write lock right
            // away, so it can itself throw SQLITE_BUSY/SQLITE_LOCKED if a concurrent writer already
            // holds that lock past busy_timeout — the exact scenario BEGIN IMMEDIATE exists to guard
            // against is also the realistic trigger for this (the SSMS-paired engine and the web
            // engine both starting against the shared history db at once, both attempting this same
            // migration). Catching that here — instead of letting it escape InitializeAsync and take
            // down engine startup — is deliberate and safe: the migration is idempotent (rows stay
            // session_id IS NULL on any failure here), so the very next engine start simply retries
            // the whole backfill. This is a self-healing retry, not a silent data-loss swallow.
            //
            // No explicit rollback in the catch below: `await using` disposes an uncommitted
            // SqliteTransaction by rolling it back as the exception unwinds past this scope (same
            // rationale as QuerySessionStore.InsertAsync — an explicit RollbackAsync after CommitAsync
            // itself threw would run against an already-completed transaction and raise a second,
            // masking exception).
            await using var tx = conn.BeginTransaction(deferred: false);

            // Ordered so ordinals follow first-execution time within each local day.
            // Finding 3 (PR #249 review): executed_at is NOT uniformly formatted (see the class
            // doc on DeleteEntriesOlderThanAsync / SaveVersionBySourceAsync) -- most rows are ISO
            // 'o' (InsertEntryAsync) but SaveVersionBySourceAsync rewrites to a space-separated
            // form via datetime('now'). A raw MIN(executed_at) is a lexicographic string compare:
            // for two rows on the SAME calendar date, a space-format timestamp ALWAYS sorts below
            // any ISO ('T'-separated) one (' ' 0x20 < 'T' 0x54), regardless of actual time of day
            // -- so ordinals could be assigned out of true chronological order, breaking the
            // "query-01 is the day's first session" promise. datetime(substr(executed_at,1,19))
            // canonicalises both forms (same technique DeleteEntriesOlderThanAsync uses) so the
            // comparison is by real time, not by which format happened to write the row.
            var groups = new System.Collections.Generic.List<(string Date, string Title, string Server, string Db)>();
            await using (var cmd = new SqliteCommand(@"
                SELECT date(substr(executed_at, 1, 19), 'localtime') AS local_date,
                       COALESCE(tab_title, '')      AS title,
                       COALESCE(server, '')         AS server,
                       COALESCE(database_name, '')  AS db
                  FROM history
                 WHERE session_id IS NULL
                 GROUP BY local_date, title, server, db
                 ORDER BY local_date, MIN(datetime(substr(executed_at, 1, 19)));", conn, (SqliteTransaction)tx))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    // date(substr(executed_at, 1, 19), 'localtime') returns NULL for a row whose
                    // executed_at is malformed or too short to parse as a date/time. r.GetString(0)
                    // on a NULL column throws InvalidCastException, which — since the whole backfill
                    // is one transaction (see BEGIN IMMEDIATE above) — would abort the ENTIRE run and
                    // leave every other, well-formed group ungrouped too, on every future engine
                    // start (the migration always retries from scratch, so one bad row permanently
                    // blocks it). Skip just the bad group instead: it stays session_id IS NULL and
                    // falls back to per-content-hash grouping in SearchAsync's GroupKey.
                    if (r.IsDBNull(0))
                    {
                        Log.Warning(
                            "History: skipping backfill group with unparseable executed_at (title='{Title}')",
                            r.IsDBNull(1) ? "" : r.GetString(1));
                        continue;
                    }
                    groups.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
                }
            }

            var perDay = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            var created = 0;

            foreach (var g in groups)
            {
                // Synthetic key: stable, unique, and obviously not a client-issued GUID.
                var sessionKey = $"legacy:{g.Date}|{g.Title}|{g.Server}|{g.Db}";

                // A LATER backfill run can regroup a row into a (local_date, tab_title, server,
                // database) group a PREVIOUS run already created a session for -- e.g. a fresh
                // session_id-NULL row that arrives after the first backfill (InsertEntryAsync
                // stores session_id NULL whenever session resolution fails, or a pre-session
                // client sends no SessionKey at all). Rebuilding the identical synthetic key would
                // violate the UNIQUE index on session_key; since the WHOLE backfill is one
                // transaction, that failure would roll back every OTHER, well-formed group in THIS
                // run too, and the same collision would recur on every future start, leaving those
                // rows ungrouped forever (Finding 1, PR #249 review). Look the key up first and
                // REUSE the existing session instead of re-inserting -- this is not just a
                // workaround, it is the semantically correct answer: the same (local_date,
                // tab_title, server, database) IS the same inferred session, so a later row that
                // falls into it belongs there, not in a session of its own. This also makes the
                // backfill naturally resumable instead of self-poisoning.
                long? existingSessionId;
                await using (var find = new SqliteCommand(
                    "SELECT id FROM query_sessions WHERE session_key = @key;", conn, (SqliteTransaction)tx))
                {
                    find.Parameters.AddWithValue("@key", sessionKey);
                    var found = await find.ExecuteScalarAsync();
                    existingSessionId = found == null || found == DBNull.Value ? (long?)null : Convert.ToInt64(found);
                }

                long sessionId;
                if (existingSessionId.HasValue)
                {
                    // Reusing an existing session consumes no new ordinal — nothing else changes.
                    sessionId = existingSessionId.Value;
                }
                else
                {
                    await using (var maxCmd = new SqliteCommand(
                        "SELECT COALESCE(MAX(ordinal), 0) FROM query_sessions WHERE local_date = @d",
                        conn, (SqliteTransaction)tx))
                    {
                        maxCmd.Parameters.AddWithValue("@d", g.Date);
                        if (!perDay.ContainsKey(g.Date))
                            perDay[g.Date] = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());
                    }

                    var ordinal = ++perDay[g.Date];
                    var isScratch = QuerySessionNamer.IsScratchTabTitle(g.Title);
                    // Trim for display consistency with QuerySessionStore.InsertAsync's `tabTitle!.Trim()`
                    // (a legacy tab_title with stray whitespace would otherwise look cosmetically
                    // inconsistent). Only the DISPLAY name is trimmed; g.Title itself stays untouched so
                    // the later grouping UPDATE's WHERE clause still matches the raw tab_title exactly.
                    var name = isScratch ? QuerySessionNamer.FormatName(ordinal) : g.Title.Trim();

                    await using (var ins = new SqliteCommand(@"
                        INSERT INTO query_sessions
                            (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
                        VALUES (@key, @d, @ord, @name, @src, @server, @db, @created);
                        SELECT last_insert_rowid();", conn, (SqliteTransaction)tx))
                    {
                        ins.Parameters.AddWithValue("@key", sessionKey);
                        ins.Parameters.AddWithValue("@d", g.Date);
                        ins.Parameters.AddWithValue("@ord", ordinal);
                        ins.Parameters.AddWithValue("@name", name);
                        ins.Parameters.AddWithValue("@src", isScratch ? 0 : 1);
                        ins.Parameters.AddWithValue("@server", g.Server.Length == 0 ? DBNull.Value : g.Server);
                        ins.Parameters.AddWithValue("@db", g.Db.Length == 0 ? DBNull.Value : g.Db);
                        ins.Parameters.AddWithValue("@created",
                            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                        sessionId = Convert.ToInt64(await ins.ExecuteScalarAsync());
                    }

                    created++;
                }

                await using (var upd = new SqliteCommand(@"
                    UPDATE history
                       SET session_id = @sid
                     WHERE session_id IS NULL
                       AND date(substr(executed_at, 1, 19), 'localtime') = @d
                       AND COALESCE(tab_title, '')     = @title
                       AND COALESCE(server, '')        = @server
                       AND COALESCE(database_name, '') = @db;", conn, (SqliteTransaction)tx))
                {
                    upd.Parameters.AddWithValue("@sid", sessionId);
                    upd.Parameters.AddWithValue("@d", g.Date);
                    upd.Parameters.AddWithValue("@title", g.Title);
                    upd.Parameters.AddWithValue("@server", g.Server);
                    upd.Parameters.AddWithValue("@db", g.Db);
                    await upd.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();
            Log.Information("History: backfill created {Count} sessions in {Ms} ms",
                created, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Covers both a failed BEGIN IMMEDIATE (no transaction was ever opened — including a
            // busy/locked write-lock acquisition failure, see the comment above) and a failure during
            // the body (where `await using` has already rolled the transaction back while unwinding
            // to get here). Either way: legacy rows are untouched (still session_id IS NULL), so the
            // next InitializeAsync call retries this whole backfill from scratch.
            Log.Error(ex, "History: session backfill failed; legacy rows remain ungrouped");
        }
    }

    /// <summary>Metadata key guarding <see cref="CorrectMisclassifiedScratchNamesAsync"/> — see its
    /// doc comment. Value is informational only (a UTC timestamp); presence of the row is the
    /// entire guard.</summary>
    private const string ScratchNameCorrectionFlagKey = "scratch_name_correction_v1";

    /// <summary>
    /// One-time repair for <c>query_sessions</c> rows created by the PRE-FIX scratch-name regex
    /// (<c>^(SQLQuery\d+|[a-z0-9]{8})\.sql$</c>, which required EXACTLY one dot before "sql"). Real
    /// SSMS scratch-tab titles observed on a live database use TWO dots ("epxoezf5..sql"), which
    /// that regex never matched — so <see cref="BackfillSessionsAsync"/>, run under the old code,
    /// misclassified every such session as a genuine saved filename (<c>name_source = 1</c>) and
    /// left it with its meaningless scratch name instead of an auto query-NN name.
    ///
    /// <para>
    /// Simply re-running <see cref="BackfillSessionsAsync"/> after <see cref="QuerySessionNamer"/>'s
    /// regex was widened does NOT repair this: that method only ever considers <c>history</c> rows
    /// with <c>session_id IS NULL</c>, and every affected row already has a (wrongly-named) session
    /// assigned. This pass targets the already-created <c>query_sessions</c> rows directly, using
    /// the WIDENED (current) <see cref="QuerySessionNamer.IsScratchTabTitle"/> to re-classify names
    /// that were stored under the old, narrower one.
    /// </para>
    ///
    /// <para>
    /// Renaming reuses the session's OWN EXISTING <c>ordinal</c> — it is never recomputed. Because
    /// <c>(local_date, ordinal)</c> is already UNIQUE (<c>IX_qs_date_ordinal</c>), renaming session
    /// X on day D to <c>query-&lt;X's own ordinal&gt;</c> can never collide with a query-NN session
    /// already correctly named on that same day, and it preserves the per-day ordering the original
    /// backfill established (this session was, and remains, the Nth session created that day).
    /// </para>
    ///
    /// <para>
    /// Only <c>name_source = 1</c> rows are candidates, and only those whose CURRENT stored name
    /// still matches the (now-widened) scratch pattern — a genuine <c>name_source = 1</c> filename
    /// like "MonthlyReport.sql" is left untouched. <c>name_source = 2</c> (a user's manual rename)
    /// is never selected at all, regardless of what its name looks like: those are final. Corrected
    /// rows are set to <c>name_source = 0</c> (auto), matching what they always should have been.
    /// </para>
    ///
    /// <para>
    /// Guarded by the <c>metadata</c> row keyed <see cref="ScratchNameCorrectionFlagKey"/>, INSERTed
    /// in the SAME transaction as the renames — so a crash mid-pass leaves the flag absent (not
    /// half-written), and the WHOLE pass (not a half-applied one) retries on the next
    /// <see cref="InitializeAsync"/>, exactly like <see cref="BackfillSessionsAsync"/>'s own
    /// idempotency story. Once the flag is present, later starts skip the scan entirely — a repaired
    /// session is never re-examined, so a user's real filename typed with 8 lowercase-alphanumeric
    /// characters (the documented <see cref="QuerySessionNamer.IsScratchTabTitle"/> false positive)
    /// that happens to sit at <c>name_source = 1</c> right when this runs is a one-time risk, same as
    /// it already is for every other consumer of that heuristic — not a new exposure this pass adds.
    /// </para>
    /// </summary>
    private async Task CorrectMisclassifiedScratchNamesAsync(SqliteConnection conn)
    {
        await using (var probe = new SqliteCommand(
            "SELECT COUNT(*) FROM metadata WHERE key = @k;", conn))
        {
            probe.Parameters.AddWithValue("@k", ScratchNameCorrectionFlagKey);
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) > 0) return;
        }

        Log.Information("History: checking for query sessions misnamed before the scratch-name regex fix…");

        try
        {
            // BEGIN IMMEDIATE for the same reason as BackfillSessionsAsync directly above: this
            // transaction reads (the name_source = 1 scan) before it writes (the renames + flag
            // insert), and the history database is SHARED between the SSMS-paired engine and the
            // web engine. Acquired INSIDE the try for the same reason too — a busy/locked failure to
            // even open the transaction is exactly the concurrent-migration scenario this guards
            // against, and must be caught here rather than escape and take down engine startup.
            await using var tx = conn.BeginTransaction(deferred: false);

            var candidates = new List<(long Id, int Ordinal)>();
            await using (var cmd = new SqliteCommand(
                "SELECT id, ordinal, name FROM query_sessions WHERE name_source = 1;",
                conn, (SqliteTransaction)tx))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    if (QuerySessionNamer.IsScratchTabTitle(r.GetString(2)))
                        candidates.Add((r.GetInt64(0), r.GetInt32(1)));
                }
            }

            foreach (var (id, ordinal) in candidates)
            {
                await using var upd = new SqliteCommand(@"
                    UPDATE query_sessions
                       SET name = @name, name_source = 0
                     WHERE id = @id;", conn, (SqliteTransaction)tx);
                upd.Parameters.AddWithValue("@name", QuerySessionNamer.FormatName(ordinal));
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync();
            }

            await using (var flag = new SqliteCommand(
                "INSERT INTO metadata (key, value) VALUES (@k, @v) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;", conn, (SqliteTransaction)tx))
            {
                flag.Parameters.AddWithValue("@k", ScratchNameCorrectionFlagKey);
                flag.Parameters.AddWithValue("@v", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                await flag.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            Log.Information("History: scratch-name correction renamed {Count} session(s)", candidates.Count);
        }
        catch (Exception ex)
        {
            // Same rationale as BackfillSessionsAsync's catch: the flag is only written inside the
            // transaction that also does the renames (see the doc comment above), so any failure
            // here — including a failed BEGIN IMMEDIATE — leaves the flag absent and every session
            // untouched, ready for the next InitializeAsync to retry the whole pass. Log-and-continue,
            // not fatal: this must never block engine startup.
            Log.Error(ex, "History: scratch-name correction failed; will retry on next start");
        }
    }

    /// <summary>
    /// Determines whether a <see cref="SqliteException"/> indicates database corruption.
    /// Checks both the SQLite error code and the message text for known corruption indicators.
    /// </summary>
    private static bool IsCorruptionError(SqliteException ex)
    {
        // SQLite error code 11 = SQLITE_CORRUPT
        if (ex.SqliteErrorCode == 11)
            return true;

        var msg = ex.Message;
        return msg.Contains("malformed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("corrupt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not a database", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Handles a corrupted database by renaming the corrupted file to
    /// <c>sqlhistory.db.corrupt.{timestamp}</c> so a fresh database can be created.
    /// Also renames associated WAL and SHM files if they exist.
    /// </summary>
    private Task HandleCorruptionAsync()
    {
        var dbPath = ExtractDbPath();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var corruptPath = $"{dbPath}.corrupt.{timestamp}";

        try
        {
            if (File.Exists(dbPath))
            {
                File.Move(dbPath, corruptPath);
                Log.Warning("HistoryDatabase: corrupted database renamed to {CorruptPath}", corruptPath);
            }

            // Also rename WAL and SHM files if present
            var walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                File.Move(walPath, $"{corruptPath}-wal");
            }

            var shmPath = dbPath + "-shm";
            if (File.Exists(shmPath))
            {
                File.Move(shmPath, $"{corruptPath}-shm");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HistoryDatabase: failed to rename corrupted database file");
            // If we cannot rename, try to delete so InitializeCoreAsync can start fresh
            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
                var walPath = dbPath + "-wal";
                if (File.Exists(walPath)) File.Delete(walPath);
                var shmPath = dbPath + "-shm";
                if (File.Exists(shmPath)) File.Delete(shmPath);
            }
            catch (Exception deleteEx)
            {
                Log.Error(deleteEx, "HistoryDatabase: also failed to delete corrupted database file");
                throw; // Cannot recover
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Extracts the database file path from the connection string.
    /// </summary>
    private string ExtractDbPath()
    {
        // Connection string format: "Data Source=<path>"
        const string prefix = "Data Source=";
        if (_connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return _connectionString.Substring(prefix.Length);
        }
        // Fallback to default path
        return Path.Combine(AkmlSql.Core.Constants.AppDataPath, "history", "sqlhistory.db");
    }

    /// <summary>
    /// Inserts a new execution history entry and returns its auto-generated row ID.
    /// Computes SHA-256 content hash from whitespace-normalized, case-folded SQL text.
    /// Truncates SQL text at 1 MB if necessary.
    /// </summary>
    public async Task<long> InsertEntryAsync(
        string sqlText,
        bool truncated,
        string? server,
        string? database,
        string? username,
        long durationMs,
        long rowCount,
        int status,
        string? errorMessage,
        string? source,
        string? tabTitle,
        string? sessionKey = null)
    {
        // Truncate SQL text if over limit
        if (sqlText.Length > MaxSqlTextChars)
        {
            sqlText = sqlText.Substring(0, MaxSqlTextChars);
            truncated = true;
        }

        // Compute content hash from normalized SQL
        var contentHash = ComputeContentHash(sqlText);
        var executedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        // Resolve (or create) the query session BEFORE the insert, so session_id is written in
        // the same statement. A null/empty key means a client that predates session grouping;
        // the row is stored with session_id NULL and the backfill will infer one later.
        long? sessionId = null;
        if (!string.IsNullOrEmpty(sessionKey))
        {
            try
            {
                sessionId = await new QuerySessionStore(_connectionString)
                    .GetOrCreateAsync(sessionKey!, DateTime.UtcNow, tabTitle, server, database);
            }
            catch (Exception ex)
            {
                // History capture is best-effort and must never break query execution.
                Log.Warning(ex, "History: session resolution failed for key {Key}", sessionKey);
            }
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            const string sql = @"
                INSERT INTO history (
                    sql_text, truncated, server, database_name, username,
                    executed_at, duration_ms, row_count, status, error_msg,
                    source, tab_title, content_hash, is_favorite, session_id
                ) VALUES (
                    @sqlText, @truncated, @server, @database, @username,
                    @executedAt, @durationMs, @rowCount, @status, @errorMsg,
                    @source, @tabTitle, @contentHash, 0, @sessionId
                );
                SELECT last_insert_rowid();";

            await using var cmd = new SqliteCommand(sql, conn, (SqliteTransaction)transaction);
            cmd.Parameters.AddWithValue("@sqlText", sqlText);
            cmd.Parameters.AddWithValue("@truncated", truncated ? 1 : 0);
            cmd.Parameters.AddWithValue("@server", (object?)server ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@database", (object?)database ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@username", (object?)username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@executedAt", executedAt);
            cmd.Parameters.AddWithValue("@durationMs", durationMs);
            cmd.Parameters.AddWithValue("@rowCount", rowCount);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@errorMsg", (object?)errorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source", (object?)source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tabTitle", (object?)tabTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@contentHash", contentHash);
            cmd.Parameters.AddWithValue("@sessionId", (object?)sessionId ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            var entryId = Convert.ToInt64(result);

            // Spec 040 (HIS-14, data-model §2.1): a real run replaces the session's drafts; their
            // text stays as versions of the query.
            if (sessionId.HasValue && status != (int)ExecutionStatus.NotExecuted)
                await AbsorbDraftsAsync(conn, (SqliteTransaction)transaction, sessionId.Value, entryId, contentHash);

            await transaction.CommitAsync();

            Log.Debug("History entry inserted: Id={EntryId}, Hash={Hash}, Duration={Duration}ms",
                entryId, contentHash, durationMs);

            return entryId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Moves the session's draft rows (status 3) under the run <paramref name="runId"/>: each
    /// draft's text becomes a version (unless it is the run's own text), its versions follow, and
    /// the draft row goes.
    /// </summary>
    private static async Task AbsorbDraftsAsync(SqliteConnection conn, SqliteTransaction tx, long sessionId, long runId, string runHash)
    {
        var drafts = new List<(long Id, string Sql, string At, string Hash)>();
        await using (var select = new SqliteCommand(
            "SELECT id, sql_text, executed_at, content_hash FROM history WHERE session_id = @sid AND status = 3 AND id <> @run;", conn, tx))
        {
            select.Parameters.AddWithValue("@sid", sessionId);
            select.Parameters.AddWithValue("@run", runId);
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                drafts.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        foreach (var draft in drafts)
        {
            if (!string.Equals(draft.Hash, runHash, StringComparison.Ordinal))
            {
                await using var version = new SqliteCommand(
                    "INSERT INTO history_versions (history_id, sql_text, saved_at, content_hash) VALUES (@run, @sql, @at, @hash);", conn, tx);
                version.Parameters.AddWithValue("@run", runId);
                version.Parameters.AddWithValue("@sql", draft.Sql);
                version.Parameters.AddWithValue("@at", draft.At);
                version.Parameters.AddWithValue("@hash", draft.Hash);
                await version.ExecuteNonQueryAsync();
            }

            await using (var move = new SqliteCommand("UPDATE history_versions SET history_id = @run WHERE history_id = @draft;", conn, tx))
            {
                move.Parameters.AddWithValue("@run", runId);
                move.Parameters.AddWithValue("@draft", draft.Id);
                await move.ExecuteNonQueryAsync();
            }
            await using (var delete = new SqliteCommand("DELETE FROM history WHERE id = @draft;", conn, tx))
            {
                delete.Parameters.AddWithValue("@draft", draft.Id);
                await delete.ExecuteNonQueryAsync();
            }
        }
    }

    /// <summary>
    /// Purges expired and excess history entries:
    /// 1. Deletes non-favorite entries older than <paramref name="retentionDays"/>.
    /// 2. If total count exceeds <paramref name="maxEntries"/>, deletes oldest non-favorite entries.
    /// FTS cleanup is handled automatically by the AFTER DELETE trigger.
    /// </summary>
    public async Task PurgeExpiredEntriesAsync(int retentionDays, int maxEntries)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Step 1: Delete non-favorite entries older than retention period
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays).ToString("o", CultureInfo.InvariantCulture);

        await using (var cmd = new SqliteCommand(
            "DELETE FROM history WHERE is_favorite = 0 AND executed_at < @cutoff;", conn))
        {
            cmd.Parameters.AddWithValue("@cutoff", cutoffDate);
            var expiredCount = await cmd.ExecuteNonQueryAsync();
            if (expiredCount > 0)
            {
                Log.Information("History purge: deleted {Count} entries older than {Days} days",
                    expiredCount, retentionDays);
            }
        }

        // Step 2: If still over limit, delete oldest non-favorite entries
        await using (var countCmd = new SqliteCommand("SELECT COUNT(*) FROM history;", conn))
        {
            var totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
            if (totalCount > maxEntries)
            {
                var excessCount = totalCount - maxEntries;
                await using var deleteCmd = new SqliteCommand(@"
                    DELETE FROM history WHERE id IN (
                        SELECT id FROM history
                        WHERE is_favorite = 0
                        ORDER BY executed_at ASC
                        LIMIT @limit
                    );", conn);
                deleteCmd.Parameters.AddWithValue("@limit", excessCount);
                var deletedCount = await deleteCmd.ExecuteNonQueryAsync();
                Log.Information("History purge: deleted {Count} excess entries (total was {Total}, limit is {Max})",
                    deletedCount, totalCount, maxEntries);
            }
        }
    }

    /// <summary>
    /// Version-preserving retention (FR-039): trims OLD version snapshots from
    /// <c>history_versions</c> while keeping each query's latest version and ALL execution
    /// records (the <c>history</c> rows themselves are never touched here).
    /// <para>
    /// A version row is deleted only when BOTH:
    /// (1) it is older than <paramref name="retentionDays"/>, AND
    /// (2) it is not the latest version for its parent entry (highest <c>id</c> per
    ///     <c>history_id</c> — autoincrement encodes insertion recency, robust against
    ///     second-granularity ties in <c>saved_at</c>).
    /// </para>
    /// Favorites are preserved implicitly: this method never deletes <c>history</c> rows.
    /// </summary>
    /// <returns>The number of version rows deleted.</returns>
    public async Task<int> PurgeOldVersionsAsync(int retentionDays)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Note: saved_at is populated by the column default datetime('now') which yields
        // 'YYYY-MM-DD HH:MM:SS' (space-separated, no 'T'/'Z'), NOT the ISO 8601 "o" format
        // used for executed_at. Wrap BOTH sides in datetime() so the comparison is
        // format-agnostic and never over-trims due to string-collation differences.
        await using var cmd = new SqliteCommand(@"
            DELETE FROM history_versions
            WHERE datetime(saved_at) < datetime('now', @cutoffOffset)
              AND id NOT IN (
                  SELECT MAX(id) FROM history_versions GROUP BY history_id
              );", conn);
        cmd.Parameters.AddWithValue("@cutoffOffset", $"-{retentionDays} days");

        var deletedCount = await cmd.ExecuteNonQueryAsync();
        if (deletedCount > 0)
        {
            Log.Information(
                "History version trim: deleted {Count} old version snapshots older than {Days} days (latest versions + executions kept)",
                deletedCount, retentionDays);
        }

        return deletedCount;
    }

    /// <summary>
    /// Searches the history database with the given filter criteria.
    /// Supports full-text search, column filters, date ranges, deduplication, and pagination.
    /// Returns a tuple of (matching entries for the current page, total count across all pages).
    /// </summary>
    public async Task<(List<HistoryEntryDto> Entries, int TotalCount)> SearchAsync(HistoryFilter filter)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var parameters = new List<SqliteParameter>();
        var whereClauses = new List<string>();

        // Spec 040 (HIS-07, data-model §2.4): the session name is searchable, so both queries join it.
        var fromClause = "history h LEFT JOIN query_sessions qs ON qs.id = h.session_id";

        // Free text finds a query by its SQL (full-text index) OR its name, source path, server or
        // database (LIKE). The search clause is built twice at most: with the full-text index, and —
        // when FTS5 rejects the query — with LIKE on the SQL instead.
        string? searchClauseFts = null, searchClauseLike = null;
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            (searchClauseFts, searchClauseLike) = BuildSearchClauses(filter.SearchText!, parameters);
        if (searchClauseFts != null) whereClauses.Add(searchClauseFts);

        if (!string.IsNullOrEmpty(filter.PathFilter))
        {
            whereClauses.Add(@"h.source LIKE '%' || @pathFilter || '%' ESCAPE '\'");
            parameters.Add(new SqliteParameter("@pathFilter", EscapeLike(filter.PathFilter!)));
        }

        // Column filters
        if (!string.IsNullOrEmpty(filter.Server))
        {
            whereClauses.Add("h.server = @server");
            parameters.Add(new SqliteParameter("@server", filter.Server));
        }

        if (!string.IsNullOrEmpty(filter.Database))
        {
            whereClauses.Add("h.database_name = @database");
            parameters.Add(new SqliteParameter("@database", filter.Database));
        }

        if (filter.Status.HasValue)
        {
            whereClauses.Add("h.status = @status");
            parameters.Add(new SqliteParameter("@status", filter.Status.Value));
        }

        // Spec 040 (HIS-07): compare instants, not strings — a bound carrying an offset
        // ("…+03:00") sorts differently from the stored "…Z" text.
        if (filter.DateFrom.HasValue)
        {
            whereClauses.Add("datetime(h.executed_at) >= datetime(@dateFrom)");
            parameters.Add(new SqliteParameter("@dateFrom",
                filter.DateFrom.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        if (filter.DateTo.HasValue)
        {
            whereClauses.Add("datetime(h.executed_at) <= datetime(@dateTo)");
            parameters.Add(new SqliteParameter("@dateTo",
                filter.DateTo.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        // Spec 040 (HIS-02/HIS-04): with grouping on, "starred" and "open" describe the grouped
        // query — any run starred, any run open — so they filter the GROUP values (outer WHERE),
        // not individual runs. Filtering runs made "closed only" list a query with an open run.
        var groupFilters = new List<string>();
        if (filter.FavoritesOnly)
        {
            if (filter.Deduplicate) groupFilters.Add("MAX(h.is_favorite) = 1");
            else whereClauses.Add("h.is_favorite = 1");
        }

        if (filter.IsOpen.HasValue)
        {
            if (filter.Deduplicate) groupFilters.Add("MAX(h.is_open) = @isOpen");
            else whereClauses.Add("h.is_open = @isOpen");
            parameters.Add(new SqliteParameter("@isOpen", filter.IsOpen.Value ? 1 : 0));
        }

        if (!string.IsNullOrEmpty(filter.NameFilter))
        {
            whereClauses.Add("COALESCE(qs.name, h.tab_title) LIKE '%' || @nameFilter || '%'");
            parameters.Add(new SqliteParameter("@nameFilter", filter.NameFilter));
        }

        // COALESCE so a NULL session_id degrades to per-content grouping instead of
        // lumping every ungrouped row together.
        const string GroupKey = "COALESCE(CAST(h.session_id AS TEXT), 'hash:' || h.content_hash)";

        var whereClause = whereClauses.Count > 0
            ? "WHERE " + string.Join(" AND ", whereClauses)
            : "";
        var groupHaving = groupFilters.Count > 0
            ? "HAVING " + string.Join(" AND ", groupFilters)
            : "";

        // Count: groups (after the group filters) or rows. With no filter at all the groups are
        // counted straight off IX_history_group.
        string BuildCountSql() => !filter.Deduplicate
            ? $"SELECT COUNT(*) FROM {fromClause} {whereClause}"
            : whereClauses.Count == 0 && groupFilters.Count == 0
                ? $"SELECT COUNT(DISTINCT {GroupKey}) FROM history h"
                : $@"SELECT COUNT(*) FROM (
                        SELECT {GroupKey} AS group_key
                          FROM {fromClause} {whereClause}
                         GROUP BY group_key {groupHaving}) AS g";

        // FTS5 can still reject a query SanitizeFts5Query let through (a dangling OR/NOT): then
        // search with LIKE on the SQL for every word instead (the name, path, server and database
        // branches are LIKE already).
        bool IsFtsError(Exception ex) =>
            ex is Microsoft.Data.Sqlite.SqliteException se && se.SqliteErrorCode == 1 && searchClauseFts != null;
        void UseLikeFallback(Exception ex)
        {
            Log.Warning(ex, "FTS5 query parse error for '{SearchText}', falling back to LIKE search", filter.SearchText);
            whereClauses.Remove(searchClauseFts!);
            if (searchClauseLike != null) whereClauses.Add(searchClauseLike);
            parameters.RemoveAll(p => p.ParameterName.StartsWith("@fts", StringComparison.Ordinal));
            searchClauseFts = null;
            whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";
        }
        async Task<int> CountAsync()
        {
            await using var countCmd = new SqliteCommand(BuildCountSql(), conn);
            foreach (var p in parameters) countCmd.Parameters.Add(CloneParameter(p));
            return Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // Spec 040 (SC-009): a filtered grouped search reads its total from the page query itself
        // (COUNT(*) OVER () on the groups), so the runs are scanned once, not twice.
        var countFromPage = filter.Deduplicate && (whereClauses.Count > 0 || groupFilters.Count > 0);
        var totalCount = -1;
        if (!countFromPage)
        {
            try { totalCount = await CountAsync(); }
            catch (Exception ex) when (IsFtsError(ex))
            {
                UseLikeFallback(ex);
                totalCount = await CountAsync();
            }
        }

        // Build the data query
        string BuildDataSql()
        {
            if (filter.Deduplicate)
            {
                // Deduplicated view: one representative row per SESSION (falling back to per-content_hash
                // grouping for legacy rows with no session_id — see GroupKey above): the group's latest
                // run (latest executed_at, id as tiebreak). Every scalar column comes from that row.
                // exec_count is the number of runs (not drafts) MATCHING THE CURRENT FILTER;
                // favourite/open are "any run" and filter the groups (HAVING); the display name comes
                // from the joined query_sessions row (falling back to the latest row's own tab_title).
                //
                // Spec 040 (HIS-04): version_count is the number of DISTINCT contents across the
                // group's runs AND snapshots — the same set the Versions panel lists
                // (GetVersionsForGroupAsync) — so "M versions" on the row equals the panel's count.
                // It is computed only for the page's rows, through the session index or, for legacy
                // rows, the hash index.
                //
                // Spec 040 (SC-009): aggregate each group once, page the groups, then read only the page's
                // representative rows. Window functions over every run cost ~400 ms at 100,000 runs; this
                // is ~150 ms. The representative is the latest run (executed_at, then id): the fixed-width
                // ISO timestamp followed by the zero-padded id sorts in exactly that order.
                return $@"
                    WITH g AS (
                        SELECT {GroupKey} AS gk,
                               MAX(h.executed_at || printf('%019d', h.id)) AS k,
                               SUM(CASE WHEN h.status <> 3 THEN 1 ELSE 0 END) AS exec_count,
                               MAX(h.is_favorite) AS is_favorite,
                               MAX(h.is_open) AS is_open
                          FROM {fromClause}
                          {whereClause}
                         GROUP BY gk
                         {groupHaving}
                    ),
                    page AS (
                        SELECT CAST(substr(k, length(k) - 18) AS INTEGER) AS rep_id, exec_count, is_favorite, is_open, k,
                               COUNT(*) OVER () AS total_groups
                          FROM g
                         ORDER BY k DESC
                         LIMIT @limit OFFSET @offset
                    )
                    SELECT
                        h.id,
                        substr(h.sql_text, 1, 500) as sql_text,
                        h.server,
                        h.database_name,
                        h.username,
                        h.executed_at,
                        h.duration_ms,
                        h.row_count,
                        h.status,
                        h.error_msg,
                        h.source,
                        COALESCE(qs.name, h.tab_title, '') as tab_title,
                        page.is_favorite,
                        page.exec_count,
                        (SELECT COUNT(*) FROM (
                            SELECT h2.content_hash AS ch FROM history h2 WHERE h2.session_id = h.session_id
                            UNION
                            SELECT h2.content_hash FROM history h2
                             WHERE h.session_id IS NULL AND h2.session_id IS NULL AND h2.content_hash = h.content_hash
                            UNION
                            SELECT v.content_hash FROM history_versions v
                             WHERE v.content_hash IS NOT NULL
                               AND v.history_id IN (SELECT h3.id FROM history h3 WHERE h3.session_id = h.session_id)
                            UNION
                            SELECT v.content_hash FROM history_versions v
                             WHERE v.content_hash IS NOT NULL AND h.session_id IS NULL
                               AND v.history_id IN (SELECT h3.id FROM history h3
                                                     WHERE h3.session_id IS NULL AND h3.content_hash = h.content_hash)
                        )) as version_count,
                        h.content_hash,
                        page.is_open,
                        qs.session_key,
                        page.total_groups
                    FROM page
                    JOIN history h ON h.id = page.rep_id
                    LEFT JOIN query_sessions qs ON qs.id = h.session_id
                    ORDER BY page.k DESC";
            }
            else
            {
                // Non-dedup rows need the same session-name resolution as the dedup branch above
                // (COALESCE(qs.name, h.tab_title, '')) — otherwise, with Deduplicate off, a row whose
                // session was renamed keeps showing its pre-rename tab_title, and a row with no
                // tab_title at all (unsaved scratch tab) falls straight through to raw SQL text
                // instead of its auto-assigned query-NN session name.
                return $@"
                    SELECT
                        h.id,
                        substr(h.sql_text, 1, 500) as sql_text,
                        h.server,
                        h.database_name,
                        h.username,
                        h.executed_at,
                        h.duration_ms,
                        h.row_count,
                        h.status,
                        h.error_msg,
                        h.source,
                        COALESCE(qs.name, h.tab_title, '') as tab_title,
                        h.is_favorite,
                        CASE WHEN h.status <> 3 THEN 1 ELSE 0 END as exec_count,
                        h.content_hash,
                        h.is_open,
                        qs.session_key
                    FROM {fromClause}
                    {whereClause}
                    ORDER BY h.executed_at DESC
                    LIMIT @limit OFFSET @offset";
            }
        }

        async Task<(SqliteCommand Cmd, SqliteDataReader Reader)> OpenDataAsync()
        {
            var cmd = new SqliteCommand(BuildDataSql(), conn);
            foreach (var p in parameters) cmd.Parameters.Add(CloneParameter(p));
            cmd.Parameters.AddWithValue("@limit", filter.Limit);
            cmd.Parameters.AddWithValue("@offset", filter.Offset);
            try { return (cmd, await cmd.ExecuteReaderAsync()); }
            catch { await cmd.DisposeAsync(); throw; }
        }

        var entries = new List<HistoryEntryDto>();
        SqliteCommand dataCmd;
        SqliteDataReader reader;
        try { (dataCmd, reader) = await OpenDataAsync(); }
        catch (Exception ex) when (IsFtsError(ex))
        {
            UseLikeFallback(ex);
            (dataCmd, reader) = await OpenDataAsync();
        }
        await using var dataCmdScope = dataCmd;
        await using var readerScope = reader;
        // content_hash/is_open shift by one position in the Deduplicate branch (version_count is
        // inserted ahead of them), so look them up by name rather than trusting a fixed ordinal
        // shared across both branches' differently-shaped SELECT lists.
        var contentHashOrdinal = reader.GetOrdinal("content_hash");
        var isOpenOrdinal = reader.GetOrdinal("is_open");
        var versionCountOrdinal = filter.Deduplicate ? reader.GetOrdinal("version_count") : -1;
        var sessionKeyOrdinal = reader.GetOrdinal("session_key");
        var totalOrdinal = countFromPage ? reader.GetOrdinal("total_groups") : -1;
        while (await reader.ReadAsync())
        {
            if (totalOrdinal >= 0 && totalCount < 0) totalCount = reader.GetInt32(totalOrdinal);
            entries.Add(new HistoryEntryDto
            {
                Id = reader.GetInt64(0),
                SqlText = reader.GetString(1),
                Server = reader.IsDBNull(2) ? null : reader.GetString(2),
                Database = reader.IsDBNull(3) ? null : reader.GetString(3),
                Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                ExecutedAt = reader.GetString(5),
                DurationMs = reader.GetInt64(6),
                RowCount = reader.GetInt64(7),
                Status = reader.GetInt32(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                Source = reader.IsDBNull(10) ? null : reader.GetString(10),
                TabTitle = reader.IsDBNull(11) ? null : reader.GetString(11),
                IsFavorite = reader.GetInt32(12) != 0,
                ExecutionCount = reader.GetInt32(13),
                ContentHash = reader.IsDBNull(contentHashOrdinal) ? null : reader.GetString(contentHashOrdinal),
                IsOpen = reader.GetInt32(isOpenOrdinal) != 0,
                VersionCount = versionCountOrdinal < 0
                    ? 1
                    : (reader.IsDBNull(versionCountOrdinal) ? 1 : Math.Max(1, reader.GetInt32(versionCountOrdinal))),
                SessionKey = reader.IsDBNull(sessionKeyOrdinal) ? null : reader.GetString(sessionKeyOrdinal)
            });
        }

        // An empty page (no match, or paged past the end) carries no total.
        if (totalCount < 0)
            totalCount = filter.Offset == 0 ? 0 : await CountAsync();

        // Apply CamelCase post-filtering in memory if CamelCaseTokens are provided.
        // This filters results to only entries whose sql_text contains words matching
        // ALL CamelCase tokens at CamelCase/underscore boundaries.
        if (filter.CamelCaseTokens is { Length: > 0 })
        {
            var beforeCount = entries.Count;
            entries = entries.Where(e => MatchesAllCamelCaseTokens(e.SqlText, filter.CamelCaseTokens)).ToList();
            var removed = beforeCount - entries.Count;
            if (removed > 0)
            {
                totalCount = Math.Max(0, totalCount - removed);
                Log.Debug("CamelCase post-filter removed {Removed} entries for tokens [{Tokens}]",
                    removed, string.Join(", ", filter.CamelCaseTokens));
            }
        }

        Log.Debug("History search completed: {Count} entries returned, {Total} total matches",
            entries.Count, totalCount);

        return (entries, totalCount);
    }

    /// <summary>Most values the filter menu lists per column.</summary>
    internal const int MaxFilterValues = 500;

    /// <summary>
    /// Spec 040 (HIS-07) — the distinct, non-empty servers and databases in History, sorted, at
    /// most <see cref="MaxFilterValues"/> each, for History's filter menu.
    /// </summary>
    public async Task<(List<string> Servers, List<string> Databases)> GetFilterValuesAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        async Task<List<string>> Distinct(string column)
        {
            var values = new List<string>();
            await using var cmd = new SqliteCommand(
                $"SELECT DISTINCT {column} FROM history WHERE {column} IS NOT NULL AND TRIM({column}) <> '' " +
                $"ORDER BY {column} COLLATE NOCASE LIMIT {MaxFilterValues};", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) values.Add(reader.GetString(0));
            return values;
        }

        return (await Distinct("server"), await Distinct("database_name"));
    }

    /// <summary>
    /// Returns distinct server names from the history database for populating filter dropdowns.
    /// </summary>
    public async Task<List<string>> GetDistinctServersAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var servers = new List<string>();
        await using var cmd = new SqliteCommand(
            "SELECT DISTINCT server FROM history WHERE server IS NOT NULL ORDER BY server;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            servers.Add(reader.GetString(0));
        }

        return servers;
    }

    /// <summary>
    /// Returns distinct database names from the history database for populating filter dropdowns.
    /// </summary>
    public async Task<List<string>> GetDistinctDatabasesAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var databases = new List<string>();
        await using var cmd = new SqliteCommand(
            "SELECT DISTINCT database_name FROM history WHERE database_name IS NOT NULL ORDER BY database_name;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    /// <summary>
    /// Retrieves the full (non-truncated) SQL text for a single history entry by ID.
    /// Returns null if the entry does not exist.
    /// </summary>
    /// <summary>
    /// Spec 040 (HIS-14): the given entries with their full text, query name and session key, in
    /// the order asked (restore on start reopens them). Unknown ids are left out; at most 500.
    /// </summary>
    public async Task<List<HistoryEntryDto>> GetEntriesAsync(IReadOnlyList<long> ids)
    {
        var result = new List<HistoryEntryDto>();
        if (ids == null || ids.Count == 0) return result;
        var wanted = ids.Distinct().Take(500).ToList();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand { Connection = conn };
        var names = new List<string>(wanted.Count);
        for (var i = 0; i < wanted.Count; i++)
        {
            names.Add("@id" + i);
            cmd.Parameters.AddWithValue("@id" + i, wanted[i]);
        }
        cmd.CommandText = $@"
            SELECT h.id, h.sql_text, h.server, h.database_name, h.executed_at, h.status, h.source,
                   COALESCE(qs.name, h.tab_title), h.is_favorite, h.is_open, qs.session_key
              FROM history h LEFT JOIN query_sessions qs ON qs.id = h.session_id
             WHERE h.id IN ({string.Join(", ", names)});";

        var byId = new Dictionary<long, HistoryEntryDto>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var entry = new HistoryEntryDto
                {
                    Id = reader.GetInt64(0),
                    SqlText = reader.GetString(1),
                    Server = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Database = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ExecutedAt = reader.GetString(4),
                    Status = reader.GetInt32(5),
                    Source = reader.IsDBNull(6) ? null : reader.GetString(6),
                    TabTitle = reader.IsDBNull(7) ? null : reader.GetString(7),
                    IsFavorite = reader.GetInt32(8) != 0,
                    IsOpen = reader.GetInt32(9) != 0,
                    SessionKey = reader.IsDBNull(10) ? null : reader.GetString(10),
                };
                byId[entry.Id] = entry;
            }
        }

        foreach (var id in wanted)
            if (byId.TryGetValue(id, out var entry)) result.Add(entry);
        return result;
    }

    public async Task<string?> GetFullSqlAsync(long entryId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(
            "SELECT sql_text FROM history WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("@id", entryId);

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    /// <summary>
    /// Retrieves the SQL text for two history entries, used for side-by-side diff comparison.
    /// Returns (sql1, sql2) tuple. Either value may be null if the entry does not exist.
    /// </summary>
    public async Task<(string? Sql1, string? Sql2)> GetEntriesForDiffAsync(long id1, long id2)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string? sql1 = null;
        string? sql2 = null;

        await using var cmd = new SqliteCommand(
            "SELECT id, sql_text FROM history WHERE id IN (@id1, @id2);", conn);
        cmd.Parameters.AddWithValue("@id1", id1);
        cmd.Parameters.AddWithValue("@id2", id2);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0);
            var text = reader.GetString(1);
            if (id == id1) sql1 = text;
            else if (id == id2) sql2 = text;
        }

        return (sql1, sql2);
    }

    /// <summary>
    /// Toggles the is_favorite flag on a history entry (0 -> 1, 1 -> 0).
    /// Returns the new favorite state.
    /// </summary>
    public async Task<bool> ToggleFavoriteAsync(long entryId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Step 1: Toggle the favorite flag
        await using var updateCmd = new SqliteCommand(
            "UPDATE history SET is_favorite = 1 - is_favorite WHERE id = @id", conn);
        updateCmd.Parameters.AddWithValue("@id", entryId);
        await updateCmd.ExecuteNonQueryAsync();

        // Step 2: Read back the new state (separate command — ExecuteScalarAsync only runs first statement)
        await using var selectCmd = new SqliteCommand(
            "SELECT is_favorite FROM history WHERE id = @id", conn);
        selectCmd.Parameters.AddWithValue("@id", entryId);
        var result = await selectCmd.ExecuteScalarAsync();
        var newState = result != null && Convert.ToInt32(result) != 0;

        Log.Debug("History entry {Id}: favorite toggled to {State}", entryId, newState);
        return newState;
    }

    /// <summary>
    /// Sets the is_open flag on one or more history entries.
    /// </summary>
    public async Task SetOpenStatusAsync(long[] entryIds, bool isOpen)
    {
        if (entryIds.Length == 0) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var paramNames = new string[entryIds.Length];
        for (int i = 0; i < entryIds.Length; i++)
            paramNames[i] = $"@id{i}";

        var sql = $"UPDATE history SET is_open = @val WHERE id IN ({string.Join(", ", paramNames)})";
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@val", isOpen ? 1 : 0);
        for (int i = 0; i < entryIds.Length; i++)
            cmd.Parameters.AddWithValue(paramNames[i], entryIds[i]);

        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Spec 040 (HIS-02, HIS-04): grouped rows and open state ─────────────────

    /// <summary>
    /// The grouped row an entry belongs to, matching SearchAsync's GroupKey: its query session,
    /// or — for a legacy row with no session — every session-less row with the same content hash.
    /// Null when the entry does not exist.
    /// </summary>
    private static async Task<(long? SessionId, string ContentHash)?> ResolveGroupAsync(
        SqliteConnection conn, long entryId, SqliteTransaction? tx = null)
    {
        await using var cmd = new SqliteCommand(
            "SELECT session_id, content_hash FROM history WHERE id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("@id", entryId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.IsDBNull(0) ? (long?)null : r.GetInt64(0), r.GetString(1));
    }

    /// <summary>WHERE fragment (alias <c>h</c>) selecting a group's rows; binds @gsid or @ghash.</summary>
    private static string GroupPredicate((long? SessionId, string ContentHash) group, SqliteCommand cmd)
    {
        if (group.SessionId.HasValue)
        {
            cmd.Parameters.AddWithValue("@gsid", group.SessionId.Value);
            return "h.session_id = @gsid";
        }
        cmd.Parameters.AddWithValue("@ghash", group.ContentHash);
        return "h.session_id IS NULL AND h.content_hash = @ghash";
    }

    /// <summary>
    /// Deletes a whole grouped query in one transaction: its snapshots, its runs, then its query
    /// session. Returns the number of runs deleted (0 when the entry does not exist).
    /// </summary>
    public async Task<int> DeleteGroupAsync(long entryId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction(deferred: false);

        var group = await ResolveGroupAsync(conn, entryId, tx);
        if (group == null) return 0;

        await using (var versions = new SqliteCommand { Connection = conn, Transaction = tx })
        {
            versions.CommandText =
                $"DELETE FROM history_versions WHERE history_id IN (SELECT h.id FROM history h WHERE {GroupPredicate(group.Value, versions)});";
            await versions.ExecuteNonQueryAsync();
        }

        int deleted;
        await using (var rows = new SqliteCommand { Connection = conn, Transaction = tx })
        {
            rows.CommandText = $"DELETE FROM history AS h WHERE {GroupPredicate(group.Value, rows)};";
            deleted = await rows.ExecuteNonQueryAsync();
        }

        if (group.Value.SessionId.HasValue)
        {
            await using var session = new SqliteCommand("DELETE FROM query_sessions WHERE id = @sid;", conn, tx);
            session.Parameters.AddWithValue("@sid", group.Value.SessionId.Value);
            await session.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        Log.Information("History: deleted grouped query of entry {Id} ({Count} runs)", entryId, deleted);
        return deleted;
    }

    /// <summary>
    /// Stars or un-stars a whole grouped query: every run is set to <c>1 - MAX(is_favorite)</c>
    /// (the row shows starred when any run is). Returns the new state.
    /// </summary>
    public async Task<bool> ToggleFavoriteGroupAsync(long entryId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction(deferred: false);

        var group = await ResolveGroupAsync(conn, entryId, tx);
        if (group == null) return false;

        int current;
        await using (var read = new SqliteCommand { Connection = conn, Transaction = tx })
        {
            read.CommandText = $"SELECT COALESCE(MAX(h.is_favorite), 0) FROM history h WHERE {GroupPredicate(group.Value, read)};";
            current = Convert.ToInt32(await read.ExecuteScalarAsync());
        }

        var next = current == 0 ? 1 : 0;
        await using (var write = new SqliteCommand { Connection = conn, Transaction = tx })
        {
            write.CommandText = $"UPDATE history AS h SET is_favorite = @fav WHERE {GroupPredicate(group.Value, write)};";
            write.Parameters.AddWithValue("@fav", next);
            await write.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return next == 1;
    }

    /// <summary>
    /// The versions of a grouped query: its runs and snapshots, one per distinct content (the
    /// newest occurrence), newest first with the id as tiebreak. The count equals the grouped
    /// row's <c>VersionCount</c>.
    /// </summary>
    public async Task<List<(long Id, string SqlText, string SavedAt, string? Server, string? Database)>> GetVersionsForGroupAsync(long entryId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var group = await ResolveGroupAsync(conn, entryId);
        if (group == null) return new List<(long, string, string, string?, string?)>();

        var all = new List<(long Id, string Sql, string At, string Hash, string? Server, string? Database)>();
        await using (var cmd = new SqliteCommand { Connection = conn })
        {
            // Spec 040 (HIS-12): each version carries the server and database it ran on (a
            // snapshot: its run's), for the versions pane.
            var predicate = GroupPredicate(group.Value, cmd);
            cmd.CommandText = $@"
                SELECT h.id, h.sql_text, h.executed_at, h.content_hash, h.server, h.database_name FROM history h WHERE {predicate}
                UNION ALL
                SELECT v.id, v.sql_text, v.saved_at, COALESCE(v.content_hash, ''), h.server, h.database_name
                  FROM history_versions v JOIN history h ON h.id = v.history_id WHERE {predicate};";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var sql = r.GetString(1);
                var hash = r.GetString(3);
                all.Add((r.GetInt64(0), sql, r.GetString(2), hash.Length == 0 ? ComputeContentHash(sql) : hash,
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return all
            .OrderByDescending(v => ParseStoredTime(v.At))
            .ThenByDescending(v => v.Id)
            .Where(v => seen.Add(v.Hash))
            .Select(v => (v.Id, v.Sql, v.At, v.Server, v.Database))
            .ToList();
    }

    /// <summary>A stored timestamp (ISO "o", or a legacy 'YYYY-MM-DD HH:MM:SS' UTC value) as UTC.</summary>
    private static DateTime ParseStoredTime(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : DateTime.MinValue;

    /// <summary>
    /// Opens or closes every run of a query session. Open records the owning shell process;
    /// close clears it.
    /// </summary>
    public async Task SetOpenStatusBySessionAsync(string sessionKey, bool isOpen, int ownerPid)
    {
        if (string.IsNullOrEmpty(sessionKey)) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand(@"
            UPDATE history SET is_open = @open, open_pid = @pid
             WHERE session_id = (SELECT id FROM query_sessions WHERE session_key = @key);", conn);
        cmd.Parameters.AddWithValue("@open", isOpen ? 1 : 0);
        cmd.Parameters.AddWithValue("@pid", isOpen ? ownerPid : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Brings open state in line with reality when a shell starts:
    /// <list type="bullet">
    /// <item>closes rows <paramref name="ownerPid"/> owns whose session is not in <paramref name="openKeys"/>;</item>
    /// <item>closes rows owned by a process that is no longer running (SSMS exited or crashed with
    ///   the query open) and returns those groups' representative entry ids, newest first — the
    ///   queries to offer for restore.</item>
    /// </list>
    /// Rows with no owner (web rows) and rows owned by other live shells are left alone.
    /// </summary>
    public async Task<long[]> ReconcileOpenAsync(int ownerPid, string[] openKeys)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 1. This shell's own rows whose document is no longer open.
        await using (var mine = new SqliteCommand { Connection = conn })
        {
            var keyParams = new List<string>();
            for (int i = 0; i < (openKeys?.Length ?? 0); i++)
            {
                keyParams.Add("@k" + i);
                mine.Parameters.AddWithValue("@k" + i, openKeys![i]);
            }
            var stillOpen = keyParams.Count == 0
                ? ""
                : $" AND (session_id IS NULL OR session_id NOT IN (SELECT id FROM query_sessions WHERE session_key IN ({string.Join(", ", keyParams)})))";
            mine.CommandText = $"UPDATE history SET is_open = 0, open_pid = NULL WHERE open_pid = @pid{stillOpen};";
            mine.Parameters.AddWithValue("@pid", ownerPid);
            await mine.ExecuteNonQueryAsync();
        }

        // 2. Rows owned by shells that are gone.
        var owners = new List<int>();
        await using (var cmd = new SqliteCommand(
            "SELECT DISTINCT open_pid FROM history WHERE is_open = 1 AND open_pid IS NOT NULL AND open_pid <> @pid;", conn))
        {
            cmd.Parameters.AddWithValue("@pid", ownerPid);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) owners.Add(r.GetInt32(0));
        }

        var dead = owners.Where(pid => !IsProcessRunning(pid)).ToList();
        if (dead.Count == 0) return Array.Empty<long>();

        var deadList = string.Join(", ", dead.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        const string GroupKey = "COALESCE(CAST(h.session_id AS TEXT), 'hash:' || h.content_hash)";

        var restorable = new List<long>();
        await using (var cmd = new SqliteCommand($@"
            SELECT id FROM (
                SELECT h.id, h.executed_at,
                       ROW_NUMBER() OVER (PARTITION BY {GroupKey} ORDER BY h.executed_at DESC, h.id DESC) AS rn
                  FROM history h
                 WHERE {GroupKey} IN (SELECT {GroupKey} FROM history h WHERE h.is_open = 1 AND h.open_pid IN ({deadList}))
            ) WHERE rn = 1
            ORDER BY executed_at DESC, id DESC;", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) restorable.Add(r.GetInt64(0));
        }

        await using (var close = new SqliteCommand(
            $"UPDATE history SET is_open = 0, open_pid = NULL WHERE open_pid IN ({deadList});", conn))
        {
            await close.ExecuteNonQueryAsync();
        }

        Log.Information("History: reconciled open state — {Count} queries were open when their SSMS closed", restorable.Count);
        return restorable.ToArray();
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Deletes one or more history entries by ID.
    /// FTS cleanup is handled automatically by the AFTER DELETE trigger.
    /// Returns the number of entries deleted.
    /// </summary>
    public async Task<int> DeleteEntriesAsync(long[] entryIds)
    {
        if (entryIds.Length == 0) return 0;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Build parameterized IN clause
        var paramNames = new string[entryIds.Length];
        for (int i = 0; i < entryIds.Length; i++)
        {
            paramNames[i] = $"@id{i}";
        }

        var sql = $"DELETE FROM history WHERE id IN ({string.Join(", ", paramNames)});";
        await using var cmd = new SqliteCommand(sql, conn);
        for (int i = 0; i < entryIds.Length; i++)
        {
            cmd.Parameters.AddWithValue(paramNames[i], entryIds[i]);
        }

        var deletedCount = await cmd.ExecuteNonQueryAsync();
        Log.Information("History: deleted {Count} entries (requested {Requested} IDs)",
            deletedCount, entryIds.Length);
        return deletedCount;
    }

    /// <summary>
    /// Spec 030 T074 / FR-041 — deletes every entry STRICTLY OLDER than the reference entry's
    /// <c>executed_at</c> (so the reference entry itself is kept). The cutoff is resolved server-side
    /// (SELECT by id) to avoid timestamp-format drift across the IPC boundary. When
    /// <paramref name="keepFavorites"/> is true (default), favorited entries are preserved — matching
    /// the auto-trim purge convention. The FTS index is kept in sync by the AFTER DELETE trigger.
    /// Returns the number of entries deleted (0 if the reference id is unknown).
    /// <para>
    /// Both sides of the comparison are wrapped in SQLite <c>datetime()</c> (as
    /// <see cref="PurgeOldVersionsAsync"/> does): <c>executed_at</c> is NOT uniformly ISO-8601 — most
    /// rows are ISO 'o' (<see cref="InsertEntryAsync"/>) but <c>SaveVersionBySourceAsync</c> rewrites
    /// it via <c>datetime('now')</c> (space-separated). A raw lexicographic compare would treat a
    /// space-format row as older than any ISO row on the same day (space &lt; 'T'), silently deleting
    /// NEWER entries. <c>datetime()</c> canonicalises both forms so the comparison is by real time.
    /// </para>
    /// </summary>
    public async Task<int> DeleteEntriesOlderThanAsync(long referenceEntryId, bool keepFavorites = true)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string? cutoff;
        await using (var lookup = new SqliteCommand(
            "SELECT executed_at FROM history WHERE id = @id;", conn))
        {
            lookup.Parameters.AddWithValue("@id", referenceEntryId);
            cutoff = (await lookup.ExecuteScalarAsync()) as string;
        }

        if (string.IsNullOrEmpty(cutoff))
        {
            Log.Warning("History: RemoveOlderThan reference entry {Id} not found; nothing deleted", referenceEntryId);
            return 0;
        }

        var sql = keepFavorites
            ? "DELETE FROM history WHERE datetime(executed_at) < datetime(@cutoff) AND is_favorite = 0;"
            : "DELETE FROM history WHERE datetime(executed_at) < datetime(@cutoff);";
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        var deletedCount = await cmd.ExecuteNonQueryAsync();
        Log.Information("History: RemoveOlderThan deleted {Count} entries older than {Cutoff} (keepFavorites={Keep})",
            deletedCount, cutoff, keepFavorites);
        return deletedCount;
    }

    /// <summary>
    /// Deletes all non-favorite history entries. Returns the number deleted.
    /// </summary>
    public async Task<int> DeleteAllNonFavoriteAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(
            "DELETE FROM history WHERE is_favorite = 0;", conn);
        var deletedCount = await cmd.ExecuteNonQueryAsync();
        Log.Information("History: deleted all {Count} non-favorite entries", deletedCount);
        return deletedCount;
    }

    /// <summary>
    /// Exports history entries matching a filter to a file in the specified format.
    /// Retrieves full sql_text (not truncated) for the export.
    /// </summary>
    public async Task ExportAsync(HistoryFilter filter, ExportFormat format, string outputPath)
    {
        // Validate output path is absolute
        if (!Path.IsPathRooted(outputPath))
            throw new ArgumentException("Export path must be absolute.", nameof(outputPath));

        var entries = await GetFullEntriesForExportAsync(filter);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        switch (format)
        {
            case ExportFormat.Csv:
                await ExportCsvAsync(entries, outputPath);
                break;
            case ExportFormat.Json:
                await ExportJsonAsync(entries, outputPath);
                break;
            case ExportFormat.Sql:
                await ExportSqlAsync(entries, outputPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format");
        }

        Log.Information("History export: {Count} entries exported as {Format} to {Path}",
            entries.Count, format, outputPath);
    }

    /// <summary>
    /// Retrieves full history entries (with complete sql_text) matching the given filter.
    /// Used by the export feature.
    /// </summary>
    private async Task<List<HistoryExportEntry>> GetFullEntriesForExportAsync(HistoryFilter filter)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var parameters = new List<SqliteParameter>();
        var whereClauses = new List<string>();

        var fromClause = "history h";
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var sanitized = filter.SearchText.Replace("\"", "\"\"");
            fromClause = "history h INNER JOIN history_fts fts ON h.id = fts.rowid";
            whereClauses.Add("history_fts MATCH @search");
            parameters.Add(new SqliteParameter("@search", $"\"{sanitized}\""));
        }

        if (!string.IsNullOrEmpty(filter.Server))
        {
            whereClauses.Add("h.server = @server");
            parameters.Add(new SqliteParameter("@server", filter.Server));
        }

        if (!string.IsNullOrEmpty(filter.Database))
        {
            whereClauses.Add("h.database_name = @database");
            parameters.Add(new SqliteParameter("@database", filter.Database));
        }

        if (filter.Status.HasValue)
        {
            whereClauses.Add("h.status = @status");
            parameters.Add(new SqliteParameter("@status", filter.Status.Value));
        }

        if (filter.DateFrom.HasValue)
        {
            whereClauses.Add("h.executed_at >= @dateFrom");
            parameters.Add(new SqliteParameter("@dateFrom",
                filter.DateFrom.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        if (filter.DateTo.HasValue)
        {
            whereClauses.Add("h.executed_at <= @dateTo");
            parameters.Add(new SqliteParameter("@dateTo",
                filter.DateTo.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        if (filter.FavoritesOnly)
        {
            whereClauses.Add("h.is_favorite = 1");
        }

        if (!string.IsNullOrEmpty(filter.NameFilter))
        {
            // Same name resolution as SearchAsync (:721): NameFilter means the SESSION name, which
            // falls back to the row's own tab_title only when it has no session. Matching against
            // the bare column here would silently stop matching any renamed or auto-named session.
            whereClauses.Add(
                "COALESCE((SELECT qs2.name FROM query_sessions qs2 WHERE qs2.id = h.session_id), h.tab_title) " +
                "LIKE '%' || @nameFilter || '%'");
            parameters.Add(new SqliteParameter("@nameFilter", filter.NameFilter));
        }

        var whereClause = whereClauses.Count > 0
            ? "WHERE " + string.Join(" AND ", whereClauses)
            : "";

        // Finding 5 (PR #249 review): project the SAME resolved name the NameFilter WHERE clause
        // above matches against, not the raw h.tab_title column. Since unsaved scratch documents
        // store tab_title NULL, filtering by the session name (e.g. "query-07") would otherwise
        // match rows whose exported TabTitle came back empty -- filter and output must agree on
        // what "name" means.
        var sql = $@"
            SELECT
                h.id, h.sql_text, h.server, h.database_name, h.username,
                h.executed_at, h.duration_ms, h.row_count, h.status,
                h.error_msg, h.source,
                COALESCE((SELECT qs3.name FROM query_sessions qs3 WHERE qs3.id = h.session_id), h.tab_title, '') AS resolved_name,
                h.is_favorite, h.content_hash
            FROM {fromClause}
            {whereClause}
            ORDER BY h.executed_at DESC";

        var entries = new List<HistoryExportEntry>();
        await using var cmd = new SqliteCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(CloneParameter(p));
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new HistoryExportEntry
            {
                Id = reader.GetInt64(0),
                SqlText = reader.GetString(1),
                Server = reader.IsDBNull(2) ? null : reader.GetString(2),
                Database = reader.IsDBNull(3) ? null : reader.GetString(3),
                Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                ExecutedAt = reader.GetString(5),
                DurationMs = reader.GetInt64(6),
                RowCount = reader.GetInt64(7),
                Status = reader.GetInt32(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                Source = reader.IsDBNull(10) ? null : reader.GetString(10),
                TabTitle = reader.IsDBNull(11) ? null : reader.GetString(11),
                IsFavorite = reader.GetInt32(12) != 0,
                ContentHash = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        }

        return entries;
    }

    private static async Task ExportCsvAsync(List<HistoryExportEntry> entries, string path)
    {
        await using var writer = new StreamWriter(path, false, Encoding.UTF8);
        await writer.WriteLineAsync("Id,ExecutedAt,Server,Database,Username,Status,DurationMs,RowCount,IsFavorite,ErrorMessage,Source,TabTitle,SqlText");

        foreach (var e in entries)
        {
            await writer.WriteLineAsync(string.Join(",",
                e.Id,
                CsvEscape(e.ExecutedAt),
                CsvEscape(e.Server),
                CsvEscape(e.Database),
                CsvEscape(e.Username),
                StatusToString(e.Status),
                e.DurationMs,
                e.RowCount,
                e.IsFavorite,
                CsvEscape(e.ErrorMessage),
                CsvEscape(e.Source),
                CsvEscape(e.TabTitle),
                CsvEscape(e.SqlText)));
        }
    }

    private static async Task ExportJsonAsync(List<HistoryExportEntry> entries, string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(entries, options);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    private static async Task ExportSqlAsync(List<HistoryExportEntry> entries, string path)
    {
        await using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var e in entries)
        {
            await writer.WriteLineAsync($"-- ============================================================");
            await writer.WriteLineAsync($"-- Server: {e.Server ?? "(unknown)"}");
            await writer.WriteLineAsync($"-- Database: {e.Database ?? "(unknown)"}");
            await writer.WriteLineAsync($"-- Executed: {e.ExecutedAt}");
            await writer.WriteLineAsync($"-- Status: {StatusToString(e.Status)}");
            await writer.WriteLineAsync($"-- Duration: {e.DurationMs}ms, Rows: {e.RowCount}");
            if (!string.IsNullOrEmpty(e.ErrorMessage))
                await writer.WriteLineAsync($"-- Error: {e.ErrorMessage}");
            await writer.WriteLineAsync($"-- ============================================================");
            await writer.WriteLineAsync(e.SqlText);
            await writer.WriteLineAsync("GO");
            await writer.WriteLineAsync();
        }
    }

    private static string CsvEscape(string? value)
    {
        if (value == null) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static string StatusToString(int status)
    {
        return status switch
        {
            0 => "Success",
            1 => "Error",
            2 => "Cancelled",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Computes a SHA-256 content hash from whitespace-normalized, case-folded SQL text.
    /// This is used for deduplication detection.
    /// </summary>
    internal static string ComputeContentHash(string sqlText)
    {
        // Normalize: collapse all whitespace runs to single space, trim, lowercase
        var normalized = Regex.Replace(sqlText, @"\s+", " ").Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Updates the display name for a history entry. Used by the "Rename" feature for closed queries.
    /// <para>
    /// (Task 6 fix-round-1) The deduplicated search reads its display name from
    /// <c>query_sessions.name</c> via a <c>LEFT JOIN</c> (falling back to a row's own
    /// <c>tab_title</c> only when it has no session — see <see cref="SearchAsync"/>). Task 5's
    /// backfill assigns a session to every pre-existing row, so writing <c>history.tab_title</c>
    /// alone (the pre-fix-round-1 behaviour) was silently invisible: the rename succeeded but
    /// <c>qs.name</c> always won the COALESCE, so the new name never appeared in the list. This
    /// method now targets whichever place the read path actually consults:
    /// </para>
    /// <para>
    /// <b>Entry has a session</b> (the common case): renames the SESSION —
    /// <c>UPDATE query_sessions SET name = @newName, name_source = 2</c>. Renaming necessarily
    /// renames every execution grouped under that session; that is intended, since they are all the
    /// same entry in the deduplicated list. <c>name_source = 2</c> (manual) is REQUIRED: it is the
    /// only value <see cref="QuerySessionStore"/>'s <c>MaybeUpgradeNameAsync</c> precedence rule
    /// never overwrites, so a later execution carrying a real filename cannot clobber the user's
    /// chosen name.
    /// </para>
    /// <para>
    /// <b>Entry has no session</b> (legacy/mid-upgrade row with <c>session_id IS NULL</c>): falls
    /// back to the pre-fix-round-1 behaviour — stamp <c>tab_title</c> on every row sharing the
    /// target's <c>content_hash</c>, since the deduplicated view's per-row fallback name is a
    /// window aggregate over that same partition and a single-row stamp would vanish whenever a
    /// filter excludes the renamed row.
    /// </para>
    /// </summary>
    public async Task UpdateTabTitleAsync(long entryId, string newName)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        long? sessionId;
        await using (var lookupCmd = new SqliteCommand(
            "SELECT session_id FROM history WHERE id = @id;", conn))
        {
            lookupCmd.Parameters.AddWithValue("@id", entryId);
            var result = await lookupCmd.ExecuteScalarAsync();
            sessionId = result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
        }

        if (sessionId.HasValue)
        {
            await using var cmd = new SqliteCommand(
                "UPDATE query_sessions SET name = @name, name_source = 2 WHERE id = @sessionId;", conn);
            cmd.Parameters.AddWithValue("@name", newName);
            cmd.Parameters.AddWithValue("@sessionId", sessionId.Value);
            await cmd.ExecuteNonQueryAsync();
            Log.Debug(
                "History entry {Id}: renamed session {SessionId} to '{Name}' (name_source=2/manual)",
                entryId, sessionId.Value, newName);
            return;
        }

        await using var fallbackCmd = new SqliteCommand(
            "UPDATE history SET tab_title = @name WHERE content_hash = (SELECT content_hash FROM history WHERE id = @id);", conn);
        fallbackCmd.Parameters.AddWithValue("@name", newName);
        fallbackCmd.Parameters.AddWithValue("@id", entryId);
        await fallbackCmd.ExecuteNonQueryAsync();
        Log.Debug(
            "History entry {Id}: tab_title updated to '{Name}' (no session; applied to whole content_hash group)",
            entryId, newName);
    }

    /// <summary>
    /// Inserts a version snapshot for a history entry (for version history tracking).
    /// </summary>
    /// <summary>
    /// Finds the most recent history entry by <c>source</c> (the document's full path) and inserts
    /// a version snapshot. Used for auto-save on tab close / focus change (records as version, not
    /// a new entry). Returns true if a matching entry was found and a version was saved.
    /// <para>
    /// Keyed on <c>source</c>, NOT <c>tab_title</c>: since this branch (F1 fix), the shell sends
    /// <c>TabTitle</c> only for a document that is actually saved to disk (see
    /// <c>ExecutionCapture.OnAfterCommandExecute</c>), so an unsaved SSMS scratch tab's <c>tab_title</c>
    /// is NULL. <c>source</c> carries <c>activeDoc.FullName</c> unconditionally for both saved and
    /// unsaved documents, so it is the one identifier this lookup can always find a row by.
    /// </para>
    /// <para>
    /// Finding 4 (PR #249 review): the "most recent entry" lookup orders by <c>id DESC</c>, NOT
    /// <c>executed_at DESC</c>. <c>executed_at</c> is not uniformly formatted (see the class doc on
    /// <see cref="DeleteEntriesOlderThanAsync"/>) -- this very method rewrites it to a
    /// space-separated form via <c>datetime('now')</c> a few lines below, while a fresh
    /// <see cref="InsertEntryAsync"/> row is ISO 'o'. Once a row has been snapshotted once (and so
    /// carries the space format), a raw <c>ORDER BY executed_at DESC</c> stops picking it back up
    /// on the NEXT snapshot -- a space-format timestamp always sorts lexicographically BELOW any
    /// ISO ('T'-separated) one sharing the same calendar date, so an older, never-snapshotted ISO
    /// row would win instead. <c>id</c> (INTEGER PRIMARY KEY AUTOINCREMENT) is insertion order and
    /// is immune to the format problem entirely.
    /// </para>
    /// <para>
    /// <b>Known remaining limitation</b> (documented, not fixed here): this lookup has no
    /// session/day scoping. Reopening a saved <c>.sql</c> file mints a NEW query session
    /// (<see cref="AkmlSql.Shell.Shared.History.DocumentSessionKeys.Forget"/> /
    /// re-mint on next execution) but does not itself insert a fresh <c>history</c> row -- only an
    /// actual execution does. If the tab is auto-saved (tab-close / focus-change) BEFORE any
    /// execution happens in the new session, this lookup still finds and reattaches to the
    /// PREVIOUS session's newest row for the same <c>source</c>, because <c>id DESC</c> answers
    /// "the newest row for this path" scoped only by <c>source</c>, not by session. Scoping this
    /// to "the newest row's own session" would need the caller (<c>ExecutionCapture.SaveVersionSnapshot</c>
    /// / <see cref="HistoryRequestHandler"/>'s SaveVersion action) to thread a SessionKey through
    /// <c>HistoryActionRequest</c>, which today only carries <c>NewName</c> (repurposed to carry
    /// the source path) and <c>SqlText</c> for this action -- adding that field is a real IPC
    /// surface change, not a same-file fix, so it is left as a follow-up rather than invented here.
    /// </para>
    /// </summary>
    public async Task<bool> SaveVersionBySourceAsync(string source, string sqlText, string? sessionKey = null)
    {
        if ((string.IsNullOrEmpty(source) && string.IsNullOrEmpty(sessionKey)) || string.IsNullOrWhiteSpace(sqlText)) return false;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Spec 040 (T064): the session's own newest row when the shell knows the session — the
        // path alone can match another session's row for the same file. Otherwise the most recent
        // entry for this document, by id (insertion order; see the Finding 4 remarks above).
        object? result = null;
        if (!string.IsNullOrEmpty(sessionKey))
        {
            await using var bySession = new SqliteCommand(@"
                SELECT h.id FROM history h JOIN query_sessions qs ON qs.id = h.session_id
                 WHERE qs.session_key = @key ORDER BY h.id DESC LIMIT 1", conn);
            bySession.Parameters.AddWithValue("@key", sessionKey);
            result = await bySession.ExecuteScalarAsync();
        }
        if (result == null && !string.IsNullOrEmpty(source))
        {
            await using var findCmd = new SqliteCommand(
                "SELECT id FROM history WHERE source = @source ORDER BY id DESC LIMIT 1", conn);
            findCmd.Parameters.AddWithValue("@source", source);
            result = await findCmd.ExecuteScalarAsync();
        }
        if (result == null) return false;

        var historyId = Convert.ToInt64(result);
        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var hash = ComputeContentHash(sqlText);
        await using var insertCmd = new SqliteCommand(@"
            INSERT INTO history_versions (history_id, sql_text, saved_at, content_hash)
            VALUES (@historyId, @sqlText, @savedAt, @hash);", conn);
        insertCmd.Parameters.AddWithValue("@historyId", historyId);
        insertCmd.Parameters.AddWithValue("@sqlText", sqlText);
        insertCmd.Parameters.AddWithValue("@savedAt", now);
        insertCmd.Parameters.AddWithValue("@hash", hash);
        await insertCmd.ExecuteNonQueryAsync();

        // Also update the main entry to the latest version. Same ISO "o" timestamp and content
        // hash the insert path writes (v2 used datetime('now') and left the hash stale); the
        // history_au trigger updates the search index.
        await using var updateCmd = new SqliteCommand(
            "UPDATE history SET sql_text = @sql, executed_at = @now, content_hash = @hash WHERE id = @id", conn);
        updateCmd.Parameters.AddWithValue("@sql", sqlText);
        updateCmd.Parameters.AddWithValue("@now", now);
        updateCmd.Parameters.AddWithValue("@hash", hash);
        updateCmd.Parameters.AddWithValue("@id", historyId);
        await updateCmd.ExecuteNonQueryAsync();

        Log.Debug("History: saved version snapshot for source '{Source}' (entry {Id})", source, historyId);
        return true;
    }

    public async Task InsertVersionAsync(long historyId, string sqlText)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(@"
            INSERT INTO history_versions (history_id, sql_text, saved_at, content_hash)
            VALUES (@historyId, @sqlText, @savedAt, @hash);", conn);
        cmd.Parameters.AddWithValue("@historyId", historyId);
        cmd.Parameters.AddWithValue("@sqlText", sqlText);
        cmd.Parameters.AddWithValue("@savedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@hash", ComputeContentHash(sqlText));

        await cmd.ExecuteNonQueryAsync();
        Log.Debug("History version inserted for entry {Id}", historyId);
    }

    /// <summary>
    /// Retrieves all version snapshots for a history entry, ordered by save time descending.
    /// Returns a list of (id, sqlText, savedAt) tuples.
    /// </summary>
    public async Task<List<(long Id, string SqlText, string SavedAt)>> GetVersionsAsync(long historyId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var versions = new List<(long, string, string)>();
        await using var cmd = new SqliteCommand(
            "SELECT id, sql_text, saved_at FROM history_versions WHERE history_id = @historyId ORDER BY saved_at DESC, id DESC;", conn);
        cmd.Parameters.AddWithValue("@historyId", historyId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return versions;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Debug("HistoryDatabase: disposed");
        // SqliteConnection is opened per-call, no persistent connection to dispose
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Creates a clone of a SqliteParameter so it can be reused across multiple commands.
    /// SQLite parameters are bound to a single command; cloning avoids "already belongs" errors.
    /// </summary>
    private static SqliteParameter CloneParameter(SqliteParameter source)
    {
        return new SqliteParameter(source.ParameterName, source.Value);
    }

    /// <summary>
    /// Sanitizes a search query for FTS5: removes characters that are special to FTS5 syntax
    /// (colons, parentheses, carets, curly braces, square brackets) while preserving
    /// valid FTS5 operators: * (prefix wildcard), " (phrase quotes), OR/NOT/AND (boolean keywords).
    /// Unbalanced double quotes are fixed by appending a closing quote.
    /// </summary>
    /// <summary>
    /// Spec 040 (HIS-07, data-model §2.4) — the free-text clause, with the full-text index and as a
    /// LIKE-only fallback. Plain words: every word must match the SQL (full-text phrase) or the name,
    /// source, server or database. A query using FTS5 syntax (OR, NOT, quotes, *) keeps it: the
    /// whole query must match the SQL, or every word must match the metadata.
    /// </summary>
    private static (string? Fts, string? Like) BuildSearchClauses(string searchText, List<SqliteParameter> parameters)
    {
        var terms = AkmlSql.Core.Text.HistorySearchTerms.Extract(searchText);
        var metadata = new List<string>();
        var sqlLike = new List<string>();
        for (var i = 0; i < terms.Count; i++)
        {
            var p = "@term" + i;
            parameters.Add(new SqliteParameter(p, "%" + EscapeLike(terms[i]) + "%"));
            metadata.Add($@"(COALESCE(qs.name, h.tab_title, '') LIKE {p} ESCAPE '\' OR h.source LIKE {p} ESCAPE '\' " +
                         $@"OR h.server LIKE {p} ESCAPE '\' OR h.database_name LIKE {p} ESCAPE '\')");
            sqlLike.Add($@"h.sql_text LIKE {p} ESCAPE '\'");
        }
        if (terms.Count == 0) return (null, null);

        var like = "(" + string.Join(" AND ", metadata.Select((m, i) => $"({sqlLike[i]} OR {m})")) + ")";

        if (UsesFtsSyntax(searchText))
        {
            var sanitized = SanitizeFts5Query(searchText);
            if (string.IsNullOrWhiteSpace(sanitized)) return (like, like);
            parameters.Add(new SqliteParameter("@fts0", sanitized));
            var fts = $"(h.id IN (SELECT rowid FROM history_fts WHERE history_fts MATCH @fts0) OR ({string.Join(" AND ", metadata)}))";
            return (fts, like);
        }

        var perTerm = new List<string>();
        for (var i = 0; i < terms.Count; i++)
        {
            parameters.Add(new SqliteParameter("@fts" + i, "\"" + terms[i].Replace("\"", "") + "\""));
            perTerm.Add($"(h.id IN (SELECT rowid FROM history_fts WHERE history_fts MATCH @fts{i}) OR {metadata[i]})");
        }
        return ("(" + string.Join(" AND ", perTerm) + ")", like);
    }

    private static readonly Regex FtsSyntax = new Regex(@"(^|\s)(OR|NOT|AND)(\s|$)|[""*]", RegexOptions.Compiled);

    private static bool UsesFtsSyntax(string text) => FtsSyntax.IsMatch(text);

    /// <summary>Escapes LIKE wildcards so a typed % or _ is matched literally (ESCAPE '\').</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string SanitizeFts5Query(string query)
    {
        // Characters that are special in FTS5 and must be removed:
        // : (column filter), ( ) (grouping), ^ (initial token), { } [ ] (reserved)
        var sb = new StringBuilder(query.Length);
        int quoteCount = 0;
        for (int i = 0; i < query.Length; i++)
        {
            var c = query[i];
            switch (c)
            {
                case ':':
                case '(':
                case ')':
                case '^':
                case '{':
                case '}':
                case '[':
                case ']':
                    // Strip these FTS5-unsafe characters
                    sb.Append(' ');
                    break;
                case '"':
                    quoteCount++;
                    sb.Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        // Balance unmatched quotes — FTS5 throws on unbalanced double quotes
        if (quoteCount % 2 != 0)
            sb.Append('"');

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Returns true if the SQL text contains words matching ALL CamelCase tokens.
    /// Each token (e.g., "PC") is checked against every word in the SQL text at
    /// CamelCase and underscore boundaries.
    /// </summary>
    private static bool MatchesAllCamelCaseTokens(string? sqlText, string[] tokens)
    {
        if (string.IsNullOrEmpty(sqlText))
            return false;

        // Extract words from SQL text (identifiers, keywords — split on whitespace and SQL punctuation)
        var words = ExtractWords(sqlText);

        foreach (var token in tokens)
        {
            bool found = false;
            foreach (var word in words)
            {
                if (MatchesCamelCaseToken(word, token))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts identifier-like words from SQL text by splitting on whitespace and
    /// SQL punctuation characters (commas, parentheses, semicolons, operators, etc.).
    /// </summary>
    private static List<string> ExtractWords(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                if (sb.Length > 0)
                {
                    words.Add(sb.ToString());
                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
        {
            words.Add(sb.ToString());
        }

        return words;
    }

    /// <summary>
    /// CamelCase / underscore boundary matching.
    /// Extracts initials from word boundaries in <paramref name="word"/> and checks if
    /// <paramref name="token"/> matches them as a prefix (case-insensitive).
    /// Examples: "PC" matches "ProductCategory", "GCO" matches "GetCustomerOrders",
    /// "SC" matches "sys_columns", "pc" matches "price_calculator".
    /// </summary>
    private static bool MatchesCamelCaseToken(string word, string token)
    {
        if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(token))
            return false;

        // Extract initials: first char + each char at an uppercase boundary or after underscore
        var initials = new char[word.Length];
        int count = 0;
        initials[count++] = word[0];

        for (int i = 1; i < word.Length; i++)
        {
            var c = word[i];
            // Uppercase letter after lowercase = CamelCase boundary
            if (char.IsUpper(c) && char.IsLower(word[i - 1]))
            {
                initials[count++] = c;
            }
            // Letter/digit after underscore = boundary
            else if (char.IsLetterOrDigit(c) && word[i - 1] == '_')
            {
                initials[count++] = c;
            }
        }

        if (count < token.Length)
            return false;

        // Check if token matches the initials as a prefix (case-insensitive)
        for (int i = 0; i < token.Length; i++)
        {
            if (char.ToUpperInvariant(token[i]) != char.ToUpperInvariant(initials[i]))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Internal model for history export entries containing full (non-truncated) SQL text.
/// </summary>
internal sealed class HistoryExportEntry
{
    public long Id { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public string? Server { get; set; }
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string ExecutedAt { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public long RowCount { get; set; }
    public int Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Source { get; set; }
    public string? TabTitle { get; set; }
    public bool IsFavorite { get; set; }
    public string? ContentHash { get; set; }
}
