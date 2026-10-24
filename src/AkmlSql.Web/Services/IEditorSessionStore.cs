using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T051. Persists the editor session (text, caret,
/// selected profile id) to IndexedDB so a reload restores the working state. Writes are
/// debounced 500 ms by <see cref="SaveAsync"/>; restoration on <see cref="RestoreAsync"/>
/// returns the most recent record or null.
/// <para>
/// Spec 043 (research R8, data-model §5): the query tabs. Each tab has its own record (<c>tab:{id}</c> in the
/// <c>editorTabs</c> store) and the set of tabs one more (<c>set</c>: their order, the active one, the next default
/// name number). The single-document record above stays readable for the one-time migration into tab 1.
/// </para>
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

    // ── Spec 043 — query tabs. Default members: a fake that predates them is one document, as before tabs. ────────────

    /// <summary>Schedule a debounced write of one tab's record (one pending write per tab). A tab no longer in the set when the
    /// write is due writes nothing.</summary>
    Task SaveTabAsync(string tabId, EditorSessionRecord record) => SaveAsync(record);

    /// <summary>One tab's record: its save still in its debounce, else the stored one, else null.</summary>
    Task<EditorSessionRecord?> RestoreTabAsync(string tabId) => RestoreAsync();

    /// <summary>A closed tab: its pending save is dropped and its record deleted.</summary>
    Task DeleteTabAsync(string tabId) => Task.CompletedTask;

    /// <summary>Write the tab set at once (create, close, reorder, activate).</summary>
    Task SaveSetAsync(EditorTabSetRecord set) => Task.CompletedTask;

    /// <summary>The tab set, or null before the first load after the upgrade (the migration then makes it).</summary>
    Task<EditorTabSetRecord?> RestoreSetAsync() => Task.FromResult<EditorTabSetRecord?>(null);

    /// <summary>Review of US1: write the tab set only when there is none yet (the move to tabs); false when another window of
    /// the app made one first, which then stands.</summary>
    async Task<bool> CreateSetAsync(EditorTabSetRecord set)
    {
        if (await RestoreSetAsync().ConfigureAwait(false) != null) return false;
        await SaveSetAsync(set).ConfigureAwait(false);
        return true;
    }

    /// <summary>Settings › Editor › Reset editor session: every tab record, the set and the old single document.</summary>
    Task ClearTabsAsync() => ClearAsync();

    /// <summary>The old single-document record as stored, whatever the set says (null when there is none). A window of an
    /// older build may still write it after the move to tabs; the next load brings it back as a tab and deletes it.</summary>
    Task<EditorSessionRecord?> RestoreLegacyAsync() => Task.FromResult<EditorSessionRecord?>(null);
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

    /// <summary>The number the next default name takes (<c>SQLQuery{n}</c>); never reused. Spec 043: for a tab record the
    /// set's counter is the one that counts; this copy is kept for the single-document record.</summary>
    public int NextDefaultNumber { get; set; } = 1;

    // Spec 043 (data-model §5) — a query tab's record. Additive, as above.

    /// <summary>The tab this record belongs to; null in the single-document record.</summary>
    public string? TabId { get; set; }

    /// <summary>The tab's engine session id, reused after a reload (research R2).</summary>
    public string? EngineSessionId { get; set; }

    /// <summary>The kind of server the tab was last bound to (a <c>DatabaseKind</c> number).</summary>
    public int Kind { get; set; }

    /// <summary>What the tab connected to, without the password; null for a tab with no connection.</summary>
    public ConnectionBinding? Binding { get; set; }

    /// <summary>History's Re-execute put the query here: run it once the tab is connected.</summary>
    public bool AutorunPending { get; set; }

    /// <summary>Spec 042 (FR-070): the kind (a DatabaseKind number) the re-executed entry ran on; null for any.</summary>
    public int? AutorunKind { get; set; }

    /// <summary>Review of US1: the user disconnected this tab. A reload leaves it without a connection: the status bar's one-time
    /// restore of the most recent saved connection passes it by.</summary>
    public bool DisconnectedByUser { get; set; }

    /// <summary>Review of US1: the history key of the single document an older build kept writing that this tab was brought back
    /// from. A later copy of that document updates this tab instead of adding another.</summary>
    public string? ImportedFromKey { get; set; }

    /// <summary>Review of US1: the fingerprint of the text the tab was last brought back with (see <see cref="ImportedFromKey"/>).</summary>
    public string? ImportedTextHash { get; set; }
}

