using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Browser-side facade over the engine's SQL History IPC (HistoryRecord=40, HistorySearch=41,
/// HistoryAction=42), reachable over the WebSocket bridge. Every method no-ops / returns an empty
/// result when the bridge is not <see cref="BridgeState.Open"/>; failures are logged, never thrown.
/// </summary>
public interface IHistoryService
{
    /// <summary>True when the engine bridge is open (history can be read/written).</summary>
    bool IsAvailable { get; }

    Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct);

    /// <summary>Records a completed execution (fire-and-forget notification).</summary>
    Task RecordAsync(HistoryRecordRequest request, CancellationToken ct);

    Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct);
    Task<bool> RenameAsync(long id, string newName, CancellationToken ct);
    Task<int> DeleteAsync(long[] ids, CancellationToken ct);
    Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct);
    Task<string?> GetFullSqlAsync(long id, CancellationToken ct);
    Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct);
    Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct);
}

internal sealed class HistoryService : IHistoryService
{
    private readonly IEngineBridge _bridge;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public HistoryService(IEngineBridge bridge, IDiagnosticsRingBuffer diagnostics)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public bool IsAvailable => _bridge.State == BridgeState.Open;

    public async Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct)
    {
        if (!IsAvailable)
            return new HistorySearchResponse { Success = false, Entries = Array.Empty<HistoryEntryDto>(), TotalCount = 0 };
        try
        {
            var r = await _bridge.SendAsync<HistorySearchRequest, HistorySearchResponse>(
                MessageTypes.HistorySearch, request, ct).ConfigureAwait(false);
            return r ?? new HistorySearchResponse { Success = false, Entries = Array.Empty<HistoryEntryDto>() };
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "history", $"HistorySearch failed: {ex.Message}");
            return new HistorySearchResponse { Success = false, Entries = Array.Empty<HistoryEntryDto>(), Error = ex.Message };
        }
    }

    public async Task RecordAsync(HistoryRecordRequest request, CancellationToken ct)
    {
        if (!IsAvailable) return;
        try
        {
            await _bridge.SendNotificationAsync(MessageTypes.HistoryRecord, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Trace, "history", $"HistoryRecord send failed: {ex.Message}");
        }
    }

    public Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct) =>
        ActionBool(new HistoryActionRequest { Action = HistoryActions.ToggleFavorite, EntryIds = new[] { id } }, ct);

    public Task<bool> RenameAsync(long id, string newName, CancellationToken ct) =>
        ActionBool(new HistoryActionRequest { Action = HistoryActions.Rename, EntryIds = new[] { id }, NewName = newName }, ct);

    public async Task<int> DeleteAsync(long[] ids, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.Delete, EntryIds = ids }, ct);
        return r?.DeletedCount ?? 0;
    }

    public async Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest
        {
            Action = HistoryActions.RemoveOlderThan,
            EntryIds = new[] { anchorId },
            KeepFavorites = keepFavorites,
        }, ct);
        return r?.DeletedCount ?? 0;
    }

    public async Task<string?> GetFullSqlAsync(long id, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.GetFullSql, EntryIds = new[] { id } }, ct);
        return r?.FullSqlText;
    }

    public async Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.GetVersions, EntryIds = new[] { id } }, ct);
        return r?.Versions ?? Array.Empty<HistoryVersionDto>();
    }

    public async Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.GetDiff, EntryIds = new[] { a, b } }, ct);
        return r is { Success: true } ? (r.DiffLeftSql ?? string.Empty, r.DiffRightSql ?? string.Empty) : null;
    }

    private async Task<HistoryActionResponse?> Action(HistoryActionRequest request, CancellationToken ct)
    {
        if (!IsAvailable) return null;
        try
        {
            return await _bridge.SendAsync<HistoryActionRequest, HistoryActionResponse>(
                MessageTypes.HistoryAction, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "history", $"HistoryAction {request.Action} failed: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> ActionBool(HistoryActionRequest request, CancellationToken ct)
    {
        var r = await Action(request, ct);
        return r?.Success == true;
    }
}
