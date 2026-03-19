using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Schema;

/// <summary>
/// T075: Detects schema changes using sys.objects checksum comparison.
/// T077: Detects DDL statements (CREATE/ALTER/DROP) in query text.
/// </summary>
public partial class ChangeDetector
{
    /// <summary>
    /// Checks whether the database schema has changed since the last cache refresh.
    /// Uses CHECKSUM_AGG over sys.objects to detect modifications efficiently.
    /// </summary>
    public async Task<bool> CheckForChangesAsync(string connectionString, DatabaseCache cache, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Compute aggregate checksum of all user objects
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT CHECKSUM_AGG(BINARY_CHECKSUM(object_id, CAST(modify_date AS INT), CAST(type AS INT)))
                FROM sys.objects
                WHERE is_ms_shipped = 0";
            cmd.CommandTimeout = 10;

            var result = await cmd.ExecuteScalarAsync(ct);
            var currentChecksum = result is DBNull || result is null ? 0 : (int)result;

            if (currentChecksum == cache.LastChangeChecksum)
            {
                Log.Debug("ChangeDetector: no changes detected for {Key} (checksum={Checksum})",
                    cache.CacheKey, currentChecksum);
                return false;
            }

            Log.Information("ChangeDetector: schema changes detected for {Key} (old={Old}, new={New})",
                cache.CacheKey, cache.LastChangeChecksum, currentChecksum);

            // Identify which objects changed
            var changedObjects = await GetChangedObjectIdsAsync(conn, cache.LastFullRefresh, ct);
            Log.Information("ChangeDetector: {Count} objects changed since last refresh", changedObjects.Count);

            cache.LastChangeChecksum = currentChecksum;
            cache.IsStale = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ChangeDetector: failed to check for changes on {Key}", cache.CacheKey);
            return false;
        }
    }

    /// <summary>
    /// Returns the object_ids that have been modified since the last refresh timestamp.
    /// </summary>
    private static async Task<List<int>> GetChangedObjectIdsAsync(SqlConnection conn, DateTime lastRefresh, CancellationToken ct)
    {
        var objectIds = new List<int>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT object_id
            FROM sys.objects
            WHERE modify_date > @LastRefresh AND is_ms_shipped = 0";
        cmd.Parameters.AddWithValue("@LastRefresh", lastRefresh);
        cmd.CommandTimeout = 10;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            objectIds.Add(reader.GetInt32(0));
        }

        return objectIds;
    }

    /// <summary>
    /// T077: Scans query text for DDL keywords (CREATE, ALTER, DROP) at the start of statements.
    /// Returns true if DDL was detected, indicating that schema cache should be refreshed.
    /// </summary>
    public static bool DetectDdlInQuery(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return false;

        return DdlPattern().IsMatch(queryText);
    }

    // Matches CREATE/ALTER/DROP at the start of a line (possibly preceded by whitespace),
    // followed by a DDL target keyword.
    [GeneratedRegex(
        @"^\s*(CREATE|ALTER|DROP)\s+(TABLE|VIEW|PROCEDURE|PROC|FUNCTION|INDEX|TRIGGER|SCHEMA|TYPE|SEQUENCE|SYNONYM)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DdlPattern();
}
