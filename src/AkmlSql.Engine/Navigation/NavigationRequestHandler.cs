using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Navigation;

/// <summary>
/// Handles navigation IPC requests: GetObjectDefinition (60), FindReferences (61).
/// Delegates to <see cref="ObjectDefinitionService"/> and <see cref="ReferenceCollector"/> for
/// database queries, and uses <see cref="SchemaCacheManager"/> to resolve the session's schema cache.
/// (ObjectSearch (62) moved to the typed <c>ObjectSearchHandler</c> in spec 030.)
/// </summary>
public class NavigationRequestHandler(SchemaCacheManager schemaCacheManager)
{
    private readonly ObjectDefinitionService _definitionService = new();
    private readonly ReferenceCollector _referenceCollector = new();
    private readonly SchemaCacheManager _schemaCacheManager = schemaCacheManager ?? throw new ArgumentNullException(nameof(schemaCacheManager));

    /// <summary>
    /// Handles GetObjectDefinition (MessageType 60).
    /// Retrieves the SQL definition of a database object.
    /// </summary>
    public async Task<RpcMessage?> HandleGetObjectDefinitionAsync(
        RpcMessage request,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateDefinitionResponse(request.RequestId, new GetObjectDefinitionResponse
                {
                    Success = false,
                    Error = "Payload required"
                });
            }

            var req = MessagePackSerializer.Deserialize<GetObjectDefinitionRequest>(request.Payload);
            var (connectionString, databaseName) = sessionLookup(req.SessionId);

            if (string.IsNullOrEmpty(connectionString))
            {
                return CreateDefinitionResponse(request.RequestId, new GetObjectDefinitionResponse
                {
                    Success = false,
                    Error = "No active database connection for this session"
                });
            }

            // Cache is keyed by SESSION ID (the populators use GetOrCreateCache(SessionId, db));
            // passing the connection string here always missed and forced the live-query fallback.
            var dbCache = databaseName != null
                ? _schemaCacheManager.GetCache(req.SessionId, databaseName)
                : null;

            var (definition, objectType, fullName) = await _definitionService.GetDefinitionAsync(
                req.ObjectName, req.SchemaName, connectionString, dbCache, ct);

            if (definition == null)
            {
                return CreateDefinitionResponse(request.RequestId, new GetObjectDefinitionResponse
                {
                    Success = false,
                    Error = $"Object '{(req.SchemaName != null ? req.SchemaName + "." : "")}{req.ObjectName}' not found"
                });
            }

            Log.Debug("GetObjectDefinition: found {Type} {Name}", objectType, fullName);

            return CreateDefinitionResponse(request.RequestId, new GetObjectDefinitionResponse
            {
                Success = true,
                Definition = definition,
                ObjectType = objectType,
                FullName = fullName
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetObjectDefinition failed");
            return CreateDefinitionResponse(request.RequestId, new GetObjectDefinitionResponse
            {
                Success = false,
                Error = $"Failed to get definition: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Handles FindReferences (MessageType 61).
    /// Finds all objects that reference a given database object.
    /// </summary>
    public async Task<RpcMessage?> HandleFindReferencesAsync(
        RpcMessage request,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateReferencesResponse(request.RequestId, new FindReferencesResponse
                {
                    Success = false,
                    Error = "Payload required"
                });
            }

            var req = MessagePackSerializer.Deserialize<FindReferencesRequest>(request.Payload);
            var (connectionString, _) = sessionLookup(req.SessionId);

            if (string.IsNullOrEmpty(connectionString))
            {
                return CreateReferencesResponse(request.RequestId, new FindReferencesResponse
                {
                    Success = false,
                    Error = "No active database connection for this session"
                });
            }

            var references = await _referenceCollector.FindReferencesAsync(
                req.ObjectName, req.SchemaName, connectionString, ct);

            Log.Debug("FindReferences: found {Count} references to {Object}", references.Length, req.ObjectName);

            return CreateReferencesResponse(request.RequestId, new FindReferencesResponse
            {
                Success = true,
                References = references
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FindReferences failed");
            return CreateReferencesResponse(request.RequestId, new FindReferencesResponse
            {
                Success = false,
                Error = $"Failed to find references: {ex.Message}"
            });
        }
    }

    private static RpcMessage CreateDefinitionResponse(int requestId, GetObjectDefinitionResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.GetObjectDefinitionResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }

    private static RpcMessage CreateReferencesResponse(int requestId, FindReferencesResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.FindReferencesResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }
}
