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
    /// Connecting needs nothing the browser does not hold: Windows authentication, or a PostgreSQL password the engine
    /// remembers. Such a tab reconnects when it is shown; any other asks for the password on its first run (FR-009).
    /// </summary>
    [JsonIgnore]
    public bool NeedsNoPassword => Kind == DatabaseKind.SqlServer ? WindowsAuth : RememberPassword;

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
