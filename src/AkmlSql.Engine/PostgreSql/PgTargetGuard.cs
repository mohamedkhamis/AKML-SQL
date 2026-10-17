using System.Data.Common;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>Why the engine will not open a connection: a stable id (<see cref="DialectErrors"/>) and a sentence for the person who asked.</summary>
public sealed record PgTargetRefusal(string Code, string Message);

/// <summary>
/// Spec 042 (FR-100 to FR-103, contracts/ipc.md "Engine-side guard for bridge clients") - which PostgreSQL connections a
/// request that arrived over the WebSocket bridge may make the engine open. The engine-side half of the check the web
/// edition also runs: a browser-side check alone protects nothing, because anything that can reach the bridge can skip it.
///
/// <para>
/// It is an <b>allow-list</b>. The web edition sends exactly the settings <c>PgConnectionString</c> writes, so anything else
/// is refused. That closes, without enumerating them, the ways a hostile page could use the engine as a confused deputy:
/// signing in with the engine's own identity (integrated security, Kerberos, GSS), making the engine read a file the page
/// names (client certificate and key, a password file, a root certificate at a relative or UNC path - the latter would also
/// make the engine authenticate to a machine of the page's choosing), or changing the session through startup options.
/// A remote host with an explicit user name and password is allowed: the engine only forwards what it was given.
/// </para>
/// <para>
/// Requests over the named pipe come from the SSMS extension running as the signed-in user and are not checked. A refusal
/// never repeats the connection string, so a password in it cannot reach a log through this path.
/// </para>
/// </summary>
internal static class PgTargetGuard
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Port", "Database", "Username", "Password", "SSL Mode", "Root Certificate",
        "Application Name", "Timeout", "Command Timeout",
    };

    private static readonly HashSet<string> AmbientIdentity = new(StringComparer.OrdinalIgnoreCase)
    {
        "Integrated Security", "Kerberos Service Name", "Include Realm", "GSS Encryption Mode",
    };

    private static readonly HashSet<string> EngineSideFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SSL Certificate", "SSL Key", "SSL Password", "Passfile", "Client Certificate", "Client Certificate Key",
    };

    internal const string AmbientIdentityRefused =
        "PostgreSQL connections from the web edition need an explicit user name and password. The engine runs as a service, " +
        "so Windows, Kerberos or GSS sign-in would use the service account, not you.";

    internal const string FileRefused =
        "Client certificates, key files and password files on the engine machine are not available from the web edition.";

    internal const string UserRequired = "A PostgreSQL connection needs a user name.";
    internal const string HostRequired = "A PostgreSQL server must be addressed by host name or IP address.";
    internal const string CertificatePathRefused =
        "The root certificate must be given as a full local path on the engine machine (for example C:\\certs\\root.crt); " +
        "relative, network (UNC) and non-canonical paths are not accepted.";

    /// <summary>Null when the engine may open <paramref name="connectionString"/> for the current request, otherwise the refusal.</summary>
    public static PgTargetRefusal? Check(string connectionString) =>
        Check(connectionString, fromBridge: BridgeSourceIp.Current is not null);

    internal static PgTargetRefusal? Check(string? connectionString, bool fromBridge)
    {
        if (!fromBridge) return null;

        DbConnectionStringBuilder raw;
        try
        {
            raw = new DbConnectionStringBuilder { ConnectionString = connectionString ?? string.Empty };
        }
        catch (ArgumentException)
        {
            return Refuse("The connection string is not valid.");
        }

        foreach (string key in raw.Keys)
        {
            if (AmbientIdentity.Contains(key)) return Refuse(AmbientIdentityRefused);
            if (EngineSideFiles.Contains(key)) return Refuse(FileRefused);
            if (!Allowed.Contains(key))
                return Refuse($"The setting '{key}' is not supported for a PostgreSQL connection made from the web edition.");
        }

        var host = Get(raw, "Host");
        if (host.Length == 0 || host[0] == '/' || host[0] == '\\') return Refuse(HostRequired);

        if (Get(raw, "Username").Length == 0) return Refuse(UserRequired);

        var certificate = Get(raw, "Root Certificate");
        if (certificate.Length > 0 && !IsLocalCanonicalPath(certificate)) return Refuse(CertificatePathRefused);

        return null;
    }

    private static PgTargetRefusal Refuse(string message) => new(DialectErrors.CredentialsRequired, message);

    private static string Get(DbConnectionStringBuilder builder, string key) =>
        builder.TryGetValue(key, out var value) ? (Convert.ToString(value) ?? string.Empty).Trim() : string.Empty;

    /// <summary>
    /// A rooted local path equal to its own canonical form. Canonical comparison is the rule the rest of the engine uses for
    /// paths (never a substring test for "..", which a different spelling defeats); UNC and device paths are excluded.
    /// </summary>
    private static bool IsLocalCanonicalPath(string path)
    {
        try
        {
            if (!Path.IsPathRooted(path)) return false;
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return false;
            return string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
