using System;
using System.Text.Json.Serialization;
using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 043 (data-model §4) — what a query tab remembers about its server, so a reload brings the tab back connected to the
/// same place: the kind, the server and database, how it signs in, and the security choices (trust the certificate for SQL
/// Server; TLS mode and CA path for PostgreSQL). A snapshot of the connection dialog's target <b>without the password</b>,
/// which is never stored in the browser: a tab that needs one asks for it on its first run after a reload.
/// <see cref="SavedConnectionId"/> only points at the saved entry it came from; deleting that entry leaves the binding usable.
/// Persisted inside the tab record (IndexedDB <c>editorTabs</c>), whose tripwire test forbids the word "password" anywhere in
/// it, so <see cref="RememberPassword"/> is stored as <c>RememberOnEngine</c>, as <see cref="SavedSqlConnection"/> does.
/// </summary>
public sealed class ConnectionBinding
{
    public string? SavedConnectionId { get; set; }
    public DatabaseKind Kind { get; set; } = DatabaseKind.SqlServer;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public bool WindowsAuth { get; set; } = true;
    public string? Login { get; set; }
    public int? Port { get; set; }
    public string? SslMode { get; set; }
    public string? RootCertificatePath { get; set; }
    public bool TrustServerCertificate { get; set; }

    [JsonPropertyName("RememberOnEngine")]
    public bool RememberPassword { get; set; }

    /// <summary>
    /// Connecting needs nothing the browser does not hold: Windows authentication, or a password the engine remembers
    /// (PostgreSQL, and SQL Server authentication since the spec 043 follow-up). Such a tab reconnects when it is shown; any
    /// other asks for the password on its first run (FR-009).
    /// </summary>
    [JsonIgnore]
    public bool NeedsNoPassword => Kind == DatabaseKind.SqlServer ? WindowsAuth || RememberPassword : RememberPassword;

    /// <summary>The binding a successful connect to <paramref name="target"/> leaves on the tab (the password is dropped).</summary>
    public static ConnectionBinding FromTarget(ConnectionTarget target, string? savedConnectionId) => new()
    {
        SavedConnectionId = savedConnectionId,
        Kind = target.Kind,
        Server = target.Server ?? string.Empty,
        Database = target.Database ?? string.Empty,
        WindowsAuth = target.Kind == DatabaseKind.SqlServer && target.WindowsAuth,
        Login = target.Kind == DatabaseKind.SqlServer && target.WindowsAuth ? null : target.User,
        Port = target.Port,
        SslMode = target.SslMode,
        RootCertificatePath = target.RootCertificatePath,
        TrustServerCertificate = target.TrustServerCertificate,
        RememberPassword = target.RememberPassword,
    };

    /// <summary>The target to connect this binding with; <paramref name="password"/> is the one the user just typed, if any.</summary>
    public ConnectionTarget ToTarget(string? password = null) => new(
        Kind, Server, Database,
        WindowsAuth: Kind == DatabaseKind.SqlServer && WindowsAuth,
        User: Login,
        Password: string.IsNullOrEmpty(password) ? null : password,
        Port: Port,
        SslMode: SslMode,
        RootCertificatePath: RootCertificatePath,
        RememberPassword: RememberPassword,
        TrustServerCertificate: TrustServerCertificate,
        SavedConnectionId: SavedConnectionId);

    /// <summary>A copy, so a tab never shares its binding object with another (New query inherits a copy).</summary>
    public ConnectionBinding Clone() => (ConnectionBinding)MemberwiseClone();

