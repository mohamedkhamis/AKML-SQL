#nullable enable
using System.Text;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Navigation;

/// <summary>
/// Retrieves object definitions from SQL Server. For programmable objects (procedures,
/// functions, views, triggers), queries <c>sys.sql_modules</c>. For tables, builds a
/// CREATE TABLE script from the schema cache metadata.
/// </summary>
public class ObjectDefinitionService
{
    /// <summary>
    /// Retrieves the definition of a database object.
    /// </summary>
    /// <param name="objectName">The object name to look up.</param>
    /// <param name="schemaName">Optional schema qualifier. If null, searches all schemas.</param>
    /// <param name="connectionString">Connection string to the target database.</param>
    /// <param name="dbCache">Schema cache for the target database (used for tables).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of (definition, objectType, fullName) or nulls if not found.</returns>
    public async Task<(string? Definition, string? ObjectType, string? FullName)> GetDefinitionAsync(
        string objectName,
        string? schemaName,
        string connectionString,
        DatabaseCache? dbCache,
        CancellationToken ct)
    {
        try
        {
            // First, try to get the definition from sys.sql_modules (procedures, functions, views, triggers)
            var (moduleDef, moduleType, moduleFullName) = await GetModuleDefinitionAsync(
                objectName, schemaName, connectionString, ct);

            if (moduleDef != null)
            {
                return (moduleDef, moduleType, moduleFullName);
            }

            // If not found in sys.sql_modules, check if it's a table and build DDL from schema cache
            if (dbCache != null)
            {
                var tableDef = BuildTableDefinition(objectName, schemaName, dbCache);
                if (tableDef != null)
                {
                    return tableDef.Value;
                }
            }

            // Try sys.objects to see if the object exists at all
            var objType = await GetObjectTypeAsync(objectName, schemaName, connectionString, ct);
            if (objType != null)
            {
                return (
                    $"-- Object '{(schemaName != null ? schemaName + "." : "")}{objectName}' exists as type '{objType}' but no definition is available.",
                    objType,
                    schemaName != null ? $"{schemaName}.{objectName}" : objectName);
            }

            return (null, null, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get definition for {Schema}.{Object}", schemaName, objectName);
            return (null, null, null);
        }
    }

    /// <summary>
    /// Queries sys.sql_modules for programmable object definitions.
    /// </summary>
    private async Task<(string? Definition, string? ObjectType, string? FullName)> GetModuleDefinitionAsync(
        string objectName, string? schemaName, string connectionString, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (schemaName != null)
        {
            cmd.CommandText = @"
                SELECT m.definition, o.type_desc, s.name AS schema_name, o.name AS object_name
                FROM sys.sql_modules m
                JOIN sys.objects o ON o.object_id = m.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE o.name = @objectName
                  AND s.name = @schemaName
                  AND o.is_ms_shipped = 0";
            cmd.Parameters.AddWithValue("@schemaName", schemaName);
        }
        else
        {
            cmd.CommandText = @"
                SELECT m.definition, o.type_desc, s.name AS schema_name, o.name AS object_name
                FROM sys.sql_modules m
                JOIN sys.objects o ON o.object_id = m.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE o.name = @objectName
                  AND o.is_ms_shipped = 0";
        }

        cmd.Parameters.AddWithValue("@objectName", objectName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var definition = reader.IsDBNull(0) ? null : reader.GetString(0);
            var typeDesc = reader.GetString(1);
            var schema = reader.GetString(2);
            var name = reader.GetString(3);
            var objectType = MapTypeDescription(typeDesc);
            return (definition, objectType, $"{schema}.{name}");
        }

        return (null, null, null);
    }

    /// <summary>
    /// Builds a CREATE TABLE script from the schema cache for tables that don't have sys.sql_modules entries.
    /// </summary>
    private (string Definition, string ObjectType, string FullName)? BuildTableDefinition(
        string objectName, string? schemaName, DatabaseCache dbCache)
    {
        DatabaseObject? tableObj = null;

        if (schemaName != null)
        {
            tableObj = dbCache.FindObject(schemaName, objectName);
        }
        else
        {
            // Search all schemas for the object
            foreach (var schema in dbCache.Schemas.Values)
            {
                var found = schema.Objects.FirstOrDefault(o =>
                    o.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase) &&
                    o.ObjectType == DbObjectType.Table);
                if (found != null)
                {
                    tableObj = found;
                    break;
                }
            }
        }

        if (tableObj == null || tableObj.ObjectType != DbObjectType.Table)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{tableObj.SchemaName}].[{tableObj.ObjectName}]");
        sb.AppendLine("(");

        // Columns
        var columns = tableObj.Columns;
        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            sb.Append($"    [{col.ColumnName}] {col.TypeDisplay}");

