using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Productivity
{
    /// <summary>
    /// T102: IPC handler for Script As requests. Looks up object metadata from the schema
    /// cache and delegates to <see cref="ScriptAsGenerator"/>.
    /// </summary>
    internal sealed class ScriptAsHandler(SchemaCacheManager schemaCacheManager)
    {
        private readonly ScriptAsGenerator _generator = new();
        private readonly SchemaCacheManager _schemaCacheManager = schemaCacheManager ?? throw new ArgumentNullException(nameof(schemaCacheManager));

        /// <summary>
        /// Handles a ScriptAs request (MessageType 67).
        /// </summary>
        public async Task<RpcMessage?> HandleAsync(
            RpcMessage message,
            Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
            CancellationToken ct)
        {
            try
            {
                var request = MessagePackSerializer.Deserialize<ScriptAsRequest>(message.Payload!);

                Log.Information("ScriptAsHandler: generating {Type} script for [{Schema}].[{Object}]",
                    request.TemplateType, request.SchemaName, request.ObjectName);

                // Look up session to get connection/database info
                var (connectionString, databaseName) = sessionLookup(request.SessionId);

                if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))
                {
                    return CreateErrorResponse(message.RequestId,
                        "No active database connection for this session. Ensure a connection is active.");
                }

                // Get the database cache for this session
                var dbCache = _schemaCacheManager.GetCache(connectionString, databaseName);
                if (dbCache == null)
                {
                    return CreateErrorResponse(message.RequestId,
                        $"Schema cache not available for database '{databaseName}'. " +
                        "Ensure the schema cache is populated (try reconnecting or refreshing).");
                }

                // Look up the object from the cache
                var dbObject = dbCache.FindObject(request.SchemaName, request.ObjectName);
                if (dbObject == null || !dbObject.ColumnsLoaded)
                {
                    return CreateErrorResponse(message.RequestId,
                        $"Object [{request.SchemaName}].[{request.ObjectName}] not found in schema cache, " +
                        "or columns have not been loaded yet. Try refreshing the schema cache.");
                }

                // Convert schema cache columns to ColumnInfo for the generator
                var columns = dbObject.Columns.Select(c => new ColumnInfo
                {
                    Name = c.ColumnName,
                    DataType = c.TypeDisplay,
                    IsNullable = c.IsNullable,
                    IsIdentity = c.IsIdentity,
                    IsComputed = c.IsComputed,
                    IsPrimaryKey = c.IsPrimaryKey
                }).ToList();

                // Map DbObjectType to the string expected by ScriptAsGenerator
                var objectType = dbObject.ObjectType switch
                {
                    DbObjectType.Table => "TABLE",
                    DbObjectType.View => "VIEW",
                    DbObjectType.Procedure => "PROCEDURE",
                    DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction => "FUNCTION",
                    _ => dbObject.ObjectType.ToString().ToUpperInvariant()
                };

                var response = _generator.Generate(request, columns, objectType);

                return new RpcMessage
                {
                    MessageType = MessageTypes.ScriptAsResult,
                    RequestId = message.RequestId,
                    Payload = MessagePackSerializer.Serialize(response)
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScriptAsHandler: request failed");
                return CreateErrorResponse(message.RequestId, ex.Message);
            }
        }

        private static RpcMessage CreateErrorResponse(int requestId, string error)
        {
            var errorResponse = new ScriptAsResponse
            {
                Success = false,
                Error = error
            };

            return new RpcMessage
            {
                MessageType = MessageTypes.ScriptAsResult,
                RequestId = requestId,
                Payload = MessagePackSerializer.Serialize(errorResponse)
            };
        }
    }
}
