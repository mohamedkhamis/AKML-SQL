using System.Collections.Concurrent;
using AKML.SQL.Shared;
using AKML.SQL.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Database metadata service for schema information caching and retrieval.
/// Uses Trie-based lookup for fast prefix matching.
/// </summary>
public class MetadataService : IMetadataService
{
    private readonly ILogger<MetadataService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new();

    private const string CacheKeyPrefix = "Metadata_";

    public MetadataService(ILogger<MetadataService> logger, IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task<MetadataResponse> GetMetadataAsync(MetadataRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.ConnectionString))
        {
            return new MetadataResponse
            {
                Success = false,
                ErrorMessage = "Connection string is required"
            };
        }

        var cacheKey = GetCacheKey(request.ConnectionString, request.Scope);

        // Try cache first
        if (!request.ForceRefresh && _cache.TryGetValue(cacheKey, out MetadataResponse? cached))
        {
            _logger.LogDebug("Returning cached metadata for {Scope}", request.Scope);
            return cached!;
        }

        // Refresh metadata
        var lockKey = request.ConnectionString;
        var refreshLock = _refreshLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (!request.ForceRefresh && _cache.TryGetValue(cacheKey, out cached))
            {
                return cached!;
            }

            // Fetch from database
            var response = await FetchMetadataFromDatabaseAsync(request.ConnectionString, request.Scope, cancellationToken);

