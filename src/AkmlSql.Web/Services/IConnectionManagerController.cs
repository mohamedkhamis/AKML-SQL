using System;

namespace AkmlSql.Web.Services;

/// <summary>
/// Phase 4 (web connection manager). Tiny stateless singleton that decouples the global
/// <c>ConnectionManagerModal</c> (hosted once in MainLayout) from the surfaces that open it.
/// The modal subscribes to <see cref="OpenRequested"/> on init; the command palette's
/// <c>sql:connect</c> action, the Settings page's "Manage connections…" button, and the
/// StatusBar indicator all call <see cref="Open"/>. Mirrors the shared-singleton pattern used
/// by <c>ICommandRegistry</c>: a single instance both producers and the consumer resolve from DI.
/// <para>
/// Spec 043 (T056): the dialog connects a query tab — the one that asked (<see cref="TargetSession"/>; a run on a tab with no
/// connection, or a restored tab whose connection needs a password), else the active one — and says when it closes, so the
/// run that opened it can go on after a successful connect (US1 scenario 7).
/// </para>
/// </summary>
public interface IConnectionManagerController
{
    /// <summary>Raised when a surface asks for the connection-manager modal to open.</summary>
    event Action? OpenRequested;

    /// <summary>Request that the connection-manager modal open. No-op if nothing is subscribed.</summary>
    void Open();

    /// <summary>
    /// Spec 042 (T064): the kind the last <see cref="Open(AkmlSql.Core.DatabaseKind?)"/> asked the dialog to start on, or
    /// null for the saved connection's own kind (SQL Server for a new one). Read by the dialog when it opens.
    /// </summary>
    AkmlSql.Core.DatabaseKind? RequestedKind => null;

    /// <summary>Spec 042 (T064): open the dialog on a given kind ("Connect to PostgreSQL…"); null is <see cref="Open()"/>.</summary>
    void Open(AkmlSql.Core.DatabaseKind? kind) => Open();

    /// <summary>Spec 043: the query tab session the dialog connects; null for the active tab's. Read by the dialog when it opens.</summary>
    SqlSessionState? TargetSession => null;

    /// <summary>Spec 043: what the form starts with (a restored tab's connection, its password still to type); null as before.</summary>
    ConnectionBinding? Prefill => null;

    /// <summary>Spec 043: open the dialog to connect <paramref name="target"/>, its form filled from <paramref name="prefill"/>.</summary>
    void Open(SqlSessionState? target, ConnectionBinding? prefill) => Open();

    /// <summary>Spec 043: the dialog closed; true when it connected its tab.</summary>
    event Action<bool>? Closed { add { } remove { } }

    /// <summary>Spec 043: called by the dialog when it closes.</summary>
    void NotifyClosed(bool connected) { }
}

internal sealed class ConnectionManagerController : IConnectionManagerController
{
    public event Action? OpenRequested;

    public event Action<bool>? Closed;

    public AkmlSql.Core.DatabaseKind? RequestedKind { get; private set; }

    public SqlSessionState? TargetSession { get; private set; }

    public ConnectionBinding? Prefill { get; private set; }

    public void Open() => Open(null);

    public void Open(AkmlSql.Core.DatabaseKind? kind)
    {
        RequestedKind = kind;
        TargetSession = null;
        Prefill = null;
        OpenRequested?.Invoke();
    }

    public void Open(SqlSessionState? target, ConnectionBinding? prefill)
    {
        RequestedKind = prefill?.Kind;
        TargetSession = target;
        Prefill = prefill?.Clone();
        OpenRequested?.Invoke();
    }

    public void NotifyClosed(bool connected) => Closed?.Invoke(connected);
}
