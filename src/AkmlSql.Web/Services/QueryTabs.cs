using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>Spec 043 (data-model §1): hands out the default names <c>SQLQuery{n}</c>; a number is never handed out twice.</summary>
public interface IDefaultNameCounter
{
    /// <summary>The number the next default name takes.</summary>
    int Next { get; }

    /// <summary>Take the next number (the counter moves on).</summary>
    int Take();
}

/// <summary>
/// Spec 043 (research R1, data-model §1) — one open query: its document (name, modified state, history key), its text, its
/// own engine session and connection, and what the editor page shows for it (results, findings, a run in flight). The page
/// keeps every tab's results pane mounted, so a tab's pending grid edits survive a switch.
/// </summary>
public sealed class QueryTab
{
    internal QueryTab(string id, EditorDocumentFlow document, SqlSessionState session)
    {
        Id = id;
        Document = document;
        Session = session;
    }

    public string Id { get; }

    /// <summary>The document: name, modified state, history session key, Save and Save as.</summary>
    public EditorDocumentFlow Document { get; }

    /// <summary>The tab's engine session and connection.</summary>
    public SqlSessionState Session { get; }

    /// <summary>The tab's text as the page last saw it (the editor has the live one while the tab is shown).</summary>
    public string Text { get; set; } = string.Empty;

    public int CursorOffset { get; set; }

    /// <summary>History's Re-execute: run once the tab is connected, then cleared.</summary>
    public bool AutorunPending { get; set; }

    /// <summary>Spec 042 (FR-070): the kind of server the re-executed entry ran on; a connection of the other kind never runs it.</summary>
    public int? AutorunKind { get; set; }

    /// <summary>The query History asked to run: an edit of the tab's text before it runs cancels the run (it is no longer that
    /// query).</summary>
    public string? AutorunSql { get; set; }

    /// <summary>The engine session id the tab's stored record holds. Another live window of the app may own that session; this
    /// window then runs the tab on a session of its own (<see cref="Session"/>) and keeps the record's id for its owner.</summary>
    internal string? RecordSessionId { get; set; }

    /// <summary>The tab is being closed while its run finishes (data-model §3 Closing); hidden from the strip.</summary>
    public bool IsClosing { get; set; }

    public string Name => Document.Name;
    public bool IsModified => Document.IsModified;
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && !IsModified;

    // ── What the editor page shows for the tab (not persisted) ───────────────────────────────────────────────
    public ExecuteQueryResult? Result { get; set; }
    public int ResultGeneration { get; set; }
    public DateTimeOffset ResultReceivedAt { get; set; } = DateTimeOffset.Now;
    public int SelectionStartLine { get; set; } = 1;
    public int SelectionStartColumn { get; set; } = 1;
    public string LastExecutedSql { get; set; } = string.Empty;
    public CodeAnalysisResponse? Findings { get; set; }
    public bool IsExecuting { get; set; }
    public string? ActiveQueryId { get; set; }
    public DateTimeOffset? RunStartedAt { get; set; }
    public bool GridHasPendingEdits { get; set; }

    /// <summary>The results tab the page showed for this tab (Results, Messages, Problems).</summary>
    public string? ResultsTab { get; set; }

    /// <summary>Cancel (or a close) came while the run was still connecting: the query is not sent.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>The tab was formatted or refreshed while another tab was shown: it is analysed when it is shown again (T034).</summary>
    public bool AnalysisPending { get; set; }

    /// <summary>Spec 043 (T036): the tab's run as the status bar shows it while the tab is active.</summary>
    public RunSnapshot Run { get; set; } = RunSnapshot.Idle;

    /// <summary>The error line the user last went to from this tab's messages (spec 041 FR-023).</summary>
    public AkmlSql.Web.Shared.EditorDiagnostic? ErrorMark { get; set; }

    /// <summary>Grows per finished run of this tab (the page's <c>execute-complete</c> marker shows the active tab's).</summary>
    public int ExecuteVersion { get; set; }

    /// <summary>The <see cref="ExecuteStatus"/> of this tab's last finished run.</summary>
    public int LastExecuteStatus { get; set; }

