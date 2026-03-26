using AkmlSql.Core.Ipc.Messages;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Navigation;

/// <summary>
/// Collects references to a database object by querying <c>sys.sql_expression_dependencies</c>.
/// Returns a list of <see cref="ObjectReferenceDto"/> identifying all objects that reference the target.
/// </summary>
public class ReferenceCollector
{
    /// <summary>
    /// Finds all objects in the current database that reference the specified object.
    /// Uses <c>sys.sql_expression_dependencies</c> which tracks cross-object references
    /// for procedures, functions, views, and triggers.
    /// </summary>
    /// <param name="objectName">Name of the referenced object.</param>
    /// <param name="schemaName">Optional schema qualifier. When null, all schemas are searched.</param>
    /// <param name="connectionString">Connection string to the target database.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Array of referencing objects.</returns>
    public async Task<ObjectReferenceDto[]> FindReferencesAsync(
        string objectName,
        string? schemaName,
        string connectionString,
        CancellationToken ct)
    {
        var references = new List<ObjectReferenceDto>();

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();

            if (schemaName != null)
            {
                cmd.CommandText = @"
                    SELECT DISTINCT
                        s.name AS referencing_schema,
                        o.name AS referencing_name,
                        o.type_desc AS referencing_type
                    FROM sys.sql_expression_dependencies d
                    JOIN sys.objects o ON o.object_id = d.referencing_id
                    JOIN sys.schemas s ON s.schema_id = o.schema_id
                    WHERE d.referenced_entity_name = @objectName
                      AND d.referenced_schema_name = @schemaName
                      AND o.is_ms_shipped = 0";
                cmd.Parameters.AddWithValue("@schemaName", schemaName);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT DISTINCT
                        s.name AS referencing_schema,
                        o.name AS referencing_name,
                        o.type_desc AS referencing_type
                    FROM sys.sql_expression_dependencies d
                    JOIN sys.objects o ON o.object_id = d.referencing_id
                    JOIN sys.schemas s ON s.schema_id = o.schema_id
                    WHERE d.referenced_entity_name = @objectName
                      AND o.is_ms_shipped = 0";
            }

            cmd.Parameters.AddWithValue("@objectName", objectName);

            // Use explicit block so the reader is closed before we reuse the connection
            {
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    references.Add(new ObjectReferenceDto
                    {
                        SchemaName = reader.GetString(0),
                        ObjectName = reader.GetString(1),
                        ObjectType = MapTypeDescription(reader.GetString(2))
                    });
                }
            }

            // Try to find line references within each referencing object's definition
            // Reuse the already-open connection to avoid opening a second one
            await EnrichWithLineNumbersAsync(references, objectName, conn, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to find references for {Schema}.{Object}", schemaName, objectName);
        }

        return references.ToArray();
    }

    /// <summary>
    /// Maximum number of object names per IN-clause batch to avoid query size limits.
    /// </summary>
    private const int BatchSize = 100;

    /// <summary>
    /// For each referencing object, reads its definition and finds the first line
    /// containing the referenced object name. Uses batched queries to avoid N+1.
    /// </summary>
    private async Task EnrichWithLineNumbersAsync(
        List<ObjectReferenceDto> references,
        string targetObjectName,
        SqlConnection conn,
        CancellationToken ct)
    {
        if (references.Count == 0)
            return;

        try
        {
            // Build a lookup from "schema.object" -> list of references (handles duplicates)
            var refLookup = new Dictionary<string, List<ObjectReferenceDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in references)
            {
                var key = $"{reference.SchemaName}.{reference.ObjectName}";
                if (!refLookup.TryGetValue(key, out var list))
                {
                    list = new List<ObjectReferenceDto>();
                    refLookup[key] = list;
                }
                list.Add(reference);
            }

            // Fetch definitions in batches to avoid overly large IN clauses
            var distinctRefs = refLookup.Keys.ToList();
            for (int batchStart = 0; batchStart < distinctRefs.Count; batchStart += BatchSize)
            {
                if (ct.IsCancellationRequested)
                    break;

                var batch = distinctRefs
                    .Skip(batchStart)
                    .Take(BatchSize)
                    .ToList();

                await FetchDefinitionBatchAsync(batch, refLookup, targetObjectName, conn, ct);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enrich references with line numbers");
        }
    }

    /// <summary>
    /// Fetches definitions for a batch of referencing objects in a single query
    /// and enriches the corresponding <see cref="ObjectReferenceDto"/> instances with line numbers.
    /// </summary>
    private static async Task FetchDefinitionBatchAsync(
        List<string> batchKeys,
        Dictionary<string, List<ObjectReferenceDto>> refLookup,
        string targetObjectName,
        SqlConnection conn,
        CancellationToken ct)
    {
        try
        {
            await using var cmd = conn.CreateCommand();

            // Build parameterized WHERE clause: (s.name = @s0 AND o.name = @o0) OR ...
            // Use LastIndexOf to handle schema names containing dots (quoted identifiers)
            var whereClauses = new List<string>();
            for (int i = 0; i < batchKeys.Count; i++)
            {
                var key = batchKeys[i];
                var dotIndex = key.LastIndexOf('.');
                if (dotIndex <= 0) continue; // skip malformed keys
                var schema = key.Substring(0, dotIndex);
                var objName = key.Substring(dotIndex + 1);

                var sParam = $"@s{i}";
                var oParam = $"@o{i}";
                whereClauses.Add($"(s.name = {sParam} AND o.name = {oParam})");
                cmd.Parameters.AddWithValue(sParam, schema);
                cmd.Parameters.AddWithValue(oParam, objName);
            }

            cmd.CommandText = $@"
                SELECT s.name AS schema_name, o.name AS object_name, m.definition
                FROM sys.sql_modules m
                JOIN sys.objects o ON o.object_id = m.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE {string.Join(" OR ", whereClauses)}";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var schemaName = reader.GetString(0);
                var objectName = reader.GetString(1);
                var definition = reader.IsDBNull(2) ? null : reader.GetString(2);

                if (definition == null)
                    continue;

                var lineNumber = FindLineContaining(definition, targetObjectName);
                if (lineNumber <= 0)
                    continue;

                var lookupKey = $"{schemaName}.{objectName}";
                if (refLookup.TryGetValue(lookupKey, out var refs))
                {
                    foreach (var r in refs)
                    {
                        r.ReferenceLine = lineNumber;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to fetch definition batch for line number enrichment");
        }
    }

    /// <summary>
    /// Finds the first line number (1-based) in <paramref name="definition"/> that contains
    /// <paramref name="searchText"/> (case-insensitive).
    /// </summary>
    private static int FindLineContaining(string definition, string searchText)
    {
        var lines = definition.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static string MapTypeDescription(string typeDesc)
    {
        return typeDesc.Replace("_", " ") switch
        {
            "SQL STORED PROCEDURE" => "Procedure",
            "SQL SCALAR FUNCTION" => "ScalarFunction",
            "SQL TABLE VALUED FUNCTION" => "TableFunction",
            "SQL INLINE TABLE VALUED FUNCTION" => "InlineFunction",
            "VIEW" => "View",
            "SQL TRIGGER" => "Trigger",
            "SQL DML TRIGGER" => "Trigger",
            "USER TABLE" => "Table",
            _ => typeDesc
        };
    }
}
