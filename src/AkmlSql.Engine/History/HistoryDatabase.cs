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
    private const int SchemaVersion = 2;   // v2: query_sessions + history.session_id
    private const int MaxSqlTextChars = 1_048_576; // 1 MB

    private readonly string _connectionString;
    private bool _disposed;

    public HistoryDatabase()
    {
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AKML SQL", "history");
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

        // Insert schema_version if not present
        await ExecuteNonQueryAsync(conn, $@"
            INSERT OR IGNORE INTO metadata (key, value)
            VALUES ('schema_version', '{SchemaVersion}');");

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

        Log.Information("History database initialized at {ConnectionString}", _connectionString);
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
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AKML SQL", "history", "sqlhistory.db");
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
        string? tabTitle)
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

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            const string sql = @"
                INSERT INTO history (
                    sql_text, truncated, server, database_name, username,
                    executed_at, duration_ms, row_count, status, error_msg,
                    source, tab_title, content_hash, is_favorite
                ) VALUES (
                    @sqlText, @truncated, @server, @database, @username,
                    @executedAt, @durationMs, @rowCount, @status, @errorMsg,
                    @source, @tabTitle, @contentHash, 0
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

            var result = await cmd.ExecuteScalarAsync();
            var entryId = Convert.ToInt64(result);

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

        // FTS5 full-text search join
        var fromClause = "history h";
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // Sanitize for FTS5: remove unsafe special chars but preserve FTS5 syntax:
            //   * (prefix wildcard), " (phrase quotes), OR/NOT/AND (boolean operators)
            var sanitized = SanitizeFts5Query(filter.SearchText);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                fromClause = "history h INNER JOIN history_fts fts ON h.id = fts.rowid";
                whereClauses.Add("history_fts MATCH @search");
                parameters.Add(new SqliteParameter("@search", sanitized));
            }
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

        if (filter.IsOpen.HasValue)
        {
            whereClauses.Add("h.is_open = @isOpen");
            parameters.Add(new SqliteParameter("@isOpen", filter.IsOpen.Value ? 1 : 0));
        }

        if (!string.IsNullOrEmpty(filter.NameFilter))
        {
            whereClauses.Add("h.tab_title LIKE '%' || @nameFilter || '%'");
            parameters.Add(new SqliteParameter("@nameFilter", filter.NameFilter));
        }

        var whereClause = whereClauses.Count > 0
            ? "WHERE " + string.Join(" AND ", whereClauses)
            : "";

        // Build the count query
        string countSql;
        if (filter.Deduplicate)
        {
            countSql = $"SELECT COUNT(DISTINCT h.content_hash) FROM {fromClause} {whereClause}";
        }
        else
        {
            countSql = $"SELECT COUNT(*) FROM {fromClause} {whereClause}";
        }

        // Execute count query — wrap in try/catch for FTS5 parse error fallback.
        // Despite quote-balancing in SanitizeFts5Query, other malformed queries can still
        // cause FTS5 to throw (e.g., dangling operators like trailing OR/NOT).
        int totalCount;
        try
        {
            await using (var countCmd = new SqliteCommand(countSql, conn))
            {
                foreach (var p in parameters)
                {
                    countCmd.Parameters.Add(CloneParameter(p));
                }
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            // FTS5 parse error — fall back to LIKE-based search
            Log.Warning(ex, "FTS5 query parse error for '{SearchText}', falling back to LIKE search",
                filter.SearchText);

            // Rebuild without FTS5: replace MATCH join with LIKE on sql_text
            fromClause = "history h";
            var likeSearch = "%" + filter.SearchText!.Replace("%", "").Replace("_", "") + "%";
            whereClauses.RemoveAll(c => c.Contains("history_fts MATCH"));
            parameters.RemoveAll(p => p.ParameterName == "@search");
            whereClauses.Add("h.sql_text LIKE @searchLike");
            parameters.Add(new SqliteParameter("@searchLike", likeSearch));

            whereClause = whereClauses.Count > 0
                ? "WHERE " + string.Join(" AND ", whereClauses)
                : "";
            countSql = filter.Deduplicate
                ? $"SELECT COUNT(DISTINCT h.content_hash) FROM {fromClause} {whereClause}"
                : $"SELECT COUNT(*) FROM {fromClause} {whereClause}";

            await using var fallbackCountCmd = new SqliteCommand(countSql, conn);
            foreach (var p in parameters)
                fallbackCountCmd.Parameters.Add(CloneParameter(p));
            totalCount = Convert.ToInt32(await fallbackCountCmd.ExecuteScalarAsync());
        }

        // Build the data query
        string dataSql;
        if (filter.Deduplicate)
        {
            // Deduplicated view: one representative row per content_hash = the MOST RECENT execution,
            // chosen deterministically by ROW_NUMBER (latest executed_at, id as tiebreak). Every scalar
            // column therefore comes from that single latest row. This replaces the prior
            // GROUP-BY-with-bare-columns query, where SQLite (with several MAX() aggregates present)
            // pulled name/status/row-count/duration from an ARBITRARY row in the group — so a repeated
            // query could show a stale status or the wrong duration. exec_count is the number of
            // executions MATCHING THE CURRENT FILTER (equal to the total when unfiltered, because
            // COUNT(*) OVER runs after {whereClause}); favourite/open are "any version" (MAX over the
            // partition, matching the FavoritesOnly filter); and the display name is the latest NON-NULL
            // tab_title within the filtered partition so a rename survives later re-executions. The
            // tab_title is a WINDOW column computed INSIDE the ranked subquery so it respects
            // {whereClause} (a correlated subquery over the bare table would ignore the filters). The
            // {whereClause} filters live INSIDE the windowed subquery so
            // COUNT()/ROW_NUMBER() see the filtered set; only `rn = 1` is applied outside.
            dataSql = $@"
                SELECT
                    ranked.id,
                    substr(ranked.sql_text, 1, 500) as sql_text,
                    ranked.server,
                    ranked.database_name,
                    ranked.username,
                    ranked.executed_at,
                    ranked.duration_ms,
                    ranked.row_count,
                    ranked.status,
                    ranked.error_msg,
                    ranked.source,
                    ranked.tab_title,
                    ranked.is_favorite,
                    ranked.exec_count,
                    ranked.content_hash,
                    ranked.is_open
                FROM (
                    SELECT
                        h.id,
                        h.sql_text,
                        h.server,
                        h.database_name,
                        h.username,
                        h.executed_at,
                        h.duration_ms,
                        h.row_count,
                        h.status,
                        h.error_msg,
                        h.source,
                        h.content_hash,
                        COUNT(*)           OVER (PARTITION BY h.content_hash) as exec_count,
                        MAX(h.is_favorite) OVER (PARTITION BY h.content_hash) as is_favorite,
                        MAX(h.is_open)     OVER (PARTITION BY h.content_hash) as is_open,
                        FIRST_VALUE(h.tab_title) OVER (
                            PARTITION BY h.content_hash
                            ORDER BY (CASE WHEN h.tab_title IS NULL THEN 1 ELSE 0 END), h.executed_at DESC, h.id DESC
                            ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING
                        ) as tab_title,
                        ROW_NUMBER()       OVER (PARTITION BY h.content_hash
                                                 ORDER BY h.executed_at DESC, h.id DESC) as rn
                    FROM {fromClause}
                    {whereClause}
                ) AS ranked
                WHERE ranked.rn = 1
                ORDER BY ranked.executed_at DESC, ranked.id DESC
                LIMIT @limit OFFSET @offset";
        }
        else
        {
            dataSql = $@"
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
                    h.tab_title,
                    h.is_favorite,
                    1 as exec_count,
                    h.content_hash,
                    h.is_open
                FROM {fromClause}
                {whereClause}
                ORDER BY h.executed_at DESC
                LIMIT @limit OFFSET @offset";
        }

        var entries = new List<HistoryEntryDto>();
        await using var dataCmd = new SqliteCommand(dataSql, conn);
        foreach (var p in parameters)
        {
            dataCmd.Parameters.Add(CloneParameter(p));
        }
        dataCmd.Parameters.AddWithValue("@limit", filter.Limit);
        dataCmd.Parameters.AddWithValue("@offset", filter.Offset);

        await using var reader = await dataCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
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
                ContentHash = reader.IsDBNull(14) ? null : reader.GetString(14),
                IsOpen = reader.GetInt32(15) != 0
            });
        }

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

    /// <summary>
    /// Marks all entries with a given tab title as closed (is_open = 0).
    /// </summary>
    public async Task CloseByTabTitleAsync(string tabTitle)
    {
        if (string.IsNullOrEmpty(tabTitle)) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(
            "UPDATE history SET is_open = 0 WHERE tab_title = @title AND is_open = 1", conn);
        cmd.Parameters.AddWithValue("@title", tabTitle);
        await cmd.ExecuteNonQueryAsync();
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
    /// rows are ISO 'o' (<see cref="InsertEntryAsync"/>) but <c>SaveVersionByTabTitleAsync</c> rewrites
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
            whereClauses.Add("h.tab_title LIKE '%' || @nameFilter || '%'");
            parameters.Add(new SqliteParameter("@nameFilter", filter.NameFilter));
        }

        var whereClause = whereClauses.Count > 0
            ? "WHERE " + string.Join(" AND ", whereClauses)
            : "";

        var sql = $@"
            SELECT
                h.id, h.sql_text, h.server, h.database_name, h.username,
                h.executed_at, h.duration_ms, h.row_count, h.status,
                h.error_msg, h.source, h.tab_title, h.is_favorite, h.content_hash
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
    /// Updates the tab_title (display name) for a history entry.
    /// Used by the "Rename" feature for closed queries.
    /// <para>
    /// The rename is applied to EVERY row sharing the target entry's <c>content_hash</c> (the whole
    /// deduplication group), not just the single row identified by <paramref name="entryId"/>. The
    /// display name is a query-level label: the deduplicated search derives it via a window function
    /// over the filtered partition, so a per-row name would vanish whenever a filter excludes the
    /// renamed row (e.g. a server filter that hides the exact execution that was renamed). Stamping
    /// the name on all rows of the group makes it consistent across executions and filters. This
    /// cannot reintroduce the old "name bleeds across a name filter" bug, because every row of the
    /// content_hash carries the SAME name. The AFTER UPDATE FTS sync (if any) still fires per row.
    /// </para>
    /// </summary>
    public async Task UpdateTabTitleAsync(long entryId, string newName)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(
            "UPDATE history SET tab_title = @name WHERE content_hash = (SELECT content_hash FROM history WHERE id = @id);", conn);
        cmd.Parameters.AddWithValue("@name", newName);
        cmd.Parameters.AddWithValue("@id", entryId);

        await cmd.ExecuteNonQueryAsync();
        Log.Debug("History entry {Id}: tab_title updated to '{Name}' (applied to whole content_hash group)", entryId, newName);
    }

    /// <summary>
    /// Inserts a version snapshot for a history entry (for version history tracking).
    /// </summary>
    /// <summary>
    /// Finds the most recent history entry by tab title and inserts a version snapshot.
    /// Used for auto-save on tab close / focus change (records as version, not new entry).
    /// Returns true if a matching entry was found and a version was saved.
    /// </summary>
    public async Task<bool> SaveVersionByTabTitleAsync(string tabTitle, string sqlText)
    {
        if (string.IsNullOrEmpty(tabTitle) || string.IsNullOrWhiteSpace(sqlText)) return false;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Find the most recent entry for this tab
        await using var findCmd = new SqliteCommand(
            "SELECT id FROM history WHERE tab_title = @title ORDER BY executed_at DESC LIMIT 1", conn);
        findCmd.Parameters.AddWithValue("@title", tabTitle);
        var result = await findCmd.ExecuteScalarAsync();
        if (result == null) return false;

        var historyId = Convert.ToInt64(result);
        await using var insertCmd = new SqliteCommand(@"
            INSERT INTO history_versions (history_id, sql_text)
            VALUES (@historyId, @sqlText);", conn);
        insertCmd.Parameters.AddWithValue("@historyId", historyId);
        insertCmd.Parameters.AddWithValue("@sqlText", sqlText);
        await insertCmd.ExecuteNonQueryAsync();

        // Also update the main entry's sql_text to the latest version
        await using var updateCmd = new SqliteCommand(
            "UPDATE history SET sql_text = @sql, executed_at = datetime('now') WHERE id = @id", conn);
        updateCmd.Parameters.AddWithValue("@sql", sqlText);
        updateCmd.Parameters.AddWithValue("@id", historyId);
        await updateCmd.ExecuteNonQueryAsync();

        Log.Debug("History: saved version snapshot for tab '{Title}' (entry {Id})", tabTitle, historyId);
        return true;
    }

    public async Task InsertVersionAsync(long historyId, string sqlText)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new SqliteCommand(@"
            INSERT INTO history_versions (history_id, sql_text)
            VALUES (@historyId, @sqlText);", conn);
        cmd.Parameters.AddWithValue("@historyId", historyId);
        cmd.Parameters.AddWithValue("@sqlText", sqlText);

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
            "SELECT id, sql_text, saved_at FROM history_versions WHERE history_id = @historyId ORDER BY saved_at DESC;", conn);
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
