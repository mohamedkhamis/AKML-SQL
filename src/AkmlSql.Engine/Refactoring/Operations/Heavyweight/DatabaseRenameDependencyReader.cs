using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Spec 030 / T061 / FR-018 / R8 — the thin async DB wrapper for database-wide Smart Rename.
/// It does ONLY the live catalog work; the reviewable-script generation is the pure
/// <see cref="DatabaseRenameScriptBuilder"/> (so T060 can unit-test it with synthetic rows).
/// <para>
/// Steps:
/// <list type="number">
/// <item><description>Resolve the rename target: classify object-vs-column. The caller passes a
/// (schema, name) and an optional parent-table hint; this reader confirms via
/// <c>sys.objects</c>/<c>sys.columns</c> and discovers the <c>object_id</c> (+ column <c>column_id</c>
/// for a column rename) that <c>sys.sql_expression_dependencies</c> keys on.</description></item>
/// <item><description>Query <c>sys.sql_expression_dependencies</c> for the REFERENCING modules
/// (<c>referenced_id = the target object_id</c>; for a column also
/// <c>referenced_minor_id = column_id</c>) and fetch each dependent's body from
/// <c>sys.sql_modules</c>.</description></item>
/// </list>
/// Query / <c>ChangeDatabase</c> / permission handling mirror
/// <c>FindInvalidObjectsHandler.ReadDependencyRowsAsync</c> and <c>ObjectDefinitionService</c>.
/// </para>
/// </summary>
internal sealed class DatabaseRenameDependencyReader
{
    /// <summary>The resolved rename target plus the referencing modules whose bodies must be ALTERed.</summary>
    internal sealed class RenamePlan
    {
        public required DatabaseRenameScriptBuilder.RenameTarget Target { get; init; }
        public required IReadOnlyList<DatabaseRenameScriptBuilder.DependentDefinition> Dependents { get; init; }

        /// <summary>
        /// False when the (schema, name[, parentTable]) could not be matched to any object or column in
        /// the connected database. The caller MUST refuse (CanApply=false) rather than emit an sp_rename
        /// for a nonexistent target — otherwise a misclassified or alias-qualified column silently
        /// produces a wrong script.
        /// </summary>
        public required bool Resolved { get; init; }
    }

