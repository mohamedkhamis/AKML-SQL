using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T072 + M5 task T109. Routes IntelliSense
/// completion through the engine bridge when the bridge is open; when the bridge
/// is closed, falls back to the most-recently-used IndexedDB schema snapshot and
/// synthesises a completion list from the cached schemas + objects (+ columns when
/// Phase B is present). Column-context-aware completion (e.g. dot-after-alias)
/// requires a parser pass on the document text and is deferred — the offline
/// fallback returns the full schema/object surface so the user has something
/// useful while disconnected (FR-016).
/// </summary>
public interface ICompletionService
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct);
}

internal sealed class CompletionService : ICompletionService
{
    private readonly IEngineBridge _bridge;
    private readonly ISchemaCacheStore? _cache;

    public CompletionService(IEngineBridge bridge, ISchemaCacheStore? cache = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _cache = cache;   // null in tests that only exercise the online path
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open)
        {
            // T109 cache-backed fallback: if a schema snapshot is available locally,
            // synthesise a completion list from it. Returns empty when no cache.
            if (_cache != null)
            {
                return await BuildFromCacheAsync().ConfigureAwait(false);
            }
            return new CompletionResponse();
        }
        try
        {
            return await _bridge.SendAsync<CompletionRequest, CompletionResponse>(
                MessageTypes.RequestCompletion, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Bridge dropped mid-call -- try cache before returning empty.
            if (_cache != null)
            {
                return await BuildFromCacheAsync().ConfigureAwait(false);
            }
            return new CompletionResponse();
        }
    }

    private async Task<CompletionResponse> BuildFromCacheAsync()
    {
        // Keywords are ALWAYS available offline — they don't depend on any cache.
        // When no schema snapshot is present (fresh browser, never paired with an
        // engine, OR paired but the engine has no SQL session yet), the user still
        // gets SQL keywords. This makes "type WHERE then space" produce a useful
        // popup instead of nothing. Schemas / objects / columns layer on top
        // whenever a snapshot is available.
        var items = new List<CompletionItem>();
        AppendKeywords(items);

        // Pick the most-recently-used snapshot. The store sorts ascending; the user's
        // active session collapses to the LAST entry. Multi-server scenarios would
        // need an explicit active-session pointer; deferred for T109.
        var snapshots = await _cache!.ListAsync().ConfigureAwait(false);
        var active = snapshots.Count > 0 ? snapshots[snapshots.Count - 1] : null;
        if (active?.PhaseA != null && active.PhaseA.Length > 0)
        {
            SchemaPhasePayload? payloadA = null;
            try { payloadA = MessagePackSerializer.Deserialize<SchemaPhasePayload>(active.PhaseA); }
            catch (MessagePackSerializationException) { /* keywords only */ }

            // Phase B is optional; if present, it overrides Phase A's view (it's a
            // superset). The column items only appear when Phase B is cached.
            SchemaPhasePayload? payloadB = null;
            if (active.PhaseB != null && active.PhaseB.Length > 0)
            {
                try { payloadB = MessagePackSerializer.Deserialize<SchemaPhasePayload>(active.PhaseB); }
                catch (MessagePackSerializationException) { /* fall through with Phase A only */ }
            }

            if (payloadA != null || payloadB != null)
            {
                AppendSchemaSurface(items, payloadB ?? payloadA!);
                if (payloadB != null) AppendColumns(items, payloadB);
            }
        }

        return new CompletionResponse
        {
            Items = items.ToArray(),
            IsIncomplete = false,
        };
    }

    // The minimal SQL keyword surface. The online engine has a richer set, but for
    // offline this is enough to keep IntelliSense feeling alive while disconnected.
    private static readonly string[] BasicKeywords =
    {
        "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER",
        "ON", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN", "IS", "NULL", "AS",
        "GROUP BY", "ORDER BY", "HAVING", "DISTINCT", "TOP", "OFFSET", "FETCH",
        "INSERT", "UPDATE", "DELETE", "MERGE", "INTO", "VALUES", "SET",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PROCEDURE", "FUNCTION",
        "BEGIN", "END", "DECLARE", "IF", "ELSE", "WHILE", "RETURN", "CASE", "WHEN", "THEN",
    };

    private static void AppendKeywords(List<CompletionItem> items)
    {
        foreach (var kw in BasicKeywords)
        {
            items.Add(new CompletionItem
            {
                DisplayText = kw,
                InsertText = kw,
                ObjectType = (int)CompletionObjectType.Keyword,
                SortPriority = 50,
            });
        }
    }

    private static void AppendSchemaSurface(List<CompletionItem> items, SchemaPhasePayload payload)
    {
        foreach (var schema in payload.Schemas)
        {
            items.Add(new CompletionItem
            {
                DisplayText = schema.Name,
                InsertText = schema.Name,
                ObjectType = (int)CompletionObjectType.Schema,
                SortPriority = 30,
            });
            foreach (var obj in schema.Objects)
            {
                items.Add(new CompletionItem
                {
                    DisplayText = $"{obj.SchemaName}.{obj.ObjectName}",
                    InsertText = obj.ObjectName,
                    ObjectType = MapObjectType(obj.ObjectType),
                    SecondaryText = obj.SchemaName,
                    SourceObject = $"{obj.SchemaName}.{obj.ObjectName}",
                    SortPriority = 10,
                });
            }
        }
    }

    private static void AppendColumns(List<CompletionItem> items, SchemaPhasePayload payload)
    {
        foreach (var schema in payload.Schemas)
        {
            foreach (var obj in schema.Objects)
            {
                foreach (var col in obj.Columns)
                {
                    items.Add(new CompletionItem
                    {
                        DisplayText = col.Name,
                        InsertText = col.Name,
                        ObjectType = (int)CompletionObjectType.Column,
                        SecondaryText = col.TypeName,
                        SourceObject = $"{obj.SchemaName}.{obj.ObjectName}",
                        SortPriority = 20,
                    });
                }
            }
        }
    }

    private static int MapObjectType(int dbObjectType)
    {
        // Maps engine's DbObjectType (Table=0, View=1, Procedure=2, …) to the
        // CompletionObjectType enum (Table=0, View=1, Column=2, …, Procedure=6).
        return dbObjectType switch
        {
            0 => (int)CompletionObjectType.Table,
            1 => (int)CompletionObjectType.View,
            2 => (int)CompletionObjectType.Procedure,
            3 or 4 or 5 => (int)CompletionObjectType.Function,
            _ => (int)CompletionObjectType.Table,
        };
    }
}