            if (col.IsIdentity)
                sb.Append(" IDENTITY(1,1)");
            if (col.IsComputed && col.ComputedDefinition != null)
            {
                sb.Append($" AS {col.ComputedDefinition}");
            }
            else
            {
                sb.Append(col.IsNullable ? " NULL" : " NOT NULL");
            }

            if (col.DefaultValue != null)
                sb.Append($" DEFAULT {col.DefaultValue}");

            if (i < columns.Count - 1 || HasConstraints(tableObj, dbCache))
                sb.Append(',');

            sb.AppendLine();
        }

        // Primary key constraint
        var pkColumns = columns.Where(c => c.IsPrimaryKey).ToList();
        if (pkColumns.Count > 0)
        {
            var pkIndex = tableObj.Indexes.FirstOrDefault(ix => ix.IsPrimaryKey);
            var pkName = pkIndex?.IndexName ?? $"PK_{tableObj.ObjectName}";
            var pkType = pkIndex?.IndexType == IndexType.Clustered ? "CLUSTERED" : "NONCLUSTERED";
            var pkColNames = string.Join(", ", pkColumns.Select(c => $"[{c.ColumnName}]"));
            sb.AppendLine($"    CONSTRAINT [{pkName}] PRIMARY KEY {pkType} ({pkColNames})");
        }

        sb.AppendLine(");");

        // Foreign key constraints
        var fks = dbCache.GetForeignKeysForTable(tableObj.SchemaName, tableObj.ObjectName)
            .Where(fk => fk.ParentSchema.Equals(tableObj.SchemaName, StringComparison.OrdinalIgnoreCase) &&
                         fk.ParentTable.Equals(tableObj.ObjectName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var fk in fks)
        {
            sb.AppendLine();
            var parentCols = string.Join(", ", fk.ParentColumns.Select(c => $"[{c}]"));
            var refCols = string.Join(", ", fk.ReferencedColumns.Select(c => $"[{c}]"));
            sb.AppendLine($"ALTER TABLE [{tableObj.SchemaName}].[{tableObj.ObjectName}]");
            sb.AppendLine($"    ADD CONSTRAINT [{fk.FkName}]");
            sb.AppendLine($"    FOREIGN KEY ({parentCols})");
            sb.AppendLine($"    REFERENCES [{fk.ReferencedSchema}].[{fk.ReferencedTable}] ({refCols});");
        }

        // Non-clustered indexes (excluding PK)
        var ncIndexes = tableObj.Indexes.Where(ix => !ix.IsPrimaryKey).ToList();
        foreach (var ix in ncIndexes)
        {
            sb.AppendLine();
            var unique = ix.IsUnique ? "UNIQUE " : "";
            var ixType = ix.IndexType == IndexType.Clustered ? "CLUSTERED" : "NONCLUSTERED";
            var ixCols = string.Join(", ",
                ix.Columns.Select(c => $"[{c.ColumnName}]" + (c.IsDescending ? " DESC" : " ASC")));
            sb.AppendLine($"CREATE {unique}{ixType} INDEX [{ix.IndexName}]");
            sb.AppendLine($"    ON [{tableObj.SchemaName}].[{tableObj.ObjectName}] ({ixCols});");
        }

        return (sb.ToString(), "Table", tableObj.FullName);
    }

    private static bool HasConstraints(DatabaseObject tableObj, DatabaseCache dbCache)
    {
        if (tableObj.Columns.Any(c => c.IsPrimaryKey))
            return true;

        var fks = dbCache.GetForeignKeysForTable(tableObj.SchemaName, tableObj.ObjectName);
        return fks.Count > 0;
    }

    /// <summary>
    /// Queries sys.objects to determine what type an object is, even if it has no definition.
    /// </summary>
    private async Task<string?> GetObjectTypeAsync(
        string objectName, string? schemaName, string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();

            if (schemaName != null)
            {
                cmd.CommandText = @"
                    SELECT o.type_desc
                    FROM sys.objects o
                    JOIN sys.schemas s ON s.schema_id = o.schema_id
                    WHERE o.name = @objectName AND s.name = @schemaName AND o.is_ms_shipped = 0";
                cmd.Parameters.AddWithValue("@schemaName", schemaName);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT o.type_desc
                    FROM sys.objects o
                    WHERE o.name = @objectName AND o.is_ms_shipped = 0";
            }

            cmd.Parameters.AddWithValue("@objectName", objectName);

            var result = await cmd.ExecuteScalarAsync(ct);
            return result != null ? MapTypeDescription(result.ToString()!) : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get object type for {Object}", objectName);
            return null;
        }
    }

    /// <summary>
    /// Maps SQL Server type_desc values to friendly type names.
    /// </summary>
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
            "SYNONYM" => "Synonym",
            "SEQUENCE OBJECT" => "Sequence",
            _ => typeDesc
        };
    }
}
