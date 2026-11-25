using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 047 (ENG-001, contracts/engine-pairing.md §5–6): the browsers paired with the connected engine, listed and revoked one
/// by one from Settings › Connections. Needs an open bridge to an engine that advertises <c>pairing.manage.v1</c>.
/// </summary>
public interface IPairingService
{
    /// <summary>The bridge is open and the engine offers pairing management.</summary>
    bool Available { get; }

    /// <summary>Raised when <see cref="Available"/> may have changed (the bridge opened, closed or reconnected).</summary>
    event Action? AvailabilityChanged;

    /// <summary>The engine's paired browsers; null when the engine could not be asked.</summary>
    Task<PairingListResponse?> ListAsync(CancellationToken ct);

    /// <summary>Removes one pairing (and closes its connections); null when the engine could not be asked.</summary>
    Task<PairingRevokeResponse?> RevokeAsync(string id, CancellationToken ct);
}

internal sealed class PairingService : IPairingService, IDisposable
{
    /// <summary>The capability the engine advertises for the two messages (Capabilities.PairingManageV1).</summary>
    public const string Capability = "pairing.manage.v1";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly IEngineBridge _bridge;

    public PairingService(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _bridge.StateChanged += OnBridgeStateChanged;
    }

    public bool Available =>
        _bridge.State == BridgeState.Open && Array.IndexOf(_bridge.EngineCapabilities ?? Array.Empty<string>(), Capability) >= 0;

    public event Action? AvailabilityChanged;

    public Task<PairingListResponse?> ListAsync(CancellationToken ct) =>
        SendAsync<PairingListRequest, PairingListResponse>(MessageTypes.PairingListRequest, new PairingListRequest(), ct);

    public Task<PairingRevokeResponse?> RevokeAsync(string id, CancellationToken ct) =>
        SendAsync<PairingRevokeRequest, PairingRevokeResponse>(MessageTypes.PairingRevokeRequest, new PairingRevokeRequest { Id = id }, ct);

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct)
        where TRequest : class where TResponse : class
    {
        if (!Available) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await _bridge.SendAsync<TRequest, TResponse>(type, request, timeout.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return null; }   // the bridge dropped, or the engine answered with an error
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }   // no answer in time
    }

    private void OnBridgeStateChanged(BridgeState _) => AvailabilityChanged?.Invoke();

    public void Dispose() => _bridge.StateChanged -= OnBridgeStateChanged;
}
