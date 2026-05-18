using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T073 + M5 task T109 follow-up. Routes
/// signature-help through the engine bridge when open; when closed, scans the
/// persisted editor document for the enclosing function-call site, looks the
/// procedure / function up in the cached PhaseB blob, and synthesises a
/// SignatureResponse with the parameter list and the current parameter index.
/// </summary>
public interface ISignatureHelpService
{
    Task<SignatureResponse> GetAsync(SignatureRequest request, CancellationToken ct);
}

internal sealed class SignatureHelpService : ISignatureHelpService
{
    private readonly IEngineBridge _bridge;
    private readonly ISchemaCacheStore? _cache;
    private readonly IEditorSessionStore? _session;

    public SignatureHelpService(
        IEngineBridge bridge,
        ISchemaCacheStore? cache = null,
        IEditorSessionStore? session = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _cache = cache;
        _session = session;
    }

    public async Task<SignatureResponse> GetAsync(SignatureRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open)
        {
            return await BuildOfflineAsync(request).ConfigureAwait(false);
        }
        try
        {
            return await _bridge.SendAsync<SignatureRequest, SignatureResponse>(
                MessageTypes.RequestSignatureHelp, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return await BuildOfflineAsync(request).ConfigureAwait(false); }
    }

    private async Task<SignatureResponse> BuildOfflineAsync(SignatureRequest request)
    {
        if (_cache == null || _session == null) return new SignatureResponse();

        var session = await _session.RestoreAsync().ConfigureAwait(false);
        if (session == null || string.IsNullOrEmpty(session.DocumentText)) return new SignatureResponse();

        var callSite = OfflineSqlScanner.FindEnclosingCall(session.DocumentText, request.CursorOffset);
        if (!callSite.IsValid) return new SignatureResponse();

        var snapshots = await _cache.ListAsync().ConfigureAwait(false);
        if (snapshots.Count == 0) return new SignatureResponse();
        var active = snapshots[snapshots.Count - 1];

        // PhaseB is mandatory for signature help -- parameters live there only.
        if (active.PhaseB == null || active.PhaseB.Length == 0) return new SignatureResponse();
        SchemaPhasePayload payload;
        try { payload = MessagePackSerializer.Deserialize<SchemaPhasePayload>(active.PhaseB); }
        catch (MessagePackSerializationException) { return new SignatureResponse(); }

        SchemaPhaseObject? procedure = null;
        if (!string.IsNullOrEmpty(callSite.Prefix))
        {
            var schemaEntry = payload.Schemas.FirstOrDefault(s =>
                string.Equals(s.Name, callSite.Prefix, StringComparison.OrdinalIgnoreCase));
            procedure = schemaEntry?.Objects.FirstOrDefault(o => MatchesCall(o, callSite.FunctionName));
        }
        if (procedure == null)
        {
            foreach (var schema in payload.Schemas)
            {
                procedure = schema.Objects.FirstOrDefault(o => MatchesCall(o, callSite.FunctionName));
                if (procedure != null) break;
            }
        }
        if (procedure == null) return new SignatureResponse();

        var overload = BuildOverload(procedure);
        return new SignatureResponse
        {
            FunctionName = $"{procedure.SchemaName}.{procedure.ObjectName}",
            Overloads = new[] { overload },
            ActiveOverload = 0,
            ActiveParameter = System.Math.Min(callSite.ParameterIndex, procedure.Parameters.Length - 1),
        };
    }

    private static bool MatchesCall(SchemaPhaseObject obj, string callName)
    {
        if (!string.Equals(obj.ObjectName, callName, StringComparison.OrdinalIgnoreCase)) return false;
        // Only procedures + functions carry parameters; ignore tables / views / sequences.
        return obj.ObjectType >= 2 && obj.ObjectType <= 5;
    }

    private static SignatureOverload BuildOverload(SchemaPhaseObject procedure)
    {
        var paramSegments = procedure.Parameters
            .Select(p => p.Name + " " + p.TypeName + (p.IsOutput ? " OUT" : "") + (p.HasDefault ? " = …" : ""))
            .ToArray();
        var label = $"{procedure.SchemaName}.{procedure.ObjectName}({string.Join(", ", paramSegments)})";
        return new SignatureOverload
        {
            Label = label,
            Documentation = procedure.Description ?? string.Empty,
            Parameters = procedure.Parameters.Select(p => new ParameterInfo
            {
                Name = p.Name,
                Type = p.TypeName,
                IsOptional = p.HasDefault,
            }).ToArray(),
        };
    }
}
