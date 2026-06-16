using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Navigation;
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

                // Get the database cache for this session. The cache is keyed by SESSION ID (see
                // ConnectionChangedHandler / AnalysisHandlers / SchemaRefreshService, which all populate
                // via GetOrCreateCache(SessionId, db)) — NOT the connection string. Passing the raw
                // connection string here made every lookup miss → spurious "Schema cache not available".
                var dbCache = _schemaCacheManager.GetCache(request.SessionId, databaseName);

                // Spec 030 T066 (FR-022): "Script as → ALTER" rewrites a programmable object's live
                // definition (sys.sql_modules). It uses the connection directly and does NOT need the
                // column cache, so branch here — before the dbCache null-guard — so it still works on a
                // cold start where columns aren't loaded yet.
                if (string.Equals(request.TemplateType, "ALTER", StringComparison.OrdinalIgnoreCase))
                {
                    var alterResponse = await GenerateAlterAsync(request, connectionString, dbCache, ct);
                    return new RpcMessage
                    {
                        MessageType = MessageTypes.ScriptAsResult,
                        RequestId = message.RequestId,
                        Payload = MessagePackSerializer.Serialize(alterResponse)
                    };
                }

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

        /// <summary>
        /// Spec 030 T066 — fetches the object's live module definition and rewrites its leading
        /// CREATE to ALTER. Programmable objects only: a table (whose "definition" is a synthesised
        /// CREATE TABLE script) is refused, since ALTER TABLE has different semantics.
        /// </summary>
        private static async Task<ScriptAsResponse> GenerateAlterAsync(
            ScriptAsRequest request, string connectionString, DatabaseCache? dbCache, CancellationToken ct)
        {
            var (definition, objectType, fullName) = await new ObjectDefinitionService()
                .GetDefinitionAsync(request.ObjectName, request.SchemaName, connectionString, dbCache, ct);

            if (string.IsNullOrEmpty(definition))
            {
                return new ScriptAsResponse
                {
                    Success = false,
                    Error = $"Could not retrieve a definition for [{request.SchemaName}].[{request.ObjectName}]. " +
                            "The object may not exist or its schema cache is not populated."
                };
            }

            if (string.Equals(objectType, "Table", StringComparison.OrdinalIgnoreCase))
            {
                return new ScriptAsResponse
                {
                    Success = false,
                    Error = "Script as ALTER applies to procedures, views, functions and triggers — not tables."
                };
            }

            var (ok, altered, error) = ScriptAsAlterRewriter.ToAlter(definition);
            if (!ok)
            {
                return new ScriptAsResponse { Success = false, Error = error };
            }

            return new ScriptAsResponse
            {
                Success = true,
                Sql = altered,
                TemplateType = "ALTER",
                FullObjectName = fullName ?? $"[{request.SchemaName}].[{request.ObjectName}]"
            };
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
