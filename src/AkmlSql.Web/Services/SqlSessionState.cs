using System;
using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 043 (research R1, R2, data-model §3) — one query tab's connection: its own engine session id and the state of the
/// connection the engine holds for it. Before query tabs the browser had one of these, inside
/// <see cref="SqlConnectionService"/>; now every tab has its own, and the service works on the active tab's unless a call
/// names another. Mutated only by <see cref="SqlConnectionService"/>; everything else reads it.
/// <para>
/// The id is persisted with the tab and reused after a reload, so the engine finds the session (and its schema cache)
/// again. <see cref="NeedsReattach"/> marks a restored tab whose connection has not been opened yet in this page: the
/// first activation (when no password is needed) or the first run sends <c>SessionDisconnect</c>, awaits it, then
/// <c>ConnectionChanged</c>.
/// </para>
/// </summary>
public sealed class SqlSessionState
{
    public SqlSessionState(string? sessionId = null)
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId!;
    }

    /// <summary>The engine session id: <c>ConnectionChanged</c>, <c>DocumentChanged</c>, executes and completions carry it.</summary>
    public string SessionId { get; }

    public bool IsConnected { get; internal set; }
    public string? Server { get; internal set; }
    public string? Database { get; internal set; }
    public bool WindowsAuth { get; internal set; } = true;
    public string? Login { get; internal set; }
    public DatabaseKind Kind { get; internal set; } = DatabaseKind.SqlServer;
    public string? ServerVersionText { get; internal set; }
    public TransactionState TransactionState { get; internal set; } = TransactionState.None;

    /// <summary>Bumped whenever the connection's target changes (results read under an older one are never edited).</summary>
    public int ConnectionGeneration { get; internal set; }

    /// <summary>What the tab connected to last, without the password; persisted with the tab and used to reconnect it.</summary>
    public ConnectionBinding? Binding { get; internal set; }

    /// <summary>A restored tab whose connection is not open in this page yet (data-model §3).</summary>
    public bool NeedsReattach { get; internal set; }

    /// <summary>
    /// Spec 043 follow-up: a new tab opened on a connected tab (New query, as in SSMS) names that tab's engine session, so its first
    /// connect needs no password typed — the engine takes it from that session (<c>session.copy.v1</c>). Not persisted: after a
    /// reload the tab asks, unless the engine remembers the password. Cleared once the tab has connected.
    /// </summary>
    public string? CopyPasswordFrom { get; internal set; }

    /// <summary>The tab can connect again without asking: nothing to type (<see cref="ConnectionBinding.NeedsNoPassword"/>), or a
    /// connected tab to take the password from.</summary>
    public bool CanReconnectAlone => Binding is { } b && (b.NeedsNoPassword || CopyPasswordFrom != null);

    /// <summary>A run is in flight on this tab: its connection and database cannot be changed until it ends.</summary>
    public bool IsBusy { get; set; }

    // The PostgreSQL schema-cache key, and the engine the connection was opened through (SqlConnectionService.SchemaIdentity).
    internal string? PgServerKey { get; set; }
    internal string? EngineUrl { get; set; }

    /// <summary>Raised when this tab's connection state changes (whether or not the tab is the active one).</summary>
    public event Action? StateChanged;

    internal void RaiseChanged() => StateChanged?.Invoke();

    /// <summary>Review of US1: the engine may hold this session — it was connected in this page, or it is a stored session an
    /// earlier page used. A new tab's fresh session, never sent to the engine, has nothing there to release or warn about.</summary>
    public bool MayBeOnEngine { get; internal set; }

    /// <summary>Review of US1: the user disconnected this tab (Settings › Connections › Disconnect). Kept with the tab, so a reload
    /// leaves it without a connection; cleared by the next connect.</summary>
    public bool DisconnectedByUser { get; internal set; }

    /// <summary>A restored tab: its binding is known, its connection is not open in this page.</summary>
    internal void Restore(ConnectionBinding? binding, bool disconnectedByUser = false)
    {
        Binding = binding;
        NeedsReattach = binding != null;
        DisconnectedByUser = binding == null && disconnectedByUser;
    }

    /// <summary>The tab was closed (its session released or forgotten): a connect still on its way sends nothing for it.</summary>
    public bool IsReleased { get; internal set; }

    // Every connect of this session takes the next number; one that finds a newer number when its ConnectionChanged is due
    // gives way (a reconnect must not land after the user's own connect, nor after a close).
    internal int ConnectEpoch { get; set; }
}
