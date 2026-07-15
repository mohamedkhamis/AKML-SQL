using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 — routes the "expand SELECT *" (Tab after a <c>*</c>) request through the bridge to the
/// engine's <c>WildcardExpansion</c> handler, which parses the statement and returns the columns of
/// the FROM tables (from the session's schema cache). Returns null when the bridge is not open, so
/// the caller silently falls back to the normal Tab behaviour.
/// </summary>
public interface IWildcardExpansionService
{
    Task<WildcardExpansionResponse?> GetAsync(WildcardExpansionRequest request, CancellationToken ct);
}

internal sealed class WildcardExpansionService : IWildcardExpansionService
{
    private readonly IEngineBridge _bridge;

    public WildcardExpansionService(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<WildcardExpansionResponse?> GetAsync(WildcardExpansionRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open) return null;
        try
        {
            return await _bridge.SendAsync<WildcardExpansionRequest, WildcardExpansionResponse>(
                MessageTypes.WildcardExpansion, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return null; }
    }
}
