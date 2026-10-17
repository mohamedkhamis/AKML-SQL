using System;

namespace AkmlSql.Web.Services;

/// <summary>
/// Phase 4 (web connection manager). Tiny stateless singleton that decouples the global
/// <c>ConnectionManagerModal</c> (hosted once in MainLayout) from the surfaces that open it.
/// The modal subscribes to <see cref="OpenRequested"/> on init; the command palette's
/// <c>sql:connect</c> action, the Settings page's "Manage connections…" button, and the
/// StatusBar indicator all call <see cref="Open"/>. Mirrors the shared-singleton pattern used
/// by <c>ICommandRegistry</c>: a single instance both producers and the consumer resolve from DI.
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
}

internal sealed class ConnectionManagerController : IConnectionManagerController
{
    public event Action? OpenRequested;

    public AkmlSql.Core.DatabaseKind? RequestedKind { get; private set; }

    public void Open() => Open(null);

    public void Open(AkmlSql.Core.DatabaseKind? kind)
    {
        RequestedKind = kind;
        OpenRequested?.Invoke();
    }
}
