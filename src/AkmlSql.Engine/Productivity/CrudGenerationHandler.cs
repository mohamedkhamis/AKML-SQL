using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Productivity
{
    /// <summary>
    /// T102: IPC handler for CRUD generation requests. Looks up table metadata from the schema
    /// cache and delegates to <see cref="CrudGenerator"/>.
    /// </summary>
    internal sealed class CrudGenerationHandler(SchemaCacheManager schemaCacheManager)
    {
        private readonly CrudGenerator _generator = new();
        private readonly SchemaCacheManager _schemaCacheManager = schemaCacheManager ?? throw new ArgumentNullException(nameof(schemaCacheManager));

        /// <summary>
        /// Handles a CrudGeneration request (MessageType 66).
        /// </summary>
        public async Task<RpcMessage?> HandleAsync(
            RpcMessage message,
            Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
            CancellationToken ct)
        {
            try
            {
                var request = MessagePackSerializer.Deserialize<CrudGenerationRequest>(message.Payload!);

                Log.Information("CrudGenerationHandler: generating CRUD for [{Schema}].[{Table}], ops={Ops}",
                    request.SchemaName, request.TableName, request.Operations);

                // Look up session to get connection/database info
                var (connectionString, databaseName) = sessionLookup(request.SessionId);

                if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))
                {
                    return CreateErrorResponse(message.RequestId,
                        "No active database connection for this session. Ensure a connection is active.");
                }

                // Get the database cache for this session. Keyed by SESSION ID (the populators use
                // GetOrCreateCache(SessionId, db)) — passing the connection string always misses.
                var dbCache = _schemaCacheManager.GetCache(request.SessionId, databaseName);
                if (dbCache == null)
                {
                    return CreateErrorResponse(message.RequestId,
                        $"Schema cache not available for database '{databaseName}'. " +
                        "Ensure the schema cache is populated (try reconnecting or refreshing).");
                }

                // Look up the table object from the cache
                var tableObj = dbCache.FindObject(request.SchemaName, request.TableName);
                if (tableObj == null || !tableObj.ColumnsLoaded)
                {
                    return CreateErrorResponse(message.RequestId,
                        $"Table [{request.SchemaName}].[{request.TableName}] not found in schema cache, " +
                        "or columns have not been loaded yet. Try refreshing the schema cache.");
                }

                // Convert schema cache columns to ColumnInfo for the generator
                var columns = tableObj.Columns.Select(c => new ColumnInfo
                {
                    Name = c.ColumnName,
                    DataType = c.TypeDisplay,
                    IsNullable = c.IsNullable,
                    IsIdentity = c.IsIdentity,
                    IsComputed = c.IsComputed,
                    IsPrimaryKey = c.IsPrimaryKey
                }).ToList();

                var pkColumns = columns
                    .Where(c => c.IsPrimaryKey)
                    .Select(c => c.Name)
                    .ToList();

                var response = _generator.Generate(request, columns, pkColumns);

                return new RpcMessage
                {
                    MessageType = MessageTypes.CrudGenerationResult,
                    RequestId = message.RequestId,
                    Payload = MessagePackSerializer.Serialize(response)
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CrudGenerationHandler: request failed");
                return CreateErrorResponse(message.RequestId, ex.Message);
            }
        }

        private static RpcMessage CreateErrorResponse(int requestId, string error)
        {
            var errorResponse = new CrudGenerationResponse
            {
                Success = false,
                Error = error
            };

            return new RpcMessage
            {
                MessageType = MessageTypes.CrudGenerationResult,
                RequestId = requestId,
                Payload = MessagePackSerializer.Serialize(errorResponse)
            };
        }
    }
}
