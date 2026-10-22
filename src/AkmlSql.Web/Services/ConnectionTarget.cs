using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (task T063, contracts/web-ui.md) - everything the connection dialog knows about a server, for either kind.
/// SQL Server uses <see cref="Server"/> (as typed: <c>host</c>, <c>host\instance</c>, <c>host,port</c>),
/// <see cref="WindowsAuth"/>, <see cref="User"/> and <see cref="Password"/>. PostgreSQL uses <see cref="Server"/> as the
/// host (optionally <c>host:port</c> or <c>[::1]:port</c>), <see cref="Port"/>, <see cref="User"/> (required: there is no
/// integrated sign-in), <see cref="Password"/>, <see cref="SslMode"/> and <see cref="RootCertificatePath"/>.
/// <see cref="TrustServerCertificate"/> (SQL Server, review WEB-02) skips certificate validation for this connection only —
/// for a remote server with a self-signed certificate the user trusts; a server on the engine's own machine always skips it.
/// The password lives in memory only for the time of the call; it is never part of a saved record.
/// <see cref="SavedConnectionId"/> (spec 043) is the saved connection the dialog connected from, if any: the query tab's
/// binding keeps it as a reference.
/// <see cref="CopyPasswordFrom"/> (spec 043 follow-up, review) names the engine session of the tab a new tab was opened from,
/// so a test, a database list or a connect with no password typed is sent with that tab's password by the engine
/// (<c>session.copy.v2</c>; review, round 2). Never persisted; ignored when a password is typed.
/// </summary>
public sealed record ConnectionTarget(
    DatabaseKind Kind,
    string Server,
    string Database,
    bool WindowsAuth = false,
    string? User = null,
    string? Password = null,
    int? Port = null,
    string? SslMode = null,
    string? RootCertificatePath = null,
    bool RememberPassword = false,
    bool TrustServerCertificate = false,
    string? SavedConnectionId = null,
    string? CopyPasswordFrom = null);

/// <summary>
/// Review WEB-06: the outcome of a Disconnect. <see cref="Released"/> means the engine closed the session's connection;
/// <see cref="RolledBack"/> that it rolled back an open transaction first. <see cref="Warning"/> says what could not be done
/// (the engine was unreachable or too old); the browser has forgotten the connection either way.
/// </summary>
public sealed record DisconnectResult(bool Released, bool RolledBack, string? Warning);

/// <summary>
/// The outcome of a connect or test. <see cref="PasswordRequired"/> means the server wants a password the engine does not
/// hold, so the dialog should ask for it (it is not a failure to be worded). <see cref="ServerVersion"/> is the
/// product and version the server reported, for example <c>PostgreSQL 18.1</c>.
/// <see cref="Remembered"/> (spec 043 follow-up, review): the SQL Server password the connect asked the engine to remember is
/// in the engine's store now; false when it was not asked for or the engine could not keep it. Review, round 2: an engine with
/// only <c>credentials.sqlserver.v1</c> cannot say, so a password it was asked to keep counts as kept, as it did before.
/// </summary>
public sealed record ConnectResult(bool Ok, string? Error = null, bool PasswordRequired = false, string? ServerVersion = null,
    string? ErrorCode = null, bool Remembered = false)
{
    public static ConnectResult Failed(string error) => new(false, error);
}
