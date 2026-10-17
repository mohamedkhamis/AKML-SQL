using AkmlSql.Core.Connections;
using Npgsql;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T080, FR-043, contracts/catalog-queries.md "Change detection") - the PostgreSQL counterpart of the SQL Server
/// <c>CHECKSUM_AGG</c> probe: two cheap hashes over the catalog, one of the relations (tables, views, materialized views,
/// sequences) and one of the functions and procedures. They change when an object is created, dropped or renamed. A column
/// change does not move them; the in-product trigger (a script that runs DDL) and a manual refresh cover that.
/// </summary>
public static class PgChangeChecksum
{
    private const string RelationsSql =
        "SELECT md5(coalesce(string_agg(c.oid::text || ':' || c.relname || ':' || c.relkind::text || ':' || c.relnamespace::text, ',' ORDER BY c.oid), '')) " +
        "FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
        "WHERE c.relkind IN ('r','p','f','v','m','S') AND n.nspname NOT LIKE 'pg\\_%' AND n.nspname <> 'information_schema'";

    private const string RoutinesSql =
        "SELECT md5(coalesce(string_agg(p.oid::text || ':' || p.proname || ':' || p.pronamespace::text, ',' ORDER BY p.oid), '')) " +
        "FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace " +
        "WHERE n.nspname NOT LIKE 'pg\\_%' AND n.nspname <> 'information_schema'";

    /// <summary>Opens a connection, reads both hashes and folds them into the int the cache keeps.</summary>
    public static async Task<int> ComputeAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = PgConnectionFactory.Create(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return await ComputeAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>The checksum over an open connection (the session's own, when it is the one being asked).</summary>
    public static async Task<int> ComputeAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var relations = await ScalarAsync(connection, RelationsSql, ct).ConfigureAwait(false);
        var routines = await ScalarAsync(connection, RoutinesSql, ct).ConfigureAwait(false);
        return Fold(relations, routines);
    }

    /// <summary>
    /// Folds the two 32-digit hex hashes into one int: each contributes its first and last 8 digits, mixed so that a change
    /// in either hash changes the result. Deterministic across runs and machines, which a persisted cache needs.
    /// </summary>
    public static int Fold(string relationsHash, string routinesHash)
    {
        unchecked
        {
            var value = 17;
            foreach (var hash in new[] { relationsHash, routinesHash })
            {
                value = value * 31 + Eight(hash, 0);
                value = value * 31 + Eight(hash, Math.Max(0, hash.Length - 8));
            }
            return value;
        }
    }

    private static int Eight(string hash, int from)
    {
        if (string.IsNullOrEmpty(hash)) return 0;
        var span = hash.AsSpan(from, Math.Min(8, hash.Length - from));
        return int.TryParse(span, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 };
        return (await command.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString() ?? string.Empty;
    }
}
