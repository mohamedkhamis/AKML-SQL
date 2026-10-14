using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using Npgsql;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T057, FR-011, FR-018, research R10) - reads one PostgreSQL result set into an
/// <see cref="ExecuteResultSet"/>. Every cell is the server's own text output (the command asks for all result types as
/// "unknown", so the server formats arrays, intervals, json, bytea and enums exactly as psql prints them and nothing is
/// converted on the way). A NULL is a null cell.
///
/// <para>
/// Row and byte caps work as <see cref="ResultSetReader"/>'s do: the set is announced to the sink before its rows, rows stop
/// at the cap or at the reply's shared byte budget, and a bounded number of the rows left over are counted so the banner
/// can say that more exist. Where the columns come from is read only when the caller passes a collector (the reader must then
/// have been opened with KeyInfo); the collector decides editability once the connection is free. Without one the set is read-only.
/// </para>
/// </summary>
public sealed class PgResultSetReader
{
    // Once truncated, rows are still counted for the banner, but never more than this many: the row cap and the timeout
    // bound the work, the figure only has to be representative.
    private const int MaxOmittedCount = 10_000;

    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "smallint", "integer", "bigint", "real", "double precision", "numeric",
        "int2", "int4", "int8", "float4", "float8", "oid",
    };

    /// <summary>
    /// Reads the reader's current result set into the sink and returns it (null when the sink declined it because the
    /// reply already holds the most sets it may). The reader is left on the set's last row; the caller moves on.
    /// </summary>
    public async Task<ExecuteResultSet?> ReadSetAsync(NpgsqlDataReader reader, int maxRows, IResultSink sink, CancellationToken ct,
        PgProvenanceCollector? provenance = null)
    {
        var fieldCount = reader.FieldCount;
        var names = new string[fieldCount];
        var types = new string[fieldCount];
        var hints = new int[fieldCount];

        for (var i = 0; i < fieldCount; i++)
        {
            names[i] = reader.GetName(i);
            types[i] = TypeNameOf(reader, i);
            hints[i] = IsNumeric(types[i]) ? ClrTypeHint.ServerNumber : ClrTypeHint.ServerText;
        }

        var set = new ExecuteResultSet
        {
            ColumnNames = names,
            ColumnSqlTypes = types,
            ClrTypeHints = hints,
            IsEditable = false,
        };

        if (provenance != null) await provenance.CaptureAsync(reader, set, ct).ConfigureAwait(false);

        // Announce the set before its rows: the statement's command tag arrives while it is current.
        if (sink.BeginSet(set) < 0)
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) { /* over the set cap: drain */ }
            return null;
        }
        var byteBudget = sink.BudgetRemaining;

        var rows = new List<string?[]>();
        long approxBytes = 0;
        var truncated = false;
        var omitted = 0;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (rows.Count >= maxRows || approxBytes >= byteBudget)
            {
                truncated = true;
                omitted++;
                if (omitted >= MaxOmittedCount) break;
                continue;
            }

            var row = new string?[fieldCount];
            for (var c = 0; c < fieldCount; c++)
            {
                var cell = reader.IsDBNull(c) ? null : reader.GetString(c);
                row[c] = cell;
                approxBytes += EstimateCellBytes(cell);
            }
            rows.Add(row);
        }

        set.Rows = rows.ToArray();
        set.Truncated = truncated;
        set.RowsOmitted = omitted;
        sink.Consume(approxBytes);
        return set;
    }

    /// <summary>True for the types the grid should align and sort as numbers.</summary>
    public static bool IsNumeric(string typeName)
    {
        // "numeric(10,2)" is numeric; "integer[]" is an array, shown as the server's text.
        var modifier = typeName.IndexOf('(');
        return NumericTypes.Contains(modifier < 0 ? typeName : typeName.Substring(0, modifier));
    }

    private static string TypeNameOf(NpgsqlDataReader reader, int ordinal)
    {
        // The driver reports the declared form, "character varying(5)" and "numeric(10, 2)"; the server's own text has no space.
        try { return reader.GetDataTypeName(ordinal).Replace(", ", ","); }
        catch (Exception ex)
        {
            Log.Debug(ex, "PgResultSetReader: the type of column {Ordinal} could not be read.", ordinal);
            return "unknown";
        }
    }

    // +5 is roughly a MessagePack string header, the same estimate the SQL Server reader uses.
    private static long EstimateCellBytes(string? cell) => cell == null ? 1 : Encoding.UTF8.GetByteCount(cell) + 5;
}
