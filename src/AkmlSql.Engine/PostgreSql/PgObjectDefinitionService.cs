using System.Text;
using AkmlSql.Core.Text;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Npgsql;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T084, FR-068, contracts/catalog-queries.md "Object definitions") - go to definition for a PostgreSQL object.
/// A view or materialized view comes from <c>pg_get_viewdef</c>, a function or procedure from <c>pg_get_functiondef</c> (the server's
/// own text, so it is exactly what <c>CREATE OR REPLACE</c> would take back). PostgreSQL has no function that prints a table, so a
/// table is rebuilt from the loaded catalog: columns with their types, nullability, defaults and generated expressions, the
/// primary key, the foreign keys and the other indexes. The header comment says so, because identity columns are shown in their
/// by-default form whether they were "always" or not.
/// </summary>
public sealed class PgObjectDefinitionService
{
    private const string RelationLookupSql =
        "SELECT c.oid, n.nspname, c.relname, c.relkind::text FROM pg_catalog.pg_class c " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
        "WHERE c.relname = $1 AND c.relkind IN ('v','m') AND ($2::text IS NULL OR n.nspname = $2)";

    private const string RoutineLookupSql =
        "SELECT p.oid, n.nspname, p.proname, p.prokind::text FROM pg_catalog.pg_proc p " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace " +
        "WHERE p.proname = $1 AND p.prokind IN ('f','p') AND n.nspname NOT IN ('pg_catalog','information_schema') " +
        "AND ($2::text IS NULL OR n.nspname = $2)";

