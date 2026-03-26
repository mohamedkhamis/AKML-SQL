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
    private const int SchemaVersion = 1;
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
                is_favorite   INTEGER NOT NULL DEFAULT 0
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
            // Sanitize the search text for FTS5: escape double quotes and wrap terms
            var sanitized = filter.SearchText.Replace("\"", "\"\"");
            fromClause = "history h INNER JOIN history_fts fts ON h.id = fts.rowid";
            whereClauses.Add("history_fts MATCH @search");
            parameters.Add(new SqliteParameter("@search", $"\"{sanitized}\""));
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

        // Execute count query
        int totalCount;
        await using (var countCmd = new SqliteCommand(countSql, conn))
        {
            foreach (var p in parameters)
            {
                countCmd.Parameters.Add(CloneParameter(p));
            }
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // Build the data query
        string dataSql;
        if (filter.Deduplicate)
        {
            dataSql = $@"
                SELECT
                    MAX(h.id) as id,
                    substr(h.sql_text, 1, 500) as sql_text,
                    h.server,
                    h.database_name,
                    h.username,
                    MAX(h.executed_at) as executed_at,
                    h.duration_ms,
                    h.row_count,
                    h.status,
                    h.error_msg,
                    h.source,
                    h.tab_title,
                    h.is_favorite,
                    COUNT(*) as exec_count,
                    h.content_hash
                FROM {fromClause}
                {whereClause}
                GROUP BY h.content_hash
                ORDER BY MAX(h.executed_at) DESC
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
                    h.content_hash
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
                ContentHash = reader.IsDBNull(14) ? null : reader.GetString(14)
            });
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

        // Toggle: set is_favorite = 1 - is_favorite
        await using var cmd = new SqliteCommand(
            "UPDATE history SET is_favorite = 1 - is_favorite WHERE id = @id; SELECT is_favorite FROM history WHERE id = @id2;", conn);
        cmd.Parameters.AddWithValue("@id", entryId);
        cmd.Parameters.AddWithValue("@id2", entryId);

        var result = await cmd.ExecuteScalarAsync();
        var newState = result != null && Convert.ToInt32(result) != 0;

        Log.Debug("History entry {Id}: favorite toggled to {State}", entryId, newState);
        return newState;
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