    /// <summary>
    /// SQL error numbers that indicate a permission/visibility denial (VIEW DEFINITION etc.). Surfaced
    /// to the preview as a friendly CanApply=false rather than a crash. Same set as FindInvalidObjects.
    /// </summary>
    internal static bool IsPermissionDenied(SqlException ex)
    {
        foreach (SqlError error in ex.Errors)
        {
            if (error.Number is 229 or 230 or 262)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resolves the target and reads its dependents. <paramref name="parentTableHint"/> is the table the
    /// shell believes owns the identifier when the caret was on a column (the shell cannot always tell an
    /// object from a column); when supplied and the (schema, parentTable, name) resolves to a real column,
    /// this is treated as a COLUMN rename, otherwise it is treated as an OBJECT rename.
    /// </summary>
    public async Task<RenamePlan> BuildPlanAsync(
        string connectionString,
        string? targetDatabase,
        string schema,
        string name,
        string newName,
        string? parentTableHint,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        if (!string.IsNullOrEmpty(targetDatabase) &&
            !string.Equals(conn.Database, targetDatabase, StringComparison.OrdinalIgnoreCase))
        {
            // ChangeDatabase is parameter-safe (no T-SQL injection surface, unlike USE [...]).
            conn.ChangeDatabase(targetDatabase);
        }

        // ── 1. Classify object vs column and discover the object_id (+ column_id) ──
        var (objectId, columnId, parentTable, isColumn) =
            await ResolveTargetAsync(conn, schema, name, parentTableHint, ct);

        var target = new DatabaseRenameScriptBuilder.RenameTarget(
            Schema: schema,
            Name: name,
            NewName: newName,
            IsColumn: isColumn,
            ParentTable: isColumn ? parentTable : null);

        if (objectId == null)
        {
            // Could not resolve the target as either a column or a standalone object. Do NOT emit an
            // sp_rename for a nonexistent target — signal Resolved=false so the caller refuses with a
            // clear message (this is the safety net for a misclassified / alias-qualified column).
            return new RenamePlan { Target = target, Dependents = [], Resolved = false };
        }

        // ── 2. Read referencing modules ──
        var dependents = await ReadDependentsAsync(conn, objectId.Value, isColumn ? columnId : null, ct);
        return new RenamePlan { Target = target, Dependents = dependents, Resolved = true };
    }

    /// <summary>
    /// Determines whether (schema, name) — optionally owned by <paramref name="parentTableHint"/> — is a
    /// column or a standalone object, and returns the object_id the dependency view keys on plus the
    /// column_id for a column rename. A column rename keys on the PARENT TABLE's object_id with a non-null
    /// referenced_minor_id; an object rename keys on the object's own object_id with minor_id 0.
    /// </summary>
    private static async Task<(int? ObjectId, int? ColumnId, string? ParentTable, bool IsColumn)>
        ResolveTargetAsync(SqlConnection conn, string schema, string name, string? parentTableHint, CancellationToken ct)
    {
        // Prefer a column interpretation only when the shell handed us a parent-table hint.
        if (!string.IsNullOrEmpty(parentTableHint))
        {
            await using var colCmd = conn.CreateCommand();
            colCmd.CommandText = @"
                SELECT o.object_id, c.column_id, o.name AS table_name
                FROM sys.columns c
                JOIN sys.objects o ON o.object_id = c.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = @schema
                  AND o.name = @table
                  AND c.name = @column
                  AND o.is_ms_shipped = 0";
            colCmd.Parameters.AddWithValue("@schema", schema);
            colCmd.Parameters.AddWithValue("@table", parentTableHint);
            colCmd.Parameters.AddWithValue("@column", name);

            await using var colReader = await colCmd.ExecuteReaderAsync(ct);
            if (await colReader.ReadAsync(ct))
            {
                return (colReader.GetInt32(0), colReader.GetInt32(1), colReader.GetString(2), true);
            }
        }

        // Otherwise resolve as a standalone object.
        await using var objCmd = conn.CreateCommand();
        objCmd.CommandText = @"
            SELECT o.object_id
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = @schema
              AND o.name = @name
              AND o.is_ms_shipped = 0";
        objCmd.Parameters.AddWithValue("@schema", schema);
        objCmd.Parameters.AddWithValue("@name", name);

        var objResult = await objCmd.ExecuteScalarAsync(ct);
        if (objResult != null && objResult != DBNull.Value)
        {
            return (Convert.ToInt32(objResult), null, null, false);
        }

        return (null, null, null, false);
    }

    /// <summary>
    /// Reads the modules that reference <paramref name="targetObjectId"/> (and, for a column rename,
    /// the column <paramref name="columnId"/>) and projects each into a
    /// <see cref="DatabaseRenameScriptBuilder.DependentDefinition"/> carrying its
    /// <c>sys.sql_modules.definition</c>. Duplicate referencing rows (a module can reference an object
    /// many times) are collapsed by object_id.
    /// </summary>
    private static async Task<IReadOnlyList<DatabaseRenameScriptBuilder.DependentDefinition>>
        ReadDependentsAsync(SqlConnection conn, int targetObjectId, int? columnId, CancellationToken ct)
    {
        var byObject = new Dictionary<int, DatabaseRenameScriptBuilder.DependentDefinition>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                d.referencing_id            AS referencing_id,
                s.name                      AS schema_name,
                o.name                      AS object_name,
                o.type_desc                 AS type_desc,
                m.definition                AS definition
            FROM sys.sql_expression_dependencies d
            JOIN sys.objects o   ON o.object_id = d.referencing_id
            JOIN sys.schemas s   ON s.schema_id = o.schema_id
            LEFT JOIN sys.sql_modules m ON m.object_id = d.referencing_id
            WHERE d.referenced_id = @targetId
              AND o.is_ms_shipped = 0
              AND (@columnId IS NULL OR d.referenced_minor_id = @columnId OR d.referenced_minor_id = 0)";
        cmd.Parameters.AddWithValue("@targetId", targetObjectId);
        cmd.Parameters.AddWithValue("@columnId", (object?)columnId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            int referencingId = reader.GetInt32(0);
            var definition = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            if (string.IsNullOrEmpty(definition)) continue; // no module body to ALTER (e.g. a FK/constraint)

            byObject[referencingId] = new DatabaseRenameScriptBuilder.DependentDefinition(
                Schema: reader.GetString(1),
                Name: reader.GetString(2),
                TypeDesc: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Definition: definition);
        }

        Log.Debug("DatabaseRenameDependencyReader: {Count} referencing module(s) for object_id={Id} (column_id={Col})",
            byObject.Count, targetObjectId, columnId);

        return byObject.Values.ToList();
    }
}
