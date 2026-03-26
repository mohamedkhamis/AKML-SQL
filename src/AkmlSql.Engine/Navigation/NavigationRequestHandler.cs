#nullable enable
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Navigation;

/// <summary>
/// Handles navigation IPC requests: GetObjectDefinition (60), FindReferences (61), ObjectSearch (62).
/// Delegates to <see cref="ObjectDefinitionService"/> and <see cref="ReferenceCollector"/> for
/// database queries, and uses <see cref="SchemaCacheManager"/> for object search.
/// </summary>
public class NavigationRequestHandler
{
    private readonly ObjectDefinitionService _definitionService = new();
    private readonly ReferenceCollector _referenceCollector = new();
    private readonly SchemaCacheManager _schemaCacheManager;

    public NavigationRequestHandler(SchemaCacheManager schemaCacheManager)
    {
        _schemaCacheManager = schemaCacheManager ?? throw new ArgumentNullException(nameof(schemaCacheManager));
    }

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

            var dbCache = databaseName != null
                ? _schemaCacheManager.GetCache(connectionString, databaseName)
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

    /// <summary>
    /// Handles ObjectSearch (MessageType 62).
    /// Searches the schema cache for objects matching a fuzzy name pattern.
    /// </summary>
    public Task<RpcMessage?> HandleObjectSearchAsync(
        RpcMessage request,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        try
        {
            if (request.Payload == null)
            {
                return Task.FromResult<RpcMessage?>(CreateSearchResponse(request.RequestId, new ObjectSearchResponse
                {
                    Success = false,
                    Error = "Payload required"
                }));
            }

            var req = MessagePackSerializer.Deserialize<ObjectSearchRequest>(request.Payload);
            var (_, databaseName) = sessionLookup(req.SessionId);

            if (string.IsNullOrEmpty(databaseName))
            {
                return Task.FromResult<RpcMessage?>(CreateSearchResponse(request.RequestId, new ObjectSearchResponse
                {
                    Success = false,
                    Error = "No active database connection for this session"
                }));
            }

            var (connectionString, _) = sessionLookup(req.SessionId);
            var dbCache = !string.IsNullOrEmpty(connectionString)
                ? _schemaCacheManager.GetCache(connectionString, databaseName)
                : null;
            if (dbCache == null)
            {
                return Task.FromResult<RpcMessage?>(CreateSearchResponse(request.RequestId, new ObjectSearchResponse
                {
                    Success = true,
                    Results = []
                }));
            }

            var results = SearchObjects(dbCache, req.SearchText, req.MaxResults);

            Log.Debug("ObjectSearch: found {Count} results for '{Search}'", results.Length, req.SearchText);

            return Task.FromResult<RpcMessage?>(CreateSearchResponse(request.RequestId, new ObjectSearchResponse
            {
                Success = true,
                Results = results
            }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ObjectSearch failed");
            return Task.FromResult<RpcMessage?>(CreateSearchResponse(request.RequestId, new ObjectSearchResponse
            {
                Success = false,
                Error = $"Search failed: {ex.Message}"
            }));
        }
    }

    /// <summary>
    /// Searches the schema cache for objects matching the search text.
    /// Uses fuzzy matching: prefix match, contains match, and abbreviation match.
    /// Results are sorted by match quality (prefix > contains > abbreviation).
    /// </summary>
    private static ObjectSearchResultDto[] SearchObjects(DatabaseCache dbCache, string searchText, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [];
        }

        var searchLower = searchText.ToLowerInvariant();
        var results = new List<(ObjectSearchResultDto Result, int Score)>();

        foreach (var obj in dbCache.GetAllObjects())
        {
            var nameLower = obj.ObjectName.ToLowerInvariant();
            var fullNameLower = obj.FullName.ToLowerInvariant();
            int score = 0;

            // Exact match (highest priority)
            if (nameLower == searchLower || fullNameLower == searchLower)
            {
                score = 100;
            }
            // Prefix match
            else if (nameLower.StartsWith(searchLower, StringComparison.Ordinal))
            {
                score = 80;
            }
            // Full name prefix match (schema.name)
            else if (fullNameLower.StartsWith(searchLower, StringComparison.Ordinal))
            {
                score = 70;
            }
            // Contains match
            else if (nameLower.Contains(searchLower, StringComparison.Ordinal))
            {
                score = 50;
            }
            // Abbreviation/camelCase match (e.g. "gau" matches "GetAllUsers")
            else if (MatchesAbbreviation(obj.ObjectName, searchText))
            {
                score = 30;
            }

            if (score > 0)
            {
                results.Add((new ObjectSearchResultDto
                {
                    SchemaName = obj.SchemaName,
                    ObjectName = obj.ObjectName,
                    ObjectType = obj.ObjectType.ToString()
                }, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Result.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(r => r.Result)
            .ToArray();
    }

    /// <summary>
    /// Checks if the search text matches as an abbreviation of the object name.
    /// Matches uppercase letters and word boundaries (e.g. "gau" matches "GetAllUsers",
    /// "usp_gu" matches "usp_GetUsers").
    /// </summary>
    private static bool MatchesAbbreviation(string objectName, string searchText)
    {
        if (string.IsNullOrEmpty(searchText) || string.IsNullOrEmpty(objectName))
            return false;

        int searchIdx = 0;
        for (int i = 0; i < objectName.Length && searchIdx < searchText.Length; i++)
        {
            if (char.ToLowerInvariant(objectName[i]) == char.ToLowerInvariant(searchText[searchIdx]))
            {
                // Match if at start, after underscore, or uppercase letter
                if (i == 0 || objectName[i - 1] == '_' || char.IsUpper(objectName[i]) || searchIdx > 0)
                {
                    searchIdx++;
                }
            }
        }

        return searchIdx == searchText.Length;
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

    private static RpcMessage CreateSearchResponse(int requestId, ObjectSearchResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.ObjectSearchResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }
}