/// <summary>Spec 043 (data-model §2) — the query tabs: their order, the active one, and the next default name number.</summary>
public sealed class EditorTabSetRecord
{
    public List<string> Order { get; set; } = new();
    public string? ActiveId { get; set; }
    public int NextDefaultNumber { get; set; } = 1;
    public int Version { get; set; } = 1;
}

internal sealed class EditorSessionStore : IEditorSessionStore, IDisposable
{
    private const string Key = "current";
    internal const string SetKey = "set";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    internal static string TabKey(string tabId) => "tab:" + tabId;

    private readonly IIndexedDbAdapter _store;
    private readonly object _gate = new();

    // Spec 043 (R8): one pending write per record (the single document, and each tab), so saves of two tabs within the
    // pause do not collapse into one.
    private readonly Dictionary<(string Store, string Key), (EditorSessionRecord Record, CancellationTokenSource Cts)> _pending = new();

    // A tab save already past its debounce when the tab is deleted (or every tab is cleared) must not write the record back as
    // an orphan: deleted tab keys (tab ids are never reused) and a generation that a reset moves on.
    private readonly HashSet<string> _deletedTabKeys = new();
    private int _tabsGeneration;

    public EditorSessionStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task SaveAsync(EditorSessionRecord record) => Schedule(StoreNames.EditorSession, Key, record);

    public Task SaveTabAsync(string tabId, EditorSessionRecord record)
    {
        if (string.IsNullOrEmpty(tabId)) throw new ArgumentException("A tab id is required.", nameof(tabId));
        if (record != null) record.TabId = tabId;
        return Schedule(StoreNames.EditorTabs, TabKey(tabId), record!);
    }

