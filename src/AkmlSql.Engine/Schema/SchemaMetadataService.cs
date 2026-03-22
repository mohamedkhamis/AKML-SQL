using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using AkmlSql.Engine.Schema.Models;
using Serilog;

namespace AkmlSql.Engine.Schema;

public enum PermissionLevel
{
    Full,              // VIEW DEFINITION + VIEW DATABASE STATE
    NoDmv,             // VIEW DEFINITION only
    InformationSchema, // No sys access
    PublicOnly          // Only accessible objects
}

[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
[SuppressMessage("ReSharper", "InvalidXmlDocComment")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class SchemaMetadataService
{
    public async Task<PermissionLevel> ProbePermissionsAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();

            // Check VIEW DATABASE STATE
            cmd.CommandText = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW DATABASE STATE')";
            var hasDbState = (int)(await cmd.ExecuteScalarAsync(ct))! == 1;

            // Check VIEW DEFINITION
            cmd.CommandText = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW ANY DEFINITION')";
            var hasViewDef = (int)(await cmd.ExecuteScalarAsync(ct))! == 1;

            if (hasViewDef && hasDbState)
            {
                return PermissionLevel.Full;
            }

            if (hasViewDef)
            {
                return PermissionLevel.NoDmv;
            }

            // Check if sys.objects is accessible
            cmd.CommandText = "SELECT TOP 0 1 FROM sys.objects";
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
                return PermissionLevel.InformationSchema;
            }
            catch
            {
                return PermissionLevel.PublicOnly;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Permission probe failed, defaulting to PublicOnly");
            return PermissionLevel.PublicOnly;
        }
    }

    /// Phase A: Load object names (tables, views, procs, functions) — target <500ms
    public async Task PopulatePhaseAAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var query = @"
                SELECT s.name AS SchemaName, o.object_id, o.name AS ObjectName, o.type_desc, o.modify_date,
                       ISNULL(SUM(p.rows), 0) AS ApproxRowCount
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                LEFT JOIN sys.partitions p ON o.object_id = p.object_id AND p.index_id IN (0, 1)
                WHERE o.type IN ('U','V','P','FN','IF','TF','SN','SO')
                  AND o.is_ms_shipped = 0
                GROUP BY s.name, o.object_id, o.name, o.type_desc, o.modify_date
                ORDER BY s.name, o.name";

            await using var cmd = new SqlCommand(query, conn);
            cmd.CommandTimeout = 10;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var schemaName = reader.GetString(0);
                var objectId = reader.GetInt32(1);
                var objectName = reader.GetString(2);
                var typeDesc = reader.GetString(3).Trim();
                var modifyDate = reader.GetDateTime(4);
                var rowCount = reader.GetInt64(5);

                var entry = cache.Schemas.GetOrAdd(schemaName, _ => new SchemaEntry { SchemaName = schemaName });

                entry.Objects.Add(new DatabaseObject
                {
                    ObjectId = objectId,
                    SchemaName = schemaName,
                    ObjectName = objectName,
                    ObjectType = MapObjectType(typeDesc),
                    ModifyDate = modifyDate,
                    ApproxRowCount = rowCount
                });
            }

            cache.Phase = PopulationPhase.PhaseA;
            cache.LastFullRefresh = DateTime.UtcNow;
            Log.Information("Phase A complete for {Key}: {Count} objects",
                cache.CacheKey, cache.GetAllObjects().Count());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Phase A failed for {Key}", cache.CacheKey);
            cache.IsStale = true;
        }
    }

    /// Phase B: Load columns, FKs, parameters — background
    public async Task PopulatePhaseBAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
    {
        if (cache.Phase < PopulationPhase.PhaseA)
        {
            Log.Warning("Phase B skipped for {Key}: Phase A has not completed", cache.CacheKey);
            return;
        }

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Load columns
            await LoadColumnsAsync(conn, cache, ct);

            // Load foreign keys
            await LoadForeignKeysAsync(conn, cache, ct);

            // Load parameters for procs/functions
            await LoadParametersAsync(conn, cache, ct);

            // Load extended properties (descriptions)
            await LoadDescriptionsAsync(conn, cache, ct);

            cache.Phase = PopulationPhase.PhaseB;
            Log.Information("Phase B complete for {Key}", cache.CacheKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Phase B failed for {Key}", cache.CacheKey);
        }
    }

    private async Task LoadColumnsAsync(SqlConnection conn, DatabaseCache cache, CancellationToken ct)
    {
        var query = @"
            SELECT c.object_id, c.column_id, c.name, t.name AS TypeName,
                   c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed,
                   cc.definition AS ComputedDef, dc.definition AS DefaultVal,
                   CASE WHEN ic.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPK
            FROM sys.columns c
            JOIN sys.types t ON c.user_type_id = t.user_type_id
            JOIN sys.objects o ON c.object_id = o.object_id
            LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.index_columns ic
                JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0
            ORDER BY c.object_id, c.column_id";

        await using var cmd = new SqlCommand(query, conn);
        cmd.CommandTimeout = 30;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var objectIndex = new Dictionary<int, DatabaseObject>();
        foreach (var obj in cache.GetAllObjects())
            objectIndex[obj.ObjectId] = obj;

        while (await reader.ReadAsync(ct))
        {
            var objectId = reader.GetInt32(0);
            if (!objectIndex.TryGetValue(objectId, out var dbObj))
            {
                continue;
            }

            dbObj.Columns.Add(new Column
            {
                ColumnId = reader.GetInt32(1),
                ColumnName = reader.GetString(2),
                TypeName = reader.GetString(3),
                MaxLength = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                IsNullable = reader.GetBoolean(7),
                IsIdentity = reader.GetBoolean(8),
                IsComputed = reader.GetBoolean(9),
                ComputedDefinition = reader.IsDBNull(10) ? null : reader.GetString(10),
                DefaultValue = reader.IsDBNull(11) ? null : reader.GetString(11),
                IsPrimaryKey = reader.GetInt32(12) == 1
            });
            dbObj.ColumnsLoaded = true;
        }
    }

    private async Task LoadForeignKeysAsync(SqlConnection conn, DatabaseCache cache, CancellationToken ct)
    {
        var query = @"
            SELECT fk.name AS FkName,
                   ps.name AS ParentSchema, OBJECT_NAME(fk.parent_object_id) AS ParentTable,
                   pc.name AS ParentColumn,
                   rs.name AS RefSchema, OBJECT_NAME(fk.referenced_object_id) AS RefTable,
                   rc.name AS RefColumn,
                   fk.is_disabled, fk.delete_referential_action, fk.update_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.schemas ps ON OBJECT_SCHEMA_ID(fk.parent_object_id) = ps.schema_id
            JOIN sys.schemas rs ON OBJECT_SCHEMA_ID(fk.referenced_object_id) = rs.schema_id
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
            JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
            ORDER BY fk.name, fkc.constraint_column_id";

        await using var cmd = new SqlCommand(query, conn);
        cmd.CommandTimeout = 30;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var fks = new Dictionary<string, ForeignKey>();
        while (await reader.ReadAsync(ct))
        {
            var fkName = reader.GetString(0);
            if (!fks.TryGetValue(fkName, out var fk))
            {
                fk = new ForeignKey
                {
                    FkName = fkName,
                    ParentSchema = reader.GetString(1),
                    ParentTable = reader.GetString(2),
                    ReferencedSchema = reader.GetString(4),
                    ReferencedTable = reader.GetString(5),
                    IsDisabled = reader.GetBoolean(7),
                    DeleteAction = (FkAction)reader.GetByte(8),
                    UpdateAction = (FkAction)reader.GetByte(9)
                };
                fks[fkName] = fk;
            }
            fk.ParentColumns.Add(reader.GetString(3));
            fk.ReferencedColumns.Add(reader.GetString(6));
        }

        cache.ForeignKeys = fks.Values.ToList();
    }

    private async Task LoadParametersAsync(SqlConnection conn, DatabaseCache cache, CancellationToken ct)
    {
        var query = @"
            SELECT p.object_id, p.parameter_id, p.name, t.name AS TypeName,
                   p.max_length, p.precision, p.scale, p.is_output, p.has_default_value, p.default_value
            FROM sys.parameters p
            JOIN sys.types t ON p.user_type_id = t.user_type_id
            JOIN sys.objects o ON p.object_id = o.object_id
            WHERE o.type IN ('P','FN','IF','TF') AND o.is_ms_shipped = 0 AND p.parameter_id > 0
            ORDER BY p.object_id, p.parameter_id";

        await using var cmd = new SqlCommand(query, conn);
        cmd.CommandTimeout = 30;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var objectIndex = new Dictionary<int, DatabaseObject>();
        foreach (var obj in cache.GetAllObjects())
            objectIndex[obj.ObjectId] = obj;

        while (await reader.ReadAsync(ct))
        {
            var objectId = reader.GetInt32(0);
            if (!objectIndex.TryGetValue(objectId, out var dbObj))
            {
                continue;
            }

            dbObj.Parameters.Add(new Parameter
            {
                ParameterId = reader.GetInt32(1),
                ParameterName = reader.GetString(2),
                TypeName = reader.GetString(3),
                MaxLength = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                IsOutput = reader.GetBoolean(7),
                HasDefault = reader.GetBoolean(8),
                DefaultValue = reader.IsDBNull(9) ? null : reader.GetValue(9)
            });
        }
    }

    private async Task LoadDescriptionsAsync(SqlConnection conn, DatabaseCache cache, CancellationToken ct)
    {
        var query = @"
            SELECT ep.major_id, ep.minor_id, CAST(ep.value AS NVARCHAR(4000))
            FROM sys.extended_properties ep
            WHERE ep.class = 1 AND ep.name = 'MS_Description'";

        await using var cmd = new SqlCommand(query, conn);
        cmd.CommandTimeout = 15;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var objectIndex = new Dictionary<int, DatabaseObject>();
        foreach (var obj in cache.GetAllObjects())
            objectIndex[obj.ObjectId] = obj;

        while (await reader.ReadAsync(ct))
        {
            var majorId = reader.GetInt32(0);
            var minorId = reader.GetInt32(1);
            var value = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (value == null)
            {
                continue;
            }

            if (!objectIndex.TryGetValue(majorId, out var dbObj))
            {
                continue;
            }

            if (minorId == 0)
            {
                dbObj.Description = value;
            }
            else
            {
                var col = dbObj.Columns.FirstOrDefault(c => c.ColumnId == minorId);
                if (col != null)
                {
                    col.Description = value;
                }
            }
        }
    }

    /// <summary>
    /// T106: Detects the SQL Server engine edition to determine if we're connected to Azure SQL Database.
    /// Returns the EngineEdition value from SERVERPROPERTY:
    ///   1 = Personal/Desktop, 2 = Standard, 3 = Enterprise, 4 = Express,
    ///   5 = SQL Database (Azure), 6 = SQL Data Warehouse, 8 = Managed Instance,
    ///   9 = Azure SQL Edge, 11 = Azure Synapse serverless
    /// </summary>
    public async Task<int> DetectEngineEditionAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CAST(SERVERPROPERTY('EngineEdition') AS INT)";
            cmd.CommandTimeout = 5;

            var result = await cmd.ExecuteScalarAsync(ct);
            var edition = result is int i ? i : 0;
            Log.Information("Detected SQL Server EngineEdition={Edition}", edition);
            return edition;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect EngineEdition, defaulting to 0");
            return 0;
        }
    }

    /// <summary>
    /// Returns true if the engine edition indicates Azure SQL Database (edition 5)
    /// or Azure SQL Managed Instance (edition 8).
    /// </summary>
    public static bool IsAzureSql(int engineEdition)
    {
        return engineEdition is 5 or 8;
    }

    /// <summary>
    /// T107: Skeleton for INFORMATION_SCHEMA fallback when sys.objects is not accessible.
    /// Used for PublicOnly permission level or when sys catalog views are restricted.
    /// </summary>
    public async Task PopulateFromInformationSchemaAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Phase A equivalent: load tables, views, routines
            var tableQuery = @"
                SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
                FROM INFORMATION_SCHEMA.TABLES
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

            await using var tableCmd = new SqlCommand(tableQuery, conn);
            tableCmd.CommandTimeout = 15;
            await using var tableReader = await tableCmd.ExecuteReaderAsync(ct);

            while (await tableReader.ReadAsync(ct))
            {
                var schemaName = tableReader.GetString(0);
                var tableName = tableReader.GetString(1);
                var tableType = tableReader.GetString(2).Trim();

                var entry = cache.Schemas.GetOrAdd(schemaName, _ => new SchemaEntry { SchemaName = schemaName });
                entry.Objects.Add(new DatabaseObject
                {
                    SchemaName = schemaName,
                    ObjectName = tableName,
                    ObjectType = tableType == "VIEW" ? DbObjectType.View : DbObjectType.Table,
                    ModifyDate = DateTime.UtcNow
                });
            }
            await tableReader.CloseAsync();

            // Load routines (procedures and functions)
            var routineQuery = @"
                SELECT ROUTINE_SCHEMA, ROUTINE_NAME, ROUTINE_TYPE
                FROM INFORMATION_SCHEMA.ROUTINES
                ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME";

            await using var routineCmd = new SqlCommand(routineQuery, conn);
            routineCmd.CommandTimeout = 15;
            await using var routineReader = await routineCmd.ExecuteReaderAsync(ct);

            while (await routineReader.ReadAsync(ct))
            {
                var schemaName = routineReader.GetString(0);
                var routineName = routineReader.GetString(1);
                var routineType = routineReader.GetString(2).Trim();

                var entry = cache.Schemas.GetOrAdd(schemaName, _ => new SchemaEntry { SchemaName = schemaName });
                entry.Objects.Add(new DatabaseObject
                {
                    SchemaName = schemaName,
                    ObjectName = routineName,
                    ObjectType = routineType == "PROCEDURE" ? DbObjectType.Procedure : DbObjectType.ScalarFunction,
                    ModifyDate = DateTime.UtcNow
                });
            }
            await routineReader.CloseAsync();

            // Load columns
            var colQuery = @"
                SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE,
                       CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE,
                       IS_NULLABLE, COLUMN_DEFAULT, ORDINAL_POSITION
                FROM INFORMATION_SCHEMA.COLUMNS
                ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION";

            await using var colCmd = new SqlCommand(colQuery, conn);
            colCmd.CommandTimeout = 30;
            await using var colReader = await colCmd.ExecuteReaderAsync(ct);

            while (await colReader.ReadAsync(ct))
            {
                var schemaName = colReader.GetString(0);
                var tableName = colReader.GetString(1);
                var dbObj = cache.FindObject(schemaName, tableName);
                if (dbObj == null)
                {
                    continue;
                }

                dbObj.Columns.Add(new Column
                {
                    ColumnId = colReader.GetInt32(9),
                    ColumnName = colReader.GetString(2),
                    TypeName = colReader.GetString(3),
                    MaxLength = colReader.IsDBNull(4) ? 0 : (short)Math.Min(colReader.GetInt32(4), short.MaxValue),
                    Precision = colReader.IsDBNull(5) ? 0 : colReader.GetByte(5),
                    Scale = colReader.IsDBNull(6) ? 0 : (byte)colReader.GetInt32(6),
                    IsNullable = colReader.GetString(7) == "YES",
                    DefaultValue = colReader.IsDBNull(8) ? null : colReader.GetString(8)
                });
                dbObj.ColumnsLoaded = true;
            }

            cache.Phase = PopulationPhase.PhaseA;
            cache.LastFullRefresh = DateTime.UtcNow;
            Log.Information("INFORMATION_SCHEMA fallback complete for {Key}: {Count} objects",
                cache.CacheKey, cache.GetAllObjects().Count());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "INFORMATION_SCHEMA fallback failed for {Key}", cache.CacheKey);
            cache.IsStale = true;
        }
    }

    /// <summary>
    /// T108: Skeleton for version-aware feature detection.
    /// Different SQL Server versions support different features.
    /// </summary>
    public async Task<SqlServerFeatures> DetectFeaturesAsync(string connectionString, CancellationToken ct)
    {
        var features = new SqlServerFeatures();
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS INT)";
            cmd.CommandTimeout = 5;

            var result = await cmd.ExecuteScalarAsync(ct);
            features.MajorVersion = result is int v ? v : 0;

            // SQL Server version → major version mapping:
            // 13 = SQL 2016, 14 = SQL 2017, 15 = SQL 2019, 16 = SQL 2022, 17 = SQL 2025
            features.SupportsTemporalTables = features.MajorVersion >= 13;
            features.SupportsGraphTables = features.MajorVersion >= 14;
            features.SupportsJsonFunctions = features.MajorVersion >= 13;
            features.SupportsStringAgg = features.MajorVersion >= 14;
            features.SupportsUtf8 = features.MajorVersion >= 15;
            features.SupportsLedgerTables = features.MajorVersion >= 16;
            features.SupportsGenerateSeries = features.MajorVersion >= 16;
            features.SupportsGreatestLeast = features.MajorVersion >= 16;

            // Detect Azure SQL
            var engineEdition = await DetectEngineEditionAsync(connectionString, ct);
            features.IsAzureSql = IsAzureSql(engineEdition);

            Log.Information("Detected SQL Server features: v{Major}, Azure={Azure}",
                features.MajorVersion, features.IsAzureSql);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect SQL Server features");
        }
        return features;
    }

    private static DbObjectType MapObjectType(string typeDesc)
    {
        return typeDesc switch
        {
            "USER_TABLE" => DbObjectType.Table,
            "VIEW" => DbObjectType.View,
            "SQL_STORED_PROCEDURE" => DbObjectType.Procedure,
            "SQL_SCALAR_FUNCTION" => DbObjectType.ScalarFunction,
            "SQL_TABLE_VALUED_FUNCTION" => DbObjectType.TableFunction,
            "SQL_INLINE_TABLE_VALUED_FUNCTION" => DbObjectType.InlineFunction,
            "SYNONYM" => DbObjectType.Synonym,
            "SEQUENCE_OBJECT" => DbObjectType.Sequence,
            _ => DbObjectType.Table
        };
    }
}

/// <summary>
/// T108: Feature flags for version-aware SQL Server capabilities.
/// </summary>
public class SqlServerFeatures
{
    public int MajorVersion { get; set; }
    public bool IsAzureSql { get; set; }
    public bool SupportsTemporalTables { get; set; }   // SQL 2016+ (v13)
    public bool SupportsGraphTables { get; set; }       // SQL 2017+ (v14)
    public bool SupportsJsonFunctions { get; set; }     // SQL 2016+ (v13)
    public bool SupportsStringAgg { get; set; }         // SQL 2017+ (v14)
    public bool SupportsUtf8 { get; set; }              // SQL 2019+ (v15)
    public bool SupportsLedgerTables { get; set; }      // SQL 2022+ (v16)
    public bool SupportsGenerateSeries { get; set; }    // SQL 2022+ (v16)
    public bool SupportsGreatestLeast { get; set; }     // SQL 2022+ (v16)
}