    /// <summary>
    /// Spec 043 follow-up (review, round 2): whether <paramref name="target"/> signs in where this binding does — the same kind,
    /// server (trimmed, any case) and login (any case), and for PostgreSQL the same port (host and port compared parsed, as
    /// <see cref="SameConnectionAs"/> does). A password the engine holds for a tab (remembered, or copied from another tab), and
    /// one the server refused, belong to that sign-in; the database and the security choices are not part of it.
    /// </summary>
    public bool SameSignInAs(ConnectionTarget target)
    {
        if (target == null || target.Kind != Kind) return false;
        bool windows = Kind == DatabaseKind.SqlServer && WindowsAuth, targetWindows = Kind == DatabaseKind.SqlServer && target.WindowsAuth;
        if (windows != targetWindows) return false;
        var login = windows ? string.Empty : (Login ?? string.Empty).Trim();
        var targetLogin = targetWindows ? string.Empty : (target.User ?? string.Empty).Trim();
        if (!string.Equals(login, targetLogin, StringComparison.OrdinalIgnoreCase)) return false;
        if (Kind == DatabaseKind.PostgreSql)
        {
            var mine = SqlConnectionService.ParseHostAndPort(Server, Port);
            var theirs = SqlConnectionService.ParseHostAndPort(target.Server, target.Port);
            return mine.Host != null && theirs.Host != null
                && string.Equals(mine.Host, theirs.Host, StringComparison.OrdinalIgnoreCase) && mine.Port == theirs.Port;
        }
        return string.Equals((Server ?? string.Empty).Trim(), (target.Server ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Spec 043 follow-up (review, round 3): whether the engine would copy this connection's password into a request for
    /// <paramref name="target"/> (<c>SessionPasswordCopy</c>): the same sign-in (<see cref="SameSignInAs"/>) and the same certificate
    /// checking as the two connection strings put it on the wire. SQL Server: the effective "Trust server certificate" — a server
    /// on the engine's own machine is always trusted (<see cref="SqlConnectionService.BuildConnectionString"/>), so only a remote
    /// server's choice counts. PostgreSQL: the TLS mode (none is <c>prefer</c>), the root certificate path as typed (trimmed, any
    /// case; sent whatever the mode, as <see cref="SqlConnectionService.ParsePostgresTarget"/> does) and the user name with its
    /// case, which the engine compares exactly. A password is never offered where the engine would refuse to copy it.
    /// </summary>
    public bool CopiesPasswordTo(ConnectionTarget target)
    {
        if (!SameSignInAs(target)) return false;
        if (Kind == DatabaseKind.SqlServer)   // Windows authentication has no password to copy
            return !WindowsAuth && EffectiveTrust(Server, TrustServerCertificate) == EffectiveTrust(target.Server, target.TrustServerCertificate);
        return string.Equals((Login ?? string.Empty).Trim(), (target.User ?? string.Empty).Trim(), StringComparison.Ordinal)
            && string.Equals(TlsMode(SslMode), TlsMode(target.SslMode), StringComparison.Ordinal)
            && string.Equals((RootCertificatePath ?? string.Empty).Trim(), (target.RootCertificatePath ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // What BuildConnectionString sends as TrustServerCertificate, and the TLS mode ParsePostgresTarget sends.
    internal static bool EffectiveTrust(string? server, bool trustServerCertificate) =>
        trustServerCertificate || SqlConnectionService.IsLoopbackServer(server ?? string.Empty);

    private static string TlsMode(string? sslMode) =>
        string.IsNullOrWhiteSpace(sslMode) ? "prefer" : sslMode!.Trim().ToLowerInvariant();

    /// <summary>
    /// Review of US1: whether this is the connection <paramref name="saved"/> describes — the same kind, server, sign-in and
    /// security choices. The database is left out: a tab's database moves (a <c>USE</c>, the database selector) and the
    /// connection stays the saved one.
    /// </summary>
    public bool SameConnectionAs(SavedSqlConnection saved)
    {
        if (saved == null || saved.Kind != Kind) return false;
        if (Kind == DatabaseKind.PostgreSql)
        {
            // The host as typed ("[::1]", "db1:5433") and as saved (parsed) name the same server: compare them parsed.
            var mine = SqlConnectionService.ParseHostAndPort(Server, Port ?? SavedSqlConnection.DefaultPostgresPort);
            var theirs = SqlConnectionService.ParseHostAndPort(saved.Server, saved.EffectivePort);
            return mine.Host != null && string.Equals(mine.Host, theirs.Host, StringComparison.OrdinalIgnoreCase) && mine.Port == theirs.Port
                && string.Equals(saved.Login ?? string.Empty, Login ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(saved.SslMode ?? "prefer", SslMode ?? "prefer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(saved.RootCertificatePath ?? string.Empty, RootCertificatePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        if (!string.Equals(saved.Server?.Trim(), Server?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (saved.WindowsAuth != WindowsAuth || saved.TrustServerCertificate != TrustServerCertificate) return false;
        return WindowsAuth || string.Equals(saved.Login ?? string.Empty, Login ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