    /// <summary>Review of US1: the history key of the older build's single document this tab was brought back from (null for
    /// any other tab); a later copy of that document updates this tab while the tab still holds what was brought back.</summary>
    public string? ImportedFromKey { get; set; }

    /// <summary>A fingerprint of the text the tab was last brought back with (<see cref="ImportedFromKey"/>): an edit made here
    /// since then is never overwritten by a later copy from the older build.</summary>
    internal string? ImportedTextHash { get; set; }

    /// <summary>Review of US1: a close is asking the server whether the tab's session holds a transaction (its tooltip says so).</summary>
    public bool CheckingBeforeClose { get; set; }
}

/// <summary>
/// Spec 043 (research R1, R8, R11, data-model §1–§2, §5) — the editor's query tabs: their order, the active one, the default
/// names, and their persistence (<see cref="IEditorSessionStore"/>, store <c>editorTabs</c>). A singleton: History opens a
/// query in a new tab while the editor page is not on screen, and the page shows it when it next opens.
/// </summary>
public interface IQueryTabs
{
    /// <summary>The most tabs open at once (FR-013): each costs the engine a session and a connection.</summary>
    const int MaxTabs = 20;

    IReadOnlyList<QueryTab> Tabs { get; }

    QueryTab? Active { get; }

    /// <summary>A tab was added, closed, renamed, reordered or activated (or a page said a tab's run state changed).</summary>
    event Action? Changed;

    /// <summary>A tab's run or result changed where no page may be listening: the editor page showing the tabs re-renders.</summary>
    void NotifyChanged();

    /// <summary>FR-013: no further tab. A closed tab whose run has not answered yet still holds its engine session, so it counts
    /// until its lease is given up (<see cref="ReleaseLeaseAsync"/>).</summary>
    bool AtTabLimit { get; }

    /// <summary>Why New query is refused at the limit: how many tabs are open, and how many to close (FR-013).</summary>
    string LimitText => $"You have {MaxTabs} query tabs open; close one to open another";

    /// <summary>Give up this window's lease on a closed tab's engine session (after the page released the session).</summary>
    Task ReleaseLeaseAsync(QueryTab tab);

    /// <summary>Read the tabs (once; later calls return at once). Moves the single document of before tabs into tab 1.</summary>
    Task LoadAsync();

    /// <summary>
    /// A new tab after the others, active, named <paramref name="name"/> or the next default name, bound to a copy of
    /// <paramref name="inheritFrom"/>'s connection (New query, FR-002). Null at <see cref="MaxTabs"/>.
    /// </summary>
    Task<QueryTab?> NewAsync(QueryTab? inheritFrom, string? text = null, string? name = null);

    Task ActivateAsync(QueryTab tab);

    /// <summary>Remove a tab (the page released its session first): the previous one becomes active; the last tab closed
    /// leaves a new empty one.</summary>
    Task CloseAsync(QueryTab tab);

    /// <summary>
    /// History's Open and Re-execute (R11): the query in a new tab, or in the active one when it is empty and unmodified;
    /// active; <paramref name="autorun"/> runs it once connected, only on a connection of <paramref name="autorunKind"/> (a
    /// <see cref="DatabaseKind"/> number) when one is given. Null at <see cref="MaxTabs"/>.
    /// </summary>
    Task<QueryTab?> OpenInNewTabAsync(string text, string? name, bool autorun, int? autorunKind = null);

    /// <summary>Save a tab's record (debounced): text, document, engine session id, binding, autorun.</summary>
    Task PersistAsync(QueryTab tab);

    /// <summary>Settings › Editor › Reset editor session: every tab closed and released, one empty tab left.</summary>
    Task ResetAsync();
}

internal sealed class QueryTabs : IQueryTabs, IDefaultNameCounter
{
    private readonly IEditorSessionStore _store;
    private readonly ISqlConnectionService _sql;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IFileDownloader _downloader;
    private readonly IWorkspaceStatus? _status;
    private readonly ISessionLeases _leases;
    private readonly List<QueryTab> _tabs = new();
    private Task? _load;
    private int _next = 1;

