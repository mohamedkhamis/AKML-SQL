using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T073 + M5 task T109 follow-up. Routes hover
/// quick-info through the engine bridge when open; when closed, reads the persisted
/// editor session text (so we know what's at the caret) and the cached PhaseB blob
/// (so we know the column / object metadata) and synthesises a QuickInfoResponse.
/// Returns empty when either source is unavailable -- the caller treats empty as
/// "no info" exactly like the online path.
/// </summary>
public interface IQuickInfoService
{
    Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct);
}

internal sealed class QuickInfoService : IQuickInfoService
{
    private readonly IEngineBridge _bridge;
    private readonly ISchemaCacheStore? _cache;
    private readonly IEditorSessionStore? _session;

    public QuickInfoService(
        IEngineBridge bridge,
        ISchemaCacheStore? cache = null,
        IEditorSessionStore? session = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _cache = cache;
        _session = session;
    }

    public async Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open)
        {
            return await BuildOfflineAsync(request).ConfigureAwait(false);
        }
        try
        {
            return await _bridge.SendAsync<QuickInfoRequest, QuickInfoResponse>(
                MessageTypes.RequestQuickInfo, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return await BuildOfflineAsync(request).ConfigureAwait(false); }
    }

    private async Task<QuickInfoResponse> BuildOfflineAsync(QuickInfoRequest request)
    {
        if (_cache == null || _session == null) return new QuickInfoResponse();

        var session = await _session.RestoreAsync().ConfigureAwait(false);
        if (session == null || string.IsNullOrEmpty(session.DocumentText)) return new QuickInfoResponse();

        var token = OfflineSqlScanner.FindIdentifierAt(session.DocumentText, request.CursorOffset);
        if (!token.IsValid) return new QuickInfoResponse();

        var snapshots = await _cache.ListAsync().ConfigureAwait(false);
        if (snapshots.Count == 0) return new QuickInfoResponse();
        var active = snapshots[snapshots.Count - 1];

        // Prefer Phase B (carries columns + descriptions); fall back to A.
        SchemaPhasePayload? payload = TryDeserialise(active.PhaseB) ?? TryDeserialise(active.PhaseA);
        if (payload == null) return new QuickInfoResponse();

        // Pattern 1: dotted prefix matches a known schema → "schema.object".
        if (!string.IsNullOrEmpty(token.Prefix))
        {
            var schemaEntry = payload.Schemas.FirstOrDefault(s =>
                string.Equals(s.Name, token.Prefix, StringComparison.OrdinalIgnoreCase));
            if (schemaEntry != null)
            {
                var obj = schemaEntry.Objects.FirstOrDefault(o =>
                    string.Equals(o.ObjectName, token.Identifier, StringComparison.OrdinalIgnoreCase));
                if (obj != null) return BuildForObject(obj);
            }
            // Pattern 2: prefix didn't match a schema → maybe alias.object_column.
            // Search every schema's objects for one with a column matching the identifier
            // AND a name matching the alias's likely target. Without alias resolution we
            // can only do a column-name search across all objects.
            foreach (var schema in payload.Schemas)
            {
                foreach (var obj in schema.Objects)
                {
                    var col = obj.Columns.FirstOrDefault(c =>
                        string.Equals(c.Name, token.Identifier, StringComparison.OrdinalIgnoreCase));
                    if (col != null) return BuildForColumn(obj, col);
                }
            }
            return new QuickInfoResponse();
        }

        // Pattern 3: no prefix → search schemas first, then objects across schemas.
        var schemaMatch = payload.Schemas.FirstOrDefault(s =>
            string.Equals(s.Name, token.Identifier, StringComparison.OrdinalIgnoreCase));
        if (schemaMatch != null)
        {
            return new QuickInfoResponse
            {
                ObjectType = "Schema",
                Header = schemaMatch.Name,
                Description = $"{schemaMatch.Objects.Length} object(s) cached.",
            };
        }
        foreach (var schema in payload.Schemas)
        {
            var obj = schema.Objects.FirstOrDefault(o =>
                string.Equals(o.ObjectName, token.Identifier, StringComparison.OrdinalIgnoreCase));
            if (obj != null) return BuildForObject(obj);
        }

        return new QuickInfoResponse();
    }

    private static QuickInfoResponse BuildForObject(SchemaPhaseObject obj)
    {
        var typeName = ObjectTypeName(obj.ObjectType);
        var response = new QuickInfoResponse
        {
            ObjectType = typeName,
            Header = $"{obj.SchemaName}.{obj.ObjectName}",
            Description = obj.Description,
        };

        // Surface column or parameter summary in Details.
        if (obj.Columns.Length > 0)
        {
            response.Details = obj.Columns
                .Select(c => new QuickInfoDetail(c.Name, c.TypeName + (c.IsPrimaryKey ? " (PK)" : "")))
                .ToArray();
        }
        else if (obj.Parameters.Length > 0)
        {
            response.Details = obj.Parameters
                .Select(p => new QuickInfoDetail(p.Name, p.TypeName + (p.IsOutput ? " OUT" : "")))
                .ToArray();
        }
        return response;
    }

    private static QuickInfoResponse BuildForColumn(SchemaPhaseObject parent, SchemaPhaseColumn col)
    {
        return new QuickInfoResponse
        {
            ObjectType = "Column",
            Header = $"{parent.SchemaName}.{parent.ObjectName}.{col.Name}",
            Description = col.Description,
            Details = new[]
            {
                new QuickInfoDetail("Type", col.TypeName + (col.IsNullable ? " NULL" : " NOT NULL")),
            },
        };
    }

    private static SchemaPhasePayload? TryDeserialise(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try { return MessagePackSerializer.Deserialize<SchemaPhasePayload>(bytes); }
        catch (MessagePackSerializationException) { return null; }
    }

    private static string ObjectTypeName(int dbObjectType) => dbObjectType switch
    {
        0 => "Table",
        1 => "View",
        2 => "Procedure",
        3 => "ScalarFunction",
        4 => "TableFunction",
        5 => "InlineFunction",
        6 => "Synonym",
        7 => "Sequence",
        _ => "Object",
    };
}
