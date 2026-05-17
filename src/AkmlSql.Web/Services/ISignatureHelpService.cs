using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T073. Routes signature-help through the engine
/// bridge when open; returns an empty response otherwise.
/// </summary>
public interface ISignatureHelpService
{
    Task<SignatureResponse> GetAsync(SignatureRequest request, CancellationToken ct);
}

internal sealed class SignatureHelpService : ISignatureHelpService
{
    private readonly IEngineBridge _bridge;

    public SignatureHelpService(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<SignatureResponse> GetAsync(SignatureRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open) return new SignatureResponse();
        try
        {
            return await _bridge.SendAsync<SignatureRequest, SignatureResponse>(
                MessageTypes.RequestSignatureHelp, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return new SignatureResponse(); }
    }
}