    // Review of US1 (two windows of the app share the tab records): the set record is read, changed and written back, never
    // overwritten from this window's list. This window adds the tabs it created and removes the tabs it closed; tabs another
    // window created stay, and a tab another window closed is not put back (data-model §5).
    private readonly SemaphoreSlim _setGate = new(1, 1);
    private readonly HashSet<string> _closedHere = new();
    private readonly HashSet<string> _addedHere = new();

    public QueryTabs(IEditorSessionStore store, ISqlConnectionService sql, IDialogService dialogs, INotificationService notifications, IFileDownloader downloader,
        IWorkspaceStatus? status = null, ISessionLeases? leases = null)
    {
        _status = status;
        _leases = leases ?? NoSessionLeases.Instance;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sql = sql ?? throw new ArgumentNullException(nameof(sql));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    }

    public IReadOnlyList<QueryTab> Tabs => _tabs.Where(t => !t.IsClosing).ToList();

    public QueryTab? Active { get; private set; }

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    // Closed tabs whose engine session is not released yet (their run was in flight).
    private readonly HashSet<QueryTab> _lingering = new();

    public bool AtTabLimit => Tabs.Count + _lingering.Count >= IQueryTabs.MaxTabs;

    public string LimitText => LimitMessage(Tabs.Count, _lingering.Count);

    /// <summary>
    /// FR-013's message. Two windows of the app can together store more than <see cref="IQueryTabs.MaxTabs"/> tabs (every stored tab
    /// comes back), and a closed tab whose run has not answered still counts, so the message says how many to close.
    /// </summary>
    internal static string LimitMessage(int open, int closing)
    {
        int max = IQueryTabs.MaxTabs;
        int toClose = Math.Max(1, open + closing - max + 1);
        if (closing == 0 && toClose == 1) return $"You have {open} query tabs open; close one to open another";
        string count = closing > 0 ? $"{open} query tabs open and {closing} still closing" : $"{open} query tabs open";
        string close = toClose == 1 ? "close one" : $"close {toClose}";
        return $"You have {count}; the limit is {max}, so {close} to open another";
    }

    // ── IDefaultNameCounter ─────────────────────────────────────────────────────────────────────────────────
    public int Next => _next;

    public int Take()
    {
        var n = _next++;
        _ = SaveSetAsync();
        return n;
    }

    public Task LoadAsync() => _load ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        var set = await _store.RestoreSetAsync().ConfigureAwait(false)
                  ?? await EditorSessionKeys.MigrateLegacyAsync(_store).ConfigureAwait(false);
        _next = Math.Max(1, set.NextDefaultNumber);

        // Every tab in the set comes back, even past the limit: a set two windows filled together loses no tab (New query is
        // refused until there are fewer than MaxTabs again).
        var restored = new List<(string Id, EditorSessionRecord Record, string RecordSessionId)>();
        foreach (var id in set.Order.Distinct())
        {
            var record = await _store.RestoreTabAsync(id).ConfigureAwait(false);
            if (record == null) continue;   // a record lost or never written: the tab is not brought back
            restored.Add((id, record, string.IsNullOrWhiteSpace(record.EngineSessionId) ? NewSessionId() : record.EngineSessionId!));
        }
        var sessionIds = await ResolveSessionIdsAsync(restored.Select(r => (r.Id, r.RecordSessionId)).ToList()).ConfigureAwait(false);
        for (int i = 0; i < restored.Count; i++)
        {
            var (id, record, recordSessionId) = restored[i];
            var tab = await BuildAsync(id, record, recordSessionId, sessionIds[i].SessionId, sessionIds[i].MayBeOnEngine).ConfigureAwait(false);
            // A History Re-execute waits for its tab's connection in this page only: a reload (or a new browser session) never
            // runs a query the user did not see start.
            tab.AutorunPending = false;
            tab.AutorunKind = null;
            _tabs.Add(tab);
        }

