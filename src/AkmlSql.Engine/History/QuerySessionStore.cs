using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AkmlSql.Engine.History;

/// <summary>
/// Owns the <c>query_sessions</c> table: maps a client-supplied SessionKey to a session row,
/// assigning the per-local-day ordinal and display name on first sight.
/// </summary>
internal sealed class QuerySessionStore
{
    // SQLITE_CONSTRAINT_UNIQUE — the EXTENDED code. The primary code (19) is shared by NOT NULL,
    // FOREIGN KEY (PRAGMA foreign_keys=ON is set on this connection), CHECK and PRIMARY KEY too,
    // so matching on the primary code alone would retry-then-swallow a real schema bug instead
    // of letting it surface.
    private const int SqliteConstraintUnique = 2067;
    // SQLITE_BUSY / SQLITE_BUSY_SNAPSHOT (primary code). A DEFERRED transaction that reads before
    // writing can be told to promote after a concurrent commit and get BUSY_SNAPSHOT instead of a
    // constraint violation; InsertAsync uses BEGIN IMMEDIATE to avoid that class of race entirely,
    // but the retry still needs to absorb whatever slips through busy_timeout.
    private const int SqliteBusy = 5;
    // SQLITE_LOCKED (primary code) — a conflicting lock held elsewhere on the same connection/table.
    private const int SqliteLocked = 6;

    private readonly string _connectionString;

    internal QuerySessionStore(string connectionString) => _connectionString = connectionString;

    /// <summary>
    /// Returns the id of the session for <paramref name="sessionKey"/>, creating it if new.
    /// On an existing session a real (non-scratch) title upgrades an auto name; a manual
    /// rename (name_source = 2) is never overwritten.
    /// </summary>
    internal async Task<long> GetOrCreateAsync(
        string sessionKey, DateTime executedAtUtc, string? tabTitle, string? server, string? database)
    {
        var localDate = QuerySessionNamer.LocalDateKey(executedAtUtc);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var existing = await FindAsync(conn, sessionKey);
        if (existing.HasValue)
        {
            await MaybeUpgradeNameAsync(conn, existing.Value, tabTitle);
            return existing.Value;
        }

        // Two windows can read the same MAX(ordinal); IX_qs_date_ordinal turns that into a
        // SQLITE_CONSTRAINT_UNIQUE. A concurrent caller creating THIS same session_key hits the
        // UNIQUE index on session_key instead — same extended code, different index. Either way
        // retry re-reads the new maximum / re-checks for the racer's row. Busy/locked codes can
        // also surface here if a competing writer's lock hasn't cleared yet.
        SqliteException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await InsertAsync(conn, sessionKey, localDate, tabTitle, server, database);
            }
            catch (SqliteException ex) when (IsRetryableRaceError(ex))
            {
                lastError = ex;
                var raced = await FindAsync(conn, sessionKey);
                if (raced.HasValue)
                {
                    await MaybeUpgradeNameAsync(conn, raced.Value, tabTitle);
                    return raced.Value;
                }
                // Ordinal collision (or a transient busy/locked writer) only — loop and retry.
            }
        }

        throw new InvalidOperationException(
            $"QuerySessionStore: could not allocate an ordinal for {localDate} after 5 attempts.",
            lastError);
    }

    private static bool IsRetryableRaceError(SqliteException ex) =>
        ex.SqliteExtendedErrorCode == SqliteConstraintUnique
        || ex.SqliteErrorCode == SqliteBusy
        || ex.SqliteErrorCode == SqliteLocked;

    private static async Task<long?> FindAsync(SqliteConnection conn, string sessionKey)
    {
        await using var cmd = new SqliteCommand(
            "SELECT id FROM query_sessions WHERE session_key = @key", conn);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt64(result);
    }

    private static async Task<long> InsertAsync(
        SqliteConnection conn, string sessionKey, string localDate,
        string? tabTitle, string? server, string? database)
    {
        var isScratch = QuerySessionNamer.IsScratchTabTitle(tabTitle);

        // BEGIN IMMEDIATE (deferred: false): the write lock is acquired up front, before the
        // MAX(ordinal) read below. A DEFERRED transaction reads first and only tries to acquire
        // the write lock at the INSERT — if another connection commits in between, promoting
        // that lock fails with SQLITE_BUSY_SNAPSHOT rather than the constraint violation this
        // retry loop is built to catch. IMMEDIATE removes that gap: by the time this read runs,
        // no concurrent writer can still be mid-flight against the same table.
        //
        // No explicit rollback here: `await using` disposes an uncommitted SqliteTransaction by
        // rolling it back. An explicit RollbackAsync in a catch, after CommitAsync itself threw,
        // would run against an already-completed transaction and raise a second exception that
        // masks the original one — so the automatic dispose-time rollback is relied on instead.
        await using var tx = conn.BeginTransaction(deferred: false);

        await using var maxCmd = new SqliteCommand(
            "SELECT COALESCE(MAX(ordinal), 0) + 1 FROM query_sessions WHERE local_date = @d",
            conn, tx);
        maxCmd.Parameters.AddWithValue("@d", localDate);
        var ordinal = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());

        var name = isScratch ? QuerySessionNamer.FormatName(ordinal) : tabTitle!.Trim();
        var nameSource = isScratch ? 0 : 1;

        await using var insert = new SqliteCommand(@"
            INSERT INTO query_sessions
                (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
            VALUES (@key, @d, @ord, @name, @src, @server, @db, @created);
            SELECT last_insert_rowid();", conn, tx);
        insert.Parameters.AddWithValue("@key", sessionKey);
        insert.Parameters.AddWithValue("@d", localDate);
        insert.Parameters.AddWithValue("@ord", ordinal);
        insert.Parameters.AddWithValue("@name", name);
        insert.Parameters.AddWithValue("@src", nameSource);
        insert.Parameters.AddWithValue("@server", (object?)server ?? DBNull.Value);
        insert.Parameters.AddWithValue("@db", (object?)database ?? DBNull.Value);
        insert.Parameters.AddWithValue("@created",
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        var id = Convert.ToInt64(await insert.ExecuteScalarAsync());
        await tx.CommitAsync();

        Log.Debug("QuerySession created: id={Id} name={Name} date={Date}", id, name, localDate);
        return id;
    }

    /// <summary>auto (0) → file (1) only. Manual (2) is final.</summary>
    private static async Task MaybeUpgradeNameAsync(SqliteConnection conn, long id, string? tabTitle)
    {
        if (QuerySessionNamer.IsScratchTabTitle(tabTitle)) return;

        await using var cmd = new SqliteCommand(@"
            UPDATE query_sessions
               SET name = @name, name_source = 1
             WHERE id = @id AND name_source = 0;", conn);
        cmd.Parameters.AddWithValue("@name", tabTitle!.Trim());
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
