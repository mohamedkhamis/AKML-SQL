using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T074. Routes goto-definition through the bridge;
/// returns null when the bridge is not open (the UI renders a "requires engine"
/// notice via CapabilityNotice rather than a silent no-op).
/// </summary>
public interface IGotoDefinitionService
{
    Task<GetObjectDefinitionResponse?> GetAsync(GetObjectDefinitionRequest request, CancellationToken ct);
}

internal sealed class GotoDefinitionService : IGotoDefinitionService
{
    private readonly IEngineBridge _bridge;

    public GotoDefinitionService(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<GetObjectDefinitionResponse?> GetAsync(GetObjectDefinitionRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open) return null;
        try
        {
            return await _bridge.SendAsync<GetObjectDefinitionRequest, GetObjectDefinitionResponse>(
                MessageTypes.GetObjectDefinition, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return null; }
    }
}