        // A single document an older build saved after the move to tabs (a window of that build still open, or a first load
        // that stopped half way) comes back as a tab of its own instead of being lost. A window of that build that keeps
        // typing updates the same tab on every load, never a new copy each time.
        if (await _store.RestoreLegacyAsync().ConfigureAwait(false) is { } leftover)
        {
            var text = leftover.DocumentText ?? string.Empty;
            bool known = _tabs.Any(t => t.Document.SessionKey == leftover.SessionKey && t.Text == text);
            var copy = string.IsNullOrEmpty(leftover.SessionKey) ? null : _tabs.FirstOrDefault(t => t.ImportedFromKey == leftover.SessionKey);
            // A copy edited here since it was brought back is the user's own work now: it is left alone, and the newer copy from
            // the older build comes back as a tab of its own (which later copies update).
            if (!known && copy != null && copy.Text != text && copy.ImportedTextHash != null && copy.ImportedTextHash != TextHash(copy.Text))
            {
                copy.ImportedFromKey = null;
                copy.ImportedTextHash = null;
                await PersistAsync(copy).ConfigureAwait(false);
                copy = null;
            }
            if (!known && copy != null && copy.Text != text)
            {
                copy.Text = text;
                copy.ImportedTextHash = TextHash(text);
                await copy.Document.LoadAsync(new EditorSessionRecord
                {
                    DocumentText = text,
                    CursorOffset = leftover.CursorOffset,
                    DocumentName = copy.Document.Name,
                    IsModified = true,
                    SessionKey = copy.Document.SessionKey,
                }).ConfigureAwait(false);
                await PersistAsync(copy).ConfigureAwait(false);
            }
            else if (!known && copy == null && text.Length > 0)
            {
                var keyInUse = _tabs.Any(t => t.Document.SessionKey == leftover.SessionKey);
                await AddFromRecordAsync(new EditorSessionRecord
                {
                    DocumentText = text,
                    CursorOffset = leftover.CursorOffset,
                    DocumentName = string.IsNullOrWhiteSpace(leftover.DocumentName) ? null : leftover.DocumentName,
                    IsModified = leftover.IsModified,
                    SessionKey = keyInUse ? null : leftover.SessionKey,
                    ImportedFromKey = leftover.SessionKey,
                    ImportedTextHash = TextHash(text),
                }).ConfigureAwait(false);
            }
            await _store.FlushAsync().ConfigureAwait(false);
            await _store.ClearAsync().ConfigureAwait(false);
        }

