using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T072. Routes IntelliSense completion through
/// the engine bridge when the bridge is open; falls back to an empty response when
/// disconnected (the M5 schema-cache fallback at T109 will replace the empty path).
/// </summary>
public interface ICompletionService
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct);
}

internal sealed class CompletionService : ICompletionService
{
    private readonly IEngineBridge _bridge;

    public CompletionService(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open)
        {
            // FR-016: graceful offline. M5 / T109 wires the IndexedDB cache fallback here.
            return new CompletionResponse();
        }
        try
        {
            return await _bridge.SendAsync<CompletionRequest, CompletionResponse>(
                MessageTypes.RequestCompletion, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Bridge dropped mid-call -- return empty rather than surfacing.
            return new CompletionResponse();
        }
    }
}
