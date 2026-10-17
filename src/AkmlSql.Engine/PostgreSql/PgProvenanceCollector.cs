using AkmlSql.Core.Ipc.Messages;
using Npgsql;
using Npgsql.Schema;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>The words a PostgreSQL result set gives for not being editable (FR-130). The grid shows them where it would otherwise only say "can't be edited".</summary>
public static class PgReadOnlyReasons
{
    public const string NotATable = "The result is not a table: its columns are computed.";
    public const string ManyTables = "The result comes from more than one table.";
    public const string View = "The result comes from a view, and a view cannot be edited.";
    public const string NoPrimaryKey = "The table has no primary key, so a row cannot be identified.";
    public const string DuplicateColumn = "The same column appears twice in the result.";
    public const string Unreadable = "Where the columns come from could not be read.";

    public static string KeyMissing(IReadOnlyList<string> columns, bool wholeKey) =>
        wholeKey
            ? $"The result does not include the table's primary key ({string.Join(", ", columns)})."
            : $"The result leaves out part of the table's primary key ({string.Join(", ", columns)}).";
}

/// <summary>
/// Spec 042 (task T138, FR-130, research R11, spike S6). Works out whether a PostgreSQL result set can be edited, in two steps
/// because the rows come from the reader and the answer from the catalog, which the connection can serve only once the reader is closed:
/// <list type="number">
///   <item><see cref="CaptureAsync"/> (reader open): per column, the table the server says it came from (the table OID in the row
///   description, 0 for an expression) and its attribute number in that table. The driver reports both without being asked for more,
///   for every kind of relation, so partitioned and foreign tables are described as well as ordinary ones.</item>
///   <item><see cref="ResolveAsync"/> (reader closed): one catalog read per table - its kind, real name, primary key, and for each
///   column its real name, nullability, identity, generation and sequence default - then the verdict: editable when it is one ordinary
///   or partitioned table and every primary key column is in the result; otherwise read-only with the reason.</item>
/// </list>
/// The names come from the catalog, never from the driver's own schema query, so an alias is still the real column, quoted and
/// mixed-case names arrive exactly, and no second connection is opened. A failure anywhere leaves the set read-only with a reason
/// and never disturbs the data that was read. Needs PostgreSQL 12 or later (generated columns).
/// </summary>
public sealed class PgProvenanceCollector
{
    private sealed record Source(uint Oid, int Attnum);

    private sealed class Pending
    {
        public required ExecuteResultSet Set { get; init; }
        public required ColumnProvenanceDto[] Columns { get; init; }
        public required Source?[] Sources { get; init; }       // null for an expression
    }

    private readonly List<Pending> _pending = new();

