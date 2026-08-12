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
    private const int SqliteConstraint = 19;   // SQLITE_CONSTRAINT

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
        // constraint violation. Retry re-reads the new maximum. The second arm of the retry
        // also covers the case where a concurrent caller created THIS key first.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await InsertAsync(conn, sessionKey, localDate, tabTitle, server, database);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
            {
                var raced = await FindAsync(conn, sessionKey);
                if (raced.HasValue)
                {
                    await MaybeUpgradeNameAsync(conn, raced.Value, tabTitle);
                    return raced.Value;
                }
                // Ordinal collision only — loop and take the next one.
            }
        }

        throw new InvalidOperationException(
            $"QuerySessionStore: could not allocate an ordinal for {localDate} after 5 attempts.");
    }

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

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using var maxCmd = new SqliteCommand(
                "SELECT COALESCE(MAX(ordinal), 0) + 1 FROM query_sessions WHERE local_date = @d",
                conn, (SqliteTransaction)tx);
            maxCmd.Parameters.AddWithValue("@d", localDate);
            var ordinal = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());

            var name = isScratch ? QuerySessionNamer.FormatName(ordinal) : tabTitle!.Trim();
            var nameSource = isScratch ? 0 : 1;

            await using var insert = new SqliteCommand(@"
                INSERT INTO query_sessions
                    (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
                VALUES (@key, @d, @ord, @name, @src, @server, @db, @created);
                SELECT last_insert_rowid();", conn, (SqliteTransaction)tx);
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
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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
