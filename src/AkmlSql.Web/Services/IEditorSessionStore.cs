using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T051. Persists the editor session (text, caret,
/// selected profile id) to IndexedDB so a reload restores the working state. Writes are
/// debounced 500 ms by <see cref="SaveAsync"/>; restoration on <see cref="RestoreAsync"/>
/// returns the most recent record or null.
/// </summary>
public interface IEditorSessionStore
{
    /// <summary>Schedule a debounced write. Multiple calls within 500 ms collapse to one write.</summary>
    Task SaveAsync(EditorSessionRecord record);

    /// <summary>Force-flush any pending debounced write.</summary>
    Task FlushAsync();

    /// <summary>Read the most recent record. Returns null when nothing is persisted yet.</summary>
    Task<EditorSessionRecord?> RestoreAsync();

    /// <summary>Drop the persisted session. Used by the "Reset editor" Settings action.</summary>
    Task ClearAsync();
}

/// <summary>Editor session per data-model.md E6.</summary>
public sealed class EditorSessionRecord
{
    public string DocumentText { get; set; } = string.Empty;
    public int CursorOffset { get; set; }
    public string? ActiveProfileId { get; set; }

    /// <summary>
    /// Query-session grouping key (GUID "N" format), minted by <see cref="EditorSessionKeys"/> on
    /// first use and persisted alongside the rest of this record so a Blazor circuit reset (full
    /// page reload) keeps landing in the SAME history session. Null until first minted.
    /// </summary>
    public string? SessionKey { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    // Spec 041 (research R64, data-model.md §1.3) — the document. Additive: an older record reads the defaults.
    // SaveAsync overwrites the whole record, so every writer carries these (EditorSessionKeys).

    /// <summary>The document's name (<c>SQLQuery{n}</c> until the user names it); null in a record from before
    /// documents had names.</summary>
    public string? DocumentName { get; set; }

    /// <summary>The text differs from what was last opened, started or saved.</summary>
    public bool IsModified { get; set; }

    /// <summary>The number the next default name takes (<c>SQLQuery{n}</c>); never reused.</summary>
    public int NextDefaultNumber { get; set; } = 1;
}

internal sealed class EditorSessionStore : IEditorSessionStore, IDisposable
{
    private const string Key = "current";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly IIndexedDbAdapter _store;
    private readonly object _gate = new();
    private CancellationTokenSource? _pendingCts;
    private EditorSessionRecord? _pending;

    public EditorSessionStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task SaveAsync(EditorSessionRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        lock (_gate)
        {
            _pendingCts?.Cancel();
            _pendingCts?.Dispose();
            _pendingCts = new CancellationTokenSource();
            _pending = record;
            ScheduleDebouncedWrite(_pendingCts.Token);
        }
        return Task.CompletedTask;
    }

    private void ScheduleDebouncedWrite(CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException) { return; }

            EditorSessionRecord? snapshot;
            lock (_gate)
            {
                if (ct.IsCancellationRequested) return;
                snapshot = _pending;
                _pending = null;
            }

            if (snapshot != null)
            {
                snapshot.SavedAt = DateTimeOffset.UtcNow;
                try
                {
                    await _store.SetAsync(StoreNames.EditorSession, Key,
                        JsonSerializer.Serialize(snapshot)).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort -- session restore is a convenience, not a correctness gate.
                }
            }
        }, ct);
    }

    public async Task FlushAsync()
    {
        EditorSessionRecord? snapshot;
        lock (_gate)
        {
            _pendingCts?.Cancel();
            snapshot = _pending;
            _pending = null;
        }
        if (snapshot != null)
        {
            snapshot.SavedAt = DateTimeOffset.UtcNow;
            await _store.SetAsync(StoreNames.EditorSession, Key,
                JsonSerializer.Serialize(snapshot)).ConfigureAwait(false);
        }
    }

    public async Task<EditorSessionRecord?> RestoreAsync()
    {
        // Spec 041: a save still in its debounce is the newest record — a read-modify-write (New, Open, History's
        // "Open in editor") must not lose the keystroke saved a moment ago.
        lock (_gate)
        {
            if (_pending != null) return Copy(_pending);
        }
        var raw = await _store.GetAsync(StoreNames.EditorSession, Key).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<EditorSessionRecord>(raw); }
        catch (JsonException) { return null; }
    }

    private static EditorSessionRecord Copy(EditorSessionRecord r) => new()
    {
        DocumentText = r.DocumentText,
        CursorOffset = r.CursorOffset,
        ActiveProfileId = r.ActiveProfileId,
        SessionKey = r.SessionKey,
        SavedAt = r.SavedAt,
        DocumentName = r.DocumentName,
        IsModified = r.IsModified,
        NextDefaultNumber = r.NextDefaultNumber,
    };

    public Task ClearAsync()
    {
        lock (_gate) { _pendingCts?.Cancel(); _pending = null; }
        return _store.DeleteAsync(StoreNames.EditorSession, Key);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pendingCts?.Cancel();
            _pendingCts?.Dispose();
            _pendingCts = null;
        }
    }
}