        if (_tabs.Count == 0)
        {
            await AddAsync(null, string.Empty, null).ConfigureAwait(false);
        }
        Activate(_tabs.FirstOrDefault(t => t.Id == set.ActiveId) ?? _tabs[0]);
        await SaveSetAsync().ConfigureAwait(false);
    }

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    /// <summary>A stable fingerprint of a text (64-bit FNV-1a): the same in every page and every build, unlike string.GetHashCode.</summary>
    internal static string TextHash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text ?? string.Empty)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Review of US1 (two windows of the app; a browser that cannot tell): the engine session each restored tab runs on in this
    /// window. The stored id when this window holds its lease; otherwise the session this window — this browser tab, across its
    /// reloads — used for the tab before, when no other window holds it; otherwise a new one, remembered for the next reload.
    /// A session this window used before that the stored id now replaces is released. Every claim of a load is asked at once, so
    /// a browser that answers by asking the other windows waits once, not once per tab.
    /// </summary>
    private async Task<(string SessionId, bool MayBeOnEngine)[]> ResolveSessionIdsAsync(IReadOnlyList<(string TabId, string RecordSessionId)> tabs)
    {
        var result = new string[tabs.Count];
        var claims = await Task.WhenAll(tabs.Select(t => _leases.ClaimAsync(t.RecordSessionId))).ConfigureAwait(false);
        var windowIds = await Task.WhenAll(tabs.Select(t => _leases.WindowSessionAsync(t.TabId))).ConfigureAwait(false);
        var others = Enumerable.Range(0, tabs.Count).Where(i => windowIds[i] is { } w && w != tabs[i].RecordSessionId).ToList();
        var otherClaims = await Task.WhenAll(others.Select(i => _leases.ClaimAsync(windowIds[i]!))).ConfigureAwait(false);
        var fresh = new List<int>();
        for (int i = 0; i < tabs.Count; i++)
        {
            int k = others.IndexOf(i);
            var windowClaim = k < 0 ? (LeaseClaim?)null : otherClaims[k];
            // The session this window used for the tab is the stored one itself (a browser that cannot tell keeps every tab's
            // session that way): usable unless another window holds it.
            bool ownIsStored = windowIds[i] == tabs[i].RecordSessionId;
            if (claims[i] == LeaseClaim.Held)
            {
                result[i] = tabs[i].RecordSessionId;
                // The stored session is this window's again: the one it used meanwhile is no longer anybody's.
                if (windowClaim == LeaseClaim.Held) _ = ReleaseUnusedSessionAsync(tabs[i].TabId, windowIds[i]!);
            }
            else if (ownIsStored ? claims[i] == LeaseClaim.Unavailable : windowClaim is LeaseClaim.Held or LeaseClaim.Unavailable)
            {
                result[i] = windowIds[i]!;
            }
            else
            {
                result[i] = NewSessionId();
                fresh.Add(i);
            }
        }
        await Task.WhenAll(fresh.Select(i => _leases.ClaimAsync(result[i], fresh: true))).ConfigureAwait(false);
        await Task.WhenAll(fresh.Select(i => _leases.RememberWindowSessionAsync(tabs[i].TabId, result[i]))).ConfigureAwait(false);
        // A stored or reused session may still be on the engine (an earlier page's); a new one never was.
        return result.Select((id, i) => (id, !fresh.Contains(i))).ToArray();
    }

    private async Task ReleaseUnusedSessionAsync(string tabId, string sessionId)
    {
        await _leases.ForgetWindowSessionAsync(tabId).ConfigureAwait(false);
        var unused = _sql.CreateSession(sessionId);
        try { await _sql.DisconnectSessionAsync(unused, release: true, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { /* best effort: the engine may be away; the release is sent when it is back */ }
        _sql.ForgetSession(unused);
        await _leases.ReleaseAsync(sessionId).ConfigureAwait(false);
    }

    private async Task<QueryTab> BuildAsync(string id, EditorSessionRecord record, string recordSessionId, string sessionId, bool mayBeOnEngine)
    {
        var session = _sql.CreateSession(sessionId);
        session.Restore(record.Binding, record.DisconnectedByUser);
        session.MayBeOnEngine = mayBeOnEngine;
        QueryTab? tab = null;
        var document = new EditorDocumentFlow(_store, _dialogs, _notifications, _downloader,
            new DocumentScope(this, () => tab == null ? Task.CompletedTask : PersistAsync(tab)));
        tab = new QueryTab(id, document, session)
        {
            Text = record.DocumentText ?? string.Empty,
            CursorOffset = record.CursorOffset,
            AutorunPending = record.AutorunPending,
            AutorunKind = record.AutorunKind,
            // The id the tab's record keeps: a window that runs the tab on a session of its own keeps the stored one for its owner.
            RecordSessionId = recordSessionId,
            ImportedFromKey = record.ImportedFromKey,
            ImportedTextHash = record.ImportedTextHash,
        };
        await document.LoadAsync(record).ConfigureAwait(false);
        Wire(tab);
        return tab;
    }

    private void Wire(QueryTab tab)
    {
        // A connect, a USE, a disconnect: the tab's binding is saved with it. The user's Disconnect is written at once: a reload
        // right after it must not find the old connection in a save still in its debounce.
        tab.Session.StateChanged += () => _ = tab.Session.DisconnectedByUser && tab.Session.Binding == null
            ? PersistNowAsync(tab)
            : PersistAsync(tab);
        tab.Document.Changed += () => Changed?.Invoke();
    }

    public async Task<QueryTab?> NewAsync(QueryTab? inheritFrom, string? text = null, string? name = null)
    {
        if (AtTabLimit) return null;
        var tab = await AddAsync(inheritFrom?.Session.Binding, text ?? string.Empty, name).ConfigureAwait(false);
        Activate(tab);
        await SaveSetAsync().ConfigureAwait(false);
        Changed?.Invoke();
        return tab;
    }

    private Task<QueryTab> AddAsync(ConnectionBinding? binding, string text, string? name) =>
        AddFromRecordAsync(new EditorSessionRecord
        {
            DocumentText = text,
            DocumentName = name,
            Binding = binding?.Clone(),
            Kind = (int)(binding?.Kind ?? DatabaseKind.SqlServer),
        });

    private async Task<QueryTab> AddFromRecordAsync(EditorSessionRecord record)
    {
        var id = Guid.NewGuid().ToString("N");
        record.TabId = id;
        if (string.IsNullOrWhiteSpace(record.DocumentName)) record.DocumentName = EditorDocument.DefaultName(Take());
        record.SessionKey ??= Guid.NewGuid().ToString("N");
        var sessionId = NewSessionId();
        record.EngineSessionId = sessionId;
        // Made here: nobody else can hold it. A browser that cannot tell keeps it as this window's, for its reloads.
        if (await _leases.ClaimAsync(sessionId, fresh: true).ConfigureAwait(false) == LeaseClaim.Unavailable)
            await _leases.RememberWindowSessionAsync(id, sessionId).ConfigureAwait(false);
        var tab = await BuildAsync(id, record, sessionId, sessionId, mayBeOnEngine: false).ConfigureAwait(false);
        _tabs.Add(tab);
        _addedHere.Add(id);
        await SaveSetAsync().ConfigureAwait(false);
        await PersistAsync(tab).ConfigureAwait(false);
        // A new tab is written at once, as the set is: a reload right after New query must not find the set naming a tab
        // whose record is still in its debounce (the tab would be dropped).
        await _store.FlushAsync().ConfigureAwait(false);
        return tab;
    }

    public async Task ActivateAsync(QueryTab tab)
    {
        if (tab == null || ReferenceEquals(Active, tab) || !_tabs.Contains(tab)) return;
        Activate(tab);
        await SaveSetAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    private void Activate(QueryTab tab)
    {
        Active = tab;
        _sql.Activate(tab.Session);
        // T036: the status bar shows the active tab's run and document.
        _status?.Show(tab.Run);
        _status?.SetDocument(tab.Name, tab.IsModified);
    }

    public async Task CloseAsync(QueryTab tab)
    {
        if (tab == null) return;
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        _tabs.RemoveAt(index);
        _closedHere.Add(tab.Id);
        _addedHere.Remove(tab.Id);
        if (tab.IsExecuting) _lingering.Add(tab);
        if (ReferenceEquals(Active, tab) || Active == null)
        {
            var open = _tabs.Where(t => !t.IsClosing).ToList();
            if (open.Count == 0)
            {
                await AddAsync(null, string.Empty, null).ConfigureAwait(false);
                open = _tabs.Where(t => !t.IsClosing).ToList();
            }
            // As SSMS: the tab before the closed one (the first when the first was closed).
            var before = _tabs.Take(Math.Min(index, _tabs.Count)).LastOrDefault(t => !t.IsClosing);
            Activate(before ?? open[0]);
        }
        _sql.ForgetSession(tab.Session);   // after another tab is active: the service keeps its active session
        await _leases.ForgetWindowSessionAsync(tab.Id).ConfigureAwait(false);
        // The set first, then the record: a debounced save that reads the set after this no longer finds the tab.
        await SaveSetAsync().ConfigureAwait(false);
        await _store.DeleteTabAsync(tab.Id).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>A closed tab's session lease is given up once the engine session is released (the page calls it).</summary>
    public Task ReleaseLeaseAsync(QueryTab tab)
    {
        if (tab == null) return Task.CompletedTask;
        if (_lingering.Remove(tab)) Changed?.Invoke();   // the tab no longer counts toward the limit
        return _leases.ReleaseAsync(tab.Session.SessionId);
    }

    public async Task<QueryTab?> OpenInNewTabAsync(string text, string? name, bool autorun, int? autorunKind = null)
    {
        await LoadAsync().ConfigureAwait(false);
        QueryTab? tab;
        if (Active is { IsEmpty: true, IsExecuting: false, GridHasPendingEdits: false } reuse)
        {
            tab = reuse;
            tab.Text = text ?? string.Empty;
            await tab.Document.ReplaceUnmodifiedAsync(tab.Text, name).ConfigureAwait(false);
            // A new document: the old one's findings and error mark do not belong to it.
            tab.Findings = null;
            tab.ErrorMark = null;
        }
        else
        {
            tab = await NewAsync(Active, text, name).ConfigureAwait(false);
            if (tab == null)
            {
                _notifications.Notify(LimitText + ".", NotificationKind.Info);
                return null;
            }
        }
        tab.AutorunPending = autorun;
        tab.AutorunKind = autorun ? autorunKind : null;
        tab.AutorunSql = autorun ? tab.Text : null;
        await PersistAsync(tab).ConfigureAwait(false);
        await _store.FlushAsync().ConfigureAwait(false);   // the editor page reads it when it opens
        Changed?.Invoke();
        return tab;
    }

    public Task PersistAsync(QueryTab tab)
    {
        if (tab == null || tab.IsClosing || !_tabs.Contains(tab)) return Task.CompletedTask;
        return _store.SaveTabAsync(tab.Id, new EditorSessionRecord
        {
            TabId = tab.Id,
            DocumentText = tab.Text,
            CursorOffset = tab.CursorOffset,
            SessionKey = tab.Document.SessionKey,
            DocumentName = tab.Document.Name,
            IsModified = tab.Document.IsModified,
            NextDefaultNumber = _next,
            // The id this tab's record had: a window that had to use a session of its own keeps the stored one for its owner.
            EngineSessionId = tab.RecordSessionId ?? tab.Session.SessionId,
            Kind = (int)(tab.Session.Binding?.Kind ?? tab.Session.Kind),
            Binding = tab.Session.Binding?.Clone(),
            AutorunPending = tab.AutorunPending,
            AutorunKind = tab.AutorunPending ? tab.AutorunKind : null,
            DisconnectedByUser = tab.Session.DisconnectedByUser,
            ImportedFromKey = tab.ImportedFromKey,
            ImportedTextHash = tab.ImportedTextHash,
        });
    }

    private async Task PersistNowAsync(QueryTab tab)
    {
        await PersistAsync(tab).ConfigureAwait(false);
        await _store.FlushAsync().ConfigureAwait(false);
    }

    private async Task SaveSetAsync()
    {
        await _setGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var stored = await _store.RestoreSetAsync().ConfigureAwait(false);
            var order = new List<string>();
            if (stored != null)
            {
                foreach (var id in stored.Order)
                {
                    if (!_closedHere.Contains(id) && !order.Contains(id)) order.Add(id);
                }
                // A tab this window created that the stored set now lists is settled: from here on, the stored set decides
                // whether it stays (another window may close it).
                _addedHere.RemoveWhere(id => stored.Order.Contains(id));
                _next = Math.Max(_next, stored.NextDefaultNumber);
            }
            else
            {
                foreach (var t in _tabs.Where(t => !t.IsClosing))
                {
                    if (!order.Contains(t.Id)) order.Add(t.Id);
                }
            }
            foreach (var t in _tabs.Where(t => !t.IsClosing && _addedHere.Contains(t.Id)))
            {
                if (!order.Contains(t.Id)) order.Add(t.Id);
            }
            await _store.SaveSetAsync(new EditorTabSetRecord
            {
                Order = order,
                ActiveId = Active != null && order.Contains(Active.Id) ? Active.Id : stored?.ActiveId,
                NextDefaultNumber = _next,
            }).ConfigureAwait(false);
        }
        finally
        {
            _setGate.Release();
        }
    }

    public async Task ResetAsync()
    {
        await LoadAsync().ConfigureAwait(false);
        foreach (var tab in _tabs.ToList())
        {
            try { await _sql.DisconnectSessionAsync(tab.Session, release: true, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { /* best effort: the engine may be away */ }
            _sql.ForgetSession(tab.Session);
            await _leases.ReleaseAsync(tab.Session.SessionId).ConfigureAwait(false);
            await _leases.ForgetWindowSessionAsync(tab.Id).ConfigureAwait(false);
            _closedHere.Add(tab.Id);
        }
        _tabs.Clear();
        _addedHere.Clear();
        await _store.ClearTabsAsync().ConfigureAwait(false);
        _next = 1;
        var fresh = await AddAsync(null, string.Empty, null).ConfigureAwait(false);
        Activate(fresh);
        await SaveSetAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }
}
