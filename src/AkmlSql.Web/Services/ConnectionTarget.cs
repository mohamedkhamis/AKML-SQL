using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (task T063, contracts/web-ui.md) - everything the connection dialog knows about a server, for either kind.
/// SQL Server uses <see cref="Server"/> (as typed: <c>host</c>, <c>host\instance</c>, <c>host,port</c>),
/// <see cref="WindowsAuth"/>, <see cref="User"/> and <see cref="Password"/>. PostgreSQL uses <see cref="Server"/> as the
/// host (optionally <c>host:port</c> or <c>[::1]:port</c>), <see cref="Port"/>, <see cref="User"/> (required: there is no
/// integrated sign-in), <see cref="Password"/>, <see cref="SslMode"/> and <see cref="RootCertificatePath"/>.
/// The password lives in memory only for the time of the call; it is never part of a saved record.
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
    bool RememberPassword = false);

/// <summary>
/// The outcome of a connect or test. <see cref="PasswordRequired"/> means the server wants a password the engine does not
/// hold, so the dialog should ask for it (it is not a failure to be worded). <see cref="ServerVersion"/> is the
/// product and version the server reported, for example <c>PostgreSQL 18.1</c>.
/// </summary>
public sealed record ConnectResult(bool Ok, string? Error = null, bool PasswordRequired = false, string? ServerVersion = null)
{
    public static ConnectResult Failed(string error) => new(false, error);
}