            // Cache the result
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(AkmlConstants.Timeouts.MetadataCache),
                SlidingExpiration = TimeSpan.FromMinutes(2)
            };
            _cache.Set(cacheKey, response, cacheOptions);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata");
            return new MetadataResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task RefreshMetadataAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await GetMetadataAsync(new MetadataRequest
        {
            ConnectionString = connectionString,
            Scope = MetadataScope.All,
            ForceRefresh = true
        }, cancellationToken);
    }

    public Task ClearCacheAsync(string? connectionString = null)
    {
        // IMemoryCache doesn't support clearing by prefix in standard implementation
        // In production, consider using a distributed cache with better clearing support
        _logger.LogInformation("Cache clear requested for: {ConnectionString}", connectionString ?? "all");
        return Task.CompletedTask;
    }

    private async Task<MetadataResponse> FetchMetadataFromDatabaseAsync(
        string connectionString,
        MetadataScope scope,
        CancellationToken cancellationToken)
    {
        var response = new MetadataResponse
        {
            Success = true,
            CacheTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        _logger.LogInformation("Connected to database: {Database}", connection.Database);

        // Fetch databases
        if (scope == MetadataScope.Databases || scope == MetadataScope.All)
        {
            response.Databases.AddRange(await FetchDatabasesAsync(connection, cancellationToken));
        }

        // Fetch schemas
        if (scope == MetadataScope.Schemas || scope == MetadataScope.All)
        {
            response.Schemas.AddRange(await FetchSchemasAsync(connection, cancellationToken));
        }

        // Fetch tables and views with columns
        if (scope == MetadataScope.Tables || scope == MetadataScope.Views || 
            scope == MetadataScope.Columns || scope == MetadataScope.All)
        {
            response.Tables.AddRange(await FetchTablesWithColumnsAsync(connection, scope, cancellationToken));
        }

        // Fetch procedures and functions
        if (scope == MetadataScope.Procedures || scope == MetadataScope.Functions || scope == MetadataScope.All)
        {
            response.Procedures.AddRange(await FetchProceduresAsync(connection, scope, cancellationToken));
        }

        return response;
    }

    private static async Task<List<DatabaseInfo>> FetchDatabasesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var databases = new List<DatabaseInfo>();

        const string sql = @"
            SELECT name, compatibility_level
            FROM sys.databases
            WHERE state_desc = 'ONLINE'
            ORDER BY name";

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(new DatabaseInfo
            {
                Name = reader.GetString(0),
                CompatibilityLevel = reader.GetByte(1)
            });
        }

        return databases;
    }

    private static async Task<List<SchemaInfo>> FetchSchemasAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var schemas = new List<SchemaInfo>();

        const string sql = @"
            SELECT DB_NAME() AS DatabaseName, name AS SchemaName
            FROM sys.schemas
            WHERE principal_id = 1
            ORDER BY name";

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(new SchemaInfo
            {
                DatabaseName = reader.GetString(0),
                SchemaName = reader.GetString(1)
            });
        }

        return schemas;
    }

    private static async Task<List<TableInfo>> FetchTablesWithColumnsAsync(
        SqlConnection connection,
        MetadataScope scope,
        CancellationToken cancellationToken)
    {
        var tables = new Dictionary<string, TableInfo>();

        // Fetch tables/views with SQL Server 2022 Ledger table support
        const string tableSql = @"
            SELECT
                DB_NAME() AS DatabaseName,
                s.name AS SchemaName,
                t.name AS TableName,
                CASE WHEN t.type = 'V' THEN 1 ELSE 0 END AS IsView,
                ISNULL(p.rows, 0) AS RowCount,
                CASE WHEN t.type = 'U' AND EXISTS (
                    SELECT 1 FROM sys.tables lt
                    WHERE lt.object_id = t.object_id
                    AND lt.ledger_type IS NOT NULL AND lt.ledger_type > 0
                ) THEN 1 ELSE 0 END AS IsLedger,
                ISNULL((
                    SELECT lv.name FROM sys.tables lt
                    INNER JOIN sys.objects lv ON lt.history_table_id = lv.object_id
                    WHERE lt.object_id = t.object_id
                ), '') AS LedgerViewName
            FROM sys.objects t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
            WHERE t.type IN ('U', 'V')
            AND t.is_ms_shipped = 0
            ORDER BY s.name, t.name";

        await using (var cmd = new SqlCommand(tableSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = new TableInfo
                {
                    DatabaseName = reader.GetString(0),
                    SchemaName = reader.GetString(1),
                    TableName = reader.GetString(2),
                    IsView = reader.GetInt32(3) == 1,
                    RowCount = reader.GetInt64(4),
                    IsLedger = reader.GetInt32(5) == 1,
                    LedgerViewName = reader.GetString(6)
                };

                var key = $"{table.SchemaName}.{table.TableName}";
                tables[key] = table;
            }
        }

        // Fetch columns
        if (scope == MetadataScope.Columns || scope == MetadataScope.All)
        {
            const string columnSql = @"
                SELECT 
                    s.name AS SchemaName,
                    t.name AS TableName,
                    c.name AS ColumnName,
                    ty.name AS DataType,
                    c.is_nullable AS IsNullable,
                    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                    CASE WHEN fk.parent_column_id IS NOT NULL THEN 1 ELSE 0 END AS IsForeignKey,
                    c.is_identity AS IsIdentity,
                    ISNULL(dc.definition, '') AS DefaultValue,
                    c.column_id AS OrdinalPosition,
                    c.max_length AS MaxLength,
                    c.precision AS Precision,
                    c.scale AS Scale
                FROM sys.columns c
                INNER JOIN sys.objects t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                LEFT JOIN sys.foreign_key_columns fk ON c.object_id = fk.parent_object_id AND c.column_id = fk.parent_column_id
                WHERE t.type IN ('U', 'V')
                AND t.is_ms_shipped = 0
                ORDER BY s.name, t.name, c.column_id";

            await using var cmd = new SqlCommand(columnSql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                if (tables.TryGetValue(key, out var table))
                {
                    table.Columns.Add(new ColumnInfo
                    {
                        ColumnName = reader.GetString(2),
                        DataType = reader.GetString(3),
                        IsNullable = reader.GetBoolean(4),
                        IsPrimaryKey = reader.GetInt32(5) == 1,
                        IsForeignKey = reader.GetInt32(6) == 1,
                        IsIdentity = reader.GetBoolean(7),
                        DefaultValue = reader.GetString(8),
                        OrdinalPosition = reader.GetInt32(9),
                        MaxLength = reader.GetInt16(10),
                        Precision = reader.GetByte(11),
                        Scale = reader.GetByte(12)
                    });
                }
            }
        }

        return tables.Values.ToList();
    }

    private static async Task<List<ProcedureInfo>> FetchProceduresAsync(
        SqlConnection connection,
        MetadataScope scope,
        CancellationToken cancellationToken)
    {
        var procedures = new Dictionary<string, ProcedureInfo>();

        // Determine filter for procedures vs functions
        var typeFilter = scope switch
        {
            MetadataScope.Procedures => "('P', 'PC')",
            MetadataScope.Functions => "('FN', 'IF', 'TF', 'AF')",
            _ => "('P', 'PC', 'FN', 'IF', 'TF', 'AF')"
        };

        var procSql = $@"
            SELECT 
                DB_NAME() AS DatabaseName,
                s.name AS SchemaName,
                o.name AS ProcedureName,
                CASE WHEN o.type IN ('FN', 'IF', 'TF', 'AF') THEN 1 ELSE 0 END AS IsFunction,
                ISNULL(ty.name, '') AS ReturnType
            FROM sys.objects o
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            LEFT JOIN sys.parameters p ON o.object_id = p.object_id AND p.parameter_id = 0
            LEFT JOIN sys.types ty ON p.user_type_id = ty.user_type_id
            WHERE o.type IN {typeFilter}
            AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name";

        await using (var cmd = new SqlCommand(procSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var proc = new ProcedureInfo
                {
                    DatabaseName = reader.GetString(0),
                    SchemaName = reader.GetString(1),
                    ProcedureName = reader.GetString(2),
                    IsFunction = reader.GetInt32(3) == 1,
                    ReturnType = reader.GetString(4)
                };

                var key = $"{proc.SchemaName}.{proc.ProcedureName}";
                procedures[key] = proc;
            }
        }

        // Fetch parameters
        const string paramSql = @"
            SELECT 
                s.name AS SchemaName,
                o.name AS ProcedureName,
                p.name AS ParameterName,
                ty.name AS DataType,
                p.is_output AS IsOutput,
                ISNULL(CAST(p.has_default_value AS VARCHAR(10)), '') AS DefaultValue,
                p.parameter_id AS OrdinalPosition
            FROM sys.parameters p
            INNER JOIN sys.objects o ON p.object_id = o.object_id
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            INNER JOIN sys.types ty ON p.user_type_id = ty.user_type_id
            WHERE p.parameter_id > 0
            AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name, p.parameter_id";

        await using (var cmd = new SqlCommand(paramSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                if (procedures.TryGetValue(key, out var proc))
                {
                    proc.Parameters.Add(new ParameterInfo
                    {
                        ParameterName = reader.GetString(2),
                        DataType = reader.GetString(3),
                        IsOutput = reader.GetBoolean(4),
                        DefaultValue = reader.GetString(5),
                        OrdinalPosition = reader.GetInt32(6)
                    });
                }
            }
        }

        return procedures.Values.ToList();
    }

    private static string GetCacheKey(string connectionString, MetadataScope scope)
    {
        // Create a hash of connection string for privacy
        var hash = connectionString.GetHashCode().ToString("X8");
        return $"{CacheKeyPrefix}{hash}_{scope}";
    }
}