    private Task Schedule(string store, string key, EditorSessionRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_pending.TryGetValue((store, key), out var old))
            {
                old.Cts.Cancel();
                old.Cts.Dispose();
            }
            cts = new CancellationTokenSource();
            _pending[(store, key)] = (record, cts);
        }
        ScheduleDebouncedWrite(store, key, cts);
        return Task.CompletedTask;
    }

    private void ScheduleDebouncedWrite(string store, string key, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        int generation;
        lock (_gate) generation = _tabsGeneration;
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
                if (!_pending.TryGetValue((store, key), out var entry) || !ReferenceEquals(entry.Cts, cts)) return;
                snapshot = entry.Record;
                _pending.Remove((store, key));
            }

            try
            {
                await WriteAsync(store, key, snapshot, generation).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort -- session restore is a convenience, not a correctness gate.
            }
        }, ct);
    }

    private async Task WriteAsync(string store, string key, EditorSessionRecord record, int generation)
    {
        // Spec 043 (G9): a tab closed meanwhile (here, or in another window of the app) is not written back.
        if (store == StoreNames.EditorTabs && record.TabId is { } tabId)
        {
            var set = await RestoreSetAsync().ConfigureAwait(false);
            if (set != null && !set.Order.Contains(tabId)) return;
        }
        record.SavedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(record);
        Task write;
        lock (_gate)
        {
            // Checked again right before the write: the tab may have been deleted, or every tab cleared, while the set was read.
            if (store == StoreNames.EditorTabs && (generation != _tabsGeneration || _deletedTabKeys.Contains(key))) return;
            write = _store.SetAsync(store, key, json);
        }
        await write.ConfigureAwait(false);
    }

    public async Task FlushAsync()
    {
        List<((string Store, string Key) At, EditorSessionRecord Record)> snapshot;
        int generation;
        lock (_gate)
        {
            snapshot = _pending.Select(p => (p.Key, p.Value.Record)).ToList();
            foreach (var p in _pending.Values) p.Cts.Cancel();
            _pending.Clear();
            generation = _tabsGeneration;
        }
        foreach (var (at, record) in snapshot)
        {
            await WriteAsync(at.Store, at.Key, record, generation).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The single document — spec 043: once there are query tabs, the ACTIVE tab's record (the offline quick info and signature
    /// help read "the" document through this). Before the first load with tabs (no set yet) it is the old single-document
    /// record, which the migration reads to make tab 1.
    /// </summary>
    public async Task<EditorSessionRecord?> RestoreAsync()
    {
        var set = await RestoreSetAsync().ConfigureAwait(false);
        if (set?.ActiveId is { Length: > 0 } active) return await RestoreTabAsync(active).ConfigureAwait(false);
        return await ReadAsync(StoreNames.EditorSession, Key).ConfigureAwait(false);
    }

    public Task<EditorSessionRecord?> RestoreLegacyAsync() => ReadAsync(StoreNames.EditorSession, Key);

    public Task<EditorSessionRecord?> RestoreTabAsync(string tabId) =>
        string.IsNullOrEmpty(tabId) ? Task.FromResult<EditorSessionRecord?>(null) : ReadAsync(StoreNames.EditorTabs, TabKey(tabId));

    private async Task<EditorSessionRecord?> ReadAsync(string store, string key)
    {
        // Spec 041: a save still in its debounce is the newest record — a read-modify-write (New, Open, History's
        // "Open in editor") must not lose the keystroke saved a moment ago.
        lock (_gate)
        {
            if (_pending.TryGetValue((store, key), out var entry)) return Copy(entry.Record);
        }
        var raw = await _store.GetAsync(store, key).ConfigureAwait(false);
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
        TabId = r.TabId,
        EngineSessionId = r.EngineSessionId,
        Kind = r.Kind,
        Binding = r.Binding?.Clone(),
        AutorunPending = r.AutorunPending,
        AutorunKind = r.AutorunKind,
        DisconnectedByUser = r.DisconnectedByUser,
        ImportedFromKey = r.ImportedFromKey,
        ImportedTextHash = r.ImportedTextHash,
    };

    public async Task DeleteTabAsync(string tabId)
    {
        if (string.IsNullOrEmpty(tabId)) return;
        lock (_gate)
        {
            if (_pending.Remove((StoreNames.EditorTabs, TabKey(tabId)), out var entry)) entry.Cts.Cancel();
            _deletedTabKeys.Add(TabKey(tabId));
        }
        await _store.DeleteAsync(StoreNames.EditorTabs, TabKey(tabId)).ConfigureAwait(false);
    }

    public Task SaveSetAsync(EditorTabSetRecord set)
    {
        if (set == null) throw new ArgumentNullException(nameof(set));
        return _store.SetAsync(StoreNames.EditorTabs, SetKey, JsonSerializer.Serialize(set));
    }

    public Task<bool> CreateSetAsync(EditorTabSetRecord set)
    {
        if (set == null) throw new ArgumentNullException(nameof(set));
        return _store.AddAsync(StoreNames.EditorTabs, SetKey, JsonSerializer.Serialize(set));
    }

    public async Task<EditorTabSetRecord?> RestoreSetAsync()
    {
        var raw = await _store.GetAsync(StoreNames.EditorTabs, SetKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<EditorTabSetRecord>(raw); }
        catch (JsonException) { return null; }
    }

    public Task ClearAsync()
    {
        lock (_gate)
        {
            if (_pending.Remove((StoreNames.EditorSession, Key), out var entry)) entry.Cts.Cancel();
        }
        return _store.DeleteAsync(StoreNames.EditorSession, Key);
    }

    public async Task ClearTabsAsync()
    {
        lock (_gate)
        {
            foreach (var p in _pending.Values) p.Cts.Cancel();
            _pending.Clear();
            _tabsGeneration++;   // a tab save already past its debounce writes nothing now
        }
        await _store.ClearAsync(StoreNames.EditorTabs).ConfigureAwait(false);
        await _store.DeleteAsync(StoreNames.EditorSession, Key).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var p in _pending.Values)
            {
                p.Cts.Cancel();
                p.Cts.Dispose();
            }
            _pending.Clear();
        }
    }
}
