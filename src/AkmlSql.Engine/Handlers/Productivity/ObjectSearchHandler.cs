using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Productivity;

/// <summary>
/// Spec 030 T085 / FR-045 — typed handler for ObjectSearch (MessageType 62 → 162).
///
/// Replaces the legacy <c>NavigationRequestHandler.HandleObjectSearchAsync</c> raw dispatch.
/// Resolves the active session's <see cref="DatabaseCache"/> via
/// <see cref="SchemaCacheManager.GetCache"/> using the <c>sessionId</c> in the server slot —
/// matching every other new-style handler (CompletionHandler, SchemaChecksumHandler,
/// SchemaPhaseA/B). The legacy path keyed by connection string, which is the cache-miss bug
/// this closure fixes.
///
/// Behavioural contract is unchanged from the legacy handler so the shell consumer needs no
/// edit: a connected session is required, a missing cache yields an empty (successful) result,
/// and the fuzzy scoring buckets (exact &gt; name-prefix &gt; fullname-prefix &gt; contains &gt;
/// abbreviation) are ported verbatim.
/// </summary>
public sealed class ObjectSearchHandler : IRpcRequestHandler<ObjectSearchRequest, ObjectSearchResponse>
{
    public int RequestMessageType => MessageTypes.ObjectSearch;
    public int ResponseMessageType => MessageTypes.ObjectSearchResult;

    public Task<ObjectSearchResponse> HandleAsync(ObjectSearchRequest request, RpcContext ctx, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));

        try
        {
            var session = ctx.Sessions.GetSession(request.SessionId);
            if (session == null || !session.IsConnected || string.IsNullOrEmpty(session.DatabaseName))
            {
                return Task.FromResult(new ObjectSearchResponse
                {
                    Success = false,
                    Error = "No active database connection for this session"
                });
            }

            // sessionId in the server slot — consistent with CompletionHandler / SchemaChecksumHandler.
            var dbCache = ctx.SchemaCache.GetCache(request.SessionId, session.DatabaseName);
            if (dbCache == null)
            {
                // Schema cache not yet populated — succeed with no matches (legacy parity).
                return Task.FromResult(new ObjectSearchResponse
                {
                    Success = true,
                    Results = Array.Empty<ObjectSearchResultDto>()
                });
            }

            var results = SearchObjects(dbCache, request.SearchText, request.MaxResults);

            Log.Debug("ObjectSearch: found {Count} results for '{Search}'", results.Length, request.SearchText);

            return Task.FromResult(new ObjectSearchResponse
            {
                Success = true,
                Results = results
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ObjectSearch failed");
            return Task.FromResult(new ObjectSearchResponse
            {
                Success = false,
                Error = $"Search failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Searches the schema cache for objects matching the search text. Uses fuzzy matching:
    /// prefix match, contains match, and abbreviation match. Results are sorted by match quality
    /// (exact &gt; prefix &gt; contains &gt; abbreviation), then by name. Ported from the legacy
    /// <c>NavigationRequestHandler.SearchObjects</c> for behavioural parity.
    /// </summary>
    private static ObjectSearchResultDto[] SearchObjects(DatabaseCache dbCache, string searchText, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Array.Empty<ObjectSearchResultDto>();
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
            // Abbreviation / camelCase match (e.g. "gau" matches "GetAllUsers")
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
    /// Checks if the search text matches as an abbreviation of the object name. Matches uppercase
    /// letters and word boundaries (e.g. "gau" matches "GetAllUsers", "usp_gu" matches
    /// "usp_GetUsers").
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
}
