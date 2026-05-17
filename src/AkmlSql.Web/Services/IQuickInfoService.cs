using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T073. Routes quick-info (hover tooltips) through
/// the engine bridge when open; returns an empty response otherwise.
/// </summary>
public interface IQuickInfoService
{
    Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct);
}

internal sealed class QuickInfoService : IQuickInfoService
{
    private readonly IEngineBridge _bridge;

    public QuickInfoService(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open) return new QuickInfoResponse();
        try
        {
            return await _bridge.SendAsync<QuickInfoRequest, QuickInfoResponse>(
                MessageTypes.RequestQuickInfo, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return new QuickInfoResponse(); }
    }
}