    /// <summary>Reads where the reader's current columns come from.</summary>
    public async Task CaptureAsync(NpgsqlDataReader reader, ExecuteResultSet set, CancellationToken ct)
    {
        try
        {
            var schema = (await reader.GetColumnSchemaAsync(ct).ConfigureAwait(false)).OfType<NpgsqlDbColumn>().ToArray();
            var columns = new ColumnProvenanceDto[schema.Length];
            var sources = new Source?[schema.Length];

            for (var i = 0; i < schema.Length; i++)
            {
                var attnum = (int)(schema[i].ColumnAttributeNumber ?? 0);
                var isExpression = schema[i].TableOID == 0 || attnum <= 0;
                sources[i] = isExpression ? null : new Source(schema[i].TableOID, attnum);
                columns[i] = new ColumnProvenanceDto
                {
                    Ordinal = i,
                    IsReadOnly = isExpression,
                    IsExpression = isExpression,
                    AllowDBNull = true,
                };
            }

            set.Provenance = columns;
            _pending.Add(new Pending { Set = set, Columns = columns, Sources = sources });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "PgProvenanceCollector: the column schema could not be read - the set stays read-only.");
            set.Provenance = Array.Empty<ColumnProvenanceDto>();
            set.IsEditable = false;
            set.ReadOnlyReason = PgReadOnlyReasons.Unreadable;
        }
    }

    /// <summary>Decides editability for every captured set. The connection must be free (the reader closed).</summary>
    public async Task ResolveAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        foreach (var pending in _pending)
        {
            ct.ThrowIfCancellationRequested();
            try { await ResolveOneAsync(connection, pending, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Debug(ex, "PgProvenanceCollector: the table could not be inspected - the set stays read-only.");
                Refuse(pending, PgReadOnlyReasons.Unreadable);
            }
        }
        _pending.Clear();
    }

    private static async Task ResolveOneAsync(NpgsqlConnection connection, Pending pending, CancellationToken ct)
    {
        var oids = pending.Sources.Where(s => s != null).Select(s => s!.Oid).Distinct().ToList();
        if (oids.Count == 0) { Refuse(pending, PgReadOnlyReasons.NotATable); return; }
        if (oids.Count > 1) { Refuse(pending, PgReadOnlyReasons.ManyTables); return; }

        var info = await ReadTableAsync(connection, oids[0], ct).ConfigureAwait(false);
        if (info == null) { Refuse(pending, PgReadOnlyReasons.Unreadable); return; }

        // A view or a materialized view is its own relation and cannot be written by key; so is a foreign table.
        if (info.Kind is not ("r" or "p")) { Refuse(pending, PgReadOnlyReasons.View); return; }

        var seen = new HashSet<int>();
        for (var i = 0; i < pending.Columns.Length; i++)
        {
            var source = pending.Sources[i];
            if (source == null) continue;

            if (!info.Columns.TryGetValue(source.Attnum, out var column)) { Refuse(pending, PgReadOnlyReasons.Unreadable); return; }
            if (!seen.Add(source.Attnum)) { Refuse(pending, PgReadOnlyReasons.DuplicateColumn); return; }

            var dto = pending.Columns[i];
            dto.BaseColumnName = column.Name;
            dto.AllowDBNull = !column.NotNull;
            dto.IsAutoIncrement = column.IsIdentity || column.IsSequenceDefault;
            dto.IsReadOnly = column.IsGenerated;                    // a stored generated column cannot be written
            dto.HasDefault = column.HasDefault;                     // review WEB-20: left out of an INSERT, the server fills it in
        }

        if (info.PrimaryKey.Count == 0) { Refuse(pending, PgReadOnlyReasons.NoPrimaryKey); return; }

        var present = pending.Columns.Where(c => !c.IsExpression).Select(c => c.BaseColumnName!).ToHashSet(StringComparer.Ordinal);
        var missing = info.PrimaryKey.Where(k => !present.Contains(k)).ToList();
        if (missing.Count > 0)
        {
            Refuse(pending, PgReadOnlyReasons.KeyMissing(missing, wholeKey: missing.Count == info.PrimaryKey.Count));
            return;
        }

        foreach (var column in pending.Columns)
        {
            var inKey = !column.IsExpression && info.PrimaryKey.Contains(column.BaseColumnName!);
            column.IsKey = inKey;
            column.IsTruePrimaryKey = inKey;
        }

        pending.Set.IsEditable = true;
        pending.Set.ReadOnlyReason = null;
        pending.Set.BaseSchema = info.Schema;
        pending.Set.BaseTable = info.Name;
        pending.Set.BaseCatalog = null;                 // PostgreSQL writes go to the database the session is in
    }

    private static void Refuse(Pending pending, string reason)
    {
        pending.Set.IsEditable = false;
        pending.Set.ReadOnlyReason = reason;
        foreach (var column in pending.Columns) { column.IsKey = false; column.IsTruePrimaryKey = false; }
    }

    private sealed record ColumnInfo(string Name, bool NotNull, bool IsIdentity, bool IsGenerated, bool IsSequenceDefault, bool HasDefault);

    private sealed record TableInfo(string Kind, string Schema, string Name, List<string> PrimaryKey, Dictionary<int, ColumnInfo> Columns);

    // One round trip: the relation's kind and real name, its primary key columns in key order, and every live column with what the
    // server fills in for it. The primary key list repeats on each row; the tables are small enough that this beats a second query.
    private const string CatalogSql = """
        SELECT c.relkind::text, n.nspname::text, c.relname::text,
               COALESCE((SELECT array_agg(pa.attname::text ORDER BY k.ord)
                           FROM pg_index i
                           CROSS JOIN LATERAL unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
                           JOIN pg_attribute pa ON pa.attrelid = i.indrelid AND pa.attnum = k.attnum
                          WHERE i.indrelid = c.oid AND i.indisprimary), ARRAY[]::text[]),
               a.attnum::int, a.attname::text, a.attnotnull, a.attidentity::text <> '', a.attgenerated::text <> '',
               COALESCE(pg_get_expr(d.adbin, d.adrelid) LIKE 'nextval(%', false),
               d.adbin IS NOT NULL
          FROM pg_class c
          JOIN pg_namespace n ON n.oid = c.relnamespace
          JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
          LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
         WHERE c.oid = @oid
        """;

    private static async Task<TableInfo?> ReadTableAsync(NpgsqlConnection connection, uint oid, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(CatalogSql, connection);
        command.Parameters.Add(new NpgsqlParameter("oid", NpgsqlTypes.NpgsqlDbType.Oid) { Value = oid });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        TableInfo? info = null;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            info ??= new TableInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                ((string[])reader.GetValue(3)).ToList(), new Dictionary<int, ColumnInfo>());
            info.Columns[reader.GetInt32(4)] = new ColumnInfo(reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7),
                reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10));
        }
        return info;
    }
}