    /// <summary>(definition, object type, full name), or three nulls when the object is not found.</summary>
    public async Task<(string? Definition, string? ObjectType, string? FullName)> GetDefinitionAsync(
        string objectName, string? schemaName, string connectionString, DatabaseCache? cache, CancellationToken ct)
    {
        try
        {
            var searchPath = cache?.SearchPath ?? new List<string>();

            // A table (and a sequence) is read from the loaded catalog.
            if (cache != null)
            {
                var found = Find(cache, objectName, schemaName, searchPath, DbObjectType.Table);
                if (found != null) return (BuildTable(found, cache), "Table", found.FullName);
            }

            await using var connection = PgConnectionFactory.Create(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var relation = await LookupAsync(connection, RelationLookupSql, objectName, schemaName, searchPath, ct).ConfigureAwait(false);
            if (relation is { } view)
            {
                var body = await ScalarAsync(connection, "SELECT pg_catalog.pg_get_viewdef($1::oid, true)", view.Oid, ct).ConfigureAwait(false);
                if (body == null) return (null, null, null);
                var materialized = view.Kind == "m";
                var header = (materialized ? "CREATE MATERIALIZED VIEW " : "CREATE VIEW ") + PgIdentifierQuoting.QualifiedName(view.Schema, view.Name) + " AS\n";
                return (header + body.TrimEnd() + (body.TrimEnd().EndsWith(';') ? string.Empty : ";") + "\n",
                    materialized ? "MaterializedView" : "View", view.Schema + "." + view.Name);
            }

            var routine = await LookupAsync(connection, RoutineLookupSql, objectName, schemaName, searchPath, ct).ConfigureAwait(false);
            if (routine is { } function)
            {
                var text = await ScalarAsync(connection, "SELECT pg_catalog.pg_get_functiondef($1::oid)", function.Oid, ct).ConfigureAwait(false);
                if (text == null) return (null, null, null);
                return (text.TrimEnd() + "\n", function.Kind == "p" ? "Procedure" : "Function", function.Schema + "." + function.Name);
            }

            return (null, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning("PostgreSQL GetObjectDefinition failed for {Object} ({Type})", objectName, ex.GetType().Name);
            return (null, null, null);
        }
    }

    // The object in the cache: in the named schema, or in the first search-path schema that has it, or in any schema.
    private static DatabaseObject? Find(DatabaseCache cache, string name, string? schema, IReadOnlyList<string> searchPath, DbObjectType type)
    {
        if (schema != null)
        {
            var one = cache.FindObject(schema, name);
            return one is { } o && o.ObjectType == type ? o : null;
        }
        foreach (var candidate in searchPath)
        {
            var one = cache.FindObject(candidate, name);
            if (one != null && one.ObjectType == type) return one;
        }
        return cache.GetAllObjects().FirstOrDefault(o => o.ObjectType == type && o.ObjectName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct Hit(uint Oid, string Schema, string Name, string Kind);

    // Rows named like the request (in the schema, when one is given); the first on the search path wins, else the first found.
    private static async Task<Hit?> LookupAsync(NpgsqlConnection connection, string sql, string name, string? schema,
        IReadOnlyList<string> searchPath, CancellationToken ct)
    {
        var hits = new List<Hit>();
        await using (var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 })
        {
            command.Parameters.AddWithValue(name);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)schema ?? DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                hits.Add(new Hit(reader.GetFieldValue<uint>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        if (hits.Count == 0) return null;

        foreach (var candidate in searchPath)
        {
            var onPath = hits.FindIndex(h => h.Schema.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (onPath >= 0) return hits[onPath];
        }
        return hits[0];
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql, uint oid, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Oid, Value = oid });
        return (await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) as string;
    }

    private static string BuildTable(DatabaseObject table, DatabaseCache cache)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Rebuilt from the catalog: PostgreSQL has no function that prints a table.");
        sb.AppendLine("-- Identity columns are shown as BY DEFAULT; check the catalog for ALWAYS.");
        sb.AppendLine("CREATE TABLE " + PgIdentifierQuoting.QualifiedName(table.SchemaName, table.ObjectName) + " (");

        var primary = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        var lines = new List<string>();
        foreach (var column in table.Columns)
        {
            var line = new StringBuilder("    " + PgIdentifierQuoting.Quote(column.ColumnName) + " " + column.TypeName);
            if (column.IsComputed && column.ComputedDefinition != null)
            {
                line.Append(" GENERATED ALWAYS AS (" + column.ComputedDefinition + ") STORED");
            }
            else if (column.IsIdentity)
            {
                line.Append(" GENERATED BY DEFAULT AS IDENTITY");
            }
            else if (column.DefaultValue != null)
            {
                line.Append(" DEFAULT " + column.DefaultValue);
            }
            if (!column.IsNullable) line.Append(" NOT NULL");
            lines.Add(line.ToString());
        }
        if (primary != null && primary.Columns.Count > 0)
        {
            lines.Add("    CONSTRAINT " + PgIdentifierQuoting.Quote(primary.IndexName) + " PRIMARY KEY ("
                + string.Join(", ", primary.Columns.Select(c => PgIdentifierQuoting.Quote(c.ColumnName))) + ")");
        }
        sb.AppendLine(string.Join(",\n", lines));
        sb.AppendLine(");");

        foreach (var fk in cache.GetForeignKeysForTable(table.SchemaName, table.ObjectName).Where(f =>
                     f.ParentSchema.Equals(table.SchemaName, StringComparison.OrdinalIgnoreCase)
                     && f.ParentTable.Equals(table.ObjectName, StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine("ALTER TABLE " + PgIdentifierQuoting.QualifiedName(table.SchemaName, table.ObjectName));
            sb.AppendLine("    ADD CONSTRAINT " + PgIdentifierQuoting.Quote(fk.FkName));
            sb.AppendLine("    FOREIGN KEY (" + string.Join(", ", fk.ParentColumns.Select(PgIdentifierQuoting.Quote)) + ")");
            sb.Append("    REFERENCES " + PgIdentifierQuoting.QualifiedName(fk.ReferencedSchema, fk.ReferencedTable)
                + " (" + string.Join(", ", fk.ReferencedColumns.Select(PgIdentifierQuoting.Quote)) + ")");
            if (fk.DeleteAction == FkAction.Cascade) sb.Append(" ON DELETE CASCADE");
            else if (fk.DeleteAction == FkAction.SetNull) sb.Append(" ON DELETE SET NULL");
            else if (fk.DeleteAction == FkAction.SetDefault) sb.Append(" ON DELETE SET DEFAULT");
            if (fk.UpdateAction == FkAction.Cascade) sb.Append(" ON UPDATE CASCADE");
            else if (fk.UpdateAction == FkAction.SetNull) sb.Append(" ON UPDATE SET NULL");
            else if (fk.UpdateAction == FkAction.SetDefault) sb.Append(" ON UPDATE SET DEFAULT");
            sb.AppendLine(";");
        }

        foreach (var index in table.Indexes.Where(i => !i.IsPrimaryKey))
        {
            sb.AppendLine();
            sb.AppendLine("CREATE " + (index.IsUnique ? "UNIQUE " : string.Empty) + "INDEX " + PgIdentifierQuoting.Quote(index.IndexName));
            sb.AppendLine("    ON " + PgIdentifierQuoting.QualifiedName(table.SchemaName, table.ObjectName) + " ("
                + string.Join(", ", index.Columns.Select(c => PgIdentifierQuoting.Quote(c.ColumnName) + (c.IsDescending ? " DESC" : string.Empty))) + ");");
        }
        return sb.ToString();
    }
}
